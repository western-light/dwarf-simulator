using System;
using UnityEngine;

namespace PurrNet.Voice
{
    public interface IVoiceOutput
    {
        /// <summary>
        /// Callback for when samples entered the audio handling
        /// </summary>
        public event Action<ArraySegment<float>> onStartPlayingSample;
        
        /// <summary>
        /// Callback for when samples are finished audio handling
        /// </summary>
        public event Action<ArraySegment<float>> onEndPlayingSample;

        /// <summary>
        /// Current frequency of playback
        /// </summary>
        public int frequency { get; }

        /// <summary>
        /// Used for initializing the output with necessary context
        /// </summary>
        /// <param name="source"></param>
        /// <param name="inputSource"></param>
        /// <param name="processSamples"></param>
        /// <param name="levels"></param>
        public void Init(IAudioInputSource inputSource, ProcessSamplesDelegate processSamples = null, params FilterLevel[] levels);

        public void Start();
        public void Stop();

        /// <summary>
        /// Subscribe to the input source's sample stream and prepare internal playback state,
        /// without starting the input device itself. Use this when the input device is owned
        /// and started by an external party (e.g. shared mic lifecycle).
        /// </summary>
        public void AttachInput();

        /// <summary>
        /// Unsubscribe from the input source's sample stream and tear down internal playback
        /// state, without stopping the input device. Counterpart to <see cref="AttachInput"/>.
        /// </summary>
        public void DetachInput();

        public void HandleAudioFilterRead(float[] data, int channels);
    }
}
