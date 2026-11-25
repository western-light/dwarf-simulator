using System.Collections.Generic;
using UnityEngine;

namespace DwarfSimulator.LowPolyWorld.Editor
{
    internal sealed class LowPolyRoadGround
    {
        private readonly LowPolyRoad road;
        private readonly Terrain source;
        private readonly LowPolyTerrainSurface surface;
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

                surface = new LowPolyTerrainSurface(terrain.meshFilter);
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
            if (surface != null && surface.Sample(point, out height, out _, out _))
                return true;

            height = float.NegativeInfinity;
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

        internal Color GetSurfaceTint(Vector3 point, Color fallback) =>
            surface != null ? surface.GetSurfaceTint(point, fallback) : fallback;

        internal float RequiredLift(Vector3 a, Vector3 b, Vector3 c, float clearance) =>
            surface != null ? surface.RequiredLift(a, b, c, clearance) : 0f;
    }
}
