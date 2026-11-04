using System;

namespace PurrNet.Voice
{
    public interface IAudioCodec : IDisposable
    {
        int TargetSampleRate { get; }

        int FrameSize { get; }

        int Encode(float[] input, int offset, int count, byte[] output);

        int Decode(byte[] data, int offset, int count, float[] output);

        /// <summary>
        /// Resets the internal encoder state. Call before encoding the first
        /// frame after a deliberate gap in the outgoing stream (e.g. silence
        /// suppression) so the next frame is not delta-coded against state the
        /// receiver no longer has.
        /// </summary>
        void ResetEncoderState();

        /// <summary>
        /// Resets the internal decoder state. Call when the sender signals
        /// (via an in-band flag) that its encoder was just reset, so both sides
        /// start the new utterance from a matched fresh state.
        /// </summary>
        void ResetDecoderState();
    }
}
