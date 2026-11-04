using System;
using UnityEngine;

namespace PurrNet.Voice
{
    public sealed class NativeInputDevice : IAudioInputSource
    {
        private const int DefaultSampleRate = 48000;
        private const int DefaultBufferMilliseconds = 1000;
        private const int MinReadBufferSize = 1024;
        private const double NoSamplesWarningDelay = 2d;
        private const double RepairStartDelay = 3d;
        private const double RepairRetryDelay = 3d;
        private const double RepairRetryDelayMax = 15d;
        private const int RepairAttemptsBeforeFallback = 3;
        private const int FullResetAttemptInterval = 5;

        private int _sampleRate = DefaultSampleRate;
        private int _bufferMilliseconds = DefaultBufferMilliseconds;
        private float[] _readBuffer = new float[DefaultSampleRate / 50 * 3];
        private bool _isRecording;
        private int _startCount;
        private double _recordingStartedAt;
        private double _lastSampleAt;
        private double _nextRepairAt;
        private int _repairAttempts;
        private bool _usingFallbackDevice;
        private bool _loggedNoSamples;
        private bool _loggedRepairResult;
        private bool _loggedDeadMicrophone;
        private double _repairStartedAt;
        private static NativeInputDevice _activeDevice;

        /// <summary>
        /// Native device id used when starting capture.
        /// </summary>
        public string id { get; }

        /// <summary>
        /// Display name reported by the native audio backend.
        /// </summary>
        public string name { get; }

        /// <summary>
        /// True if the native backend marked this as the default capture device.
        /// </summary>
        public bool isDefault { get; }

        public int frequency => _sampleRate;
        public bool isRecording => _isRecording;
        public event Action<ArraySegment<float>> onSampleReady;

        internal NativeInputDevice(string id, string name, bool isDefault)
        {
            this.id = id;
            this.name = name;
            this.isDefault = isDefault;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _activeDevice = null;
        }

        /// <summary>
        /// Sets capture settings used the next time this device starts.
        /// </summary>
        public void Configure(int sampleRate, int bufferMilliseconds)
        {
            _sampleRate = Math.Max(8000, sampleRate);
            _bufferMilliseconds = Math.Max(100, bufferMilliseconds);
        }

        public StartDeviceResult Start()
        {
            if (_isRecording)
            {
                _startCount++;
                return StartDeviceResult.AlreadyRecording;
            }

            if (_activeDevice != null && _activeDevice != this && _activeDevice._isRecording)
            {
                if (IsNativeCapturing())
                    return StartDeviceResult.AlreadyRecording;

                _activeDevice.ForceStopManaged();
            }

            int result;
            try
            {
                result = NativeAudioDevices.NativeAudioPlugin.StartCapture(id, _sampleRate, _bufferMilliseconds);
            }
            catch (DllNotFoundException)
            {
                return StartDeviceResult.DeviceNotFound;
            }
            catch (EntryPointNotFoundException)
            {
                return StartDeviceResult.DeviceNotFound;
            }

            if (result == 0)
            {
                StartManagedRecording();
                return StartDeviceResult.Success;
            }

            if (result == -4)
            {
                if (_activeDevice != null && _activeDevice != this && _activeDevice._isRecording)
                    return StartDeviceResult.AlreadyRecording;

                NativeAudioDevices.NativeAudioPlugin.StopCapture();
                result = NativeAudioDevices.NativeAudioPlugin.StartCapture(id, _sampleRate, _bufferMilliseconds);
                if (result == 0)
                {
                    StartManagedRecording();
                    return StartDeviceResult.Success;
                }

                return StartDeviceResult.AlreadyRecording;
            }

            return StartDeviceResult.DeviceNotFound;
        }

        public void Stop()
        {
            if (!_isRecording)
                return;

            if (_startCount > 1)
            {
                _startCount--;
                return;
            }

            _startCount = 0;
            AudioDevices.onUpdate -= Update;
            if (_activeDevice == this)
            {
                NativeAudioDevices.NativeAudioPlugin.StopCapture();
                _activeDevice = null;
            }

            _isRecording = false;
            _usingFallbackDevice = false;
        }

        public override string ToString()
        {
            return name;
        }

