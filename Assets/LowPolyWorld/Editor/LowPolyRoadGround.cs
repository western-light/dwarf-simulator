using System.Collections.Generic;
using UnityEngine;

namespace DwarfSimulator.LowPolyWorld.Editor
{
    // Index the rendered triangles once per bake, even when their MeshCollider is disabled.
    internal sealed class LowPolyRoadGround
    {
        private const int GridSize = 48;
        private readonly LowPolyRoad road;
        private readonly Terrain source;
        private readonly List<int>[] cells;
        private readonly Vector3[] vertices;
        private readonly Color32[] colors;
        private readonly int[] indices;
        private readonly Bounds bounds;
        private readonly int[] visited;
        private readonly Vector3[] clipA = new Vector3[8];
        private readonly Vector3[] clipB = new Vector3[8];
        private int visit;
        internal Terrain Source => source;

        internal LowPolyRoadGround(LowPolyRoad road, List<Vector3> route)
        {
            this.road = road;
            source = road.terrainSource != null ? road.terrainSource : FindTerrain(road, route);
            if (source == null)
                return;

            foreach (var terrain in source.GetComponentsInChildren<LowPolyTerrainMesh>())
            {
                if (terrain.sourceTerrain != source || terrain.meshRenderer == null ||
                    !terrain.meshRenderer.enabled || terrain.meshFilter == null)
                    continue;

                Mesh mesh = terrain.meshFilter.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                    continue;

                bounds = terrain.meshRenderer.bounds;
                vertices = mesh.vertices;
                colors = mesh.colors32;
                indices = mesh.triangles;
                visited = new int[indices.Length / 3];
                cells = new List<int>[GridSize * GridSize];
                Matrix4x4 toWorld = terrain.meshFilter.transform.localToWorldMatrix;
                Vector3 min = bounds.min;
                Vector3 size = bounds.size;
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = toWorld.MultiplyPoint3x4(vertices[i]);

                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 a = vertices[indices[i]];
                    Vector3 b = vertices[indices[i + 1]];
                    Vector3 c = vertices[indices[i + 2]];
                    int x0 = Cell(Mathf.Min(a.x, Mathf.Min(b.x, c.x)), min.x, size.x);
                    int x1 = Cell(Mathf.Max(a.x, Mathf.Max(b.x, c.x)), min.x, size.x);
                    int z0 = Cell(Mathf.Min(a.z, Mathf.Min(b.z, c.z)), min.z, size.z);
                    int z1 = Cell(Mathf.Max(a.z, Mathf.Max(b.z, c.z)), min.z, size.z);
                    for (int z = z0; z <= z1; z++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            int cell = x + z * GridSize;
                            if (cells[cell] == null)
                                cells[cell] = new List<int>();
                            cells[cell].Add(i);
                        }
                    }
                }
                break;
            }
        }

        private static Terrain FindTerrain(LowPolyRoad road, List<Vector3> route)
        {
            Terrain best = null;
            int bestCoverage = 0;
            foreach (GameObject root in road.gameObject.scene.GetRootGameObjects())
            {
                foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>())
                {
                    if (terrain.terrainData == null || terrain.GetComponentInParent<LowPolyRoad>() != null)
                        continue;
                    int coverage = 0;
                    Vector3 origin = terrain.transform.position;
                    Vector3 size = terrain.terrainData.size;
                    foreach (Vector3 point in route)
                        if (Inside(point - origin, size))
                            coverage++;
                    if (coverage <= bestCoverage)
                        continue;
                    best = terrain;
                    bestCoverage = coverage;
                }
            }
            return best;
        }

        internal bool Sample(Vector3 point, out float height)
        {
            if (SampleMesh(point, out height, out _, out _))
                return true;

            if (source != null && source.terrainData != null)
            {
                Vector3 local = point - source.transform.position;
                Vector3 size = source.terrainData.size;
                if (Inside(local, size))
                {
                    height = source.terrainData.GetInterpolatedHeight(local.x / size.x, local.z / size.z) +
                        source.transform.position.y;
                    return true;
                }
            }

            float range = road.maximumFillDepth;
            var hits = Physics.RaycastAll(point + Vector3.up * range, Vector3.down, range * 2f,
                road.fallbackSurfaceLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            foreach (RaycastHit hit in hits)
            {
                if (hit.distance >= nearest || hit.collider.GetComponentInParent<LowPolyRoad>() != null)
                    continue;
                nearest = hit.distance;
                height = hit.point.y;
            }
            return !float.IsPositiveInfinity(nearest);
        }

        private static bool Inside(Vector3 local, Vector3 size) =>
            local.x >= 0f && local.z >= 0f && local.x <= size.x && local.z <= size.z;

        internal Color GetSurfaceTint(Vector3 point, Color fallback)
        {
            if (colors == null || colors.Length != vertices.Length ||
                !SampleMesh(point, out _, out int triangle, out Vector2 uv))
                return fallback;

            Color a = colors[indices[triangle]];
            Color b = colors[indices[triangle + 1]];
            Color c = colors[indices[triangle + 2]];
            return a * (1f - uv.x - uv.y) + b * uv.x + c * uv.y;
        }

        internal float RequiredLift(Vector3 a, Vector3 b, Vector3 c, float clearance)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            float area = CrossXZ(ab, ac);
            if (cells == null || Mathf.Abs(area) < 0.000001f)
                return 0f;

            Vector3 min = Vector3.Min(a, Vector3.Min(b, c));
            Vector3 max = Vector3.Max(a, Vector3.Max(b, c));
            if (min.x > bounds.max.x || max.x < bounds.min.x || min.z > bounds.max.z || max.z < bounds.min.z)
                return 0f;

            int x0 = Cell(min.x, bounds.min.x, bounds.size.x);
            int x1 = Cell(max.x, bounds.min.x, bounds.size.x);
            int z0 = Cell(min.z, bounds.min.z, bounds.size.z);
            int z1 = Cell(max.z, bounds.min.z, bounds.size.z);
            int stamp = ++visit;
            float lift = 0f;
            float side = Mathf.Sign(area);
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    List<int> candidates = cells[x + z * GridSize];
                    if (candidates == null)
                        continue;
                    foreach (int index in candidates)
                    {
                        int triangle = index / 3;
                        if (visited[triangle] == stamp)
                            continue;
                        visited[triangle] = stamp;
                        clipA[0] = vertices[indices[index]];
                        clipA[1] = vertices[indices[index + 1]];
                        clipA[2] = vertices[indices[index + 2]];
                        int count = ClipToEdge(clipA, 3, clipB, a, b, side);
                        count = ClipToEdge(clipB, count, clipA, b, c, side);
                        count = ClipToEdge(clipA, count, clipB, c, a, side);
                        for (int i = 0; i < count; i++)
                        {
                            Vector3 point = clipB[i];
                            Vector3 ap = point - a;
                            float roadHeight = a.y + (ab.y * CrossXZ(ap, ac) + ac.y * CrossXZ(ab, ap)) / area;
                            lift = Mathf.Max(lift, point.y + clearance - roadHeight);
                        }
                    }
                }
            }
            return lift;
        }

        private static int ClipToEdge(Vector3[] input, int count, Vector3[] output, Vector3 a, Vector3 b, float side)
        {
            if (count == 0)
                return 0;
            int written = 0;
            Vector3 previous = input[count - 1];
            float last = CrossXZ(b - a, previous - a) * side;
            for (int i = 0; i < count; i++)
            {
                Vector3 point = input[i];
                float current = CrossXZ(b - a, point - a) * side;
                if ((last < 0f && current > 0f) || (last > 0f && current < 0f))
                    output[written++] = Vector3.Lerp(previous, point, last / (last - current));
                if (current >= 0f)
                    output[written++] = point;
                previous = point;
                last = current;
            }
            return written;
        }

        private static float CrossXZ(Vector3 a, Vector3 b) => a.x * b.z - a.z * b.x;

        private bool SampleMesh(Vector3 point, out float height, out int triangle, out Vector2 uv)
        {
            height = float.NegativeInfinity;
            triangle = -1;
            uv = default;
            if (cells == null || point.x < bounds.min.x || point.x > bounds.max.x ||
                point.z < bounds.min.z || point.z > bounds.max.z)
                return false;

            int x = Cell(point.x, bounds.min.x, bounds.size.x);
            int z = Cell(point.z, bounds.min.z, bounds.size.z);
            List<int> triangles = cells[x + z * GridSize];
            if (triangles == null)
                return false;

            foreach (int index in triangles)
            {
                Vector3 a = vertices[indices[index]];
                Vector3 ab = vertices[indices[index + 1]] - a;
                Vector3 ac = vertices[indices[index + 2]] - a;
                Vector3 ap = point - a;
                float area = ab.x * ac.z - ab.z * ac.x;
                if (Mathf.Abs(area) < 0.000001f)
                    continue;
                float u = (ap.x * ac.z - ap.z * ac.x) / area;
                float v = (ab.x * ap.z - ab.z * ap.x) / area;
                float candidate = a.y + ab.y * u + ac.y * v;
                if (u >= -0.00001f && v >= -0.00001f && u + v <= 1.00001f && candidate > height)
                {
                    height = candidate;
                    triangle = index;
                    uv = new Vector2(u, v);
                }
            }
            return !float.IsNegativeInfinity(height);
        }

        private static int Cell(float value, float min, float size)
        {
            return Mathf.Clamp((int)((value - min) / Mathf.Max(size, 0.001f) * GridSize), 0, GridSize - 1);
        }
    }
}
