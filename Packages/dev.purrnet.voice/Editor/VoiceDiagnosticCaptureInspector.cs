using System;
using UnityEditor;
using UnityEngine;

namespace PurrVoice.Editor
{
    internal static class VoiceDiagnosticCaptureInspector
    {
        [MenuItem("Tools/PurrNet/Analysis/Inspect Voice Capture")]
        private static void InspectCapture()
        {
            string path = EditorUtility.OpenFilePanel("Inspect Voice Capture", string.Empty, "purrvoicecapture");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                Debug.Log(VoiceDiagnosticCaptureReader.ReadSummary(path));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[PurrVoice] Failed to inspect diagnostic capture: {exception.Message}");
            }
        }
    }
}
