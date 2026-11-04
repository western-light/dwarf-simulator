using System;
using System.Diagnostics;

namespace PurrNet.Voice
{
    internal static class VoiceDiagnosticWire
    {
        internal static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        internal static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)data[offset]
                   | (uint)data[offset + 1] << 8
                   | (uint)data[offset + 2] << 16
                   | (uint)data[offset + 3] << 24;
        }
    }

    internal enum VoiceDiagnosticDirection : byte
    {
        Sent,
        Received
    }

    internal enum VoiceDiagnosticDelivery : byte
    {
        None,
        First,
        InOrder,
        Gap,
        Duplicate,
        OutOfOrder,
        Unsequenced
    }

    internal struct VoiceDiagnosticFrame
    {
        public long timestamp;
        public VoiceDiagnosticDirection direction;
        public uint sequence;
        public byte flags;
        public VoiceDiagnosticDelivery delivery;
        public int missingBefore;
        public int payloadLength;
        public int capturedLength;
        public int sampleCount;
        public float rms;
        public float peak;
        public uint sampleHash;
        public byte toc;
    }

    internal sealed class VoiceDiagnosticContext
    {
        public string productName;
        public string applicationVersion;
        public string unityVersion;
        public string platform;
        public string deviceModel;
        public string operatingSystem;
        public string processorType;
        public string scriptingBackend;
        public string streamName;
        public ulong networkObjectId;
        public int unityInstanceId;
        public string owner;
        public string localPlayer;
        public bool isOwner;
        public bool isController;
        public string transport;
        public string codecName;
        public string codecVersion;
        public string encoderBackend;
        public string decoderBackend;
        public int sampleRate;
        public int frameSize;
        public int channels;
        public int bitrate;
        public int complexity;
        public bool useInbandFec;
        public bool useDtx;
        public int packetLossPercent;
    }

    internal sealed class VoiceDiagnosticSnapshot
    {
        public long startedUtcTicks;
        public long snapshotUtcTicks;
        public long stopwatchFrequency;
        public VoiceDiagnosticContext context;
        public VoiceDiagnosticFrame[] frames;
        public int frameStart;
        public int frameCount;
        public byte[] packetData;
        public int packetStride;
        public bool includesEncodedPackets;
    }

    internal sealed class VoiceDiagnosticCapture
    {
        private const int FramesPerSecond = 50;
        private const int MinCaptureSeconds = 1;
        private const int MaxCaptureSeconds = 60;
        private const int PacketStride = 1275;

        private readonly object _sync = new object();
        private VoiceDiagnosticFrame[] _frames;
        private byte[] _packetData;
        private int _writeIndex;
        private int _count;
        private long _startedUtcTicks;
        private volatile bool _active;
        private bool _includesEncodedPackets;

        internal bool active => _active;

        internal int frameCount
        {
            get
            {
                lock (_sync)
                    return _count;
            }
        }

        internal void Start(int captureSeconds, bool includeEncodedPackets)
        {
            int seconds = Math.Clamp(captureSeconds, MinCaptureSeconds, MaxCaptureSeconds);
            int capacity = seconds * FramesPerSecond;

            lock (_sync)
            {
                _frames = new VoiceDiagnosticFrame[capacity];
                _packetData = includeEncodedPackets ? new byte[capacity * PacketStride] : null;
                _writeIndex = 0;
                _count = 0;
                _startedUtcTicks = DateTime.UtcNow.Ticks;
                _includesEncodedPackets = includeEncodedPackets;
                _active = true;
            }
        }

        internal void Stop()
        {
            _active = false;
        }

        internal void RecordSent(byte[] data, int offset, int count, uint sequence, byte flags,
            bool encoded, float[] samples, int sampleOffset, int sampleCount)
        {
            Record(VoiceDiagnosticDirection.Sent, data, offset, count, sequence, flags,
                VoiceDiagnosticDelivery.None, 0, encoded, samples, sampleOffset, sampleCount);
        }

        internal void RecordReceived(byte[] data, int offset, int count, uint sequence, byte flags,
            VoiceDiagnosticDelivery delivery, int missingBefore, bool encoded,
            float[] samples, int sampleOffset, int sampleCount)
        {
            Record(VoiceDiagnosticDirection.Received, data, offset, count, sequence, flags,
                delivery, missingBefore, encoded, samples, sampleOffset, sampleCount);
        }

        internal VoiceDiagnosticSnapshot CreateSnapshot(VoiceDiagnosticContext context)
        {
            lock (_sync)
            {
                if (_frames == null)
                    throw new InvalidOperationException("Diagnostic capture has not been started.");

                int start = (_writeIndex - _count + _frames.Length) % _frames.Length;
                _active = false;

                return new VoiceDiagnosticSnapshot
                {
                    startedUtcTicks = _startedUtcTicks,
                    snapshotUtcTicks = DateTime.UtcNow.Ticks,
                    stopwatchFrequency = Stopwatch.Frequency,
                    context = context,
                    frames = _frames,
                    frameStart = start,
                    frameCount = _count,
                    packetData = _packetData,
                    packetStride = PacketStride,
                    includesEncodedPackets = _includesEncodedPackets
                };
            }
        }

        private void Record(VoiceDiagnosticDirection direction, byte[] data, int offset, int count,
            uint sequence, byte flags, VoiceDiagnosticDelivery delivery, int missingBefore,
            bool encoded, float[] samples, int sampleOffset, int sampleCount)
        {
            if (!_active)
                return;

            CalculateLevels(samples, sampleOffset, sampleCount, out float rms, out float peak, out uint sampleHash);

            lock (_sync)
            {
                if (!_active || _frames == null)
                    return;

                int capturedLength = 0;
                if (_packetData != null && encoded && data != null && count > 0)
                {
                    capturedLength = Math.Min(count, PacketStride);
                    Buffer.BlockCopy(data, offset, _packetData, _writeIndex * PacketStride, capturedLength);
                }

                _frames[_writeIndex] = new VoiceDiagnosticFrame
                {
                    timestamp = Stopwatch.GetTimestamp(),
                    direction = direction,
                    sequence = sequence,
                    flags = flags,
                    delivery = delivery,
                    missingBefore = missingBefore,
                    payloadLength = count,
                    capturedLength = capturedLength,
                    sampleCount = sampleCount,
                    rms = rms,
                    peak = peak,
                    sampleHash = sampleHash,
                    toc = encoded && data != null && count > 0 ? data[offset] : byte.MaxValue
                };

                _writeIndex = (_writeIndex + 1) % _frames.Length;
                if (_count < _frames.Length)
                    _count++;
            }
        }

        private static void CalculateLevels(float[] samples, int offset, int count,
            out float rms, out float peak, out uint sampleHash)
        {
            if (samples == null || count <= 0)
            {
                rms = float.NaN;
                peak = float.NaN;
                sampleHash = 0;
                return;
            }

            double sumSquares = 0d;
            float max = 0f;
            uint hash = 2166136261u;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                float sample = samples[i];
                float absolute = Math.Abs(sample);
                if (absolute > max)
                    max = absolute;
                sumSquares += sample * sample;
                short quantized = (short)(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
                hash = (hash ^ (byte)quantized) * 16777619u;
                hash = (hash ^ (byte)(quantized >> 8)) * 16777619u;
            }

            rms = (float)Math.Sqrt(sumSquares / count);
            peak = max;
            sampleHash = hash;
        }
    }
}
