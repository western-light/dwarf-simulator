using System.Collections.Generic;
using UnityEngine;

namespace PurrNet.Voice.WwiseIntegration
{
    static class PurrVoiceWwiseAudioInputManager
    {
        public delegate bool AudioSamplesDelegate(uint playingId, uint channelIndex, float[] samples);
        public delegate void AudioFormatDelegate(uint playingId, AkAudioFormat format);

        private struct AudioInputDelegates
        {
            public AudioSamplesDelegate samples;
            public AudioFormatDelegate format;
        }

        private static readonly Dictionary<uint, AudioInputDelegates> Delegates = new();
        private static readonly object Sync = new();

        private static AudioInputDelegates _pendingDelegates;
        private static bool _hasPendingDelegates;

        public static uint Post(uint eventId, GameObject gameObject, AudioSamplesDelegate samplesDelegate,
            AudioFormatDelegate formatDelegate)
        {
            lock (Sync)
            {
                _pendingDelegates = new AudioInputDelegates
                {
                    samples = samplesDelegate,
                    format = formatDelegate
                };
                _hasPendingDelegates = true;
            }

            uint playingId = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
            bool posted = false;
            try
            {
                playingId = AkAudioInputManager.PostAudioInputEvent(eventId, gameObject, ForwardSamples, ForwardFormat);
                posted = true;
            }
            finally
            {
                lock (Sync)
                {
                    _pendingDelegates = default;
                    _hasPendingDelegates = false;

                    if (posted && playingId != AkUnitySoundEngine.AK_INVALID_PLAYING_ID && samplesDelegate != null)
                    {
                        Delegates[playingId] = new AudioInputDelegates
                        {
                            samples = samplesDelegate,
                            format = formatDelegate
                        };
                    }
                }
            }

            return playingId;
        }

        public static void Remove(uint playingId)
        {
            if (playingId == AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
                return;

            lock (Sync)
            {
                Delegates.Remove(playingId);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            lock (Sync)
            {
                Delegates.Clear();
                _pendingDelegates = default;
                _hasPendingDelegates = false;
            }
        }

        private static bool ForwardSamples(uint playingId, uint channelIndex, float[] samples)
        {
            AudioSamplesDelegate callback = null;
            lock (Sync)
            {
                if (Delegates.TryGetValue(playingId, out var delegates))
                    callback = delegates.samples;
                else if (_hasPendingDelegates)
                    callback = _pendingDelegates.samples;
            }

            if (callback != null)
                return callback(playingId, channelIndex, samples);

            System.Array.Clear(samples, 0, samples.Length);
            return false;
        }

        private static void ForwardFormat(uint playingId, AkAudioFormat format)
        {
            AudioFormatDelegate callback = null;

            lock (Sync)
            {
                if (Delegates.TryGetValue(playingId, out var delegates))
                    callback = delegates.format;
                else if (_hasPendingDelegates)
                    callback = _pendingDelegates.format;
            }

            callback?.Invoke(playingId, format);
        }
    }
}
