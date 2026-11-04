using System;
using System.Collections.Generic;
using UnityEngine;

namespace PurrNet.Voice
{
    public class NativeInputProvider : InputProvider
    {
        [Tooltip("Native microphone device id. Empty uses the default device.")]
        [SerializeField] private string _deviceId;

        [Tooltip("Capture sample rate requested from the native backend.")]
        [SerializeField, Min(8000)] private int _sampleRate = 48000;

        [Tooltip("Native capture ring buffer capacity in milliseconds.")]
        [SerializeField, Min(100)] private int _bufferCapacityMs = 1000;

        private NativeInputDevice _device;

        public override IAudioInputSource input => _device;
        public override event Action onDeviceChanged;

        /// <summary>
        /// Available native microphone devices from the latest refresh.
        /// </summary>
        public IReadOnlyList<NativeInputDevice> availableDevices => NativeAudioDevices.devices;

        /// <summary>
        /// Native microphone device id used by this provider.
        /// </summary>
        public string deviceId
        {
            get => _deviceId;
            set
            {
                if (_deviceId == value)
                    return;

                _deviceId = value;
                SelectConfiguredDevice();
            }
        }

        public override void Init(PurrVoicePlayer purrVoicePlayer)
        {
            RefreshDevices();
        }

        public override void Cleanup()
        {
            _device?.Stop();
        }

        public override void ChangeInput(IAudioInputSource newInput)
        {
            if (_device == newInput)
                return;

            Cleanup();

            if (newInput == null)
            {
                _device = null;
                onDeviceChanged?.Invoke();
                return;
            }

            if (newInput is NativeInputDevice nativeDevice)
            {
                _device = nativeDevice;
                _deviceId = nativeDevice.id;
                _device.Configure(_sampleRate, _bufferCapacityMs);
                onDeviceChanged?.Invoke();
            }
        }

        /// <summary>
        /// Refreshes native microphone devices and selects the configured device.
        /// </summary>
        public bool RefreshDevices()
        {
            if (!NativeAudioDevices.Refresh())
                return false;

            SelectConfiguredDevice();
            return _device != null;
        }

        private void SelectConfiguredDevice()
        {
            var nextDevice = NativeAudioDevices.GetDevice(_deviceId) ?? NativeAudioDevices.defaultDevice;
            if (nextDevice == null)
            {
                if (_device != null)
                    _device.Stop();

                _device = null;
                onDeviceChanged?.Invoke();
                return;
            }

            bool wasRecording = _device != null && _device.isRecording;
            if (_device != null && _device != nextDevice)
                _device.Stop();

            _device = nextDevice;
            _deviceId = _device.id;
            _device.Configure(_sampleRate, _bufferCapacityMs);

            if (wasRecording)
                _device.Start();

            onDeviceChanged?.Invoke();
        }
    }
}
