using System;
using System.Buffers;
using System.Collections.Concurrent;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet.Voice
{
    public delegate ArraySegment<float> ProcessSamplesDelegate(ArraySegment<float> input, int frequency, params FilterLevel[] level);

    public partial class PurrVoicePlayer : NetworkIdentity
    {
        [SerializeField, PurrLock] private InputProvider _inputProvider;
        [SerializeField, PurrLock] private OutputProvider _outputProvider;
        [SerializeField, PurrLock] private AudioCodecSettings _codecSettings;

#if PURR_PIPES
        [SerializeField]
#endif
        private NetworkAudioModule _transport = new ();

        /// <summary>
        /// Handles whether this PurrVoicePlayer is muted.
        /// If it's the local one, no audio will be sent.
        /// If it's a remote client, we won't replay their audio
        /// </summary>
        public bool muted;
        [SerializeField, PurrLock] private bool _enableLocalPlayback = false;
        [SerializeField, PurrLock, PurrShowIf("_enableLocalPlayback")]
        private OutputProvider _localOutputProvider;

        [SerializeField] private SilenceSuppressionSettings _silenceSuppression = SilenceSuppressionSettings.Default;

        /// <summary>
        /// Sender-side silence suppression. When enabled, chunks whose post-filter peak
        /// amplitude stays below the threshold are dropped before encoding/sending,
        /// eliminating network packets during silence. Receivers fill gaps with zeros
        /// via the normal pre-buffer mechanism.
        /// </summary>
        public SilenceSuppressionSettings silenceSuppression
        {
            get => _silenceSuppression;
            set
            {
                _silenceSuppression = value;
                if (_workerSuppressor != null) _workerSuppressor.settings = value;
                _transport?.SetSilenceSuppression(value);
            }
        }

        private bool _localPlaybackActive;
        private bool _localOutputProviderInitialized;

        /// <summary>
        /// True when local playback (mic monitoring) is currently running.
        /// </summary>
        public bool isLocalPlaybackActive => _localPlaybackActive;

        /// <summary>
        /// Legacy accessor mirroring the inspector-configured default for local playback.
        /// </summary>
        public bool usingLocalPlayback => _enableLocalPlayback;

        /// <summary>
        /// Output provider used for local mic monitoring. Can be assigned at runtime, but
        /// only while local playback is stopped — assignments are rejected while active.
        /// </summary>
        public OutputProvider localOutputProvider
        {
            get => _localOutputProvider;
            set
            {
                if (_localPlaybackActive)
                {
                    PurrLogger.LogError($"Cannot change localOutputProvider while local playback is active. Call {nameof(StopLocalPlayback)}() first.", this);
                    return;
                }

                if (_localOutputProvider == value)
                    return;

                _localOutputProvider = value;
                _localOutputProviderInitialized = false;
            }
        }

        private IAudioInputSource micDevice => _inputProvider.input; 

        public int inputFrequency => micDevice?.frequency ?? -1;
        public IVoiceOutput output => _outputProvider?.output;

        [SerializeField, PurrReadOnly, UsedImplicitly] private string _currentDevice;

        /// <summary>
        /// Whenever you start playing audio from the microphone, this event will be invoked with the samples.
        /// </summary>
        public event Action<ArraySegment<float>> onReceivedSample;

        /// <summary>
        /// When you locally record audio from the microphone, this event will be invoked with the samples.
        /// </summary>
        public event Action<ArraySegment<float>> onLocalSample;

        /// <summary>
        /// Latest peak from the local or received voice sample.
        /// </summary>
        public float peak => _isLocalVoiceOwner ? _lastLocalSamplePeak : _lastReceivedSamplePeak;

        /// <summary>
        /// Returns true when the latest local or received voice sample is above the given peak threshold.
        /// </summary>
        public bool IsSpeaking(float threshold)
        {
            double lastSampleTime = _isLocalVoiceOwner ? _lastLocalSampleTime : _lastReceivedSampleTime;
            if (lastSampleTime <= 0d)
                return false;

            if (Time.realtimeSinceStartupAsDouble - lastSampleTime > SPEAKING_SAMPLE_TIMEOUT)
                return false;

            return peak >= threshold;
        }

        private bool _initializedController;
        private bool _initializedRemote;
        private bool _outputInitialized;
        private bool _isLocalVoiceOwner;
        private VoiceDebugSettings _debugSettings;
        private float _lastLocalSamplePeak;
        private float _lastReceivedSamplePeak;
        private double _lastLocalSampleTime;
        private double _lastReceivedSampleTime;
        private const double SPEAKING_SAMPLE_TIMEOUT = 0.15d;

        /// <summary>
        /// True when this local player controls microphone input for this voice player.
        /// </summary>
        public bool isLocalVoiceOwner => _isLocalVoiceOwner;

        /// <summary>
        /// True once the player's output pipeline and filter chain have been initialized.
        /// </summary>
        public bool isInitialized => _outputInitialized;

        /// <summary>
        /// Voice packets received from the network for this player, before decoding.
        /// </summary>
        public int receivedPacketCount => _transport?.receivedPacketCount ?? 0;

        /// <summary>
        /// Voice packets decoded into samples for this player.
        /// </summary>
        public int decodedPacketCount => _transport?.decodedPacketCount ?? 0;

        /// <summary>
        /// Voice packets dropped because they arrived before the stream frequency was known.
        /// </summary>
        public int droppedBeforeReadyCount => _transport?.droppedBeforeReadyCount ?? 0;

        /// <summary>
        /// Seconds since the last decoded remote sample, or -1 when nothing was received yet.
        /// </summary>
        public double secondsSinceLastReceivedSample =>
            _lastReceivedSampleTime <= 0d ? -1d : Time.realtimeSinceStartupAsDouble - _lastReceivedSampleTime;

        /// <summary>
        /// One-line description of this player's voice pipeline state, for logs and consoles.
        /// </summary>
        public string DescribeState()
        {
            string outputState = output != null ? output.ToString() : "<no output>";
            return $"{name}: owner={owner}, localVoiceOwner={_isLocalVoiceOwner}, controller={isController}, muted={muted}, " +
                   $"initialized={_outputInitialized}, remotePlayback={_initializedRemote}, frequency={_transport?.frequency ?? -1}, " +
                   $"packets(received={receivedPacketCount}, decoded={decodedPacketCount}, droppedEarly={droppedBeforeReadyCount}), " +
                   $"lastReceived={secondsSinceLastReceivedSample:0.00}s, peak={peak:0.000}, output={outputState}";
        }

        /// <summary>
        /// Invoked once when the player's output pipeline and filter chain finish initializing.
        /// Subscribers added after initialization are invoked immediately.
        /// </summary>
        public event Action onInitialized
        {
            add
            {
                _onInitialized += value;
                if (_outputInitialized)
                    value?.Invoke();
            }
            remove => _onInitialized -= value;
        }
        private Action _onInitialized;

        // Cached filter level array to avoid params allocation per mic callback
        private static readonly FilterLevel[] SenderLevels = { FilterLevel.Sender };

        // Async sender filter processing state
        private struct SenderWorkItem
        {
            public float[] buffer;
            public int count;
            public int frequency;
        }

        private struct SenderResult
        {
            public float[] buffer;
            public int count;
        }

        private struct EncodedSendItem
        {
            public byte[] buffer;
            public int length;
            public bool resume;
#if UNITY_EDITOR
            public float[] debugBuffer;
            public int debugCount;
#endif
        }

        private ConcurrentQueue<SenderWorkItem> _senderInputQueue;
        private ConcurrentQueue<SenderResult> _senderResultQueue;
        private ConcurrentQueue<EncodedSendItem> _encodedSendQueue;
        private Action _senderFilterCallback;
        private SilenceSuppressor _workerSuppressor;

        private float[] _workerChunkBuffer;
        private byte[] _workerEncodeBuffer;
        private int _workerBufferPos;
        private double _workerResampleCarry;
        private bool _workerEncodeFailureLogged;

        private const double STALL_DETECT_SECONDS = 0.15;
        private double _lastMicCallbackTime = -1;
        private volatile int _workerResetRequested;
        private bool _pendingFrequencySetup;
        private int _pendingFrequencySetupAttempts;
        private const int MAX_PENDING_FREQUENCY_SETUP_ATTEMPTS = 30;

        private void Awake()
        {
            _senderInputQueue = new ConcurrentQueue<SenderWorkItem>();
            _senderResultQueue = new ConcurrentQueue<SenderResult>();
            _encodedSendQueue = new ConcurrentQueue<EncodedSendItem>();
            _senderFilterCallback = DrainSenderFilterQueue;
            _workerSuppressor = new SilenceSuppressor { settings = _silenceSuppression };
            FilterAwake();
        }

        private void InitOutputIfNeeded()
        {
            if (_outputInitialized)
                return;
            _outputInitialized = true;

            if (!_outputProvider)
            {
                PurrLogger.LogError($"Can't initialize PurrVoicePlayer with no output provider!", this);
                return;
            }

            if (_outputProvider is MultiAudioSourceVoiceProvider && GetComponent<AudioSource>() != null)
            {
                PurrLogger.LogError($"When using the MultiAudioSource Provider, PurrVoicePlayer cannot be on the same object as an audio source!", this);
                return;
            }

            _transport.SetCodec(_codecSettings ? _codecSettings.CreateCodec() : null);
            _transport.SetSilenceSuppression(_silenceSuppression);
            _transport.OnFrequencyChanged += OnFrequencyInitialized;
            _outputProvider.Init(_transport, ProcessSamples, FilterLevel.Receiver);
            if (_outputProvider.output != null)
                _outputProvider.output.onEndPlayingSample += OnReplayingSample;
            if (_transport.frequency > 0)
                OnFrequencyInitialized(_transport.frequency);
            FilterAwake();
            _onInitialized?.Invoke();
        }

        private void OnFrequencyInitialized(int freq)
        {
            if (freq <= 0)
            {
                if (_isLocalVoiceOwner && _initializedController && micDevice != null)
                    QueueFrequencySetupRetry();
                return;
            }

            DebugAwake(freq);
            SetupVisualization(freq);
        }

        protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            base.OnOwnerChanged(oldOwner, newOwner, asServer);

            if (asServer)
                return;

            InitOutputIfNeeded();

            bool oldWasLocalOwner = oldOwner.HasValue && localPlayer.HasValue && oldOwner.Value == localPlayer.Value;
            bool newIsLocalOwner = newOwner.HasValue && localPlayer.HasValue && newOwner.Value == localPlayer.Value;
            _isLocalVoiceOwner = newIsLocalOwner;
            ClearDebugVisualization();

            if ((_initializedController || _initializedRemote) && oldWasLocalOwner && !newIsLocalOwner)
            {
                Deinitialize();
            }

            if (newIsLocalOwner)
            {
                if (_initializedRemote)
                {
                    output?.Stop();
                    _initializedRemote = false;
                }

                if (micDevice != null)
                {
                    micDevice.onSampleReady -= OnMicrophoneData;
                    micDevice.Stop();
                }

                _inputProvider.Init(this);
                SetupMicrophone();
                AudioDevices.onDevicesChanged -= OnDevicesChanged;
                AudioDevices.onDevicesChanged += OnDevicesChanged;
            }
            else
            {
                if (_initializedController)
                    Deinitialize();

                AudioDevices.onDevicesChanged -= OnDevicesChanged;
                SetupRemotePlayback();
            }

            RefreshVisualizationSubscriptions();
        }

        private void OnEnable()
        {
            if (_initializedController && micDevice != null)
            {
                micDevice.onSampleReady -= OnMicrophoneData;
                micDevice.onSampleReady += OnMicrophoneData;
                AudioDevices.onUpdate -= DrainSenderResults;
                AudioDevices.onUpdate += DrainSenderResults;
            }
        }

        private void OnDisable()
        {
            if (micDevice != null)
            {
                micDevice.onSampleReady -= OnMicrophoneData;
            }

            AudioDevices.onUpdate -= DrainSenderResults;
        }

        protected override void OnDespawned()
        {
            base.OnDespawned();
            Cleanup();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Cleanup();
        }

        private void OnValidate()
        {
            VisualizerValidate();

#if UNITY_EDITOR
            if (_localOutputProvider == _outputProvider && _outputProvider != null)
            {
                _localOutputProvider = null;
                PurrLogger.LogError($"Only one instance per output! If you want them to be the same, add 2 components of the same type", this);
            }
#endif
        }

        /// <summary>
        /// Starts local mic monitoring through <see cref="localOutputProvider"/>. Safe to call
        /// at runtime. Requires the microphone to be initialized (this player must be the
        /// controller) and a <see cref="localOutputProvider"/> to be assigned.
        /// </summary>
        public void StartLocalPlayback()
        {
            if (_localPlaybackActive)
                return;

            if (!_initializedController || micDevice == null)
            {
                PurrLogger.LogError($"Cannot start local playback before the microphone is initialized.", this);
                return;
            }

            if (!_localOutputProvider)
            {
                PurrLogger.LogError($"Cannot start local playback: no local output provider assigned.", this);
                return;
            }

            if (!_localOutputProviderInitialized)
            {
                _localOutputProvider.Init(micDevice, ProcessSamplesLocal, FilterLevel.Sender, FilterLevel.Receiver);
                if (_localOutputProvider.output != null)
                {
                    _localOutputProvider.output.onStartPlayingSample += DebugStreamedAudio;
                    _localOutputProvider.output.onEndPlayingSample += DebugStreamedAudioEnd;
                }
                _localOutputProviderInitialized = true;
            }

            _localOutputProvider.output?.AttachInput();
            _localPlaybackActive = true;
        }

        /// <summary>
        /// Stops local mic monitoring without affecting the shared microphone or the
        /// outgoing voice stream. Safe to call when already stopped.
        /// </summary>
        public void StopLocalPlayback()
        {
            if (!_localPlaybackActive)
                return;

            _localOutputProvider?.output?.DetachInput();
            _localPlaybackActive = false;
        }

        private void Cleanup()
        {
            CleanupVisualization();

            StopLocalPlayback();
            _inputProvider?.Cleanup();
            output?.Stop();

            _transport.DisposeCodec();

            AudioDevices.onDevicesChanged -= OnDevicesChanged;
            AudioDevices.onUpdate -= DrainSenderResults;
            AudioDevices.onUpdate -= RetryPendingFrequencySetup;
            _pendingFrequencySetup = false;
            _pendingFrequencySetupAttempts = 0;
            CleanupSenderQueues();
        }

        private void SetupMicrophone()
        {
            if (_initializedController)
            {
                if (micDevice == null)
                {
                    StopLocalPlayback();
                    _transport.Stop();
                    _currentDevice = "No input";
                    AudioDevices.onUpdate -= DrainSenderResults;
                    AudioDevices.onUpdate -= RetryPendingFrequencySetup;
                    _pendingFrequencySetup = false;
                    _pendingFrequencySetupAttempts = 0;
                    CleanupSenderQueues();
                    return;
                }

                micDevice.onSampleReady -= OnMicrophoneData;
                micDevice.onSampleReady += OnMicrophoneData;
                AudioDevices.onUpdate -= DrainSenderResults;
                AudioDevices.onUpdate += DrainSenderResults;

                if (!micDevice.isRecording)
                    StartMicrophone("controller refresh");

                int reportedFrequency = micDevice.frequency;
                _debugSettings = GetComponent<VoiceDebugSettings>();
                if (_debugSettings)
                    reportedFrequency = _debugSettings.ResolveFrequency(micDevice.frequency);

                ApplyMicrophoneFrequency(reportedFrequency);
                return;
            }

            _initializedController = true;
            if (micDevice != null)
            {
                _currentDevice = micDevice.ToString();
                micDevice.onSampleReady += OnMicrophoneData;
                AudioDevices.onUpdate += DrainSenderResults;

                _lastMicCallbackTime = -1;
                StartMicrophone("controller initialization");

                if (_enableLocalPlayback)
                {
                    StartLocalPlayback();
                }

                int reportedFrequency = micDevice.frequency;
                _debugSettings = GetComponent<VoiceDebugSettings>();
                if (_debugSettings)
                    reportedFrequency = _debugSettings.ResolveFrequency(micDevice.frequency);

                ApplyMicrophoneFrequency(reportedFrequency);
            }
            else
            {
                _currentDevice = "No input";
            }
        }

        private bool ApplyMicrophoneFrequency(int reportedFrequency)
        {
            if (!_transport.isController)
            {
                QueueFrequencySetupRetry();
                return false;
            }

            _transport.SetFrequency(reportedFrequency);

            if (_debugSettings)
                _debugSettings.ReportCodecTargetRate(_transport.frequency);

            _pendingFrequencySetup = false;
            _pendingFrequencySetupAttempts = 0;
            AudioDevices.onUpdate -= RetryPendingFrequencySetup;
            return true;
        }

        private void QueueFrequencySetupRetry()
        {
            if (_pendingFrequencySetup)
                return;

            _pendingFrequencySetup = true;
            _pendingFrequencySetupAttempts = 0;
            AudioDevices.onUpdate -= RetryPendingFrequencySetup;
            AudioDevices.onUpdate += RetryPendingFrequencySetup;
        }

        private void RetryPendingFrequencySetup()
        {
            if (!_pendingFrequencySetup)
            {
                AudioDevices.onUpdate -= RetryPendingFrequencySetup;
                return;
            }

            if (!_initializedController || !_isLocalVoiceOwner || micDevice == null)
            {
                _pendingFrequencySetup = false;
                _pendingFrequencySetupAttempts = 0;
                AudioDevices.onUpdate -= RetryPendingFrequencySetup;
                return;
            }

            int reportedFrequency = micDevice.frequency;
            if (_debugSettings)
                reportedFrequency = _debugSettings.ResolveFrequency(micDevice.frequency);

            if (ApplyMicrophoneFrequency(reportedFrequency))
                return;

            _pendingFrequencySetupAttempts++;
            if (_pendingFrequencySetupAttempts == MAX_PENDING_FREQUENCY_SETUP_ATTEMPTS)
                PurrLogger.LogError($"Voice frequency setup has not completed after {MAX_PENDING_FREQUENCY_SETUP_ATTEMPTS} attempts for {name}; still retrying. Current controller: {_transport.owner}, local player: {localPlayer}", this);
        }

        private void Deinitialize()
        {
            StopLocalPlayback();

            if (micDevice != null)
            {
                micDevice.onSampleReady -= OnMicrophoneData;
                micDevice.Stop();
            }

            AudioDevices.onUpdate -= DrainSenderResults;
            AudioDevices.onUpdate -= RetryPendingFrequencySetup;
            _pendingFrequencySetup = false;
            _pendingFrequencySetupAttempts = 0;
            CleanupSenderQueues();

            if(output != null)
                output.Stop();

            _initializedController = false;
            _initializedRemote = false;
        }

        public void ChangeMicrophone(IAudioInputSource mic)
        {
            if (!isController)
                return;

            if (micDevice != null)
            {
                micDevice.onSampleReady -= OnMicrophoneData;
                micDevice.Stop();
            }

            _transport.Stop();

            if (mic == null)
            {
                StopLocalPlayback();
                _inputProvider.ChangeInput(null);
                _currentDevice = "No input";
                AudioDevices.onUpdate -= DrainSenderResults;
                AudioDevices.onUpdate -= RetryPendingFrequencySetup;
                _pendingFrequencySetup = false;
                _pendingFrequencySetupAttempts = 0;
                CleanupSenderQueues();
                return;
            }

            _inputProvider.ChangeInput(mic);
            _currentDevice = micDevice?.ToString();
            if (_localPlaybackActive)
                _localOutputProvider?.SetInput(mic);
            _lastMicCallbackTime = -1;
            if (micDevice != null)
                StartMicrophone("microphone change");
            _transport.SetCodec(_codecSettings ? _codecSettings.CreateCodec() : null);

            int reportedFrequency = mic.frequency;
            if (_debugSettings)
                reportedFrequency = _debugSettings.ResolveFrequency(mic.frequency);

            ApplyMicrophoneFrequency(reportedFrequency);
            _transport.Start();

            if(micDevice != null)
                micDevice.onSampleReady += OnMicrophoneData;
        }

        private bool StartMicrophone(string context)
        {
            if (micDevice == null)
                return false;

            var result = micDevice.Start();
            if (result == StartDeviceResult.Success || result == StartDeviceResult.AlreadyRecording)
                return true;

            PurrLogger.LogWarning($"PurrVoice microphone failed to start during {context}. Result: {result}. Device: {micDevice}", this);
            return false;
        }

        private void SetupRemotePlayback()
        {
            if (_initializedRemote)
                return;

            _initializedRemote = true;
            output?.Start();
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            output?.HandleAudioFilterRead(data, channels);
            _localOutputProvider?.output?.HandleAudioFilterRead(data, channels);
        }

        private ArraySegment<float> ProcessSamples(ArraySegment<float> inputSamples, int frequency, params FilterLevel[] levels)
        {
            if (muted)
            {
                for (int i = 0; i < levels.Length; i++)
                {
                    if (levels[i] == FilterLevel.Receiver)
                        return MuteAudio(inputSamples);
                }
            }

            return DoProcessFilters(inputSamples, frequency, levels);
        }

        private ArraySegment<float> ProcessSamplesLocal(ArraySegment<float> inputSamples, int frequency, params FilterLevel[] levels)
        {
            if (muted)
                return MuteAudio(inputSamples);

            return DoLocalProcessFilters(inputSamples, frequency, levels);
        }

        private ArraySegment<float> MuteAudio(ArraySegment<float> inputSamples)
        {
            for (int i = 0; i < inputSamples.Count; i++)
            {
                inputSamples[i] = 0;
            }

            return inputSamples;
        }

        private void OnMicrophoneData(ArraySegment<float> samples)
        {
            if (!this || !enabled || _senderInputQueue == null)
                return;

            if (muted)
                return;

            double now = Time.realtimeSinceStartupAsDouble;
            if (_lastMicCallbackTime > 0 && now - _lastMicCallbackTime > STALL_DETECT_SECONDS)
            {
                FlushSenderPipelineOnStall();
                _lastMicCallbackTime = now;
                return;
            }
            _lastMicCallbackTime = now;

            DebugMicrophoneDataPreProcessing(samples);

            float[] resampleBuffer = null;
            if (_debugSettings && _debugSettings.needsResample)
            {
                samples = _debugSettings.ResampleMicData(samples, null, out int outCount);
                if (outCount > 0)
                    resampleBuffer = samples.Array;
            }

            if (VoiceThreading.IsMultithreadingSupported)
            {
                int count = samples.Count;
                var buffer = ArrayPool<float>.Shared.Rent(count);
                Array.Copy(samples.Array!, samples.Offset, buffer, 0, count);

                if (resampleBuffer != null)
                    _debugSettings.ReturnResampleBuffer(resampleBuffer);

                _senderInputQueue.Enqueue(new SenderWorkItem
                {
                    buffer = buffer,
                    count = count,
                    frequency = micDevice.frequency
                });
                VoiceThreading.QueueWork(_senderFilterCallback);
            }
            else
            {
                samples = DoProcessFilters(samples, micDevice.frequency, SenderLevels);
                TrackLocalSpeaking(samples);
                onLocalSample?.Invoke(samples);
                _transport.SendAudioChunk(samples);
                DebugMicrophoneDataPostProcessing(samples);

                if (resampleBuffer != null)
                    _debugSettings.ReturnResampleBuffer(resampleBuffer);
            }
        }

        private void FlushSenderPipelineOnStall()
        {
            while (_senderInputQueue.TryDequeue(out var item))
                ArrayPool<float>.Shared.Return(item.buffer);
            while (_senderResultQueue.TryDequeue(out var result))
                ArrayPool<float>.Shared.Return(result.buffer);
            while (_encodedSendQueue.TryDequeue(out var enc))
                ArrayPool<byte>.Shared.Return(enc.buffer);

            _workerResetRequested = 1;
            _workerSuppressor?.Reset();
        }

        private void DrainSenderFilterQueue()
        {
            if (_senderInputQueue == null || _senderResultQueue == null)
                return;

            while (_senderInputQueue.TryDequeue(out var item))
            {
                if (System.Threading.Interlocked.Exchange(ref _workerResetRequested, 0) == 1)
                {
                    _workerBufferPos = 0;
                    _workerResampleCarry = 0;
                }

                var segment = new ArraySegment<float>(item.buffer, 0, item.count);
                segment = DoProcessFilters(segment, item.frequency, SenderLevels);

                var codec = _transport.codec;
                int targetRate = _transport.frequency;
                int micFreq = _transport.micFrequency;
                int chunkSize = _transport.chunkSize;

                if (chunkSize <= 0 || targetRate <= 0)
                {
                    _senderResultQueue.Enqueue(new SenderResult { buffer = item.buffer, count = segment.Count });
                    continue;
                }

                ArraySegment<float> resampled = segment;
                float[] resampleBuf = null;

                if (micFreq > 0 && micFreq != targetRate)
                {
                    double exact = segment.Count * (double)targetRate / micFreq;
                    int outCount = (int)(exact + _workerResampleCarry);
                    _workerResampleCarry += exact - outCount;
                    resampleBuf = ArrayPool<float>.Shared.Rent(outCount);
                    float ratio = micFreq / (float)targetRate;
                    for (int i = 0; i < outCount; i++)
                    {
                        float srcIdx = i * ratio;
                        int s0 = (int)srcIdx;
                        int s1 = Math.Min(s0 + 1, segment.Count - 1);
                        float frac = srcIdx - s0;
                        resampleBuf[i] = segment.Array![segment.Offset + s0] * (1f - frac)
                                       + segment.Array![segment.Offset + s1] * frac;
                    }
                    resampled = new ArraySegment<float>(resampleBuf, 0, outCount);
                }

                if (_workerChunkBuffer == null || _workerChunkBuffer.Length != chunkSize)
                {
                    _workerChunkBuffer = new float[chunkSize];
                    _workerBufferPos = 0;
                }

                if (codec != null && (_workerEncodeBuffer == null || _workerEncodeBuffer.Length < OpusCodec.MaxEncodedBytes))
                    _workerEncodeBuffer = new byte[OpusCodec.MaxEncodedBytes];

                int offset = 0;
                while (offset < resampled.Count)
                {
                    int remaining = chunkSize - _workerBufferPos;
                    int copy = Math.Min(remaining, resampled.Count - offset);
                    if (copy <= 0) break;
                    Array.Copy(resampled.Array!, resampled.Offset + offset, _workerChunkBuffer, _workerBufferPos, copy);
                    _workerBufferPos += copy;
                    offset += copy;

                    if (_workerBufferPos == chunkSize)
                    {
                        if (!_workerSuppressor.ShouldSend(_workerChunkBuffer, 0, chunkSize, out bool resuming))
                        {
                            _workerBufferPos = 0;
                            continue;
                        }

#if UNITY_EDITOR
                        float[] debugBuffer = null;
                        if (ShouldQueueNetworkSentDebug)
                        {
                            debugBuffer = ArrayPool<float>.Shared.Rent(chunkSize);
                            Array.Copy(_workerChunkBuffer, 0, debugBuffer, 0, chunkSize);
                        }
#endif

                        if (codec != null)
                        {
                            int encodedLen;
                            try
                            {
                                if (resuming)
                                    codec.ResetEncoderState();

                                encodedLen = codec.Encode(_workerChunkBuffer, 0, chunkSize, _workerEncodeBuffer);
                                _workerEncodeFailureLogged = false;
                            }
                            catch (Exception e)
                            {
                                if (!_workerEncodeFailureLogged)
                                {
                                    _workerEncodeFailureLogged = true;
                                    Debug.LogException(e);
                                }
#if UNITY_EDITOR
                                if (debugBuffer != null)
                                    ArrayPool<float>.Shared.Return(debugBuffer);
#endif
                                _workerSuppressor.Reset();
                                _workerBufferPos = 0;
                                continue;
                            }

                            var encodedCopy = ArrayPool<byte>.Shared.Rent(encodedLen);
                            Array.Copy(_workerEncodeBuffer, 0, encodedCopy, 0, encodedLen);
                            _encodedSendQueue.Enqueue(new EncodedSendItem
                            {
                                buffer = encodedCopy,
                                length = encodedLen,
                                resume = resuming,
#if UNITY_EDITOR
                                debugBuffer = debugBuffer,
                                debugCount = chunkSize
#endif
                            });
                        }
                        else
                        {
                            int rawLen = chunkSize * sizeof(float);
                            var rawCopy = ArrayPool<byte>.Shared.Rent(rawLen);
                            Buffer.BlockCopy(_workerChunkBuffer, 0, rawCopy, 0, rawLen);
                            _encodedSendQueue.Enqueue(new EncodedSendItem
                            {
                                buffer = rawCopy,
                                length = rawLen,
                                resume = resuming,
#if UNITY_EDITOR
                                debugBuffer = debugBuffer,
                                debugCount = chunkSize
#endif
                            });
                        }

                        _workerBufferPos = 0;
                    }
                }

                if (resampleBuf != null)
                    ArrayPool<float>.Shared.Return(resampleBuf);

                _senderResultQueue.Enqueue(new SenderResult { buffer = item.buffer, count = segment.Count });
            }
        }

        private void DrainSenderResults()
        {
            if (!this || !enabled || _transport == null)
                return;

            if (_senderResultQueue != null)
            {
                while (_senderResultQueue.TryDequeue(out var result))
                {
                    var segment = new ArraySegment<float>(result.buffer, 0, result.count);
                    TrackLocalSpeaking(segment);
                    onLocalSample?.Invoke(segment);
                    DebugMicrophoneDataPostProcessing(segment);
                    ArrayPool<float>.Shared.Return(result.buffer);
                }
            }

            if (_encodedSendQueue != null)
            {
                while (_encodedSendQueue.TryDequeue(out var item))
                {
                    bool sent = item.buffer != null && _transport.SendPreEncoded(item.buffer, 0, item.length, item.resume);

#if UNITY_EDITOR
                    if (item.debugBuffer != null)
                    {
                        if (sent)
                            DebugNetworkSentData(new ArraySegment<float>(item.debugBuffer, 0, item.debugCount));

                        ArrayPool<float>.Shared.Return(item.debugBuffer);
                    }
#endif
                    if (item.buffer != null)
                        ArrayPool<byte>.Shared.Return(item.buffer);
                }
            }
        }

        private void CleanupSenderQueues()
        {
            if (_senderInputQueue != null)
            {
                while (_senderInputQueue.TryDequeue(out var item))
                    ArrayPool<float>.Shared.Return(item.buffer);
            }

            if (_senderResultQueue != null)
            {
                while (_senderResultQueue.TryDequeue(out var result))
                    ArrayPool<float>.Shared.Return(result.buffer);
            }

            if (_encodedSendQueue != null)
            {
                while (_encodedSendQueue.TryDequeue(out var item))
                {
#if UNITY_EDITOR
                    if (item.debugBuffer != null)
                        ArrayPool<float>.Shared.Return(item.debugBuffer);
#endif
                    if (item.buffer != null)
                        ArrayPool<byte>.Shared.Return(item.buffer);
                }
            }

            _workerChunkBuffer = null;
            _workerEncodeBuffer = null;
            _workerBufferPos = 0;
            _workerResampleCarry = 0d;
        }

        private void OnReplayingSample(ArraySegment<float> obj)
        {
            TrackReceivedSpeaking(obj);
            onReceivedSample?.Invoke(obj);
        }

        private void TrackLocalSpeaking(ArraySegment<float> samples)
        {
            _lastLocalSamplePeak = GetPeak(samples);
            _lastLocalSampleTime = Time.realtimeSinceStartupAsDouble;
        }

        private void TrackReceivedSpeaking(ArraySegment<float> samples)
        {
            _lastReceivedSamplePeak = GetPeak(samples);
            _lastReceivedSampleTime = Time.realtimeSinceStartupAsDouble;
        }

        private static float GetPeak(ArraySegment<float> samples)
        {
            if (samples.Array == null || samples.Count <= 0)
                return 0f;

            float peak = 0f;
            int end = samples.Offset + samples.Count;
            for (int i = samples.Offset; i < end; i++)
            {
                float value = samples.Array[i];
                if (value < 0f)
                    value = -value;

                if (value > peak)
                    peak = value;
            }

            return peak;
        }

        private void OnDevicesChanged()
        {
            if (!_isLocalVoiceOwner) return;
            if (!this || !gameObject) return;

            micDevice?.Stop();
            _localOutputProvider?.output?.Stop();
            SetupMicrophone();
        }
    }
}
