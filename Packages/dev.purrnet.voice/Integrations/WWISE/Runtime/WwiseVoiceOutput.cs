using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using PurrNet.Logging;
using UnityEngine;

namespace PurrNet.Voice.WwiseIntegration
{
    public class WwiseVoiceOutput : IVoiceOutput, IDisposable
    {
        public event Action<ArraySegment<float>> onStartPlayingSample;
        public event Action<ArraySegment<float>> onEndPlayingSample;

        public AK.Wwise.Event audioInputEvent;
        public GameObject eventTarget;
        public int outputSampleRate = 48000;
        public int preBufferMs = 120;
        public int bufferCapacityMs = 1000;

        public int frequency => _inputSource?.frequency ?? -1;

        public float volume
        {
            get => _volume;
            set => _volume = Mathf.Clamp(value, 0f, 2f);
        }

        private IAudioInputSource _inputSource;
        private ProcessSamplesDelegate _processSamples;
        private FilterLevel[] _levels;

        private float[] _ringBuffer;
        private int _ringSize;
        private volatile int _ringWritePos;
        private volatile int _ringReadPos;
        private int _desiredLag;

        private uint _playingId;
        private bool _isAttached;
        private bool _isInitialized;
        private bool _eventPosted;
        private bool _postFailed;
        private volatile bool _isReady;
        private volatile bool _shouldStream;
        private float _volume = 1f;
        private float _nextPostRetryTime;
        private float _nextPostLogTime;
        private float _nextLivenessCheckTime;
        private float _nextEndedLogTime;
        private long _postTimestamp;
        private long _lastFillTimestamp;
        private long _fillCallbackCount;
        private int _repostCount;
        private bool _loggedFirstPost;
        private string _lastEndedReason;
        private string _lastPostFailure;

        /// <summary>
        /// Raised (at most once per <see cref="PostLogInterval"/> per output) when the Audio Input event
        /// cannot be posted, after the diagnosis in <see cref="lastPostFailure"/> has been filled in.
        /// Lets the game react, e.g. by re-loading the SoundBank that holds the event.
        /// </summary>
        public static event Action<WwiseVoiceOutput> onPostFailed;

        /// <summary>
        /// Diagnosis of the most recent failed post: whether the sound engine is initialized, whether the event
        /// is in any loaded SoundBank, and the result of re-registering the game object.
        /// </summary>
        public string lastPostFailure => _lastPostFailure;

        private const float PostRetryInterval = 1f;
        private const float PostLogInterval = 10f;
        private const float LivenessCheckInterval = 1f;
        private const float FillTimeoutSeconds = 1.5f;

        /// <summary>
        /// True while an Audio Input event is believed to be playing for this output.
        /// </summary>
        public bool isEventPosted => _eventPosted;

        /// <summary>
        /// Playing id of the current Audio Input event, or AK_INVALID_PLAYING_ID.
        /// </summary>
        public uint playingId => _playingId;

        /// <summary>
        /// Samples currently buffered and waiting for Wwise to pull them.
        /// </summary>
        public int availableSamples => AvailableSamples();

        /// <summary>
        /// Total number of times Wwise pulled samples from this output.
        /// </summary>
        public long fillCallbackCount => Interlocked.Read(ref _fillCallbackCount);

        /// <summary>
        /// Number of times the Audio Input event ended outside PurrVoice and was re-posted.
        /// </summary>
        public int repostCount => _repostCount;

        /// <summary>
        /// Seconds since Wwise last pulled samples, or -1 when it never did.
        /// </summary>
        public double secondsSinceLastFill
        {
            get
            {
                long last = Interlocked.Read(ref _lastFillTimestamp);
                if (last == 0)
                    return -1d;
                return (Stopwatch.GetTimestamp() - last) / (double)Stopwatch.Frequency;
            }
        }

        public override string ToString()
        {
            string target = eventTarget ? eventTarget.name : "<none>";
            return $"WwiseVoiceOutput(target='{target}', attached={_isAttached}, posted={_eventPosted}, playingId={_playingId}, " +
                   $"fills={fillCallbackCount}, lastFill={secondsSinceLastFill:0.00}s, ring={AvailableSamples()}/{_ringSize}, " +
                   $"ready={_isReady}, reposts={_repostCount}, postFailed={_postFailed}, lastEnded='{_lastEndedReason}', lastPostFailure='{_lastPostFailure}')";
        }

