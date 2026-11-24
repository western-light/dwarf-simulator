using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace DwarfSimulator.LowPolyWorld.Editor
{
    public sealed class LowPolyWorldTool : EditorWindow
    {
        private Vector2 scroll;

        [MenuItem("Tools/Dwarf Simulator/Low Poly World Builder")]
        public static void Open()
        {
            var window = GetWindow<LowPolyWorldTool>("Low Poly World");
            window.minSize = new Vector2(390f, 470f);
        }

        [MenuItem("Tools/Dwarf Simulator/Low Poly/Convertir le Terrain selectionne")]
        private static void ConvertSelectedMenu()
        {
            CreateTerrainMeshFromSelection();
        }

        [MenuItem("GameObject/Dwarf Simulator/Low Poly Road/City", false, 10)]
        private static void CreateCityRoadMenu()
        {
            CreateRoad(LowPolyRoadStyle.City);
        }

        [MenuItem("GameObject/Dwarf Simulator/Low Poly Road/Dirt", false, 11)]
        private static void CreateDirtRoadMenu()
        {
            CreateRoad(LowPolyRoadStyle.Dirt);
        }

        [MenuItem("GameObject/Dwarf Simulator/Low Poly Road/Mountain", false, 12)]
        private static void CreateMountainRoadMenu()
        {
            CreateRoad(LowPolyRoadStyle.Mountain);
        }

        [MenuItem("GameObject/Dwarf Simulator/Low Poly Road/Fissuree", false, 13)]
        private static void CreateCrackedRoadMenu() => CreateRoad(LowPolyRoadStyle.Cracked);

        [MenuItem("GameObject/Dwarf Simulator/Low Poly Road/Neige", false, 14)]
        private static void CreateSnowRoadMenu() => CreateRoad(LowPolyRoadStyle.Snow);

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(10f);
            GUILayout.Label("Low Poly World Builder", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Le Terrain Unity reste ta source de sculpture. Le mesh final utilise des triangles independants, " +
                "des normales plates et une palette sans textures photo.", MessageType.Info);

            EditorGUILayout.Space(8f);
            GUILayout.Label("1. Terrain", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Selectionne un objet Terrain dans la Hierarchy puis genere le rendu low-poly.", EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Convertir le Terrain selectionne", GUILayout.Height(34f)))
            {
                CreateTerrainMeshFromSelection();
            }

            EditorGUILayout.Space(12f);
            GUILayout.Label("2. Routes", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Chaque route utilise les Splines de Unity et se cale sur le terrain à la génération.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Ville", GUILayout.Height(32f)))
            {
                CreateRoad(LowPolyRoadStyle.City);
            }
            if (GUILayout.Button("Terre", GUILayout.Height(32f)))
            {
                CreateRoad(LowPolyRoadStyle.Dirt);
            }
            if (GUILayout.Button("Montagne", GUILayout.Height(32f)))
            {
                CreateRoad(LowPolyRoadStyle.Mountain);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Fissurée", GUILayout.Height(32f)))
                CreateRoad(LowPolyRoadStyle.Cracked);
            if (GUILayout.Button("Neige", GUILayout.Height(32f)))
                CreateRoad(LowPolyRoadStyle.Snow);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(12f);
            GUILayout.Label("3. Peinture manuelle", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Selectionne LP Terrain Mesh puis active Peindre dans la Scene pour tracer chemins, terre, herbe, roche ou neige sans modifier le relief.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(12f);
            GUILayout.Label("Reglages conseilles", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Terrain : Resolution 65 pour une grande carte, 129 pour des facettes plus fines.\n" +
                "Rock slope : 35-45 degres. Snow height : 0.68-0.78.\n" +
                "Route de falaise : augmente Maximum Fill Depth et garde Generate Embankment actif.\n" +
                "Modifie le tracé avec l’outil Splines de Unity, puis clique sur Régénérer la route.",
                MessageType.None);

            EditorGUILayout.Space(12f);
            if (GUILayout.Button("Creer / reparer le materiau partage"))
            {
                Material material = LowPolyWorldMeshBuilder.EnsureMaterial();
                Selection.activeObject = material;
            }

            EditorGUILayout.EndScrollView();
        }

        internal static LowPolyTerrainMesh CreateTerrainMeshFromSelection()
        {
            Terrain terrain = FindSelectedTerrain();
            if (terrain == null)
            {
                EditorUtility.DisplayDialog("Low Poly World", "Selectionne d'abord un Terrain Unity dans la Hierarchy.", "OK");
                return null;
            }

            LowPolyTerrainMesh target = null;
            LowPolyTerrainMesh[] candidates = terrain.GetComponentsInChildren<LowPolyTerrainMesh>(true);
            foreach (LowPolyTerrainMesh candidate in candidates)
            {
                if (candidate.sourceTerrain == terrain)
                {
                    target = candidate;
                    break;
                }
            }

            if (target == null)
            {
                var generated = new GameObject("LP Terrain Mesh");
                Undo.RegisterCreatedObjectUndo(generated, "Creer le terrain low poly");
                generated.transform.SetParent(terrain.transform, false);
                target = Undo.AddComponent<LowPolyTerrainMesh>(generated);
                target.sourceTerrain = terrain;
            }

            LowPolyWorldMeshBuilder.BuildTerrain(target);
            Selection.activeGameObject = target.gameObject;
            SceneView.FrameLastActiveSceneView();
            return target;
        }

        internal static LowPolyRoad CreateRoad(LowPolyRoadStyle style)
        {
            Terrain terrain = FindSelectedTerrain();
            Vector3 origin;
            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 size = terrain.terrainData.size;
                origin = terrain.transform.position + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
                origin.y = terrain.SampleHeight(origin) + terrain.transform.position.y;
            }
            else if (Selection.activeTransform != null)
            {
                origin = Selection.activeTransform.position;
            }
            else
            {
                origin = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            }

            var root = new GameObject("LP Road " + style);
            Undo.RegisterCreatedObjectUndo(root, "Creer une route low poly");
            root.transform.position = origin;
            LowPolyRoad road = Undo.AddComponent<LowPolyRoad>(root);
            road.style = style;
            road.terrainSource = terrain;
            road.ApplyStylePreset();

            float length = terrain != null && terrain.terrainData != null
                ? Mathf.Min(12f, terrain.terrainData.size.z * 0.25f) : 12f;
            var container = root.GetComponent<SplineContainer>();
            var spline = new Spline();
            spline.Add(new BezierKnot(Vector3.back * length), TangentMode.AutoSmooth);
            spline.Add(new BezierKnot(Vector3.zero), TangentMode.AutoSmooth);
            spline.Add(new BezierKnot(Vector3.forward * length), TangentMode.AutoSmooth);
            container.Splines = new[] { spline };
            if (!LowPolyWorldMeshBuilder.TryBuildRoad(road, out string message))
                Debug.LogWarning(message, road);
            Selection.activeGameObject = root;
            SceneView.FrameLastActiveSceneView();
            return road;
        }



        private static Terrain FindSelectedTerrain()
        {
            if (Selection.activeGameObject == null)
            {
                return null;
            }

            Terrain terrain = Selection.activeGameObject.GetComponent<Terrain>();
            if (terrain != null)
            {
                return terrain;
            }

            LowPolyTerrainMesh lowPoly = Selection.activeGameObject.GetComponentInParent<LowPolyTerrainMesh>();
            if (lowPoly != null)
            {
                return lowPoly.sourceTerrain;
            }

            LowPolyRoad road = Selection.activeGameObject.GetComponentInParent<LowPolyRoad>();
            return road != null ? road.terrainSource : null;
        }
    }

    [CustomEditor(typeof(LowPolyTerrainMesh))]
    internal sealed class LowPolyTerrainMeshEditor : UnityEditor.Editor
    {
        private bool paintMode;
        private bool painting;
        private Vector3 requestedTerrainSize;
        private Vector3 lastPaintWorld = new Vector3(float.PositiveInfinity, 0f, 0f);

        private void OnEnable()
        {
            var terrainMesh = (LowPolyTerrainMesh)target;
            if (terrainMesh.paintStrokes == null)
            {
                terrainMesh.paintStrokes = new System.Collections.Generic.List<LowPolyPaintStroke>();
            }
            if (terrainMesh.sourceTerrain != null && terrainMesh.sourceTerrain.terrainData != null)
            {
                requestedTerrainSize = terrainMesh.sourceTerrain.terrainData.size;
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var terrainMesh = (LowPolyTerrainMesh)target;

            if (terrainMesh.sourceTerrain != null && terrainMesh.sourceTerrain.terrainData != null)
            {
                EditorGUILayout.Space(8f);
                GUILayout.Label("Taille physique de la carte", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "La conversion garde exactement la taille du Terrain source. Resolution regle la taille des facettes, pas celle de la carte.",
                    MessageType.Info);
                requestedTerrainSize = EditorGUILayout.Vector3Field("Largeur / Hauteur / Longueur", requestedTerrainSize);
                if (GUILayout.Button("Appliquer cette taille et regenerer"))
                {
                    TerrainData data = terrainMesh.sourceTerrain.terrainData;
                    Undo.RecordObject(data, "Changer la taille du Terrain");
                    data.size = new Vector3(
                        Mathf.Max(1f, requestedTerrainSize.x),
                        Mathf.Max(1f, requestedTerrainSize.y),
                        Mathf.Max(1f, requestedTerrainSize.z));
                    EditorUtility.SetDirty(data);
                    LowPolyWorldMeshBuilder.BuildTerrain(terrainMesh);
                }
            }

            EditorGUILayout.Space(8f);
            GUILayout.Label("Peinture manuelle", EditorStyles.boldLabel);
            paintMode = GUILayout.Toggle(paintMode, paintMode ? "Quitter le mode peinture" : "Peindre dans la Scene",
                "Button", GUILayout.Height(30f));
            if (paintMode)
            {
                EditorGUILayout.HelpBox(
                    "Clic gauche et glisse sur le terrain. Alt + souris continue de controler la camera. La peinture ne modifie pas les hauteurs.",
                    MessageType.Info);
                SceneView.RepaintAll();
            }

            if (terrainMesh.paintStrokes.Count > 0 && GUILayout.Button("Effacer toute la peinture manuelle"))
            {
                Undo.RecordObject(terrainMesh, "Effacer la peinture low poly");
                terrainMesh.paintStrokes.Clear();
                LowPolyWorldMeshBuilder.BuildTerrain(terrainMesh);
            }

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Regenerer le terrain low-poly", GUILayout.Height(32f)))
            {
                LowPolyWorldMeshBuilder.BuildTerrain(terrainMesh);
            }
            if (GUILayout.Button("Afficher de nouveau le Terrain source"))
            {
                if (terrainMesh.sourceTerrain != null)
                {
                    Undo.RecordObject(terrainMesh.sourceTerrain, "Afficher le Terrain source");
                    terrainMesh.sourceTerrain.drawHeightmap = true;
                    EditorUtility.SetDirty(terrainMesh.sourceTerrain);
                }
            }
        }

        private void OnSceneGUI()
        {
            if (!paintMode)
            {
                return;
            }

            var terrainMesh = (LowPolyTerrainMesh)target;
            Event current = Event.current;
            if (current.rawType == EventType.MouseUp)
            {
                painting = false;
            }
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (!TryPaintRaycast(terrainMesh, ray, out RaycastHit hit))
            {
                return;
            }

            Handles.color = new Color(1f, 0.65f, 0.12f, 0.9f);
            Handles.DrawWireDisc(hit.point, hit.normal, terrainMesh.brushRadius);
            Handles.color = new Color(1f, 0.65f, 0.12f, 0.18f);
            Handles.DrawSolidDisc(hit.point, hit.normal, terrainMesh.brushRadius * terrainMesh.brushHardness);

            if (current.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (current.alt || current.button != 0)
            {
                return;
            }

            if (current.type == EventType.MouseDown)
            {
                painting = true;
                lastPaintWorld = new Vector3(float.PositiveInfinity, 0f, 0f);
                Undo.RecordObject(terrainMesh, "Peindre le terrain low poly");
                if (terrainMesh.meshFilter != null && terrainMesh.meshFilter.sharedMesh != null)
                {
                    Undo.RecordObject(terrainMesh.meshFilter.sharedMesh, "Peindre le terrain low poly");
                }
                PaintAt(terrainMesh, hit.point);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && painting)
            {
                float spacing = Mathf.Max(0.25f, terrainMesh.brushRadius * 0.2f);
                if (Vector3.Distance(lastPaintWorld, hit.point) >= spacing)
                {
                    PaintAt(terrainMesh, hit.point);
                }
                current.Use();
            }
            else if (current.rawType == EventType.MouseUp && painting)
            {
                painting = false;
                EditorUtility.SetDirty(terrainMesh);
                current.Use();
            }
        }

        private static bool TryPaintRaycast(LowPolyTerrainMesh terrainMesh, Ray ray, out RaycastHit hit)
        {
            if (terrainMesh.sourceTerrain != null)
            {
                TerrainCollider terrainCollider = terrainMesh.sourceTerrain.GetComponent<TerrainCollider>();
                if (terrainCollider != null && terrainCollider.enabled && terrainCollider.Raycast(ray, out hit, 100000f))
                {
                    return true;
                }
            }

            if (terrainMesh.meshCollider != null && terrainMesh.meshCollider.enabled &&
                terrainMesh.meshCollider.Raycast(ray, out hit, 100000f))
            {
                return true;
            }

            hit = default;
            return false;
        }

        private void PaintAt(LowPolyTerrainMesh terrainMesh, Vector3 worldPoint)
        {
            Vector3 local = terrainMesh.transform.InverseTransformPoint(worldPoint);
            var stroke = new LowPolyPaintStroke
            {
                localPosition = new Vector2(local.x, local.z),
                radius = terrainMesh.brushRadius,
                opacity = terrainMesh.brushOpacity,
                hardness = terrainMesh.brushHardness,
                layer = terrainMesh.paintLayer
            };
            terrainMesh.paintStrokes.Add(stroke);
            LowPolyWorldMeshBuilder.PaintTerrain(terrainMesh, stroke);
            lastPaintWorld = worldPoint;
            SceneView.RepaintAll();
        }
    }

    [CustomEditor(typeof(LowPolyRoad))]
    internal sealed class LowPolyRoadEditor : UnityEditor.Editor
    {
        private string buildMessage;
        private MessageType buildMessageType;

        public override void OnInspectorGUI()
        {
            var road = (LowPolyRoad)target;
            var previousStyle = road.style;
            if (DrawDefaultInspector())
            {
                if (road.style != previousStyle)
                {
                    Undo.RecordObject(road, "Appliquer le style de route");
                    road.ApplyStylePreset();
                    EditorUtility.SetDirty(road);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(road);
                }
                buildMessage = null;
            }

            var container = road.GetComponent<SplineContainer>();
            EditorGUILayout.Space();
            int paths = 0;
            if (container != null)
                foreach (Spline spline in container.Splines)
                    if (spline != null && spline.Count >= 2)
                        paths++;
            if (paths > 1)
                EditorGUILayout.LabelField("Tracés à générer", paths.ToString());
            if (paths == 0 && road.HasLegacySpline)
                EditorGUILayout.HelpBox("La prochaine génération convertira l’ancien tracé en spline Unity si le conteneur est vide.", MessageType.Info);
            else if (paths == 0)
                EditorGUILayout.HelpBox("Dessine au moins deux nœuds avec l’outil Splines de Unity sur cet objet.", MessageType.Info);

            if (GUILayout.Button("Réappliquer les dimensions du style"))
            {
                Undo.RecordObject(road, "Réappliquer les dimensions du style");
                road.ApplyStylePreset();
                EditorUtility.SetDirty(road);
                PrefabUtility.RecordPrefabInstancePropertyModifications(road);
                buildMessage = null;
            }

            if (GUILayout.Button("Régénérer la route", GUILayout.Height(32f)))
            {
                bool success = LowPolyWorldMeshBuilder.TryBuildRoad(road, out buildMessage);
                buildMessageType = success ? MessageType.Info : MessageType.Error;
                SceneView.RepaintAll();
            }

            if (!string.IsNullOrEmpty(buildMessage))
                EditorGUILayout.HelpBox(buildMessage, buildMessageType);
        }
    }
}
