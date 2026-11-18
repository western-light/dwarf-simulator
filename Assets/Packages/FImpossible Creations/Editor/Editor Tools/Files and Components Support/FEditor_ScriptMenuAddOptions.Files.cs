using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace FIMSpace.FEditor
{
    /// <summary>
    /// FM: Class with basic tools for working in Unity Editor level
    /// </summary>
    public static partial class FEditor_MenuAddOptions
    {
        [MenuItem("Assets/Copy Full Path To Directory")]
        private static void CopyWholePathToDir2(MenuCommand menuCommand) => CopyWholePathToDir(menuCommand);

        [MenuItem("Assets/Utilities/Copy Full Path To Directory")]
        private static void CopyWholePathToDir(MenuCommand menuCommand)
        {
            if (Selection.objects.Length == 0) return;
            string assetPath = AssetDatabase.GetAssetPath(Selection.objects[0]);
            if (string.IsNullOrEmpty(assetPath)) return;
            string fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath));
            if (!AssetDatabase.IsValidFolder(assetPath)) fullPath = Path.GetDirectoryName(fullPath);
            GUIUtility.systemCopyBuffer = fullPath;
        }

        [MenuItem("Assets/Utilities/Generate Scriptable Instance", true)]
        private static bool GenerateScrInstanceCheck(MenuCommand menuCommand)
        {
            if (Selection.objects.Length == 0) return false;

            bool isScr = false;
            for (int i = 0; i < Selection.objects.Length; i++)
            {
                if (Selection.objects[i].GetType() != typeof(MonoScript)) continue;
                MonoScript monoScr = Selection.objects[i] as MonoScript;

                if (monoScr.GetClass().IsAbstract == false && monoScr.GetClass().IsSubclassOf(typeof(ScriptableObject)))
                { isScr = true; break; }
            }

            return isScr;
        }

        [MenuItem("Assets/Utilities/Generate Scriptable Instance")]
        private static void GenerateScrInstance(MenuCommand menuCommand)
        {
            if (Selection.objects.Length == 0) return;

            for (int i = 0; i < Selection.objects.Length; i++)
            {
                if (Selection.objects[i].GetType() != typeof(MonoScript)) continue;
                MonoScript monoScr = Selection.objects[i] as MonoScript;

                if (monoScr.GetClass().IsAbstract == false)
                    if (monoScr.GetClass().IsSubclassOf(typeof(ScriptableObject)))
                    {
                        ScriptableObject newScr = ScriptableObject.CreateInstance(monoScr.GetClass());
                        string newPath = AssetDatabase.GetAssetPath(Selection.objects[i]);
                        newPath = newPath.Replace(Selection.objects[i].name, FormatNameForSpaces(Selection.objects[i].name));
                        AssetDatabase.CreateAsset(newScr, newPath.Replace(".cs", "") + " Instance.asset");
                    }
            }
        }

        private static string FormatNameForSpaces(string name, bool startUpper = true)
        {
            if (startUpper)
                return char.ToUpper(name[0]) + System.Text.RegularExpressions.Regex.Replace(name.Substring(1), "(\\B[A-Z])", " $1");
            else
                return System.Text.RegularExpressions.Regex.Replace(name, "(\\B[A-Z])", " $1");
        }