        private readonly ConcurrentQueue<FilterWorkItem> _filterInputQueue = new();
        private readonly ConcurrentQueue<PendingWrite> _pendingWrites = new();
        private Action _drainFilterQueueCallback;

        private struct FilterWorkItem
        {
            public float[] buffer;
            public int count;
            public int outRate;
        }

        private struct PendingWrite
        {
            public float[] buffer;
            public int count;
        }

        public void Init(IAudioInputSource inputSource, ProcessSamplesDelegate processSamples = null,
            params FilterLevel[] levels)
        {
            _inputSource = inputSource;
            _processSamples = processSamples;
            _levels = levels;
            _drainFilterQueueCallback = DrainFilterQueue;

            outputSampleRate = Mathf.Max(8000, outputSampleRate);
            _desiredLag = Mathf.Max(1, outputSampleRate * preBufferMs / 1000);
            _ringSize = Mathf.Max(outputSampleRate * bufferCapacityMs / 1000, _desiredLag * 2);
            _ringSize = Mathf.Max(_ringSize, outputSampleRate);
            _ringBuffer = new float[_ringSize];

            _isInitialized = true;
            if (NetworkManager.main)
                NetworkManager.main.onTick += OnTick;
        }

        public void Start()
        {
            if (!_isInitialized || _inputSource == null)
                return;

            if (!_inputSource.isRecording && _inputSource.Start() != StartDeviceResult.Success)
                return;

            AttachInput();
        }

        public void Stop()
        {
            if (_inputSource == null)
                return;

            DetachInput();
            if (_inputSource.isRecording)
                _inputSource.Stop();
        }

        public void AttachInput()
        {
            if (!_isInitialized || _inputSource == null || _isAttached)
                return;

            _isAttached = true;
            _shouldStream = true;
            _isReady = false;
            _eventPosted = false;
            _postFailed = false;
            _nextPostRetryTime = 0f;
            _nextPostLogTime = 0f;
            _ringWritePos = 0;
            _ringReadPos = 0;

            _inputSource.onSampleReady += OnSampleReady;
        }

