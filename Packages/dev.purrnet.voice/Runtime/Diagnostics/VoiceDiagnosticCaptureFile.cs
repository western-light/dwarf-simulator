using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PurrNet.Voice
{
    internal static class VoiceDiagnosticCaptureFile
    {
        private static readonly byte[] Magic = { 0x50, 0x55, 0x52, 0x52, 0x56, 0x43, 0x41, 0x50 };
        private const int FormatVersion = 2;

        internal static void Write(VoiceDiagnosticSnapshot snapshot, string path)
        {
            Write(new[] { snapshot }, path);
        }

        internal static void Write(IReadOnlyList<VoiceDiagnosticSnapshot> snapshots, string path)
        {
            if (snapshots == null || snapshots.Count == 0)
                throw new InvalidDataException("A diagnostic session must contain at least one stream.");

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, false);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(snapshots.Count);

            for (int i = 0; i < snapshots.Count; i++)
                WriteSnapshot(writer, snapshots[i]);
        }

        private static void WriteSnapshot(BinaryWriter writer, VoiceDiagnosticSnapshot snapshot)
        {
            writer.Write(snapshot.startedUtcTicks);
            writer.Write(snapshot.snapshotUtcTicks);
            writer.Write(snapshot.stopwatchFrequency);

            VoiceDiagnosticContext context = snapshot.context;
            WriteString(writer, context.productName);
            WriteString(writer, context.applicationVersion);
            WriteString(writer, context.unityVersion);
            WriteString(writer, context.platform);
            WriteString(writer, context.deviceModel);
            WriteString(writer, context.operatingSystem);
            WriteString(writer, context.processorType);
            WriteString(writer, context.scriptingBackend);
            WriteString(writer, context.streamName);
            writer.Write(context.networkObjectId);
            writer.Write(context.unityInstanceId);
            WriteString(writer, context.owner);
            WriteString(writer, context.localPlayer);
            writer.Write(context.isOwner);
            writer.Write(context.isController);
            WriteString(writer, context.transport);
            WriteString(writer, context.codecName);
            WriteString(writer, context.codecVersion);
            WriteString(writer, context.encoderBackend);
            WriteString(writer, context.decoderBackend);
            writer.Write(context.sampleRate);
            writer.Write(context.frameSize);
            writer.Write(context.channels);
            writer.Write(context.bitrate);
            writer.Write(context.complexity);
            writer.Write(context.useInbandFec);
            writer.Write(context.useDtx);
            writer.Write(context.packetLossPercent);
            writer.Write(snapshot.includesEncodedPackets);
            writer.Write(snapshot.frameCount);

            for (int i = 0; i < snapshot.frameCount; i++)
            {
                int sourceIndex = (snapshot.frameStart + i) % snapshot.frames.Length;
                VoiceDiagnosticFrame frame = snapshot.frames[sourceIndex];
                writer.Write(frame.timestamp);
                writer.Write((byte)frame.direction);
                writer.Write(frame.sequence);
                writer.Write(frame.flags);
                writer.Write((byte)frame.delivery);
                writer.Write(frame.missingBefore);
                writer.Write(frame.payloadLength);
                writer.Write(frame.capturedLength);
                writer.Write(frame.sampleCount);
                writer.Write(frame.rms);
                writer.Write(frame.peak);
                writer.Write(frame.sampleHash);
                writer.Write(frame.toc);

                if (frame.capturedLength > 0)
                    writer.Write(snapshot.packetData, sourceIndex * snapshot.packetStride, frame.capturedLength);
            }
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            writer.Write(value ?? string.Empty);
        }
    }
}
