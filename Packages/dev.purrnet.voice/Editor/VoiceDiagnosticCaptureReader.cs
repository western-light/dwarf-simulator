using System;
using System.IO;
using System.Text;

namespace PurrVoice.Editor
{
    public static class VoiceDiagnosticCaptureReader
    {
        private enum Direction : byte
        {
            Sent,
            Received
        }

        private enum Delivery : byte
        {
            None,
            First,
            InOrder,
            Gap,
            Duplicate,
            OutOfOrder,
            Unsequenced
        }

        private static readonly byte[] Magic = { 0x50, 0x55, 0x52, 0x52, 0x56, 0x43, 0x41, 0x50 };
        private const int LegacyFormatVersion = 1;
        private const int SessionFormatVersion = 2;

        /// <summary>
        /// Reads a PurrVoice diagnostic capture and returns a human-readable summary.
        /// </summary>
        public static string ReadSummary(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream, Encoding.UTF8, false);
            int version = ReadAndValidateHeader(reader);

            if (version == LegacyFormatVersion)
                return "PurrVoice diagnostic capture\n" + ReadStreamSummary(reader, version);

            int streamCount = reader.ReadInt32();
            if (streamCount <= 0)
                throw new InvalidDataException("Diagnostic session contains an invalid stream count.");

            var summary = new StringBuilder(1024);
            summary.AppendLine("PurrVoice diagnostic session");
            summary.AppendLine($"Streams: {streamCount}");
            for (int i = 0; i < streamCount; i++)
            {
                summary.AppendLine();
                summary.AppendLine($"Stream {i + 1}/{streamCount}");
                summary.Append(ReadStreamSummary(reader, version));
            }

            return summary.ToString();
        }

