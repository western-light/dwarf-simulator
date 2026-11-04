using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace PurrNet.Voice
{
    public partial class NetworkAudioModule
    {
        private readonly VoiceDiagnosticCapture _diagnosticCapture = new VoiceDiagnosticCapture();

        internal bool diagnosticCaptureActive => _diagnosticCapture.active;
        internal int diagnosticCaptureFrameCount => _diagnosticCapture.frameCount;

        internal void StartDiagnosticCapture(int captureSeconds, bool includeEncodedPackets)
        {
            _diagnosticCapture.Start(captureSeconds, includeEncodedPackets);
        }

        internal Task<string> SaveDiagnosticCaptureAsync(string path)
        {
            string resolvedPath = ResolveDiagnosticPath(path);
            VoiceDiagnosticSnapshot snapshot = CreateDiagnosticSnapshot();

#if UNITY_WEBGL && !UNITY_EDITOR
            VoiceDiagnosticCaptureFile.Write(snapshot, resolvedPath);
            return Task.FromResult(resolvedPath);
#else
            return Task.Run(() =>
            {
                VoiceDiagnosticCaptureFile.Write(snapshot, resolvedPath);
                return resolvedPath;
            });
#endif
        }

        internal VoiceDiagnosticSnapshot CreateDiagnosticSnapshot()
        {
            return _diagnosticCapture.CreateSnapshot(CreateDiagnosticContext());
        }

        private VoiceDiagnosticContext CreateDiagnosticContext()
        {
            var opus = _codec as OpusCodec;
            var player = parent as PurrVoicePlayer;
            return new VoiceDiagnosticContext
            {
                productName = Application.productName,
                applicationVersion = Application.version,
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                deviceModel = SystemInfo.deviceModel,
                operatingSystem = SystemInfo.operatingSystem,
                processorType = SystemInfo.processorType,
                scriptingBackend = GetScriptingBackend(),
                streamName = player?.name ?? string.Empty,
                networkObjectId = player?.objectId ?? 0,
                unityInstanceId = GetUnityInstanceId(player),
                owner = owner?.ToString() ?? "None",
                localPlayer = localPlayer?.ToString() ?? "None",
                isOwner = isOwner,
                isController = isController,
                transport = GetDiagnosticTransport(),
                codecName = _codec?.GetType().FullName ?? "Raw PCM",
                codecVersion = opus?.backendVersion ?? string.Empty,
                encoderBackend = opus?.encoderBackend ?? string.Empty,
                decoderBackend = opus?.decoderBackend ?? string.Empty,
                sampleRate = _codec?.TargetSampleRate ?? _frequency.value,
                frameSize = _chunkSize,
                channels = opus?.channels ?? 1,
                bitrate = opus?.bitrate ?? 0,
                complexity = opus?.complexity ?? 0,
                useInbandFec = opus?.useInbandFec ?? false,
                useDtx = opus?.useDtx ?? false,
                packetLossPercent = opus?.packetLossPercent ?? 0
            };
        }

        private string GetDiagnosticTransport()
        {
#if PURR_PIPES
            return _pipe ? "PurrPipes / Unreliable" : "RPC / Unreliable";
#else
            return "RPC / Unreliable";
#endif
        }

        private string ResolveDiagnosticPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                string streamName = SanitizeFileName((parent as PurrVoicePlayer)?.name ?? "Voice");
                string fileName = $"PurrVoice-{streamName}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.purrvoicecapture";
                return Path.Combine(Application.persistentDataPath, fileName);
            }

            return Path.IsPathRooted(path) ? path : Path.Combine(Application.persistentDataPath, path);
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var result = value.ToCharArray();
            for (int i = 0; i < result.Length; i++)
            {
                if (Array.IndexOf(invalid, result[i]) >= 0)
                    result[i] = '_';
            }

            return new string(result);
        }

        private static int GetUnityInstanceId(UnityEngine.Object target)
        {
            if (!target)
                return 0;

            // Unity 6000.4 deprecated GetInstanceID in favour of GetEntityId, and 6000.5 turned the
            // deprecation into a hard error. The low 32 bits of the entity id are the old instance id,
            // so the capture file format keeps storing an int.
#if UNITY_6000_4_OR_NEWER
            return unchecked((int)EntityId.ToULong(target.GetEntityId()));
#else
            return target.GetInstanceID();
#endif
        }

        private static string GetScriptingBackend()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#elif ENABLE_MONO
            return "Mono";
#else
            return ".NET";
#endif
        }
    }
}
