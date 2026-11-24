using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace DwarfSimulator.LowPolyWorld
{
    public enum LowPolyRoadStyle
    {
        City,
        Dirt,
        Mountain,
        Cracked,
        Snow
    }

    [Serializable]
    internal struct LowPolyRoadKnot
    {
        public Vector3 position;
        public bool manualTangents;
        public Vector3 tangentIn;
        public Vector3 tangentOut;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(SplineContainer))]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class LowPolyRoad : MonoBehaviour
    {
        [Header("Trace")]
        public LowPolyRoadStyle style = LowPolyRoadStyle.Dirt;
        [Tooltip("Facultatif. Si vide, le générateur cherche un Terrain sous la spline dans la scène.")]
        public Terrain terrainSource;
        [SerializeField, HideInInspector] private List<Transform> controlPoints = new List<Transform>();
        [SerializeField, HideInInspector] private List<LowPolyRoadKnot> knots = new List<LowPolyRoadKnot>();
        [Tooltip("Minimum de subdivisions entre deux points. La distance ajoute automatiquement les subdivisions necessaires.")]
        [Min(1)] public int segmentsPerSpan = 4;
        [Tooltip("Longueur maximale d'une section de route. Une petite valeur ameliore les virages mais ajoute des triangles.")]
        [Min(1f)] public float maximumSegmentLength = 5f;
        [SerializeField, HideInInspector] private float cornerTension = 0.3f;
        [Min(0.5f)] public float width = 5f;
        [Min(0f)] public float surfaceOffset = 0.08f;
        [Tooltip("Utilise le tracé horizontal de la spline et ajuste le profil de la chaussée au relief.")]
        public bool followTerrain = true;
        [Tooltip("Espacement maximal des mesures du relief, en mètres.")]
        [Min(0.25f)] public float terrainSampleSpacing = 1f;
        [Tooltip("Adoucit les changements de pente sur cette distance, en mètres.")]
        [Range(0f, 12f)] public float profileSmoothing = 4f;
        [Tooltip("Dévers maximal de la chaussée, en pourcentage.")]
        [Range(0f, 25f)] public float maximumCrossfall = 7f;

        [Header("Accotements et matiere")]
        [Min(0f)] public float shoulderWidth = 0.8f;
        [Min(0f)] public float shoulderDrop = 0.22f;
        public bool generateEmbankment = true;
        [Min(0.25f)] public float maximumFillDepth = 50f;
        [Tooltip("Largeur horizontale du talus par metre de denivele.")]
        [Range(0.25f, 3f)] public float embankmentSlope = 1.15f;
        [Min(1f)] public float maximumEmbankmentWidth = 18f;
        [Tooltip("Irregularite verticale des facettes du talus.")]
        [Range(0f, 2f)] public float embankmentRoughness = 0.35f;
        public LayerMask fallbackSurfaceLayers = ~0;

        [Header("Aspect")]
        public int surfaceSeed = 1337;
        [Range(0f, 0.25f)] public float colorVariation = 0.035f;
        public LowPolyPalette palette = new LowPolyPalette();

        [HideInInspector] public MeshFilter meshFilter;
        [HideInInspector] public MeshRenderer meshRenderer;
        [HideInInspector] public MeshCollider meshCollider;
        [HideInInspector] public string generatedMeshGuid;

        private void Reset()
        {
            CacheComponents();
            ApplyStylePreset();
        }

        public void CacheComponents()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();
        }

        public bool HasLegacySpline
        {
            get
            {
                if (knots.Count >= 2)
                    return true;
                int count = 0;
                foreach (Transform point in controlPoints)
                {
                    if (point != null && ++count >= 2)
                        return true;
                }
                return false;
            }
        }

        public void CopyLegacySpline(Spline destination)
        {
            var source = knots;
            if (source.Count < 2)
            {
                source = new List<LowPolyRoadKnot>();
                foreach (Transform point in controlPoints)
                {
                    if (point != null)
                        source.Add(new LowPolyRoadKnot { position = transform.InverseTransformPoint(point.position) });
                }
            }

            for (int i = 0; i < source.Count; i++)
            {
                LowPolyRoadKnot knot = source[i];
                Vector3 incoming = knot.tangentIn;
                Vector3 outgoing = knot.tangentOut;
                if (!knot.manualTangents)
                {
                    Vector3 previous = i > 0 ? knot.position - source[i - 1].position : Vector3.zero;
                    Vector3 next = i + 1 < source.Count ? source[i + 1].position - knot.position : Vector3.zero;
                    Vector3 direction = (previous.normalized + next.normalized).normalized;
                    float strength = (1f - cornerTension) / 3f;
                    incoming = -direction * previous.magnitude * strength;
                    outgoing = direction * next.magnitude * strength;
                }
                destination.Add(new BezierKnot(knot.position, incoming, outgoing), TangentMode.Broken);
            }
        }

        public void ApplyStylePreset()
        {
            switch (style)
            {
                case LowPolyRoadStyle.City:
                    width = 7f;
                    shoulderWidth = 0.45f;
                    shoulderDrop = 0.16f;
                    segmentsPerSpan = 4;
                    maximumSegmentLength = 4f;
                    embankmentSlope = 1f;
                    embankmentRoughness = 0.18f;
                    break;
                case LowPolyRoadStyle.Mountain:
                    width = 4.5f;
                    shoulderWidth = 1.25f;
                    shoulderDrop = 0.35f;
                    segmentsPerSpan = 5;
                    maximumSegmentLength = 3.5f;
                    embankmentSlope = 1.4f;
                    embankmentRoughness = 0.55f;
                    break;
                case LowPolyRoadStyle.Cracked:
                    width = 6f;
                    shoulderWidth = 0.65f;
                    shoulderDrop = 0.20f;
                    segmentsPerSpan = 5;
                    maximumSegmentLength = 2.5f;
                    embankmentSlope = 1.15f;
                    embankmentRoughness = 0.4f;
                    break;
                case LowPolyRoadStyle.Snow:
                    width = 4.5f;
                    shoulderWidth = 0.95f;
                    shoulderDrop = 0.18f;
                    segmentsPerSpan = 4;
                    maximumSegmentLength = 3.5f;
                    embankmentSlope = 1.35f;
                    embankmentRoughness = 0.35f;
                    break;
                default:
                    width = 5f;
                    shoulderWidth = 0.9f;
                    shoulderDrop = 0.24f;
                    segmentsPerSpan = 4;
                    maximumSegmentLength = 4.5f;
                    embankmentSlope = 1.2f;
                    embankmentRoughness = 0.42f;
                    break;
            }
        }
    }
}
