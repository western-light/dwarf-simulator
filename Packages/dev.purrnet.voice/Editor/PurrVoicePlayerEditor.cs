using System;
using PurrNet;
using PurrNet.Voice;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
namespace PurrNet.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(PurrVoicePlayer))]
    public class PurrVoicePlayerEditor : UnityEditor.Editor
    {
        private const float WAVEFORM_HEIGHT = 60f;
        private const float PADDING = 10f;

        /// <summary>
        /// Draws the PurrNet identity inspector together with the PurrVoice runtime visualizer.
        /// </summary>
        public override void OnInspectorGUI()
        {
            var player = (PurrVoicePlayer)target;

            if (!player)
                return;

            bool hasNetworkManagerAsChild = player.GetComponentInChildren<NetworkManager>();
            if (hasNetworkManagerAsChild)
                EditorGUILayout.HelpBox("NetworkIdentity is a child of a NetworkManager. This is not supported.",
                    MessageType.Error);

            DrawSerializedInspectorFallback();

            DrawVoiceVisualization(player);
            DrawNetworkIdentityStatus(player);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawVoiceVisualization(PurrVoicePlayer player)
        {
            if (!Application.isPlaying)
                return;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Audio Visualization", EditorStyles.boldLabel);

            if (player.isOwner && player.isLocalPlaybackActive)
            {
                DrawAudioWaveform("Microphone Input", player._micVisualizer, Color.green);
            }

            if (!player.isOwner)
            {
                DrawAudioWaveform("Network Audio", player._networkVisualizer, Color.cyan);
            }

            if (player && player.output != null)
            {
                if(player.inputFrequency > -1)
                    EditorGUILayout.LabelField($"Input frequency: {player.inputFrequency}");
                EditorGUILayout.LabelField($"Playback frequency: {player.output.frequency}");
            }

            if (Application.isPlaying)
            {
                EditorUtility.SetDirty(target);
                Repaint();
            }
        }

        private void DrawAudioWaveform(string label, AudioVisualizer visualizer, Color color)
        {
            if (visualizer == null) return;

            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

            var rect = GUILayoutUtility.GetRect(0, WAVEFORM_HEIGHT, GUILayout.ExpandWidth(true));
            rect.x += PADDING;
            rect.width -= PADDING * 2;

            EditorGUI.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f, 0.5f));

            var samples = visualizer.GetSamples();
            if (samples.Length < 2) return;

            var centerY = rect.y + rect.height * 0.5f;
            Handles.color = new Color(0.3f, 0.3f, 0.3f, 0.8f);
            Handles.DrawLine(new Vector3(rect.x, centerY), new Vector3(rect.xMax, centerY));

            Handles.color = color;
            var points = new Vector3[samples.Length];

            for (int i = 0; i < samples.Length; i++)
            {
                float x = rect.x + (float)i / (samples.Length - 1) * rect.width;
                float y = centerY - samples[i] * rect.height * 0.4f;
                points[i] = new Vector3(x, y, 0);
            }

            for (int i = 0; i < points.Length - 1; i++)
            {
                Handles.DrawLine(points[i], points[i + 1]);
            }

            GUI.color = color;
            var maxAmplitude = 0f;
            var rmsAmplitude = 0f;

            if (samples.Length > 0)
            {
                foreach (var sample in samples)
                {
                    var abs = Mathf.Abs(sample);
                    if (abs > maxAmplitude) maxAmplitude = abs;
                    rmsAmplitude += sample * sample;
                }
                rmsAmplitude = Mathf.Sqrt(rmsAmplitude / samples.Length);
            }

            var infoRect = new Rect(rect.xMax - 100, rect.y + 2, 95, 40);
            GUI.Label(infoRect, $"Peak: {maxAmplitude:F3}\nRMS: {rmsAmplitude:F3}", EditorStyles.miniLabel);
            GUI.color = Color.white;

            EditorGUILayout.Space(5);
        }

        private void DrawNetworkIdentityStatus(PurrVoicePlayer player)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("PurrNet", EditorStyles.boldLabel);

            if (player.isSpawned)
            {
                if (player.isServer)
                    EditorGUILayout.LabelField($"Observers: {player.observers.Count}");

                EditorGUILayout.BeginHorizontal("box", GUILayout.ExpandWidth(false));
                GUILayout.Label($"ID: {player.id}", GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Owner ID: {(player.owner.HasValue ? player.owner.Value.ToString() : "None")}",
                    GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                GUILayout.Label(
                    $"Local Player: {(player.localPlayer.HasValue ? player.localPlayer.Value.ToString() : "None")}",
                    GUILayout.ExpandWidth(false));
                EditorGUILayout.EndHorizontal();
            }
            else if (Application.isPlaying)
            {
                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.LabelField("Not Spawned");
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawSerializedInspectorFallback()
        {
            serializedObject.UpdateIfRequiredOrScript();
            var iterator = serializedObject.GetIterator();
            for (bool enterChildren = true; iterator.NextVisible(enterChildren); enterChildren = false)
            {
                try
                {
                    EditorGUILayout.PropertyField(iterator, true);
                }
                catch (System.IO.FileNotFoundException)
                {
                }
                catch (System.Reflection.ReflectionTypeLoadException)
                {
                }
            }
        }
    }
}
#endif
