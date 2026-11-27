using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DwarfSimulator.Car
{
    public class DriverVision : MonoBehaviour
    {
        private const float PulseSpeed = 2.2f;

        private static DriverVision _instance;

        private bool _driving;
        private float _level;
        private float _blackoutSeconds = 90f;
        private float _recoverySeconds = 30f;
        private VolumeProfile _profile;
        private Volume _volume;
        private Vignette _vignette;
        private ColorAdjustments _colorAdjustments;
        private DepthOfField _depthOfField;

        public static float Level => _instance != null ? _instance._level : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        public static void SetDriving(bool driving, float blackoutSeconds, float recoverySeconds)
        {
            if (_instance == null)
            {
                if (!driving)
                    return;
                _instance = new GameObject("DriverVision").AddComponent<DriverVision>();
            }

            _instance.enabled = true;
            _instance._driving = driving;
            _instance._blackoutSeconds = Mathf.Max(0.1f, blackoutSeconds);
            _instance._recoverySeconds = Mathf.Max(0.1f, recoverySeconds);
        }

        private void Awake()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _vignette = _profile.Add<Vignette>(false);
            _colorAdjustments = _profile.Add<ColorAdjustments>(false);
            _depthOfField = _profile.Add<DepthOfField>(false);

            _vignette.color.Override(Color.black);
            _vignette.center.Override(new Vector2(0.5f, 0.5f));
            _vignette.smoothness.Override(1f);
            _vignette.rounded.Override(false);
            _depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
            _depthOfField.gaussianStart.Override(0f);

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            _volume.sharedProfile = _profile;
            _volume.weight = 0f;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            if (_profile != null)
                Destroy(_profile);
        }

        private void Update()
        {
            float seconds = _driving ? _blackoutSeconds : _recoverySeconds;
            float direction = _driving ? 1f : -1f;
            _level = Mathf.Clamp01(_level + direction * Time.deltaTime / seconds);

            if (_level <= 0f)
            {
                _volume.weight = 0f;
                if (!_driving)
                    enabled = false;
                return;
            }

            ApplyEffects();
        }

        private void OnEnable()
        {
            if (_volume != null)
                _volume.weight = 0f;
        }

        private void ApplyEffects()
        {
            float level = _level;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * PulseSpeed);
            float pulseAmount = level * 0.06f * pulse;
            float fullBlack = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, level));

            _volume.weight = 1f;
            _vignette.intensity.Override(Mathf.Clamp01(level * 0.9f + pulseAmount));
            _colorAdjustments.postExposure.Override(-7f * level);
            _colorAdjustments.saturation.Override(-60f * level);
            _colorAdjustments.colorFilter.Override(Color.Lerp(Color.white, Color.black, fullBlack));
            _depthOfField.gaussianEnd.Override(Mathf.Lerp(60f, 0.1f, level));
            _depthOfField.gaussianMaxRadius.Override(Mathf.Lerp(0.5f, 1.5f, level));
        }
    }
}
