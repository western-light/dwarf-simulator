using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

namespace DwarfSimulator.LowPolyWorld.Editor
{
    internal static class LowPolyWorldMeshBuilder
    {
        internal const string GeneratedRoot = "Assets/LowPolyWorld/Generated";
        private const string ShaderName = "Dwarf Simulator/Low Poly Vertex Color";

        private struct RoadRow
        {
            public Vector3 outerLeft;
            public Vector3 roadLeft;
            public Vector3 roadRight;
            public Vector3 outerRight;
            public Vector3 talusMidLeft;
            public Vector3 talusMidRight;
            public Vector3 toeLeft;
            public Vector3 toeRight;
            public float distance;
            public Vector3 normal;
            public float centerAcross;
            public float patternDistance;
            public Color leftGroundTint;
            public Color rightGroundTint;
        }

        internal static void BuildTerrain(LowPolyTerrainMesh target)
        {
            if (target == null || target.sourceTerrain == null || target.sourceTerrain.terrainData == null)
            {
                Debug.LogWarning("Low Poly World : assigne un Terrain source avant de generer.", target);
                return;
            }

            target.CacheComponents();
            TerrainData data = target.sourceTerrain.terrainData;
            int resolution = Mathf.Clamp(target.resolution, 9, 257);
            int triangleCount = (resolution - 1) * (resolution - 1) * 2;
            var vertices = new List<Vector3>(triangleCount * 3);
            var normals = new List<Vector3>(triangleCount * 3);
            var colors = new List<Color>(triangleCount * 3);
            var triangles = new List<int>(triangleCount * 3);
            var uvs = new List<Vector2>(triangleCount * 3);

            for (int z = 0; z < resolution - 1; z++)
            {
                for (int x = 0; x < resolution - 1; x++)
                {
                    Vector3 p00 = TerrainPoint(data, resolution, x, z, target.vertexJitter, target.seed);
                    Vector3 p10 = TerrainPoint(data, resolution, x + 1, z, target.vertexJitter, target.seed);
                    Vector3 p01 = TerrainPoint(data, resolution, x, z + 1, target.vertexJitter, target.seed);
                    Vector3 p11 = TerrainPoint(data, resolution, x + 1, z + 1, target.vertexJitter, target.seed);

                    if (((x + z) & 1) == 0)
                    {
                        AddTerrainTriangle(p00, p01, p10, data, target, x, z, vertices, normals, colors, triangles, uvs);
                        AddTerrainTriangle(p10, p01, p11, data, target, x + 17, z + 31, vertices, normals, colors, triangles, uvs);
                    }
                    else
                    {
                        AddTerrainTriangle(p00, p01, p11, data, target, x, z, vertices, normals, colors, triangles, uvs);
                        AddTerrainTriangle(p00, p11, p10, data, target, x + 17, z + 31, vertices, normals, colors, triangles, uvs);
                    }
                }
            }

            ApplyPaintStrokes(target, vertices, colors, triangles);

            Mesh mesh = CreateMesh("LP_Terrain_" + target.sourceTerrain.name, vertices, normals, colors, triangles, uvs);
            Mesh savedMesh = SaveMesh(mesh, target.gameObject.name + "_Terrain", ref target.generatedMeshGuid);
            AssignMesh(target.meshFilter, target.meshCollider, savedMesh, target.useGeneratedMeshCollider);
            target.meshRenderer.sharedMaterial = EnsureMaterial();
            target.meshRenderer.receiveShadows = false;
            target.sourceTerrain.drawHeightmap = !target.hideSourceRendering;
            EditorUtility.SetDirty(target.sourceTerrain);
            EditorUtility.SetDirty(target);
        }

        internal static bool TryBuildRoad(LowPolyRoad road, out string message)
        {
            if (road == null || EditorUtility.IsPersistent(road) || !road.gameObject.scene.isLoaded)
            {
                message = "Sélectionne une route ouverte dans la scène.";
                return false;
            }

            if (!TryCreateRoadMesh(road, ResolveRoadSplines(road), out Mesh mesh, out message))
                return false;

            Material material = EnsureMaterial();
            if (material == null)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                message = "Le matériau Low Poly World est indisponible. Vérifie la compilation du shader URP.";
                return false;
            }
            road.CacheComponents();
            Undo.RecordObjects(new UnityEngine.Object[] { road, road.meshFilter, road.meshCollider, road.meshRenderer },
                "Régénérer la route");
            if (!string.IsNullOrEmpty(road.generatedMeshGuid))
            {
                foreach (LowPolyRoad other in Resources.FindObjectsOfTypeAll<LowPolyRoad>())
                {
                    if (other == road || other.generatedMeshGuid != road.generatedMeshGuid)
                        continue;
                    road.generatedMeshGuid = null;
                    break;
                }
            }
            Mesh savedMesh = SaveMesh(mesh, road.gameObject.name + "_Road", ref road.generatedMeshGuid, true);
            AssignMesh(road.meshFilter, road.meshCollider, savedMesh, true);
            road.meshRenderer.sharedMaterial = material;
            road.meshRenderer.receiveShadows = false;
            EditorUtility.SetDirty(road);
            PrefabUtility.RecordPrefabInstancePropertyModifications(road);
            PrefabUtility.RecordPrefabInstancePropertyModifications(road.meshFilter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(road.meshCollider);
            PrefabUtility.RecordPrefabInstancePropertyModifications(road.meshRenderer);
            AssetDatabase.SaveAssetIfDirty(savedMesh);
            return true;
        }

        internal static bool TryCreateRoadMesh(LowPolyRoad road, IReadOnlyList<Spline> splines,
            out Mesh mesh, out string message)
        {
            mesh = null;
            var paths = new List<(Spline Spline, List<Vector3> Points)>();
            var route = new List<Vector3>();
            foreach (Spline spline in splines)
            {
                if (spline == null || spline.Count < 2)
                    continue;
                List<Vector3> centers = SampleSpline(road, spline);
                if (centers == null || route.Count + centers.Count > MaximumRoadSections)
                {
                    message = "Le conteneur dépasse le budget de sections. Divise la route en plusieurs objets.";
                    return false;
                }
                if (centers.Count < (spline.Closed ? 3 : 2))
                {
                    message = "Un des tracés possède des nœuds superposés dans le plan horizontal.";
                    return false;
                }
                paths.Add((spline, centers));
                route.AddRange(centers);
            }
            if (paths.Count == 0)
            {
                message = "Ajoute au moins deux nœuds dans le Spline Container.";
                return false;
            }

            var ground = road.followTerrain || road.generateEmbankment ? new LowPolyRoadGround(road, route) : null;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();
            int sections = 0;
            int misses = 0;
            bool closeLoop = false;
            float steepestGrade = 0f;
            foreach (var path in paths)
            {
                if (road.followTerrain)
                    misses += ProjectRoadCenters(road, ground, path.Points, path.Spline.Closed);
                int spanCount = path.Points.Count - (path.Spline.Closed ? 0 : 1);
                sections += spanCount;
                if (sections > MaximumRoadSections)
                {
                    message = "Le relief demande trop de sections. Divise la route en plusieurs objets.";
                    return false;
                }
                closeLoop |= AppendRoadPath(road, path.Spline, path.Points, ground, ref steepestGrade,
                    vertices, normals, colors, triangles, uvs);
            }

            mesh = CreateMesh("LP_Road_" + road.name, vertices, normals, colors, triangles, uvs);
            message = $"Route générée ({paths.Count} tracé(s), {sections} sections).";
            if (road.followTerrain && ground.Source != null)
                message += $" Terrain utilisé : {ground.Source.name}.";
            if (misses > 0)
                message += $" {misses} points hors terrain : la route continue à la hauteur du raccord ou de la spline.";
            if (road.followTerrain && steepestGrade >= 0.45f)
                message += $" Pente maximale {steepestGrade:P0}. Sur les flancs raides, fais des lacets avec la spline.";
            if (closeLoop)
                message += " Pour raccorder deux extrémités proches, active Closed sur la spline Unity.";
            return true;
        }