        private void Update()
        {
            if (!_isRecording)
                return;

            int available = NativeAudioDevices.NativeAudioPlugin.GetAvailableSamples();
            if (available <= 0)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                double silentSince = Math.Max(_recordingStartedAt, _lastSampleAt);

                if (!_loggedNoSamples && now - silentSince >= NoSamplesWarningDelay)
                {
                    _loggedNoSamples = true;
                    Debug.Log($"[PurrVoice] Native microphone '{name}' is not producing samples. Native capturing: {IsNativeCapturing()}. Native error: {NativeAudioDevices.lastError}. Attempting capture restarts.");
                }

                if (_activeDevice == this && now - silentSince >= RepairStartDelay && now >= _nextRepairAt)
                    AttemptCaptureRepair(now);

                return;
            }

            if (_readBuffer.Length < available)
                _readBuffer = new float[Math.Max(available, MinReadBufferSize)];

            int read = NativeAudioDevices.NativeAudioPlugin.ReadSamples(_readBuffer, available);
            if (read <= 0)
                return;

            _lastSampleAt = Time.realtimeSinceStartupAsDouble;
            if (_loggedNoSamples)
            {
                _loggedNoSamples = false;
                Debug.Log($"[PurrVoice] Native microphone '{name}' is delivering samples again after {_repairAttempts} restart attempt(s){(_usingFallbackDevice ? " (running on the default capture device)" : "")}.");
                _repairAttempts = 0;
                _loggedRepairResult = false;
                _loggedDeadMicrophone = false;
            }

            onSampleReady?.Invoke(new ArraySegment<float>(_readBuffer, 0, read));
        }

        private void AttemptCaptureRepair(double now)
        {
            if (_repairAttempts == 0)
                _repairStartedAt = now;

            _repairAttempts++;
            _nextRepairAt = now + Math.Min(RepairRetryDelay * _repairAttempts, RepairRetryDelayMax);

            bool useFallback = _repairAttempts > RepairAttemptsBeforeFallback;
            bool fullReset = _repairAttempts >= FullResetAttemptInterval && _repairAttempts % FullResetAttemptInterval == 0;

            try
            {
                if (fullReset)
                {
                    NativeAudioDevices.Shutdown();
                    NativeAudioDevices.Refresh();
                }
                else
                {
                    NativeAudioDevices.NativeAudioPlugin.StopCapture();
                    NativeAudioDevices.NativeAudioPlugin.RefreshDevices();
                }

                int result = NativeAudioDevices.NativeAudioPlugin.StartCapture(useFallback ? "" : id, _sampleRate, _bufferMilliseconds);
                if (result != 0 && !useFallback)
                {
                    useFallback = true;
                    result = NativeAudioDevices.NativeAudioPlugin.StartCapture("", _sampleRate, _bufferMilliseconds);
                }

                if (result == 0)
                {
                    _usingFallbackDevice = useFallback;
                    _recordingStartedAt = now;
                }

                if (!_loggedRepairResult)
                {
                    _loggedRepairResult = true;
                    Debug.Log(result == 0
                        ? $"[PurrVoice] Restarted native microphone capture for '{name}' (attempt {_repairAttempts}{(useFallback ? ", fell back to the default capture device" : "")}). Further restarts stay silent until samples arrive."
                        : $"[PurrVoice] Failed to restart native microphone '{name}' (attempt {_repairAttempts}, result {result}, default {isDefault}). Native error: {NativeAudioDevices.lastError}. Retrying silently until samples arrive.");
                }

                if (_loggedDeadMicrophone || _repairAttempts < FullResetAttemptInterval)
                    return;

                _loggedDeadMicrophone = true;
                Debug.LogError($"[PurrVoice] Native microphone '{name}' has produced no samples for {now - _repairStartedAt + RepairStartDelay:0}s despite {_repairAttempts} capture restarts (last result {result}, default {isDefault}). Native error: {NativeAudioDevices.lastError}.");
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        private static bool IsNativeCapturing()
        {
            try
            {
                return NativeAudioDevices.NativeAudioPlugin.IsCapturing() == 1;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        private void ForceStopManaged()
        {
            _startCount = 0;
            _isRecording = false;
            _usingFallbackDevice = false;
            AudioDevices.onUpdate -= Update;
            if (_activeDevice == this)
                _activeDevice = null;
        }

        private void StartManagedRecording()
        {
            _startCount = 1;
            _isRecording = true;
            _activeDevice = this;
            _recordingStartedAt = Time.realtimeSinceStartupAsDouble;
            _lastSampleAt = 0d;
            _nextRepairAt = 0d;
            _repairAttempts = 0;
            _usingFallbackDevice = false;
            _loggedNoSamples = false;
            _loggedRepairResult = false;
            _loggedDeadMicrophone = false;
            AudioDevices.onUpdate -= Update;
            AudioDevices.onUpdate += Update;
        }
    }
}
