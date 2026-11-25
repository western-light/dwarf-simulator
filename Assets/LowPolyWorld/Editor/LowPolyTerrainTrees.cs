using UnityEditor;
using UnityEngine;

namespace DwarfSimulator.LowPolyWorld.Editor
{
    internal static class LowPolyTerrainTrees
    {
        private const string UndoLabel = "Mettre a jour le terrain low-poly";

        internal static void Rebuild(LowPolyTerrainMesh target)
        {
            if (target.generatedTrees != null && target.generatedTrees.transform.parent == target.transform)
                Undo.DestroyObjectImmediate(target.generatedTrees);
            target.generatedTrees = null;

            Terrain source = target.sourceTerrain;
            TerrainData data = source.terrainData;
            if (!target.hideSourceRendering || !source.drawTreesAndFoliage || data.treeInstanceCount == 0)
                return;

            var surface = new LowPolyTerrainSurface(target.meshFilter);
            var root = new GameObject("LP Trees");
            Undo.RegisterCreatedObjectUndo(root, UndoLabel);
            Undo.SetTransformParent(root.transform, target.transform, UndoLabel);
            root.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            target.generatedTrees = root;

            TreePrototype[] prototypes = data.treePrototypes;
            int offSurface = 0;
            foreach (TreeInstance tree in data.treeInstances)
            {
                if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length)
                    continue;
                GameObject prefab = prototypes[tree.prototypeIndex].prefab;
                if (prefab == null)
                    continue;

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                if (instance == null)
                    continue;
                Undo.RegisterCreatedObjectUndo(instance, UndoLabel);
                Vector3 point = source.transform.position + Vector3.Scale(tree.position, data.size);
                if (surface.Sample(point, out float height, out _, out _))
                    point.y = height;
                else
                {
                    point.y = source.SampleHeight(point) + source.transform.position.y;
                    offSurface++;
                }
                Quaternion rotation = Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f);
                instance.transform.SetPositionAndRotation(point, rotation * prefab.transform.rotation);
                instance.transform.localScale = Vector3.Scale(prefab.transform.localScale,
                    new Vector3(tree.widthScale, tree.heightScale, tree.widthScale));
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);

                if (!source.preserveTreePrototypeLayers)
                    foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.gameObject.layer == source.gameObject.layer)
                            continue;
                        child.gameObject.layer = source.gameObject.layer;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
                    }
            }
            if (offSurface > 0)
                Debug.LogWarning($"Low Poly World : {offSurface} arbre(s) hors du mesh utilisent la hauteur du Terrain source.", target);
        }

        internal static void SetPreview(LowPolyTerrainMesh target, bool showSource)
        {
            Terrain source = target.sourceTerrain;
            if (source == null)
                return;

            target.CacheComponents();
            Undo.RecordObjects(new Object[] { target, target.meshRenderer, target.meshCollider }, UndoLabel);
            RestoreSourceSettings(target);
            Undo.RecordObject(source, UndoLabel);
            bool useMeshCollider = !showSource && target.useGeneratedMeshCollider &&
                target.meshCollider.sharedMesh != null;
            bool showBakedTrees = !showSource && target.hideSourceRendering && target.generatedTrees != null &&
                target.generatedTrees.transform.childCount > 0;
            if (showBakedTrees || useMeshCollider)
            {
                target.previewSource = source;
                target.sourceTreeDistance = source.treeDistance;
                if (source.TryGetComponent(out TerrainCollider collider))
                {
                    target.sourceColliderEnabled = collider.enabled;
                    var settings = new SerializedObject(collider);
                    SerializedProperty trees = settings.FindProperty("m_EnableTreeColliders");
                    target.sourceTreeColliders = trees.boolValue;
                    Undo.RecordObject(collider, UndoLabel);
                    if (showBakedTrees)
                        trees.boolValue = false;
                    settings.ApplyModifiedProperties();
                    if (useMeshCollider)
                        collider.enabled = false;
                }
                if (showBakedTrees)
                    source.treeDistance = 0f;
            }

            source.drawHeightmap = showSource || !target.hideSourceRendering;
            target.meshRenderer.enabled = !showSource;
            target.meshCollider.enabled = useMeshCollider;
            if (target.generatedTrees != null)
            {
                Undo.RecordObject(target.generatedTrees, UndoLabel);
                target.generatedTrees.SetActive(showBakedTrees);
            }
            EditorUtility.SetDirty(source);
            EditorUtility.SetDirty(target);
        }

        private static void RestoreSourceSettings(LowPolyTerrainMesh target)
        {
            Terrain source = target.previewSource;
            if (source == null)
                return;

            Undo.RecordObject(source, UndoLabel);
            source.treeDistance = target.sourceTreeDistance;
            source.drawHeightmap = true;
            if (source.TryGetComponent(out TerrainCollider collider))
            {
                Undo.RecordObject(collider, UndoLabel);
                collider.enabled = target.sourceColliderEnabled;
                var settings = new SerializedObject(collider);
                settings.FindProperty("m_EnableTreeColliders").boolValue = target.sourceTreeColliders;
                settings.ApplyModifiedProperties();
            }
            target.previewSource = null;
        }
    }
}