        private static bool AppendRoadPath(LowPolyRoad road, Spline spline, List<Vector3> centers,
            LowPolyRoadGround ground, ref float steepestGrade, List<Vector3> vertices, List<Vector3> normals,
            List<Color> colors, List<int> triangles, List<Vector2> uvs)
        {
            var rows = new List<RoadRow>(centers.Count + (spline.Closed ? 1 : 0));
            var leftLimits = new float[centers.Count];
            var rightLimits = new float[centers.Count];
            var leftWidths = new float[centers.Count];
            var rightWidths = new float[centers.Count];
            var directions = new Vector3[centers.Count];
            float distance = 0f;
            for (int i = 0; i < centers.Count; i++)
            {
                Vector3 center = centers[i];
                if (i > 0)
                    distance += Flat(centers[i] - centers[i - 1]).magnitude;

                Vector3 previous = i > 0 ? Flat(center - centers[i - 1]) :
                    spline.Closed ? Flat(center - centers[centers.Count - 1]) : Vector3.zero;
                Vector3 next = i < centers.Count - 1 ? Flat(centers[i + 1] - center) :
                    spline.Closed ? Flat(centers[0] - center) : Vector3.zero;
                Vector3 tangent = (previous.normalized + next.normalized).normalized;
                if (tangent == Vector3.zero)
                    tangent = next.normalized;
                Vector3 right = Vector3.Cross(Vector3.up, tangent);

                float turn = Vector3.Cross(previous, next).y;
                float insideLimit = SafeCornerOffset(previous, next);
                leftLimits[i] = turn < 0f ? insideLimit : float.PositiveInfinity;
                rightLimits[i] = turn > 0f ? insideLimit : float.PositiveInfinity;
                directions[i] = right;
                float halfRoad = road.width * 0.5f;
                leftWidths[i] = Mathf.Min(halfRoad * EdgeWidth(road, distance, 19), leftLimits[i] * 0.92f);
                rightWidths[i] = Mathf.Min(halfRoad * EdgeWidth(road, distance, 37), rightLimits[i] * 0.92f);
                if (road.followTerrain && road.style == LowPolyRoadStyle.City)
                {
                    leftWidths[i] = ClearUrbanEdge(ground, center, -right, leftWidths[i]);
                    rightWidths[i] = ClearUrbanEdge(ground, center, right, rightWidths[i]);
                }
                rows.Add(new RoadRow { distance = distance, patternDistance = distance });
            }
            float totalLength = distance + (spline.Closed ? Flat(centers[0] - centers[centers.Count - 1]).magnitude : 0f);
            StabilizeRoadEdges(centers, directions, leftWidths, spline.Closed, -1f);
            StabilizeRoadEdges(centers, directions, rightWidths, spline.Closed, 1f);
            for (int i = 0; i < rows.Count; i++)
            {
                RoadRow row = rows[i];
                row.roadLeft = centers[i] - directions[i] * leftWidths[i];
                row.roadRight = centers[i] + directions[i] * rightWidths[i];
                row.centerAcross = leftWidths[i] / Mathf.Max(0.001f, leftWidths[i] + rightWidths[i]);
                rows[i] = row;
            }
            if (road.followTerrain)
                FitRoadProfile(road, ground, rows, spline.Closed, totalLength);

            for (int i = 0; i < rows.Count; i++)
            {
                RoadRow row = rows[i];
                float leftSpace = Mathf.Max(0f, leftLimits[i] - leftWidths[i]);
                float rightSpace = Mathf.Max(0f, rightLimits[i] - rightWidths[i]);
                BuildRoadSide(road, ground, row.roadLeft, -directions[i], Mathf.Min(road.shoulderWidth, leftSpace * 0.55f), leftSpace, row.distance, 901,
                    out row.outerLeft, out row.talusMidLeft, out row.toeLeft);
                BuildRoadSide(road, ground, row.roadRight, directions[i], Mathf.Min(road.shoulderWidth, rightSpace * 0.55f), rightSpace, row.distance, 1597,
                    out row.outerRight, out row.talusMidRight, out row.toeRight);
                row.leftGroundTint = ground != null ? ground.GetSurfaceTint(row.toeLeft, road.palette.rockLight) : road.palette.rockLight;
                row.rightGroundTint = ground != null ? ground.GetSurfaceTint(row.toeRight, road.palette.rockLight) : road.palette.rockLight;
                Vector3 before = RowCenter(rows[i > 0 ? i - 1 : spline.Closed ? rows.Count - 1 : i]);
                Vector3 after = RowCenter(rows[i < rows.Count - 1 ? i + 1 : spline.Closed ? 0 : i]);
                row.normal = Vector3.Cross(after - before, row.roadRight - row.roadLeft).normalized;
                if (row.normal.sqrMagnitude < 0.001f)
                    row.normal = Vector3.up;
                rows[i] = row;
            }
            if (spline.Closed)
            {
                var seam = rows[0];
                seam.distance = totalLength;
                rows.Add(seam);
            }

            Color roadColor = RoadColor(road);
            Color shoulderColor = road.style == LowPolyRoadStyle.City ? road.palette.cityShoulder :
                road.style == LowPolyRoadStyle.Snow ? road.palette.snowLight : Color.Lerp(roadColor, road.palette.earthFill, 0.2f);
            shoulderColor.a = 1f;


            for (int i = 0; i < rows.Count - 1; i++)
            {
                RoadRow a = rows[i];
                RoadRow b = rows[i + 1];
                float span = b.distance - a.distance;
                if (span > 0.015f)
                    steepestGrade = Mathf.Max(steepestGrade, Mathf.Abs(RowCenter(a).y - RowCenter(b).y) / span);
                AddFacetedRoadQuad(road.transform, a.outerLeft, b.outerLeft, b.roadLeft, a.roadLeft,
                    shoulderColor, Vector3.zero, Vector3.zero, 1f, i * 17 + 1,
                    vertices, normals, colors, triangles, uvs);
                AddRoadSurface(road, a, b, roadColor, i, vertices, normals, colors, triangles, uvs);
                AddFacetedRoadQuad(road.transform, a.roadRight, b.roadRight, b.outerRight, a.outerRight,
                    shoulderColor, Vector3.zero, Vector3.zero, 1f, i * 17 + 4,
                    vertices, normals, colors, triangles, uvs);

                if (road.generateEmbankment)
                {
                    AddRoadSlopes(road, a, b, vertices, normals, colors, triangles, uvs);
                }
            }

            if (road.generateEmbankment && !spline.Closed)
            {
                AddRoadEnd(road, ground, rows[0], true, vertices, normals, colors, triangles, uvs);
                AddRoadEnd(road, ground, rows[rows.Count - 1], false, vertices, normals, colors, triangles, uvs);
            }
            return !spline.Closed && Flat(centers[centers.Count - 1] - centers[0]).magnitude < road.width &&
                totalLength > road.width * 4f;
        }

