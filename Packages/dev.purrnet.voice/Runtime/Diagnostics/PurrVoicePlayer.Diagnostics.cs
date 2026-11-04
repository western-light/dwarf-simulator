using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace PurrNet.Voice
{
    public partial class PurrVoicePlayer
    {
        /// <summary>
        /// True while this voice stream is collecting diagnostic frames.
        /// </summary>
        public bool diagnosticRecordingActive => _transport?.diagnosticCaptureActive == true;

        /// <summary>
        /// Number of frames currently retained by this voice stream's diagnostic capture.
        /// </summary>
        public int diagnosticRecordingFrameCount => _transport?.diagnosticCaptureFrameCount ?? 0;

        /// <summary>
        /// Starts a 15-second rolling diagnostic recording with encoded packets included.
        /// </summary>
        public void StartDiagnosticRecording()
        {
            StartDiagnosticRecording(15, true);
        }

        /// <summary>
        /// Starts a rolling diagnostic recording. Encoded packets can contain recoverable speech and are only retained when explicitly requested.
        /// </summary>
        public void StartDiagnosticRecording(int recordingSeconds, bool includeEncodedPackets)
        {
            _transport.StartDiagnosticCapture(recordingSeconds, includeEncodedPackets);
        }

        /// <summary>
        /// Stops and saves the diagnostic recording, then logs the saved path. Intended for direct use from a Unity event.
        /// </summary>
        public async void StopDiagnosticRecording()
        {
            try
            {
                string path = await StopDiagnosticRecordingAsync();
                Debug.Log($"[PurrVoice] Diagnostic recording saved to {path}", this);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[PurrVoice] Failed to save diagnostic recording: {exception.Message}", this);
            }
        }

        /// <summary>
        /// Stops and saves the diagnostic recording, returning the saved path when writing completes.
        /// </summary>
        public Task<string> StopDiagnosticRecordingAsync(string path = null)
        {
            return _transport.SaveDiagnosticCaptureAsync(path);
        }

        /// <summary>
        /// Starts a 15-second diagnostic recording on every active PurrVoicePlayer.
        /// </summary>
        public static int StartAllDiagnosticRecordings()
        {
            return StartAllDiagnosticRecordings(15, true);
        }

        /// <summary>
        /// Starts a diagnostic recording on every active PurrVoicePlayer and returns the number of streams started.
        /// </summary>
        public static int StartAllDiagnosticRecordings(int recordingSeconds, bool includeEncodedPackets)
        {
            PurrVoicePlayer[] players = FindActiveVoicePlayers();
            int started = 0;
            for (int i = 0; i < players.Length; i++)
            {
                PurrVoicePlayer player = players[i];
                if (!player || !player.isActiveAndEnabled || player._transport == null)
                    continue;

                player.StartDiagnosticRecording(recordingSeconds, includeEncodedPackets);
                started++;
            }

            Debug.Log($"[PurrVoice] Started diagnostic recording for {started} voice stream(s).");
            return started;
        }

        /// <summary>
        /// Stops every active diagnostic recording, saves them into one session file, then logs the saved path.
        /// </summary>
        public static async void StopAllDiagnosticRecordings()
        {
            try
            {
                string path = await StopAllDiagnosticRecordingsAsync();
                Debug.Log($"[PurrVoice] Diagnostic session saved to {path}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[PurrVoice] Failed to save diagnostic session: {exception.Message}");
            }
        }

        /// <summary>
        /// Stops every active diagnostic recording and saves the streams into one session file.
        /// </summary>
        public static Task<string> StopAllDiagnosticRecordingsAsync(string path = null)
        {
            PurrVoicePlayer[] players = FindActiveVoicePlayers();
            var snapshots = new List<VoiceDiagnosticSnapshot>(players.Length);
            for (int i = 0; i < players.Length; i++)
            {
                PurrVoicePlayer player = players[i];
                if (!player || player._transport == null || !player.diagnosticRecordingActive)
                    continue;

                snapshots.Add(player._transport.CreateDiagnosticSnapshot());
            }

            if (snapshots.Count == 0)
                throw new InvalidOperationException("No active PurrVoice diagnostic recordings were found.");

            string resolvedPath = ResolveDiagnosticSessionPath(path);

#if UNITY_WEBGL && !UNITY_EDITOR
            VoiceDiagnosticCaptureFile.Write(snapshots, resolvedPath);
            return Task.FromResult(resolvedPath);
#else
            return Task.Run(() =>
            {
                VoiceDiagnosticCaptureFile.Write(snapshots, resolvedPath);
                return resolvedPath;
            });
#endif
        }

        private static PurrVoicePlayer[] FindActiveVoicePlayers()
        {
            return UnityEngine.Object.FindObjectsByType<PurrVoicePlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        private static string ResolveDiagnosticSessionPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                string fileName = $"PurrVoice-Session-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.purrvoicecapture";
                return Path.Combine(Application.persistentDataPath, fileName);
            }

            return Path.IsPathRooted(path) ? path : Path.Combine(Application.persistentDataPath, path);
        }
    }
}