        public void DetachInput()
        {
            if (!_isAttached)
                return;

            _isAttached = false;
            _shouldStream = false;

            if (_inputSource != null)
                _inputSource.onSampleReady -= OnSampleReady;

            if (_playingId != 0 && _playingId != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
            {
                AkUnitySoundEngine.StopPlayingID(_playingId);
                PurrVoiceWwiseAudioInputManager.Remove(_playingId);
            }

            _playingId = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
            _eventPosted = false;
            _postFailed = false;
            _isReady = false;
            _nextPostRetryTime = 0f;
            _nextPostLogTime = 0f;
            _postTimestamp = 0;
            Interlocked.Exchange(ref _lastFillTimestamp, 0);
            ClearQueues();
        }

        public void SetInput(IAudioInputSource input)
        {
            if (input == null)
                return;

            var wasAttached = _isAttached;
            if (wasAttached)
                DetachInput();

            if (_inputSource != null && _inputSource.isRecording)
                _inputSource.Stop();

            _inputSource = input;

            if (wasAttached)
            {
                if (!_inputSource.isRecording)
                    _inputSource.Start();
                AttachInput();
            }
        }

        public void HandleAudioFilterRead(float[] data, int channels)
        {
        }

        public void Dispose()
        {
            Stop();
            if (_isInitialized && NetworkManager.main)
                NetworkManager.main.onTick -= OnTick;

            _isInitialized = false;
            ClearQueues();
        }

        public void UpdateTracking()
        {
            if (!_isAttached || !eventTarget)
                return;

            var targetTransform = eventTarget.transform;
            AkUnitySoundEngine.SetObjectPosition(eventTarget, targetTransform);
        }

        private void OnSampleReady(ArraySegment<float> data)
        {
            if (!_isAttached)
                return;

            if (VoiceThreading.IsMultithreadingSupported)
            {
                int count = data.Count;
                var buffer = ArrayPool<float>.Shared.Rent(count);
                Array.Copy(data.Array!, data.Offset, buffer, 0, count);

                _filterInputQueue.Enqueue(new FilterWorkItem
                {
                    buffer = buffer,
                    count = count,
                    outRate = outputSampleRate
                });
                VoiceThreading.QueueWork(_drainFilterQueueCallback);
            }
            else
            {
                onStartPlayingSample?.Invoke(data);
                if (_processSamples != null)
                    data = _processSamples(data, frequency, _levels);
                WriteToRingBuffer(data, frequency);
                VoicePlaybackMonitor.ReportPlayback(data);
                onEndPlayingSample?.Invoke(data);
                TryPostEvent();
            }
        }

        private void OnTick(bool asServer)
        {
            DrainPendingWrites();
            CheckEventLiveness();
            TryPostEvent();
        }

        // Wwise can end the Audio Input event on its own (virtual-voice kill below the volume threshold,
        // playback limits, StopAll, game object unregistration) and only AkAudioInputManager sees the
        // end-of-event callback, so a posted event has to be polled or it stays "posted" and silent forever.
        private void CheckEventLiveness()
        {
            if (!_isAttached || !_eventPosted)
                return;

            float now = Time.unscaledTime;
            if (now < _nextLivenessCheckTime)
                return;

            _nextLivenessCheckTime = now + LivenessCheckInterval;

            long lastActivity = Math.Max(Interlocked.Read(ref _lastFillTimestamp), _postTimestamp);
            double idleSeconds = (Stopwatch.GetTimestamp() - lastActivity) / (double)Stopwatch.Frequency;
            if (idleSeconds < FillTimeoutSeconds)
                return;

            if (IsPlayingIdAlive(_playingId))
                return;

            MarkEventEnded($"no samples pulled for {idleSeconds:0.0} s and playing id {_playingId} is gone from the sound engine");
        }

        private static bool IsPlayingIdAlive(uint id)
        {
            if (id == 0 || id == AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
                return false;

            return AkUnitySoundEngine.GetGameObjectFromPlayingID(id) != AkUnitySoundEngine.AK_INVALID_GAME_OBJECT;
        }

        private void MarkEventEnded(string reason)
        {
            if (_playingId != 0 && _playingId != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
            {
                AkUnitySoundEngine.StopPlayingID(_playingId);
                PurrVoiceWwiseAudioInputManager.Remove(_playingId);
            }

            _playingId = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
            _eventPosted = false;
            _isReady = false;
            _postTimestamp = 0;
            _nextPostRetryTime = 0f;
            _repostCount++;
            _lastEndedReason = reason;

            float now = Time.unscaledTime;
            if (now >= _nextEndedLogTime)
            {
                _nextEndedLogTime = now + PostLogInterval;
                string eventName = audioInputEvent != null ? audioInputEvent.Name : "<none>";
                string target = eventTarget ? eventTarget.name : "<none>";
                PurrLogger.Log($"[PurrVoice] Wwise ended voice event '{eventName}' on '{target}' outside PurrVoice ({reason}); re-posting (repost #{_repostCount}).", eventTarget);
            }
        }

        private void DrainFilterQueue()
        {
            int inRate = frequency;
            while (_filterInputQueue.TryDequeue(out var item))
            {
                var segment = new ArraySegment<float>(item.buffer, 0, item.count);
                if (_processSamples != null)
                    segment = _processSamples(segment, inRate, _levels);

                if (inRate > 0 && inRate != item.outRate)
                {
                    int outCount = (int)Math.Ceiling(segment.Count * (double)item.outRate / inRate);
                    var resampled = ArrayPool<float>.Shared.Rent(outCount);
                    Resample(segment, resampled, outCount, inRate, item.outRate);
                    ArrayPool<float>.Shared.Return(item.buffer);
                    _pendingWrites.Enqueue(new PendingWrite { buffer = resampled, count = outCount });
                }
                else
                {
                    _pendingWrites.Enqueue(new PendingWrite { buffer = item.buffer, count = segment.Count });
                }
            }
        }

        private void DrainPendingWrites()
        {
            while (_pendingWrites.TryDequeue(out var pending))
            {
                var segment = new ArraySegment<float>(pending.buffer, 0, pending.count);
                onStartPlayingSample?.Invoke(segment);
                VoicePlaybackMonitor.ReportPlayback(segment);
                WriteRaw(pending.buffer, 0, pending.count);
                onEndPlayingSample?.Invoke(segment);
                ArrayPool<float>.Shared.Return(pending.buffer);
            }
        }

        private void WriteToRingBuffer(ArraySegment<float> data, int inRate)
        {
            if (inRate > 0 && inRate != outputSampleRate)
            {
                int outCount = (int)Math.Ceiling(data.Count * (double)outputSampleRate / inRate);
                var tmp = ArrayPool<float>.Shared.Rent(outCount);
                try
                {
                    Resample(data, tmp, outCount, inRate, outputSampleRate);
                    WriteRaw(tmp, 0, outCount);
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(tmp);
                }
            }
            else
            {
                WriteRaw(data.Array!, data.Offset, data.Count);
            }
        }

        private static void Resample(ArraySegment<float> data, float[] output, int outputCount, int inputRate, int outputRate)
        {
            float ratio = inputRate / (float)outputRate;
            var source = data.Array;
            int offset = data.Offset;
            int length = data.Count;

            for (int i = 0; i < outputCount; i++)
            {
                float t = i * ratio;
                int t0 = (int)t;
                int t1 = t0 + 1;
                if (t1 >= length)
                    t1 = length - 1;

                float frac = t - t0;
                output[i] = source![offset + t0] + (source[offset + t1] - source[offset + t0]) * frac;
            }
        }

        private void WriteRaw(float[] buffer, int offset, int count)
        {
            if (count <= 0 || _ringBuffer == null)
                return;

            if (count >= _ringSize)
            {
                offset += count - (_ringSize - 1);
                count = _ringSize - 1;
            }

            int read = _ringReadPos;
            int write = _ringWritePos;
            int available = (write - read + _ringSize) % _ringSize;
            int free = _ringSize - available - 1;
            if (count > free)
                _ringReadPos = (read + count - free) % _ringSize;

            int firstChunk = Math.Min(count, _ringSize - write);
            Array.Copy(buffer, offset, _ringBuffer, write, firstChunk);

            int secondChunk = count - firstChunk;
            if (secondChunk > 0)
                Array.Copy(buffer, offset + firstChunk, _ringBuffer, 0, secondChunk);

            System.Threading.Thread.MemoryBarrier();
            _ringWritePos = (write + count) % _ringSize;
        }

        private int AvailableSamples()
        {
            if (_ringBuffer == null)
                return 0;

            int read = _ringReadPos;
            int write = _ringWritePos;
            return (write - read + _ringSize) % _ringSize;
        }

        private void TryPostEvent()
        {
            if (!_isAttached || _eventPosted || _postFailed || !eventTarget || audioInputEvent == null)
                return;

            if (AvailableSamples() < _desiredLag)
                return;

            float now = Time.unscaledTime;
            if (now < _nextPostRetryTime)
                return;

            _nextPostRetryTime = now + PostRetryInterval;

            try
            {
                _playingId = PurrVoiceWwiseAudioInputManager.Post(audioInputEvent.Id, eventTarget, FillAudioInput, SetAudioFormat);
                if (_playingId != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
                {
                    _eventPosted = true;
                    _nextPostRetryTime = 0f;
                    _nextPostLogTime = 0f;
                    _postTimestamp = Stopwatch.GetTimestamp();
                    _nextLivenessCheckTime = now + LivenessCheckInterval;

                    if (!_loggedFirstPost)
                    {
                        _loggedFirstPost = true;
                        PurrLogger.Log($"[PurrVoice] Wwise voice output posted '{audioInputEvent.Name}' on '{eventTarget.name}' (playing id {_playingId}, {outputSampleRate} Hz, pre-buffer {preBufferMs} ms).", eventTarget);
                    }
                    return;
                }

                var registerResult = RepairEventTargetRegistration();

                if (now >= _nextPostLogTime)
                {
                    _nextPostLogTime = now + PostLogInterval;
                    _lastPostFailure = DescribePostFailure(audioInputEvent.Id, registerResult);
                    PurrLogger.LogWarning($"Wwise voice output could not post Audio Input Event '{audioInputEvent.Name}' ({audioInputEvent.Id}) on '{eventTarget.name}': {_lastPostFailure}. Retrying every {PostRetryInterval:0} s.", eventTarget);
                    onPostFailed?.Invoke(this);
                }
            }
            catch (Exception e)
            {
                DetachInput();
                _postFailed = true;
                PurrLogger.LogError($"Wwise voice output failed to post audio input and has been disabled. {e.GetType().Name}: {e.Message}");
            }
        }

        // A failed PostEvent usually means the target is unknown to the sound engine while a stale
        // AkGameObj flag claims it is registered; re-registering makes the retry able to succeed.
        private AKRESULT RepairEventTargetRegistration()
        {
            if (!eventTarget)
                return AKRESULT.AK_Fail;

            if (eventTarget.TryGetComponent<AkGameObj>(out var akGameObj))
            {
                akGameObj.Unregister();
                return akGameObj.Register();
            }

            return AkUnitySoundEngine.RegisterGameObj(eventTarget, eventTarget.name);
        }

        private static string DescribePostFailure(uint eventId, AKRESULT registerResult)
        {
            if (!AkUnitySoundEngine.IsInitialized())
                return "sound engine not initialized";

            uint count = 1;
            AKRESULT query;
            using (var infos = new AkObjectInfoArray(1))
                query = AkUnitySoundEngine.QueryAudioObjectIDs(eventId, ref count, infos);

            string eventState = query == AKRESULT.AK_IDNotFound
                ? "the event is in NO loaded SoundBank (was its bank unloaded?)"
                : $"the event is known to the engine (query={query})";

            return $"{eventState}; game object re-registration => {registerResult}";
        }

        private bool FillAudioInput(uint playingId, uint channelIndex, float[] samples)
        {
            Interlocked.Exchange(ref _lastFillTimestamp, Stopwatch.GetTimestamp());
            Interlocked.Increment(ref _fillCallbackCount);

            if (!_shouldStream || _ringBuffer == null)
            {
                Array.Clear(samples, 0, samples.Length);
                return true;
            }

            int read = _ringReadPos;
            int write = _ringWritePos;
            int available = (write - read + _ringSize) % _ringSize;

            if (!_isReady)
            {
                if (available < _desiredLag)
                {
                    Array.Clear(samples, 0, samples.Length);
                    return true;
                }

                _isReady = true;
            }

            int toRead = Math.Min(samples.Length, available);
            if (toRead > 0)
            {
                int firstChunk = Math.Min(toRead, _ringSize - read);
                CopyWithVolume(_ringBuffer, read, samples, 0, firstChunk);

                int secondChunk = toRead - firstChunk;
                if (secondChunk > 0)
                    CopyWithVolume(_ringBuffer, 0, samples, firstChunk, secondChunk);

                _ringReadPos = (read + toRead) % _ringSize;
            }

            if (toRead < samples.Length)
            {
                if (toRead > 0)
                {
                    const int fadeOut = 32;
                    int fadeLength = Math.Min(fadeOut, toRead);
                    for (int i = 0; i < fadeLength; i++)
                        samples[toRead - fadeLength + i] *= (fadeLength - i) / (float)(fadeLength + 1);
                }

                Array.Clear(samples, toRead, samples.Length - toRead);
                _isReady = false;
            }

            return true;
        }

        private void CopyWithVolume(float[] source, int sourceOffset, float[] destination, int destinationOffset, int count)
        {
            var gain = _volume;
            for (int i = 0; i < count; i++)
                destination[destinationOffset + i] = source[sourceOffset + i] * gain;
        }

        private void SetAudioFormat(uint playingId, AkAudioFormat format)
        {
            using var channelConfig = AkChannelConfig.Standard((uint)AkUnitySoundEngine.AK_SPEAKER_SETUP_MONO);
            format.SetAll((uint)outputSampleRate, channelConfig, 32, 4,
                AkUnitySoundEngine.AK_FLOAT, AkUnitySoundEngine.AK_NONINTERLEAVED);
        }

        private void ClearQueues()
        {
            while (_filterInputQueue.TryDequeue(out var item))
                ArrayPool<float>.Shared.Return(item.buffer);
            while (_pendingWrites.TryDequeue(out var pending))
                ArrayPool<float>.Shared.Return(pending.buffer);
        }
    }
}