        private static string ReadStreamSummary(BinaryReader reader, int formatVersion)
        {
            Stream stream = reader.BaseStream;
            long startedUtcTicks = reader.ReadInt64();
            long snapshotUtcTicks = reader.ReadInt64();
            long stopwatchFrequency = reader.ReadInt64();
            string productName = reader.ReadString();
            string applicationVersion = reader.ReadString();
            string unityVersion = reader.ReadString();
            string platform = reader.ReadString();
            string deviceModel = reader.ReadString();
            string operatingSystem = reader.ReadString();
            string processorType = reader.ReadString();
            string scriptingBackend = reader.ReadString();
            string streamName = reader.ReadString();
            ulong networkObjectId = 0;
            int unityInstanceId = 0;
            string owner = string.Empty;
            string localPlayer = string.Empty;
            bool isOwner = false;
            bool isController = false;
            if (formatVersion >= SessionFormatVersion)
            {
                networkObjectId = reader.ReadUInt64();
                unityInstanceId = reader.ReadInt32();
                owner = reader.ReadString();
                localPlayer = reader.ReadString();
                isOwner = reader.ReadBoolean();
                isController = reader.ReadBoolean();
            }
            string transport = reader.ReadString();
            string codecName = reader.ReadString();
            string codecVersion = reader.ReadString();
            string encoderBackend = reader.ReadString();
            string decoderBackend = reader.ReadString();
            int sampleRate = reader.ReadInt32();
            int frameSize = reader.ReadInt32();
            int channels = reader.ReadInt32();
            int bitrate = reader.ReadInt32();
            int complexity = reader.ReadInt32();
            bool useInbandFec = reader.ReadBoolean();
            bool useDtx = reader.ReadBoolean();
            int packetLossPercent = reader.ReadInt32();
            bool includesPackets = reader.ReadBoolean();
            int frameCount = reader.ReadInt32();

            if (frameCount < 0)
                throw new InvalidDataException("Capture contains an invalid frame count.");

            int sent = 0;
            int received = 0;
            int silk = 0;
            int hybrid = 0;
            int celt = 0;
            int unknownMode = 0;
            int narrowband = 0;
            int mediumband = 0;
            int wideband = 0;
            int superWideband = 0;
            int fullband = 0;
            int gaps = 0;
            int missing = 0;
            int duplicates = 0;
            int outOfOrder = 0;
            int unsequenced = 0;
            int capturedPackets = 0;
            int truncatedPackets = 0;
            long payloadBytes = 0;
            int minPayload = int.MaxValue;
            int maxPayload = 0;
            double rmsTotal = 0d;
            int rmsCount = 0;
            float maxPeak = 0f;
            long firstTimestamp = 0;
            long lastTimestamp = 0;
            string lastReceivedMode = null;
            int omittedEvents = 0;
            var events = new StringBuilder(512);

            for (int i = 0; i < frameCount; i++)
            {
                long timestamp = reader.ReadInt64();
                Direction direction = (Direction)reader.ReadByte();
                uint sequence = reader.ReadUInt32();
                byte flags = reader.ReadByte();
                Delivery delivery = (Delivery)reader.ReadByte();
                int missingBefore = reader.ReadInt32();
                int payloadLength = reader.ReadInt32();
                int capturedLength = reader.ReadInt32();
                reader.ReadInt32();
                float rms = reader.ReadSingle();
                float peak = reader.ReadSingle();
                uint sampleHash = reader.ReadUInt32();
                byte toc = reader.ReadByte();

                if (payloadLength < 0 || capturedLength < 0 || capturedLength > stream.Length - stream.Position)
                    throw new InvalidDataException("Capture contains an invalid packet length.");

                if (i == 0)
                    firstTimestamp = timestamp;
                lastTimestamp = timestamp;

                if (direction == Direction.Sent)
                    sent++;
                else if (direction == Direction.Received)
                    received++;

                switch (delivery)
                {
                    case Delivery.Gap:
                        gaps++;
                        missing += Math.Max(0, missingBefore);
                        break;
                    case Delivery.Duplicate:
                        duplicates++;
                        break;
                    case Delivery.OutOfOrder:
                        outOfOrder++;
                        break;
                    case Delivery.Unsequenced:
                        unsequenced++;
                        break;
                }

                string mode;
                string bandwidth;
                if (toc == byte.MaxValue)
                {
                    unknownMode++;
                    mode = "Unknown";
                    bandwidth = "Unknown";
                }
                else
                {
                    int config = toc >> 3;
                    if (config < 12)
                    {
                        silk++;
                        mode = "SILK";
                    }
                    else if (config < 16)
                    {
                        hybrid++;
                        mode = "Hybrid";
                    }
                    else
                    {
                        celt++;
                        mode = "CELT";
                    }

                    if (config < 4 || (config >= 16 && config < 20))
                    {
                        narrowband++;
                        bandwidth = "Narrowband";
                    }
                    else if (config < 8)
                    {
                        mediumband++;
                        bandwidth = "Mediumband";
                    }
                    else if (config < 12 || (config >= 20 && config < 24))
                    {
                        wideband++;
                        bandwidth = "Wideband";
                    }
                    else if (config < 14 || (config >= 24 && config < 28))
                    {
                        superWideband++;
                        bandwidth = "Super-wideband";
                    }
                    else
                    {
                        fullband++;
                        bandwidth = "Fullband";
                    }
                }

                payloadBytes += payloadLength;
                minPayload = Math.Min(minPayload, payloadLength);
                maxPayload = Math.Max(maxPayload, payloadLength);

                if (!float.IsNaN(rms))
                {
                    rmsTotal += rms;
                    rmsCount++;
                }

                if (!float.IsNaN(peak))
                    maxPeak = Math.Max(maxPeak, peak);

                if (capturedLength > 0)
                    capturedPackets++;
                if (capturedLength > 0 && capturedLength < payloadLength)
                    truncatedPackets++;

                bool modeChanged = direction == Direction.Received && mode != lastReceivedMode;
                bool notableDelivery = delivery == Delivery.Gap
                                       || delivery == Delivery.Duplicate
                                       || delivery == Delivery.OutOfOrder;
                bool resumed = (flags & 0x01) != 0;
                if (modeChanged || notableDelivery || resumed)
                {
                    if (events.Length < 4096)
                    {
                        double eventTime = stopwatchFrequency > 0
                            ? (timestamp - firstTimestamp) / (double)stopwatchFrequency
                            : 0d;
                        string deliveryText = delivery == Delivery.Gap
                            ? $"{delivery} ({missingBefore} missing)"
                            : delivery.ToString();
                        events.AppendLine($"  {eventTime:F3}s {direction} #{sequence} {deliveryText}, {mode}/{bandwidth}, {payloadLength} bytes, RMS {rms:F5}, peak {peak:F5}, hash {sampleHash:X8}{(resumed ? ", resume" : string.Empty)}");
                    }
                    else
                    {
                        omittedEvents++;
                    }
                }

                if (direction == Direction.Received)
                    lastReceivedMode = mode;
                stream.Seek(capturedLength, SeekOrigin.Current);
            }

            double duration = frameCount > 1 && stopwatchFrequency > 0
                ? (lastTimestamp - firstTimestamp) / (double)stopwatchFrequency
                : 0d;
            double averagePayload = frameCount > 0 ? payloadBytes / (double)frameCount : 0d;
            double averageRms = rmsCount > 0 ? rmsTotal / rmsCount : 0d;

            var summary = new StringBuilder(768);
            summary.AppendLine($"Capture: {new DateTime(startedUtcTicks, DateTimeKind.Utc):O} to {new DateTime(snapshotUtcTicks, DateTimeKind.Utc):O}");
            summary.AppendLine($"Application: {productName} {applicationVersion}, Unity {unityVersion}");
            summary.AppendLine($"Device: {platform}, {deviceModel}, {processorType}, {operatingSystem}, {scriptingBackend}");
            summary.AppendLine($"Stream: {streamName}");
            if (formatVersion >= SessionFormatVersion)
                summary.AppendLine($"Network: object {networkObjectId}, instance {unityInstanceId}, owner {owner}, local player {localPlayer}, is owner {isOwner}, controller {isController}");
            summary.AppendLine($"Transport: {transport}");
            summary.AppendLine($"Codec: {codecName} {codecVersion}, {sampleRate} Hz, {channels} channel(s), {frameSize} samples");
            summary.AppendLine($"Encoder settings: {bitrate} bps, complexity {complexity}, FEC {useInbandFec}, DTX {useDtx}, expected loss {packetLossPercent}%");
            summary.AppendLine($"Encoder: {encoderBackend}");
            summary.AppendLine($"Decoder: {decoderBackend}");
            summary.AppendLine($"Frames: {frameCount} ({sent} sent, {received} received) over {duration:F2}s");
            summary.AppendLine($"Modes: SILK {silk}, Hybrid {hybrid}, CELT {celt}, Unknown {unknownMode}");
            summary.AppendLine($"Bandwidth: NB {narrowband}, MB {mediumband}, WB {wideband}, SWB {superWideband}, FB {fullband}");
            summary.AppendLine($"Delivery: {gaps} gaps / {missing} missing, {outOfOrder} out of order, {duplicates} duplicates, {unsequenced} unsequenced");
            summary.AppendLine($"Packet bytes: min {(frameCount > 0 ? minPayload : 0)}, avg {averagePayload:F1}, max {maxPayload}");
            summary.AppendLine(rmsCount > 0
                ? $"Levels: avg RMS {averageRms:F5}, max peak {maxPeak:F5}"
                : "Levels: unavailable");
            summary.AppendLine($"Encoded packets: {(includesPackets ? $"{capturedPackets}/{frameCount} included, {truncatedPackets} truncated" : "not included")}");
            if (events.Length > 0)
            {
                summary.AppendLine("Notable events:");
                summary.Append(events);
                if (omittedEvents > 0)
                    summary.Append($"  {omittedEvents} more events omitted");
            }
            return summary.ToString();
        }

        private static int ReadAndValidateHeader(BinaryReader reader)
        {
            byte[] magic = reader.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length)
                throw new InvalidDataException("File is not a PurrVoice diagnostic capture.");

            for (int i = 0; i < Magic.Length; i++)
            {
                if (magic[i] != Magic[i])
                    throw new InvalidDataException("File is not a PurrVoice diagnostic capture.");
            }

            int version = reader.ReadInt32();
            if (version != LegacyFormatVersion && version != SessionFormatVersion)
                throw new InvalidDataException($"Unsupported PurrVoice diagnostic capture version: {version}.");

            return version;
        }
    }
}