#if UNITY_EDITOR_WIN

        private const uint CF_HDROP = 15; // Clipboard format for file paths

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();
        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(uint uFormat);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW", ExactSpelling = true)]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, uint cch);

        [MenuItem("Assets/Paste/Paste Clipboard Files")]
        private static void PasteClipboardFiles(MenuCommand menuCommand)
        {
            if (Selection.objects.Length == 0) return;

            string assetPath = AssetDatabase.GetAssetPath(Selection.objects[0]);
            if (string.IsNullOrEmpty(assetPath)) return;
            if (!AssetDatabase.IsValidFolder(assetPath)) assetPath = Path.GetDirectoryName(assetPath);
            assetPath = assetPath?.Replace('\\', '/');
            if (string.IsNullOrEmpty(assetPath) || (assetPath != "Assets" && !assetPath.StartsWith("Assets/", StringComparison.Ordinal)) || !AssetDatabase.IsValidFolder(assetPath)) return;

            bool copied = false;
            foreach (string source in GetCopiedFilePaths())
            {
                try
                {
                    if (string.Equals(Path.GetExtension(source), ".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(source))
                    {
                        Debug.LogWarning("Cannot paste missing file or directory: " + source);
                        continue;
                    }

                    CopyClipboardFile(source, assetPath);
                    copied = true;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Could not paste " + source + ": " + exception.Message);
                }
            }

            if (copied) AssetDatabase.Refresh();
        }

        private static void CopyClipboardFile(string source, string assetPath)
        {
            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
            string temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
            bool created = false;
            try
            {
                using (var input = File.OpenRead(source))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                {
                    created = true;
                    input.CopyTo(output);
                }

                File.Move(temporary, GetUniqueClipboardFilePath(assetPath, Path.GetFileName(source)));
            }
            finally
            {
                if (created && File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static string GetUniqueClipboardFilePath(string assetPath, string fileName)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            for (int i = 0; ; i++)
            {
                string name = i == 0 ? fileName : stem + " " + i + extension;
                string path = AssetDatabase.GenerateUniqueAssetPath(assetPath + "/" + name);
                string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
                if (!File.Exists(fullPath) && !Directory.Exists(fullPath) && !File.Exists(fullPath + ".meta") && !Directory.Exists(fullPath + ".meta")) return fullPath;
            }
        }


        [MenuItem("Assets/Paste/Paste Clipboard Texture As Png")]
        private static void PasteClipboardTexture(MenuCommand menuCommand)
        {
            if (Selection.objects.Length == 0) return;

            string assetPath = AssetDatabase.GetAssetPath(Selection.objects[0]);
            if (Selection.objects[0].GetType() != typeof(DefaultAsset)) assetPath = System.IO.Path.GetDirectoryName(assetPath);

            var paths = GetCopiedFilePaths();

            for (int i = 0; i < paths.Count; i++)
            {
                Texture2D loaded = LoadTextureFromFile(paths[i]);
                if (loaded == null) continue;
                loaded.EncodeToPNG();

                string fileName = System.IO.Path.GetFileName(paths[i]);
                string texPath = assetPath + "/" + fileName + ".png";
                System.IO.File.WriteAllBytes(texPath, loaded.EncodeToPNG());
                AssetDatabase.Refresh(ImportAssetOptions.Default);
                AssetDatabase.ImportAsset(texPath, ImportAssetOptions.Default);
            }
        }

        [MenuItem("Assets/Paste/Paste Clipboard Texture As Png Sprite")]
        private static void PasteClipboardTextureIcon(MenuCommand menuCommand)
        {
            if (Selection.objects.Length == 0) return;

            string assetPath = AssetDatabase.GetAssetPath(Selection.objects[0]);
            if (Selection.objects[0].GetType() != typeof(DefaultAsset)) assetPath = System.IO.Path.GetDirectoryName(assetPath);

            var paths = GetCopiedFilePaths();

            for (int i = 0; i < paths.Count; i++)
            {
                Texture2D loaded = LoadTextureFromFile(paths[i]);
                if (loaded == null) continue;
                loaded.EncodeToPNG();

                string fileName = System.IO.Path.GetFileName(paths[i]);
                string texPath = assetPath + "/" + fileName + ".png";
                System.IO.File.WriteAllBytes(texPath, loaded.EncodeToPNG());
                AssetDatabase.Refresh(ImportAssetOptions.Default);
                AssetDatabase.ImportAsset(texPath, ImportAssetOptions.Default);

                TextureImporter importer = TextureImporter.GetAtPath(assetPath + "/" + fileName) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.SaveAndReimport();
                }
            }

        }

        //[MenuItem("Assets/Paste/Paste Clipboard Texture As Png Sprite x256")]
        //private static void PasteClipboardTextureIcon256(MenuCommand menuCommand)
        //{
        //    if (Selection.objects.Length == 0) return;

        //    string assetPath = AssetDatabase.GetAssetPath(Selection.objects[0]);
        //    if (Selection.objects[0].GetType() != typeof(DefaultAsset)) assetPath = System.IO.Path.GetDirectoryName(assetPath);

        //    var paths = GetCopiedFilePaths();

        //    for (int i = 0; i < paths.Count; i++)
        //    {
        //        Texture2D loaded = LoadTextureFromFile(paths[i]);
        //        if (loaded == null) continue;

        //        float scaleRatio = loaded.width / 256f;

        //        loaded = FTex_ScaleLanczos.ScaleTexture(loaded, 256, Mathf.RoundToInt(loaded.height / scaleRatio));
        //        loaded.EncodeToPNG();

        //        string fileName = System.IO.Path.GetFileName(paths[i]);
        //        string texPath = assetPath + "/" + fileName + ".png";
        //        System.IO.File.WriteAllBytes(texPath, loaded.EncodeToPNG());
        //        AssetDatabase.Refresh(ImportAssetOptions.Default);
        //        AssetDatabase.ImportAsset(texPath, ImportAssetOptions.Default);

        //        TextureImporter importer = TextureImporter.GetAtPath(texPath) as TextureImporter;
        //        if (importer != null)
        //        {
        //            importer.textureType = TextureImporterType.Sprite;
        //            importer.spriteImportMode = SpriteImportMode.Single;
        //            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        //            importer.mipmapEnabled = true;
        //            importer.SaveAndReimport();
        //        }
        //    }
        //}

        static Texture2D LoadTextureFromFile(string systemFilepath)
        {
            if (File.Exists(systemFilepath))
            {
                byte[] fileData = File.ReadAllBytes(systemFilepath);
                Texture2D texture = new Texture2D(2, 2); // Create a placeholder texture

                if (texture.LoadImage(fileData)) // Load the image data into the texture
                    return texture;
            }

            return null;
        }

#endif

        public static List<string> GetCopiedFilePaths()
        {
            List<string> filePaths = new List<string>();
#if UNITY_EDITOR_WIN
            if (!OpenClipboard(IntPtr.Zero))
            {
                Debug.LogError("Failed to open clipboard!");
                return filePaths;
            }

            try
            {
                IntPtr handle = GetClipboardData(CF_HDROP);
                if (handle == IntPtr.Zero) return filePaths;

                uint fileCount = DragQueryFile(handle, uint.MaxValue, null, 0);
                for (uint i = 0; i < fileCount; i++)
                {
                    uint length = DragQueryFile(handle, i, null, 0);
                    if (length == 0) continue;
                    StringBuilder filePath = new StringBuilder(checked((int)length + 1));
                    if (DragQueryFile(handle, i, filePath, (uint)filePath.Capacity) == length)
                        filePaths.Add(filePath.ToString());
                }
            }
            finally
            {
                CloseClipboard();
            }
#endif
            return filePaths;
        }

        [MenuItem("Assets/Utilities/Check how many Assembly Definitions refers to this one", true)]
        private static bool CheckFindADUsingThisAD(MenuCommand menuCommand)
        {
            if (Selection.objects.Length == 0) return false;
            return Selection.objects[0] is AssemblyDefinitionAsset;
        }


        [MenuItem("Assets/Utilities/Check how many Assembly Definitions refers to this one")]
        private static void FindADUsingThisAD(MenuCommand menuCommand)
        {
            if (Selection.objects.Length == 0) return;

            if (Selection.objects[0] is AssemblyDefinitionAsset)
            {
                AssemblyDefinitionAsset adAsset = Selection.objects[0] as AssemblyDefinitionAsset;
                string adGuid = "GUID:" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(adAsset));

                string[] guids = AssetDatabase.FindAssets(string.Format("t:AssemblyDefinitionAsset"));
                string[] asmdefPaths = guids.Select(AssetDatabase.GUIDToAssetPath).ToArray();

                List<AssemblyDefinitionData> usedBy = new List<AssemblyDefinitionData>();

                for (int i = 0; i < asmdefPaths.Length; i++)
                {
                    var oAd = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(asmdefPaths[i]);
                    if (oAd == null) continue;
                    if (oAd == adAsset) continue;

                    string jsonContent = File.ReadAllText(asmdefPaths[i]); // Read JSON content
                    AssemblyDefinitionData asmdefData = JsonUtility.FromJson<AssemblyDefinitionData>(jsonContent);

                    if (asmdefData.references == null) continue;

                    for (int r = 0; r < asmdefData.references.Length; r++)
                    {
                        if (asmdefData.references[r] == adGuid || asmdefData.references[r] == adAsset.name) { usedBy.Add(asmdefData); break; }
                    }
                }

                if (usedBy.Count == 0)
                {
                    UnityEngine.Debug.Log(adAsset.name + " Is not used by any other Assembly Definition in the project");
                }
                else
                {
                    string adList = "<b>";

                    for (int i = 0; i < usedBy.Count; i++)
                    {
                        adList += usedBy[i].name + (i == usedBy.Count - 1 ? "" : "   ");
                    }

                    adList += "</b>";

                    UnityEngine.Debug.Log(adAsset.name + " Is used by " + usedBy.Count + " Assembly Definitions in the project, here their names: " + adList);

                }
            }
            else
            {
                Debug.Log("No Assembly Definition selected to find relations!");
            }
        }

        [System.Serializable]
        private class AssemblyDefinitionData
        {
            public string name;
            public string[] references;
        }


        [MenuItem("CONTEXT/MonoBehaviour/Go To Script's Directory")]
        private static void GoToBehaviourDirectory(MenuCommand menuCommand)
        {
            if (menuCommand.context is MonoBehaviour)
            {
                MonoBehaviour targetComponent = (MonoBehaviour)menuCommand.context;

                if (targetComponent)
                {
                    MonoScript script = MonoScript.FromMonoBehaviour(targetComponent);
                    if (script) EditorGUIUtility.PingObject(script);
                }
            }
            else if (menuCommand.context is ScriptableObject)
            {
                ScriptableObject targetComponent = (ScriptableObject)menuCommand.context;

                if (targetComponent)
                {
                    MonoScript script = MonoScript.FromScriptableObject(targetComponent);
                    if (script) EditorGUIUtility.PingObject(script);
                }
            }
        }

        [MenuItem("Assets/Utilities/Name iterative selected assets", true)]
        static bool SetFilenamesCheck(MenuCommand menuCommand)
        { return Selection.gameObjects.Length > 0; }

        [MenuItem("Assets/Utilities/Name iterative selected assets", false)]
        private static void SetFilenames(MenuCommand menuCommand)
        {
            if (Selection.gameObjects.Length == 0) return;

            string filename = EditorUtility.SaveFilePanelInProject("Type your target filename (no file will be created)", Selection.gameObjects[0].name, "", "Type your target filename (no file will be created)");
            filename = filename.Replace("Assets/", "");
            if (string.IsNullOrEmpty(filename)) return;

            List<GameObject> toRename = new List<GameObject>();
            GameObject ctx = (GameObject)menuCommand.context;
            if (ctx) toRename.Add(ctx);

            for (int i = 0; i < Selection.gameObjects.Length; i++)
            {
                if (!toRename.Contains(Selection.gameObjects[i])) toRename.Add(Selection.gameObjects[i]);
            }

            int objects = 0;
            for (int i = 0; i < toRename.Count; i++)
            {
                if (toRename[i] == null) continue;
                string assetPath = AssetDatabase.GetAssetPath(toRename[i]);
                if (string.IsNullOrEmpty(assetPath)) continue;

                AssetDatabase.RenameAsset(assetPath, filename + "_" + objects + Path.GetExtension(assetPath));
                objects++;
            }
        }

#if UNITY_2019_4_OR_NEWER
        [MenuItem("Assets/Utilities/Add name prefixes for selection", true)]
        static bool SetPrefixesCheck(MenuCommand menuCommand)
        { return Selection.objects.Length > 0; }

        [MenuItem("Assets/Utilities/Add name prefixes for selection", false)]
        private static void SetPrefixes(MenuCommand menuCommand)
        {
            if (Selection.objects.Length == 0) return;

            for (int i = 0; i < Selection.objects.Length; i++)
            {
                if (Selection.objects[i] == null) continue;

                string assetPath = AssetDatabase.GetAssetPath(Selection.objects[i]);
                if (string.IsNullOrEmpty(assetPath)) continue;

                string prefix = GetPrefix(Selection.objects[i], assetPath);
                if (string.IsNullOrEmpty(prefix)) continue;
                {
                    AssetDatabase.RenameAsset(assetPath, prefix + Selection.objects[i].name);
                }
            }
        }
#endif

    }
}
