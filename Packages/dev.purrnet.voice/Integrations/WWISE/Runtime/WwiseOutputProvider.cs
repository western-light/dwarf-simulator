using PurrNet.Logging;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet.Voice.WwiseIntegration
{
    public class WwiseOutputProvider : OutputProvider
    {
        [Tooltip("Wwise Event using an Audio Input source.")]
        [SerializeField] private AK.Wwise.Event _audioInputEvent;

        [Tooltip("Sample rate sent to Wwise.")]
        [SerializeField, PurrLock, Min(8000)]
        private int _sampleRate = 48000;

        [Tooltip("Amount of audio to buffer before playback starts, in milliseconds.")]
        [SerializeField, PurrLock, Range(20, 1000)]
        private int _preBufferMs = 250;

        [Tooltip("Total pre-allocated buffer capacity in milliseconds.")]
        [SerializeField, PurrLock, Range(250, 5000)]
        private int _bufferCapacityMs = 1000;

        [Tooltip("Linear gain applied before samples enter Wwise.")]
        [SerializeField, Range(0f, 2f)]
        private float _volume = 1f;

        [Tooltip("GameObject used for the Wwise event position. Defaults to this GameObject.")]
        [SerializeField]
        private Transform _trackingTransform;

        [Tooltip("Adds AkGameObj automatically when missing.")]
        [SerializeField]
        private bool _ensureAkGameObject = true;

        private WwiseVoiceOutput _output;

        public override IVoiceOutput output => _output;

        public float volume
        {
            get => _output?.volume ?? _volume;
            set
            {
                _volume = Mathf.Clamp(value, 0f, 2f);
                if (_output != null)
                    _output.volume = _volume;
            }
        }

        public Transform trackingTransform
        {
            get => _trackingTransform;
            set
            {
                _trackingTransform = value;
                if (_output != null)
                    _output.eventTarget = value ? value.gameObject : gameObject;
            }
        }

        public override void Init(IAudioInputSource inputSource, ProcessSamplesDelegate processSamples = null,
            params FilterLevel[] levels)
        {
            if (_audioInputEvent == null || !_audioInputEvent.IsValid())
            {
                PurrLogger.LogError("WwiseOutputProvider has no Audio Input Event assigned. Audio will be received but not played.", this);
                return;
            }

            var eventTarget = _trackingTransform ? _trackingTransform.gameObject : gameObject;
            if (_ensureAkGameObject && !eventTarget.TryGetComponent<AkGameObj>(out _))
                eventTarget.AddComponent<AkGameObj>();

            _output = new WwiseVoiceOutput
            {
                audioInputEvent = _audioInputEvent,
                eventTarget = eventTarget,
                outputSampleRate = _sampleRate,
                preBufferMs = _preBufferMs,
                bufferCapacityMs = _bufferCapacityMs,
                volume = _volume
            };

            _output.Init(inputSource, processSamples, levels);
            isInitialized = true;
        }

        public override void SetInput(IAudioInputSource input)
        {
            _output?.SetInput(input);
        }

        private void OnDestroy()
        {
            _output?.Dispose();
        }

        private void LateUpdate()
        {
            _output?.UpdateTracking();
        }

#if UNITY_EDITOR
        private void Reset()
        {
            _trackingTransform = transform;
        }
#endif
    }
}