        private static IReadOnlyList<Spline> ResolveRoadSplines(LowPolyRoad road)
        {
            var container = road.GetComponent<SplineContainer>();
            if (container != null)
            {
                foreach (Spline spline in container.Splines)
                    if (spline != null && spline.Count > 0)
                        return container.Splines;
            }
            if (!road.HasLegacySpline)
                return Array.Empty<Spline>();

            if (container == null)
                container = Undo.AddComponent<SplineContainer>(road.gameObject);
            Undo.RecordObject(container, "Migrer le tracé vers Unity Splines");
            var migrated = new Spline();
            road.CopyLegacySpline(migrated);
            container.Splines = new[] { migrated };
            EditorUtility.SetDirty(container);
            PrefabUtility.RecordPrefabInstancePropertyModifications(container);
            return container.Splines;
        }

        internal static Material EnsureMaterial()
        {
            EnsureGeneratedFolder();
            const string path = GeneratedRoot + "/M_LowPolyWorld.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError("Low Poly World : shader introuvable : " + ShaderName);
                return material;
            }

            if (material == null)
            {
                material = new Material(shader) { name = "M_LowPolyWorld" };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            material.SetFloat("_ReceiveShadows", 0f);
            material.DisableKeyword("_RECEIVE_SHADOWS");
            EditorUtility.SetDirty(material);

            return material;
        }

        internal static void PaintTerrain(LowPolyTerrainMesh target, LowPolyPaintStroke stroke)
        {
            if (target == null || target.meshFilter == null || target.meshFilter.sharedMesh == null)
            {
                return;
            }

            Mesh mesh = target.meshFilter.sharedMesh;
            var vertices = new List<Vector3>(mesh.vertexCount);
            var colors = new List<Color>(mesh.vertexCount);
            var triangles = new List<int>(mesh.vertexCount);
            mesh.GetVertices(vertices);
            mesh.GetColors(colors);
            mesh.GetTriangles(triangles, 0);
            if (colors.Count != vertices.Count)
            {
                return;
            }

            ApplyPaintStroke(target, stroke, vertices, colors, triangles);
            mesh.SetColors(colors);
            EditorUtility.SetDirty(mesh);
            EditorUtility.SetDirty(target);
        }

        private static Vector3 TerrainPoint(TerrainData data, int resolution, int x, int z, float jitter, int seed)
        {
            float u = x / (float)(resolution - 1);
            float v = z / (float)(resolution - 1);
            if (x > 0 && x < resolution - 1)
            {
                u += (Hash01(x, z, seed) - 0.5f) * jitter / (resolution - 1);
            }
            if (z > 0 && z < resolution - 1)
            {
                v += (Hash01(x, z, seed + 7919) - 0.5f) * jitter / (resolution - 1);
            }

            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);
            return new Vector3(u * data.size.x, data.GetInterpolatedHeight(u, v), v * data.size.z);
        }

        private static void AddTerrainTriangle(
            Vector3 a, Vector3 b, Vector3 c, TerrainData data, LowPolyTerrainMesh settings, int x, int z,
            List<Vector3> vertices, List<Vector3> normals, List<Color> colors, List<int> triangles, List<Vector2> uvs)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            float normalizedHeight = ((a.y + b.y + c.y) / 3f) / Mathf.Max(0.001f, data.size.y);
            float slope = Vector3.Angle(normal, Vector3.up);
            Color baseColor;
            float tone = Mathf.Clamp01(normalizedHeight * 0.65f + 0.25f);

            if (normalizedHeight >= settings.snowHeight)
            {
                baseColor = Color.Lerp(settings.palette.snowShadow, settings.palette.snowLight, tone);
            }
            else if (slope >= settings.rockSlope || normalizedHeight >= settings.rockHeight)
            {
                baseColor = Color.Lerp(settings.palette.rockShadow, settings.palette.rockLight, tone);
            }
            else
            {
                baseColor = Color.Lerp(settings.palette.grassShadow, settings.palette.grassLight, tone);
            }

