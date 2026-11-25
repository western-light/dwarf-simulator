using System;
using System.Collections.Generic;
using UnityEngine;

namespace DwarfSimulator.LowPolyWorld
{
    public enum LowPolyPaintLayer
    {
        Path,
        Dirt,
        Grass,
        Rock,
        Snow
    }

    [Serializable]
    public struct LowPolyPaintStroke
    {
        public Vector2 localPosition;
        public float radius;
        public float opacity;
        public float hardness;
        public LowPolyPaintLayer layer;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class LowPolyTerrainMesh : MonoBehaviour
    {
        [Header("Source non destructive")]
        public Terrain sourceTerrain;
        [Tooltip("Affiche le terrain low-poly et recale les arbres peints sur ses facettes. Le Terrain Unity reste editable.")]
        public bool hideSourceRendering = true;

        [Header("Geometrie")]
        [Range(9, 257)] public int resolution = 65;
        [Tooltip("Decalage aleatoire horizontal des sommets, en proportion d'une cellule.")]
        [Range(0f, 0.35f)] public float vertexJitter = 0.12f;
        public int seed = 1337;
        [Tooltip("Laisse desactive pour reutiliser le TerrainCollider source, beaucoup moins couteux.")]
        public bool useGeneratedMeshCollider;

        [Header("Repartition des couleurs")]
        [Range(0f, 1f)] public float rockHeight = 0.50f;
        [Range(0f, 1f)] public float snowHeight = 0.72f;
        [Tooltip("Altitude normalisée sous laquelle le terrain prend la couleur du sable. -1 désactive le sable.")]
        [Range(-1f, 1f)] public float sandHeight = -1f;
        public Color shoreSand = new Color(0.80f, 0.68f, 0.46f, 1f);
        [Range(0f, 90f)] public float rockSlope = 38f;
        [Range(0f, 0.25f)] public float colorVariation = 0.07f;
        public LowPolyPalette palette = new LowPolyPalette();

        [Header("Peinture low-poly")]
        public LowPolyPaintLayer paintLayer = LowPolyPaintLayer.Path;
        [Min(0.5f)] public float brushRadius = 8f;
        [Range(0.05f, 1f)] public float brushOpacity = 0.8f;
        [Range(0f, 1f)] public float brushHardness = 0.65f;
        [HideInInspector] public List<LowPolyPaintStroke> paintStrokes = new List<LowPolyPaintStroke>();

        [HideInInspector] public MeshFilter meshFilter;
        [HideInInspector] public MeshRenderer meshRenderer;
        [HideInInspector] public MeshCollider meshCollider;
        [HideInInspector] public string generatedMeshGuid;
        [HideInInspector] public GameObject generatedTrees;
        [HideInInspector] public Terrain previewSource;
        [HideInInspector] public float sourceTreeDistance;
        [HideInInspector] public bool sourceTreeColliders;
        [HideInInspector] public bool sourceColliderEnabled;

        private void Reset()
        {
            CacheComponents();
        }

        public void CacheComponents()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();
        }
    }
}
