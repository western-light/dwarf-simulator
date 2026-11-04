using System;
using UnityEngine;

namespace PurrNet.Voice
{
    /// <summary>
    /// Settings for sender-side silence suppression. When enabled, chunks whose
    /// post-filter peak amplitude stays below the threshold long enough are
    /// dropped before encoding/sending, eliminating network traffic during silence.
    /// </summary>
    [Serializable]
    public struct SilenceSuppressionSettings
    {
        [Tooltip("If enabled, silent chunks are not sent over the network.")]
        public bool enabled;

        [Tooltip("Linear peak amplitude (0..1) below which a chunk is considered silence. " +
                 "Should be slightly above whatever the noise gate leaves as residual.")]
        [Range(0f, 0.2f)]
        public float peakThreshold;

        [Tooltip("Number of additional silent chunks to keep sending after the last " +
                 "non-silent one. At 20ms chunks, 10 = 200ms tail. Prevents cutting off " +
                 "the end of a word.")]
        [Range(0, 100)]
        public int hangoverChunks;

        public static SilenceSuppressionSettings Default => new SilenceSuppressionSettings
        {
            enabled = true,
            peakThreshold = 0.005f,
            hangoverChunks = 10,
        };
    }

    /// <summary>
    /// Stateful silence detector. Not thread safe, use one instance per sender pipeline.
    /// </summary>
    internal sealed class SilenceSuppressor
    {
        public SilenceSuppressionSettings settings = SilenceSuppressionSettings.Default;
        private int _hangoverRemaining;

        // True when we are not currently sending (silent and past hangover).
        // The next chunk that crosses the threshold is the "resume" frame and
        // must be flagged so both encoder and decoder reset together.
        private bool _inSilence = true;

        public bool ShouldSend(float[] buffer, int offset, int count, out bool isResuming)
        {
            isResuming = false;

            if (!settings.enabled || count <= 0)
            {
                _inSilence = false;
                return true;
            }

            float threshold = settings.peakThreshold;
            float peak = 0f;
            for (int i = 0; i < count; i++)
            {
                float a = buffer[offset + i];
                if (a < 0f) a = -a;
                if (a > peak)
                {
                    peak = a;
                    if (peak >= threshold) break;
                }
            }

            if (peak >= threshold)
            {
                isResuming = _inSilence;
                _inSilence = false;
                _hangoverRemaining = settings.hangoverChunks;
                return true;
            }

            if (_hangoverRemaining > 0)
            {
                _hangoverRemaining--;
                return true;
            }

            _inSilence = true;
            return false;
        }

        public void Reset()
        {
            _hangoverRemaining = 0;
            _inSilence = true;
        }
    }
}