            Color faceColor = Vary(baseColor, settings.colorVariation, x * 73856093 ^ z * 19349663 ^ settings.seed);
            AddTriangle(a, b, c, normal, faceColor, vertices, normals, colors, triangles, uvs);
        }

        private static void ApplyPaintStrokes(
            LowPolyTerrainMesh target, List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            if (target.paintStrokes == null)
            {
                target.paintStrokes = new List<LowPolyPaintStroke>();
                return;
            }

            foreach (LowPolyPaintStroke stroke in target.paintStrokes)
            {
                ApplyPaintStroke(target, stroke, vertices, colors, triangles);
            }
        }

        private static void ApplyPaintStroke(
            LowPolyTerrainMesh target, LowPolyPaintStroke stroke, List<Vector3> vertices, List<Color> colors,
            List<int> triangles)
        {
            float radius = Mathf.Max(0.01f, stroke.radius);
            float radiusSquared = radius * radius;
            float innerRadius = radius * Mathf.Clamp01(stroke.hardness);
            Color paintColor = PaintColor(target, stroke.layer);

            for (int triangle = 0; triangle + 2 < triangles.Count; triangle += 3)
            {
                int indexA = triangles[triangle];
                int indexB = triangles[triangle + 1];
                int indexC = triangles[triangle + 2];
                Vector3 center3 = (vertices[indexA] + vertices[indexB] + vertices[indexC]) / 3f;
                float deltaX = center3.x - stroke.localPosition.x;
                float deltaZ = center3.z - stroke.localPosition.y;
                float distanceSquared = deltaX * deltaX + deltaZ * deltaZ;
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(distanceSquared);
                float falloff = distance <= innerRadius || Mathf.Approximately(innerRadius, radius)
                    ? 1f
                    : 1f - (distance - innerRadius) / (radius - innerRadius);
                float blend = Mathf.Clamp01(stroke.opacity * falloff);
                Color result = Color.Lerp(colors[indexA], paintColor, blend);
                colors[indexA] = result;
                colors[indexB] = result;
                colors[indexC] = result;
            }
        }

        private static Color PaintColor(LowPolyTerrainMesh target, LowPolyPaintLayer layer)
        {
            switch (layer)
            {
                case LowPolyPaintLayer.Dirt:
                    return target.palette.dirt;
                case LowPolyPaintLayer.Grass:
                    return Color.Lerp(target.palette.grassShadow, target.palette.grassLight, 0.65f);
                case LowPolyPaintLayer.Rock:
                    return Color.Lerp(target.palette.rockShadow, target.palette.rockLight, 0.58f);
                case LowPolyPaintLayer.Snow:
                    return Color.Lerp(target.palette.snowShadow, target.palette.snowLight, 0.72f);
                default:
                    return target.palette.path.a > 0f
                        ? target.palette.path
                        : new Color(0.48f, 0.34f, 0.20f, 1f);
            }
        }

        private static void AddTriangle(
            Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color color,
            List<Vector3> vertices, List<Vector3> normals, List<Color> colors, List<int> triangles, List<Vector2> uvs)
        {
            AddShadedTriangle(a, b, c, normal, normal, normal, color, color, color, vertices, normals, colors, triangles, uvs);
        }

        private static void AddShadedTriangle(
            Vector3 a, Vector3 b, Vector3 c, Vector3 normalA, Vector3 normalB, Vector3 normalC,
            Color colorA, Color colorB, Color colorC,
            List<Vector3> vertices, List<Vector3> normals, List<Color> colors, List<int> triangles, List<Vector2> uvs)
        {
            int index = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            normals.Add(normalA);
            normals.Add(normalB);
            normals.Add(normalC);
            colors.Add(colorA);
            colors.Add(colorB);
            colors.Add(colorC);
            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
            uvs.Add(new Vector2(a.x, a.z));
            uvs.Add(new Vector2(b.x, b.z));
            uvs.Add(new Vector2(c.x, c.z));
        }

        private const int MaximumRoadSections = 16000;

        private static List<Vector3> SampleSpline(LowPolyRoad road, Spline spline)
        {
            var result = new List<Vector3>();
            int curves = spline.Closed ? spline.Count : spline.Count - 1;
            float spacing = Mathf.Max(0.25f, road.maximumSegmentLength);
            if (road.style == LowPolyRoadStyle.Cracked)
                spacing = Mathf.Min(spacing, 1.4f);
            if (road.followTerrain)
                spacing = Mathf.Min(spacing, Mathf.Max(0.25f, road.terrainSampleSpacing));

            Matrix4x4 toWorld = road.transform.localToWorldMatrix;
            result.Add(toWorld.MultiplyPoint3x4((Vector3)spline[0].Position));
            for (int span = 0; span < curves; span++)
            {
                BezierCurve curve = spline.GetCurve(span);
                Vector3 a = toWorld.MultiplyPoint3x4((Vector3)curve.P0);
                Vector3 b = toWorld.MultiplyPoint3x4((Vector3)curve.P1);
                Vector3 c = toWorld.MultiplyPoint3x4((Vector3)curve.P2);
                Vector3 d = toWorld.MultiplyPoint3x4((Vector3)curve.P3);
                // The longest control edge bounds the speed of a cubic Bézier over its parameter.
                float longestEdge = Mathf.Max(Flat(b - a).magnitude,
                    Mathf.Max(Flat(c - b).magnitude, Flat(d - c).magnitude));
                int steps = Mathf.Max(1, road.segmentsPerSpan, Mathf.CeilToInt(3f * longestEdge / spacing));
                if (steps > MaximumRoadSections - result.Count)
                    return null;

                for (int i = 1; i <= steps; i++)
                {
                    if (spline.Closed && span == curves - 1 && i == steps)
                        break; // The builder reuses the first cross-section at the seam.
                    Vector3 point = toWorld.MultiplyPoint3x4((Vector3)CurveUtility.EvaluatePosition(curve, i / (float)steps));
                    if (Flat(point - result[result.Count - 1]).sqrMagnitude > 0.000001f)
                        result.Add(point);
                }
            }
            if (spline.Closed && result.Count > 1 && Flat(result[result.Count - 1] - result[0]).sqrMagnitude < 0.000001f)
                result.RemoveAt(result.Count - 1);
            return result;
        }

        private static int ProjectRoadCenters(LowPolyRoad road, LowPolyRoadGround ground,
            List<Vector3> centers, bool closed)
        {
            var onGround = new bool[centers.Count];
            int firstGround = -1;
            int misses = 0;
            for (int i = 0; i < centers.Count; i++)
            {
                Vector3 point = centers[i];
                if (!ground.Sample(point, out float height))
                {
                    misses++;
                    continue;
                }
                if (firstGround < 0)
                    firstGround = i;
                onGround[i] = true;
                point.y = height + Mathf.Max(0.02f, road.surfaceOffset);
                centers[i] = point;
            }
            if (firstGround < 0)
                return misses;

            // Keep the complete native spline even when a section crosses a gap or the map boundary.
            // Missing heights are interpolated along the route, including across a closed seam.
            int start = closed ? firstGround : 0;
            int offset = closed ? 1 : 0;
            int end = closed ? centers.Count + 1 : centers.Count;
            int lastValid = closed ? 0 : -1;
            for (int step = offset; step < end; step++)
            {
                int index = (start + step) % centers.Count;
                if (!onGround[index])
                    continue;
                InterpolateUngrounded(centers, start, lastValid, step, true);
                lastValid = step;
            }
            if (!closed)
                InterpolateUngrounded(centers, start, lastValid, centers.Count, false);

            var refined = new List<Vector3>(centers.Count) { centers[0] };
            int spans = closed ? centers.Count : centers.Count - 1;
            for (int i = 0; i < spans; i++)
            {
                int next = (i + 1) % centers.Count;
                if (onGround[i] && onGround[next])
                    InsertReliefSamples(road, ground, centers[i], centers[next], refined);
                if (!closed || next != 0)
                    refined.Add(centers[next]);
            }
            centers.Clear();
            centers.AddRange(refined);
            return misses;
        }

        private static void InterpolateUngrounded(List<Vector3> centers, int start, int from, int to, bool hasEnd)
        {
            if (to - from <= 1)
                return;

            float first = centers[(start + Mathf.Max(0, from)) % centers.Count].y;
            float last = hasEnd ? centers[(start + to) % centers.Count].y : first;
            if (from < 0)
                first = last;
            for (int i = from + 1; i < to; i++)
            {
                int index = (start + i) % centers.Count;
                Vector3 point = centers[index];
                point.y = Mathf.Lerp(first, last, (float)(i - from) / (to - from));
                centers[index] = point;
            }
        }

        private static void InsertReliefSamples(LowPolyRoad road, LowPolyRoadGround ground,
            Vector3 start, Vector3 end, List<Vector3> result)
        {
            if (Flat(end - start).sqrMagnitude <= 0.40f * 0.40f)
                return;

            Vector3 middle = (start + end) * 0.5f;
            Vector3 projected = ProjectRoadPoint(road, ground, middle);
            if (Mathf.Abs(projected.y - middle.y) <= 0.085f)
                return;

            InsertReliefSamples(road, ground, start, projected, result);
            result.Add(projected);
            InsertReliefSamples(road, ground, projected, end, result);
        }

        private static Vector3 ProjectRoadPoint(LowPolyRoad road, LowPolyRoadGround ground, Vector3 point)
        {
            if (ground.Sample(point, out float height))
                point.y = height + Mathf.Max(0.02f, road.surfaceOffset);
            return point;
        }

        private static Vector3 Flat(Vector3 vector) => new Vector3(vector.x, 0f, vector.z);

        private static float SafeCornerOffset(Vector3 previous, Vector3 next)
        {
            if (previous.sqrMagnitude == 0f || next.sqrMagnitude == 0f)
                return float.PositiveInfinity;
            float angle = Vector3.Angle(previous, next) * Mathf.Deg2Rad;
            if (angle < 0.001f)
                return float.PositiveInfinity;

            float radius = angle < 2.75f
                ? (previous + next).magnitude / (2f * Mathf.Sin(angle))
                : Mathf.Min(previous.magnitude, next.magnitude) * 0.6f;
            return Mathf.Max(0.05f, radius * 0.92f);
        }

        private static float ClearUrbanEdge(LowPolyRoadGround ground, Vector3 center, Vector3 side, float width)
        {
            // A cliff at the outer edge should taper that edge before lifting the entire street.
            for (int step = 3; step <= 6; step++)
            {
                float offset = width * (step / 6f);
                if (ground.Sample(center + side * offset, out float height) && height > center.y + 0.48f + offset * 0.12f)
                    return Mathf.Max(width * 0.55f, offset - width / 6f);
            }
            return width;
        }

        private static void StabilizeRoadEdges(List<Vector3> centers, Vector3[] directions, float[] widths,
            bool closed, float side)
        {
            int count = centers.Count;
            int spans = closed ? count : count - 1;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < spans; i++)
                {
                    int next = (i + 1) % count;
                    Vector3 chord = Flat(centers[next] - centers[i]);
                    Vector3 sideOffsetChange = (directions[next] * widths[next] - directions[i] * widths[i]) * side;
                    float scale = 1f;
                    KeepEdgeForward(chord, sideOffsetChange, directions[i], ref scale);
                    KeepEdgeForward(chord, sideOffsetChange, directions[next], ref scale);
                    widths[i] *= scale;
                    widths[next] *= scale;
                }
                // Distribute a narrow point over nearby spans, around the seam on closed roads.
                for (int i = 0; i < spans; i++)
                    LimitWidthChange(centers, widths, i, (i + 1) % count);
                for (int i = spans - 1; i >= 0; i--)
                    LimitWidthChange(centers, widths, (i + 1) % count, i);
            }
        }

        private static void KeepEdgeForward(Vector3 chord, Vector3 sideOffsetChange, Vector3 right, ref float scale)
        {
            float forward = Vector3.Cross(chord, right).y;
            float change = Vector3.Cross(sideOffsetChange, right).y;
            if (forward > 0f && change < -forward * 0.88f)
                scale = Mathf.Min(scale, -forward * 0.88f / change);
        }

        private static void LimitWidthChange(List<Vector3> centers, float[] widths, int from, int to)
        {
            float horizontal = Flat(centers[to] - centers[from]).magnitude;
            widths[to] = Mathf.Min(widths[to], widths[from] + horizontal * 0.40f);
        }

        private static Vector3 RowCenter(RoadRow row) => (row.roadLeft + row.roadRight) * 0.5f;

        private static void FitRoadProfile(LowPolyRoad road, LowPolyRoadGround ground,
            List<RoadRow> rows, bool closed, float length)
        {
            const int samplesAcross = 8;
            var banks = new float[rows.Count];
            var minimumHeights = new float[rows.Count];
            var workingHeights = new float[rows.Count];
            float maxBank = road.maximumCrossfall * 0.01f;
            float clearance = Mathf.Max(0.065f, road.surfaceOffset);
            for (int i = 0; i < rows.Count; i++)
            {
                RoadRow row = rows[i];
                Vector3 left = row.roadLeft;
                Vector3 right = row.roadRight;
                float width = Flat(right - left).magnitude;
                Vector3 a = Vector3.Lerp(left, right, 0.18f);
                Vector3 b = Vector3.Lerp(left, right, 0.82f);
                if (width > 0.01f && ground.Sample(a, out float low) && ground.Sample(b, out float high))
                    banks[i] = Mathf.Clamp((high - low) / (width * 0.64f), -maxBank, maxBank);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                RoadRow row = rows[i];
                float bank = SmoothRoadValue(banks, rows, i, road.profileSmoothing, closed, length);
                float halfWidth = Flat(row.roadRight - row.roadLeft).magnitude * 0.5f;
                row.roadLeft.y -= bank * halfWidth;
                row.roadRight.y += bank * halfWidth;
                rows[i] = row;

                float lift = 0f;
                for (int s = 0; s <= samplesAcross; s++)
                {
                    Vector3 point = Vector3.Lerp(row.roadLeft, row.roadRight, s / (float)samplesAcross);
                    if (ground.Sample(point, out float surface))
                        lift = Mathf.Max(lift, surface + clearance - point.y);
                }
                minimumHeights[i] = workingHeights[i] = RowCenter(row).y + lift;
            }

            // Check between sections as well so a sharp ridge cannot cut through a long quad.
            int spans = closed ? rows.Count : rows.Count - 1;
            for (int i = 0; i < spans; i++)
            {
                int next = (i + 1) % rows.Count;
                RoadRow a = rows[i];
                RoadRow b = rows[next];
                Vector3 left = (a.roadLeft + b.roadLeft) * 0.5f;
                Vector3 right = (a.roadRight + b.roadRight) * 0.5f;
                float liftA = minimumHeights[i] - RowCenter(a).y;
                float liftB = minimumHeights[next] - RowCenter(b).y;
                float liftAtMiddle = (liftA + liftB) * 0.5f;
                float extra = 0f;
                for (int s = 0; s <= samplesAcross; s++)
                {
                    Vector3 point = Vector3.Lerp(left, right, s / (float)samplesAcross);
                    if (ground.Sample(point, out float surface))
                        extra = Mathf.Max(extra, surface + clearance - point.y - liftAtMiddle);
                }
                int bands = SurfaceBands(road.style);
                for (int band = 0; band < bands; band++)
                {
                    Vector3 al = BandEdge(road, a, band, bands) + Vector3.up * liftA;
                    Vector3 ar = BandEdge(road, a, band + 1, bands) + Vector3.up * liftA;
                    Vector3 bl = BandEdge(road, b, band, bands) + Vector3.up * liftB;
                    Vector3 br = BandEdge(road, b, band + 1, bands) + Vector3.up * liftB;
                    bool alternate = UseOtherDiagonal(al, bl, br, ar, i * 11 + band);
                    extra = Mathf.Max(extra, alternate
                        ? ground.RequiredLift(al, bl, ar, clearance) : ground.RequiredLift(al, bl, br, clearance));
                    extra = Mathf.Max(extra, alternate
                        ? ground.RequiredLift(bl, br, ar, clearance) : ground.RequiredLift(al, br, ar, clearance));
                }
                workingHeights[i] = Mathf.Max(workingHeights[i], minimumHeights[i] + extra);
                workingHeights[next] = Mathf.Max(workingHeights[next], minimumHeights[next] + extra);
            }

            float smoothing = road.profileSmoothing * 1.5f;
            var trend = new float[rows.Count];
            var bumps = new float[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                trend[i] = SmoothRoadValue(workingHeights, rows, i, smoothing, closed, length, true);
                bumps[i] = Mathf.Max(0f, workingHeights[i] - trend[i]);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                RoadRow row = rows[i];
                float target = Mathf.Max(workingHeights[i],
                    trend[i] + RoundBump(bumps, rows, i, smoothing, closed, length));
                float lift = target - RowCenter(row).y;
                row.roadLeft.y += lift;
                row.roadRight.y += lift;
                rows[i] = row;
            }
        }

        private static float SmoothRoadValue(float[] values, List<RoadRow> rows, int index,
            float radius, bool closed, float length, bool keepGrade = false)
        {
            if (radius <= 0.001f)
                return values[index];

            if (closed)
                radius = Mathf.Min(radius, length * 0.48f);
            if (radius <= 0.001f)
                return values[index];

            float sum = values[index];
            float weights = 1f;
            float moment = 0f, momentSquared = 0f, valueMoment = 0f;
            for (int direction = -1; direction <= 1; direction += 2)
            {
                for (int step = 1; step < rows.Count; step++)
                {
                    int neighbor = index + step * direction;
                    if (!closed && (neighbor < 0 || neighbor >= rows.Count))
                        break;
                    neighbor = (neighbor + rows.Count) % rows.Count;
                    float distance = (rows[neighbor].distance - rows[index].distance) * direction;
                    if (distance < 0f)
                        distance += length;
                    if (distance >= radius)
                        break;

                    float weight = 1f - distance / radius;
                    sum += values[neighbor] * weight;
                    weights += weight;
                    if (keepGrade)
                    {
                        float signedDistance = distance * direction;
                        moment += signedDistance * weight;
                        momentSquared += signedDistance * signedDistance * weight;
                        valueMoment += values[neighbor] * signedDistance * weight;
                    }
                }
            }
            float determinant = weights * momentSquared - moment * moment;
            if (keepGrade && determinant > 0.0001f)
                return (sum * momentSquared - valueMoment * moment) / determinant;
            return sum / weights;
        }

        private static float RoundBump(float[] bumps, List<RoadRow> rows, int index, float radius, bool closed, float length)
        {
            if (closed)
                radius = Mathf.Min(radius, length * 0.48f);
            float required = bumps[index];
            if (radius <= 0.001f)
                return required;
            for (int direction = -1; direction <= 1; direction += 2)
            for (int step = 1; step < rows.Count; step++)
            {
                int neighbor = index + step * direction;
                if (!closed && (neighbor < 0 || neighbor >= rows.Count))
                    break;
                neighbor = (neighbor + rows.Count) % rows.Count;
                float distance = (rows[neighbor].distance - rows[index].distance) * direction;
                if (distance < 0f)
                    distance += length;
                if (distance >= radius)
                    break;
                float taper = 0.5f + 0.5f * Mathf.Cos(distance / radius * Mathf.PI);
                required = Mathf.Max(required, bumps[neighbor] * taper);
            }
            return required;
        }

        private static void BuildRoadSide(LowPolyRoad road, LowPolyRoadGround ground,
            Vector3 edge, Vector3 outward, float shoulderWidth, float maxWidth, float distance, int salt,
            out Vector3 shoulder, out Vector3 middle, out Vector3 toe)
        {
            shoulder = edge + outward * shoulderWidth;
            if (shoulderWidth > 0.001f)
            {
                float drop = Mathf.Min(road.shoulderDrop, shoulderWidth * 0.3f);
                shoulder.y -= drop;
                if (road.followTerrain && ground.Sample(shoulder, out float surface))
                    shoulder.y = Mathf.Clamp(surface + 0.025f, shoulder.y, edge.y);
            }
            middle = toe = shoulder;
            if (!road.generateEmbankment)
                return;

            bool hasGroundAtShoulder = ground.Sample(shoulder, out float startHeight);
            if (hasGroundAtShoulder && shoulder.y - startHeight <= 0.055f)
                return;

            // A road on a steep face needs a narrow footing rather than a new slope running
            // halfway down the mountain. Limit the search to a scale relative to the road.
            float reach = Mathf.Min(maxWidth - shoulderWidth,
                Mathf.Min(road.maximumEmbankmentWidth, Mathf.Max(1f, road.width * 0.65f)));
            if (reach <= 0.05f)
                return;

            float step = Mathf.Clamp(reach / 12f, 0.35f, 1.25f);
            float lastOffset = 0f;
            float lastGap = hasGroundAtShoulder ? shoulder.y - startHeight : 0f;
            Vector3 lastSupported = shoulder;
            bool foundToe = false;
            for (float offset = Mathf.Min(step, reach); ; offset = Mathf.Min(offset + step, reach))
            {
                Vector3 point = shoulder + outward * offset;
                if (!ground.Sample(point, out float height))
                    break;

                float gradeHeight = shoulder.y - offset / Mathf.Max(0.25f, road.embankmentSlope);
                float gap = gradeHeight - height;
                if (gap <= 0f)
                {
                    float weight = hasGroundAtShoulder || lastOffset > 0f ? Mathf.InverseLerp(lastGap, gap, 0f) : 1f;
                    float contact = Mathf.Lerp(lastOffset, offset, weight);
                    toe = shoulder + outward * contact;
                    toe.y = ground.Sample(toe, out float contactHeight) ? contactHeight - 0.04f : height - 0.04f;
                    foundToe = true;
                    break;
                }
                lastOffset = offset;
                lastGap = gap;
                lastSupported = new Vector3(point.x, height, point.z);
                if (offset >= reach)
                    break;
            }

            // On a cliff, keep a narrow retaining face with a real footing.
            if (!foundToe && lastOffset > 0f)
            {
                toe = lastSupported;
                if (lastOffset > 0.30f)
                {
                    Vector3 retaining = shoulder + outward * 0.30f;
                    if (ground.Sample(retaining, out float footing))
                        toe = new Vector3(retaining.x, footing, retaining.z);
                }
                toe.y -= 0.04f;
            }

            float fillDepth = shoulder.y - toe.y;
            if (fillDepth > road.maximumFillDepth)
                toe = shoulder;
            middle = Vector3.Lerp(shoulder, toe, fillDepth > 2f ? 0.82f : 0.48f);
            float roughness = Mathf.Min(road.embankmentRoughness * 0.25f, Mathf.Max(0f, fillDepth) * 0.06f);
            if (toe != shoulder)
                middle.y += (RoadNoise(road, distance, salt, 3.5f) * 2f - 1f) * roughness;
        }

        private static float EdgeWidth(LowPolyRoad road, float distance, int salt)
        {
            float noise = RoadNoise(road, distance, salt, 1.6f);
            switch (road.style)
            {
                case LowPolyRoadStyle.Cracked: return 1f - 0.2f * noise * noise;
                case LowPolyRoadStyle.Dirt:
                case LowPolyRoadStyle.Snow: return 1f - 0.05f * noise;
                default: return 1f;
            }
        }

        private static float RoadNoise(LowPolyRoad road, float distance, int salt, float interval)
        {
            float sample = distance / interval;
            int cell = Mathf.FloorToInt(sample);
            return Mathf.Lerp(Hash01(cell, salt, road.surfaceSeed),
                Hash01(cell + 1, salt, road.surfaceSeed), sample - cell);
        }

        private static int SurfaceBands(LowPolyRoadStyle style)
        {
            if (style == LowPolyRoadStyle.City)
                return 3;
            return style == LowPolyRoadStyle.Cracked || style == LowPolyRoadStyle.Snow ? 5 : 2;
        }

        private static void AddRoadSurface(LowPolyRoad road, RoadRow a, RoadRow b, Color baseColor, int index,
            List<Vector3> vertices, List<Vector3> normals, List<Color> colors, List<int> triangles, List<Vector2> uvs)
        {
            int bands = SurfaceBands(road.style);
            float distance = (a.distance + b.distance) * 0.5f;
            float tone = (RoadNoise(road, distance, 217, 8f) - 0.5f) * road.colorVariation;
            Color shadedBase = baseColor * (1f + tone);
            Matrix4x4 toLocalNormal = road.transform.localToWorldMatrix.transpose;
            Vector3 nA = toLocalNormal.MultiplyVector(a.normal).normalized;
            Vector3 nB = toLocalNormal.MultiplyVector(b.normal).normalized;
            for (int band = 0; band < bands; band++)
            {
                Color tint = shadedBase;
                float facet = 0.58f;
                switch (road.style)
                {
                    case LowPolyRoadStyle.City:
                        facet = 0.32f;
                        if (band == 1)
                        {
                            tint = road.palette.cityMarking;
                            facet = 0f;
                        }
                        break;
                    case LowPolyRoadStyle.Cracked:
                        if (band == 1 || band == 3)
                        {
                            float damage = RoadNoise(road, distance, 417 + band * 13, band == 1 ? 4f : 5f);
                            tint = Color.Lerp(shadedBase, road.palette.roadCrack, Mathf.SmoothStep(0f, 1f,
                                Mathf.InverseLerp(band == 1 ? 0.47f : 0.27f, 0.72f, damage)));
                        }
                        break;
                    case LowPolyRoadStyle.Snow:
                        if (band == 1 || band == 3)
                            tint = Color.Lerp(shadedBase, road.palette.snowTrack, 0.7f);
                        break;
                }

                AddFacetedRoadQuad(road.transform, BandEdge(road, a, band, bands), BandEdge(road, b, band, bands),
                    BandEdge(road, b, band + 1, bands), BandEdge(road, a, band + 1, bands),
                    tint, nA, nB, facet, index * 11 + band, vertices, normals, colors, triangles, uvs);
            }
        }

        private static void AddRoadSlopes(LowPolyRoad road, RoadRow a, RoadRow b,
            List<Vector3> vertices, List<Vector3> normals, List<Color> colors, List<int> triangles, List<Vector2> uvs)
        {
            Color material = road.style == LowPolyRoadStyle.Snow ? road.palette.snowShadow : road.palette.earthFill;
            Color stone = Color.Lerp(road.palette.rockShadow, road.palette.rockLight, 0.40f);
            Color aLeft = SlopeTint(material, stone, a.leftGroundTint, a.outerLeft.y - a.toeLeft.y);
            Color bLeft = SlopeTint(material, stone, b.leftGroundTint, b.outerLeft.y - b.toeLeft.y);
            Color aRight = SlopeTint(material, stone, a.rightGroundTint, a.outerRight.y - a.toeRight.y);
            Color bRight = SlopeTint(material, stone, b.rightGroundTint, b.outerRight.y - b.toeRight.y);
            int seed = Mathf.RoundToInt(a.distance * 31f);

            AddShadedQuad(road.transform, a.outerLeft, a.talusMidLeft, b.talusMidLeft, b.outerLeft,
                aLeft, aLeft, bLeft, bLeft, Vector3.zero, Vector3.zero,
                vertices, normals, colors, triangles, uvs, seed + 1, 1f);
            AddShadedQuad(road.transform, a.talusMidLeft, a.toeLeft, b.toeLeft, b.talusMidLeft,
                aLeft, a.leftGroundTint, b.leftGroundTint, bLeft, Vector3.zero, Vector3.zero,
                vertices, normals, colors, triangles, uvs, seed + 2, 1f);
            AddShadedQuad(road.transform, a.outerRight, b.outerRight, b.talusMidRight, a.talusMidRight,
                aRight, bRight, bRight, aRight, Vector3.zero, Vector3.zero,
                vertices, normals, colors, triangles, uvs, seed + 3, 1f);
            AddShadedQuad(road.transform, a.talusMidRight, b.talusMidRight, b.toeRight, a.toeRight,
                aRight, bRight, b.rightGroundTint, a.rightGroundTint, Vector3.zero, Vector3.zero,
                vertices, normals, colors, triangles, uvs, seed + 4, 1f);
        }

        private static Color SlopeTint(Color soil, Color stone, Color ground, float depth)
        {
            float wall = Mathf.InverseLerp(0.8f, 2.8f, depth);
            return Color.Lerp(Color.Lerp(soil, ground, 0.62f), stone, wall);
        }

        private static void AddRoadEnd(LowPolyRoad road, LowPolyRoadGround ground, RoadRow row, bool start,
            List<Vector3> vertices, List<Vector3> normals, List<Color> colors, List<int> triangles, List<Vector2> uvs)
        {
            Vector3 a = row.outerLeft;
            Vector3 footA = RoadEndFoot(road, ground, a);
            for (int i = 0; i <= 6; i++)
            {
                Vector3 b = i == 6 ? row.outerRight : Vector3.Lerp(row.roadLeft, row.roadRight, i / 5f);
                Vector3 footB = RoadEndFoot(road, ground, b);
                Color tint = ground.GetSurfaceTint((footA + footB) * 0.5f, road.palette.rockLight);
                if (start)
                    AddShadedQuad(road.transform, a, b, footB, footA, tint, tint, tint, tint,
                        Vector3.zero, Vector3.zero, vertices, normals, colors, triangles, uvs);
                else
                    AddShadedQuad(road.transform, b, a, footA, footB, tint, tint, tint, tint,
                        Vector3.zero, Vector3.zero, vertices, normals, colors, triangles, uvs);
                a = b;
                footA = footB;
            }
        }

        private static Vector3 RoadEndFoot(LowPolyRoad road, LowPolyRoadGround ground, Vector3 point)
        {
            if (ground.Sample(point, out float height) && point.y - height <= road.maximumFillDepth)
                point.y = Mathf.Min(point.y, height - 0.025f);
            return point;
        }

        private static bool UseOtherDiagonal(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int seed)
        {
            float ac = Mathf.Min(Vector3.Cross(b - a, c - a).y, Vector3.Cross(c - a, d - a).y);
            float bd = Mathf.Min(Vector3.Cross(b - a, d - a).y, Vector3.Cross(c - b, d - b).y);
            if (Mathf.Sign(ac) != Mathf.Sign(bd))
                return bd > ac;
            return (seed & 1) != 0;
        }

        private static void AddFacetedRoadQuad(Transform space, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            Color color, Vector3 normalA, Vector3 normalB, float facet, int seed,
            List<Vector3> vertices, List<Vector3> normals, List<Color> colors, List<int> triangles, List<Vector2> uvs)
        {
            AddShadedQuad(space, a, b, c, d, color, color, color, color, normalA, normalB,
                vertices, normals, colors, triangles, uvs, seed, facet);
        }

        private static void AddShadedQuad(Transform space, Vector3 worldA, Vector3 worldB, Vector3 worldC, Vector3 worldD,
            Color colorA, Color colorB, Color colorC, Color colorD, Vector3 leftNormal, Vector3 rightNormal,
            List<Vector3> vertices, List<Vector3> normals, List<Color> colors, List<int> triangles, List<Vector2> uvs,
            int seed = 0, float facet = 0f)
        {
            Vector3 a = space.InverseTransformPoint(worldA);
            Vector3 b = space.InverseTransformPoint(worldB);
            Vector3 c = space.InverseTransformPoint(worldC);
            Vector3 d = space.InverseTransformPoint(worldD);
            Vector3 up = space.localToWorldMatrix.transpose.MultiplyVector(Vector3.up).normalized;
            float amount = facet * 0.09f;
            float toneA = 1f + (Hash01(seed, 17, 157) - 0.5f) * amount - amount * 0.19f;
            float toneB = 1f + (Hash01(seed, 37, 157) - 0.5f) * amount + amount * 0.19f;
            if (UseOtherDiagonal(worldA, worldB, worldC, worldD, seed))
            {
                AddRoadFace(a, b, d, leftNormal, rightNormal, leftNormal, up, facet, toneA,
                    colorA, colorB, colorD, vertices, normals, colors, triangles, uvs);
                AddRoadFace(b, c, d, rightNormal, rightNormal, leftNormal, up, facet, toneB,
                    colorB, colorC, colorD, vertices, normals, colors, triangles, uvs);
            }
            else
            {
                AddRoadFace(a, b, c, leftNormal, rightNormal, rightNormal, up, facet, toneA,
                    colorA, colorB, colorC, vertices, normals, colors, triangles, uvs);
                AddRoadFace(a, c, d, leftNormal, rightNormal, leftNormal, up, facet, toneB,
                    colorA, colorC, colorD, vertices, normals, colors, triangles, uvs);
            }
        }

        private static void AddRoadFace(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc,
            Vector3 up, float facet, float tone, Color ca, Color cb, Color cc, List<Vector3> vertices,
            List<Vector3> normals, List<Color> colors, List<int> triangles, List<Vector2> uvs)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude <= 0.00000001f)
                return;
            normal.Normalize();
            if (na == Vector3.zero)
                na = nb = nc = Vector3.Lerp(normal, up, 0.22f).normalized;
            else
            {
                na = Vector3.Lerp(na, normal, facet).normalized;
                nb = Vector3.Lerp(nb, normal, facet).normalized;
                nc = Vector3.Lerp(nc, normal, facet).normalized;
            }
            AddShadedTriangle(a, b, c, na, nb, nc, ca * tone, cb * tone, cc * tone,
                vertices, normals, colors, triangles, uvs);
        }

        private static Vector3 BandEdge(LowPolyRoad road, RoadRow row, int edge, int bands)
        {
            if (edge == 0)
                return row.roadLeft;
            if (edge == bands)
                return row.roadRight;

            float t;
            switch (road.style)
            {
                case LowPolyRoadStyle.City:
                    float line = Mathf.Min(Mathf.Min(row.centerAcross, 1f - row.centerAcross) * 0.48f,
                        0.055f / Mathf.Max(0.001f, Flat(row.roadRight - row.roadLeft).magnitude));
                    t = row.centerAcross + (edge == 1 ? -line : line);
                    break;
                case LowPolyRoadStyle.Cracked:
                    float crack = Mathf.Lerp(0.19f, 0.81f, RoadNoise(road, row.patternDistance, 438, 2f));
                    float gap = Mathf.Lerp(0.002f, 0.027f, RoadNoise(road, row.patternDistance, 842, 3f));
                    if (edge <= 2)
                    {
                        float branch = Mathf.Lerp(0.06f, crack - gap - 0.02f, RoadNoise(road, row.patternDistance, 513, 1.6f));
                        t = branch + (edge == 1 ? -0.009f : 0.009f);
                    }
                    else
                        t = crack + (edge == 3 ? -gap : gap);
                    break;
                case LowPolyRoadStyle.Snow:
                    t = edge * 0.2f + (RoadNoise(road, row.patternDistance, 927, 4f) - 0.5f) * 0.09f;
                    break;
                default:
                    t = 0.38f + RoadNoise(road, row.patternDistance, 1106, 2.8f) * 0.24f;
                    break;
            }
            return Vector3.Lerp(row.roadLeft, row.roadRight, t);
        }

        private static Color RoadColor(LowPolyRoad road)
        {
            switch (road.style)
            {
                case LowPolyRoadStyle.City:
                    return road.palette.asphalt;
                case LowPolyRoadStyle.Mountain:
                    return road.palette.gravel;
                case LowPolyRoadStyle.Cracked:
                    return road.palette.wornAsphalt;
                case LowPolyRoadStyle.Snow:
                    return road.palette.packedSnow;
                default:
                    return road.palette.dirt;
            }
        }

        private static Color Vary(Color color, float amount, int seed)
        {
            float factor = 1f + (Hash01(seed, seed * 31, 17) * 2f - 1f) * amount;
            Color result = color * factor;
            result.a = color.a;
            return result;
        }

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint hash = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                hash = (hash ^ (hash >> 13)) * 1274126177u;
                hash ^= hash >> 16;
                return (hash & 0x00FFFFFF) / 16777215f;
            }
        }

        private static Mesh CreateMesh(
            string name, List<Vector3> vertices, List<Vector3> normals, List<Color> colors,
            List<int> triangles, List<Vector2> uvs)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();
            MeshUtility.Optimize(mesh);
            return mesh;
        }

        private static Mesh SaveMesh(Mesh generated, string baseName, ref string guid, bool recordUndo = false)
        {
            EnsureGeneratedFolder();
            string existingPath = string.IsNullOrEmpty(guid) ? string.Empty : AssetDatabase.GUIDToAssetPath(guid);
            Mesh existing = !string.IsNullOrEmpty(existingPath) &&
                existingPath.StartsWith(GeneratedRoot + "/", StringComparison.Ordinal)
                ? AssetDatabase.LoadAssetAtPath<Mesh>(existingPath) : null;
            if (existing != null)
            {
                if (recordUndo)
                    Undo.RegisterCompleteObjectUndo(existing, "Régénérer la route");
                EditorUtility.CopySerialized(generated, existing);
                UnityEngine.Object.DestroyImmediate(generated);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            string safeName = MakeSafeName(baseName);
            string path = AssetDatabase.GenerateUniqueAssetPath(GeneratedRoot + "/" + safeName + ".asset");
            AssetDatabase.CreateAsset(generated, path);
            guid = AssetDatabase.AssetPathToGUID(path);
            return generated;
        }

        private static void AssignMesh(MeshFilter filter, MeshCollider collider, Mesh mesh, bool enableCollider)
        {
            filter.sharedMesh = mesh;
            collider.sharedMesh = null;
            collider.enabled = enableCollider;
            if (enableCollider)
            {
                collider.sharedMesh = mesh;
            }
            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(collider);
        }

        private static void EnsureGeneratedFolder()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedRoot))
            {
                AssetDatabase.CreateFolder("Assets/LowPolyWorld", "Generated");
            }
        }

        private static string MakeSafeName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }
            return string.IsNullOrWhiteSpace(value) ? "LowPolyMesh" : value;
        }
    }
}
