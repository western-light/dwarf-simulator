using System;
using UnityEngine;

namespace PurrNet.Voice
{
    public partial class PurrVoicePlayer
    {
#if UNITY_EDITOR
        [HideInInspector] public AudioVisualizer micInputVisualizer;
        [HideInInspector] public AudioVisualizer senderProcessedVisualizer;
        [HideInInspector] public AudioVisualizer networkSentVisualizer;
        [HideInInspector] public AudioVisualizer receivedVisualizer;
        [HideInInspector] public AudioVisualizer streamedAudioVisualizerStart;
        [HideInInspector] public AudioVisualizer streamedAudioVisualizerEnd;

        public static int activeDebugWindows;

        public bool enableDebugVisualization => TryResolveDebugVisualization();
        private PurrVoicePlayerDebug _purrVoicePlayerDebug;
        private volatile bool _debugVisualizationReady;

        private bool ShouldDebug => activeDebugWindows > 0 && enableDebugVisualization;
        private bool ShouldQueueNetworkSentDebug => activeDebugWindows > 0 && _debugVisualizationReady;
#endif

        private void ClearDebugVisualization()
        {
#if UNITY_EDITOR
            micInputVisualizer?.Clear();
            senderProcessedVisualizer?.Clear();
            networkSentVisualizer?.Clear();
            receivedVisualizer?.Clear();
            streamedAudioVisualizerStart?.Clear();
            streamedAudioVisualizerEnd?.Clear();
            _micVisualizer?.Clear();
            _networkVisualizer?.Clear();
#endif
        }

        private void DebugAwake(int frequency)
        {
#if UNITY_EDITOR
            if (!TryResolveDebugVisualization())
            {
                _debugVisualizationReady = false;
                return;
            }

            _debugVisualizationReady = true;

            int inputFrequency = micDevice?.frequency > 0 ? micDevice.frequency : frequency;
            micInputVisualizer = new AudioVisualizer(_purrVoicePlayerDebug.timeWindow, inputFrequency);
            senderProcessedVisualizer = new AudioVisualizer(_purrVoicePlayerDebug.timeWindow, inputFrequency);
            networkSentVisualizer = new AudioVisualizer(_purrVoicePlayerDebug.timeWindow, frequency);
            receivedVisualizer = new AudioVisualizer(_purrVoicePlayerDebug.timeWindow, frequency);
            int outputFrequency = ResolveDebugOutputFrequency(frequency);
            streamedAudioVisualizerStart = new AudioVisualizer(_purrVoicePlayerDebug.timeWindow, outputFrequency);
            streamedAudioVisualizerEnd = new AudioVisualizer(_purrVoicePlayerDebug.timeWindow, outputFrequency);

            if (output != null)
            {
                output.onStartPlayingSample -= DebugStreamedAudio;
                output.onEndPlayingSample -= DebugStreamedAudioEnd;
                output.onStartPlayingSample += DebugStreamedAudio;
                output.onEndPlayingSample += DebugStreamedAudioEnd;
            }
#endif
        }

        private bool TryResolveDebugVisualization()
        {
#if UNITY_EDITOR
            if (_purrVoicePlayerDebug)
                return true;

            TryGetComponent(out _purrVoicePlayerDebug);
            return _purrVoicePlayerDebug;
#else
            return false;
#endif
        }

        private static int ResolveDebugOutputFrequency(int frequency)
        {
            int fallback = frequency > 0 ? frequency : 48000;

#if UNITY_EDITOR
            if (IsUnityAudioDisabled())
                return fallback;
#endif

            int outputFrequency = AudioSettings.outputSampleRate;
            return outputFrequency > 0 ? outputFrequency : fallback;
        }

        private static bool IsUnityAudioDisabled()
        {
#if UNITY_EDITOR
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
            if (assets == null || assets.Length == 0)
                return false;

            var audioSettings = new UnityEditor.SerializedObject(assets[0]);
            var disableAudio = audioSettings.FindProperty("m_DisableAudio");
            return disableAudio != null && disableAudio.boolValue;
#else
            return false;
#endif
        }

        private void DebugMicrophoneDataPreProcessing(ArraySegment<float> samples)
        {
#if UNITY_EDITOR
            if (ShouldDebug && _isLocalVoiceOwner)
                micInputVisualizer?.AddSamples(samples);
#endif
        }

        private void DebugMicrophoneDataPostProcessing(ArraySegment<float> samples)
        {
#if UNITY_EDITOR
            if (ShouldDebug && _isLocalVoiceOwner)
                senderProcessedVisualizer?.AddSamples(samples);
#endif
        }

        internal void DebugNetworkSentData(ArraySegment<float> samples)
        {
#if UNITY_EDITOR
            if (ShouldDebug && _isLocalVoiceOwner)
                networkSentVisualizer?.AddSamples(samples);
#endif
        }

        internal void DebugReceived(ArraySegment<float> samples)
        {
#if UNITY_EDITOR
            if (ShouldDebug && !_isLocalVoiceOwner)
                receivedVisualizer?.AddSamples(samples);
#endif
        }

        private void DebugStreamedAudio(ArraySegment<float> samples)
        {
#if UNITY_EDITOR
            if (ShouldDebug)
                streamedAudioVisualizerStart?.AddSamples(samples);
#endif
        }

        private void DebugStreamedAudioEnd(ArraySegment<float> samples)
        {
#if UNITY_EDITOR
            if (ShouldDebug)
                streamedAudioVisualizerEnd?.AddSamples(samples);
#endif
        }
    }
}
