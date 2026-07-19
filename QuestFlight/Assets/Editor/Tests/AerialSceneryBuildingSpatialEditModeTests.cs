using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class AerialSceneryBuildingSpatialEditModeTests
    {
        private const string SourceContextPath =
            "Assets/Production/Environment/ReconstructionV2/KBDU_ReferenceContextV2.json";

        [Test]
        public void SpatialManifestUsesExactNearBoundedMidAndNoOrdinaryFarGeometry()
        {
            AerialSceneryBuildingAssetAuthoring.SpatialContractDocument contract =
                ReadJson<AerialSceneryBuildingAssetAuthoring.SpatialContractDocument>(
                    AerialSceneryBuildingAssetAuthoring.SpatialContractPath);
            ContextDocument source = ReadJson<ContextDocument>(SourceContextPath);
            Assert.That(contract.records.Length + contract.far_orthophoto_feature_indices.Length,
                Is.EqualTo(27679));
            Assert.That(contract.coordinate_quantization_meters, Is.EqualTo(0.5f));
            Assert.That(contract.authored_scale_xyz, Is.EqualTo(new[] { 1f, 1f, 1f }));
            HashSet<int> identities = new HashSet<int>();
            foreach (AerialSceneryBuildingAssetAuthoring.SpatialRecord record in contract.records)
            {
                Assert.That(identities.Add(record.feature_index), Is.True, record.feature_index.ToString());
                int[] sourcePoints = Unclosed(source.openstreetmap.features[record.feature_index].points_q);
                Assert.That(float.IsNaN(record.base_y_meters) || float.IsInfinity(record.base_y_meters), Is.False);
                Assert.That(record.height_meters, Is.GreaterThan(0f));
                Assert.That(record.roof_profile,
                    Is.EqualTo("flat").Or.EqualTo("gable").Or.EqualTo("hip"));
                if (record.roof_profile == "flat")
                    Assert.That(record.roof_rise_meters, Is.EqualTo(0f));
                else
                    Assert.That(record.roof_rise_meters, Is.GreaterThan(0f));
                Assert.That(Bounds(record.geometry_points_q), Is.EqualTo(Bounds(sourcePoints)));
                if (record.tier == "hero_core" || record.tier == "near")
                {
                    Assert.That(record.geometry_points_q, Is.EqualTo(sourcePoints), record.feature_index.ToString());
                    Assert.That(record.footprint_area_ratio, Is.EqualTo(1f).Within(0.000001f));
                }
                else if (record.tier == "mid")
                {
                    Assert.That(record.geometry_points_q.Length, Is.GreaterThanOrEqualTo(6));
                    Assert.That(record.footprint_area_ratio, Is.InRange(0.9f, 1.100001f),
                        record.feature_index.ToString());
                }
                Assert.That(record.tier, Is.Not.EqualTo("far_orthophoto"));
                if (record.lidar_height_used)
                    Assert.That(record.lidar_selected_relative_error, Is.InRange(0f, 0.1f));
            }
            foreach (int farFeatureIndex in contract.far_orthophoto_feature_indices)
                Assert.That(identities.Add(farFeatureIndex), Is.True, farFeatureIndex.ToString());
            Assert.That(identities, Has.Count.EqualTo(27679));
        }

        [Test]
        public void ComponentsCarryOnlyAConnectedSpanningTreeOfFootprintParts()
        {
            AerialSceneryBuildingAssetAuthoring.SpatialContractDocument contract =
                ReadJson<AerialSceneryBuildingAssetAuthoring.SpatialContractDocument>(
                    AerialSceneryBuildingAssetAuthoring.SpatialContractPath);
            Dictionary<int, AerialSceneryBuildingAssetAuthoring.SpatialRecord> records =
                contract.records.ToDictionary(record => record.feature_index);
            foreach (AerialSceneryBuildingAssetAuthoring.SpatialComponent component in contract.multi_part_components)
            {
                Assert.That(component.feature_indices.Length, Is.GreaterThan(1));
                Assert.That(component.connectivity_edges.Length,
                    Is.EqualTo((component.feature_indices.Length - 1) * 2));
                DisjointSet connectivity = new DisjointSet(component.feature_indices);
                for (int index = 0; index < component.connectivity_edges.Length; index += 2)
                {
                    int first = component.connectivity_edges[index];
                    int second = component.connectivity_edges[index + 1];
                    Assert.That(records[first].tier, Is.EqualTo(records[second].tier));
                    Assert.That(PolygonsIntersect(records[first].geometry_points_q, records[second].geometry_points_q),
                        Is.True, first + "/" + second);
                    connectivity.Union(first, second);
                }
                Assert.That(component.feature_indices.Select(connectivity.Find).Distinct().Count(), Is.EqualTo(1));
            }
        }

        [Test]
        public void StandalonePrefabHasStableBoundsUnitScaleAndExactLegacyDisableMap()
        {
            AerialSceneryBuildingAssetAuthoring.BatchReplacementDocument manifest =
                ReadJson<AerialSceneryBuildingAssetAuthoring.BatchReplacementDocument>(
                    AerialSceneryBuildingAssetAuthoring.BatchReplacementPath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                AerialSceneryBuildingAssetAuthoring.PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            string[] dependencies = AssetDatabase.GetDependencies(
                AerialSceneryBuildingAssetAuthoring.PrefabPath, true);
            Assert.That(dependencies,
                Does.Not.Contain("Assets/Production/Environment/ProductionEnvironmentRoot.prefab"));
            Assert.That(dependencies,
                Does.Not.Contain("Assets/Production/Environment/PhotorealTerrainV1/KBDU_BatchedContext.prefab"));
            Assert.That(dependencies.Where(path =>
                    path.StartsWith(AerialSceneryBuildingAssetAuthoring.GeneratedRoot + "/") &&
                    path.IndexOf("4-6km", StringComparison.OrdinalIgnoreCase) >= 0),
                Is.Empty, "No apparent far mesh belongs in the standalone prefab dependency closure.");
            Assert.That(manifest.statistics.legacy_batch_count, Is.EqualTo(20));
            Assert.That(manifest.statistics.legacy_renderer_count_to_disable, Is.EqualTo(60));
            Assert.That(manifest.statistics.hero_replacement_source_count_omitted, Is.EqualTo(2));
            Assert.That(manifest.statistics.ordinary_far_geometry_count, Is.Zero);
            Assert.That(manifest.legacy_group_ids_to_disable.Distinct().Count(), Is.EqualTo(20));
            Assert.That(manifest.legacy_renderer_ids_to_disable.Distinct().Count(), Is.EqualTo(60));
            GameObject legacy = AssetDatabase.LoadAssetAtPath<GameObject>(
                AerialSceneryBuildingAssetAuthoring.LegacyCombinedPrefabPath);
            Assert.That(legacy, Is.Not.Null);
            HashSet<string> actualLegacyGroups = new HashSet<string>();
            HashSet<string> actualLegacyRenderers = new HashSet<string>();
            foreach (LODGroup legacyGroup in legacy.GetComponentsInChildren<LODGroup>(true)
                         .Where(group => group.gameObject.name.StartsWith("buildings_")))
            {
                Assert.That(actualLegacyGroups.Add(legacyGroup.gameObject.name), Is.True);
                foreach (LOD lod in legacyGroup.GetLODs())
                foreach (Renderer renderer in lod.renderers)
                    Assert.That(actualLegacyRenderers.Add(
                        legacyGroup.gameObject.name + "/" +
                        AnimationUtility.CalculateTransformPath(
                            renderer.transform, legacyGroup.transform)), Is.True);
            }
            Assert.That(actualLegacyGroups, Is.EquivalentTo(manifest.legacy_group_ids_to_disable));
            Assert.That(actualLegacyRenderers, Is.EquivalentTo(manifest.legacy_renderer_ids_to_disable));
            Assert.That(prefab.GetComponentsInChildren<LODGroup>(true),
                Is.Empty, "Fixed context shells must never pop between scale/shape variants.");
            Assert.That(manifest.statistics.near_reduction_lod_group_count, Is.Zero);
            Assert.That(prefab.GetComponentsInChildren<MeshRenderer>(true),
                Has.Length.EqualTo(manifest.statistics.replacement_renderer_count));
            HashSet<string> expectedMeshes = new HashSet<string>(
                manifest.batches
                    .Where(batch => batch.represented_feature_indices.Length > 0)
                    .SelectMany(batch => Enumerable.Range(0, batch.lods.Length).Select(lod =>
                        AerialSceneryBuildingAssetAuthoring.GeneratedRoot + "/" +
                        batch.new_group_id + "_LOD" + lod + ".asset")));
            HashSet<string> authoredMeshes = new HashSet<string>(
                AssetDatabase.FindAssets("t:Mesh", new[]
                    { AerialSceneryBuildingAssetAuthoring.GeneratedRoot })
                    .Select(AssetDatabase.GUIDToAssetPath));
            Assert.That(authoredMeshes, Is.EquivalentTo(expectedMeshes),
                "No empty or stale far mesh assets may remain in the generated dependency closure.");

            foreach (AerialSceneryBuildingAssetAuthoring.BatchReplacement batch in manifest.batches)
            {
                Transform group = string.IsNullOrEmpty(batch.new_group_id)
                    ? null
                    : prefab.transform.Find(batch.new_group_id);
                if (batch.represented_feature_indices.Length == 0)
                {
                    Assert.That(group, Is.Null, batch.legacy_group_id_to_disable);
                    Assert.That(batch.integration_action, Does.Contain("ORTHOPHOTO_ONLY"));
                    continue;
                }
                Assert.That(group, Is.Not.Null, batch.new_group_id);
                Assert.That(group.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(group.localRotation, Is.EqualTo(Quaternion.identity));
                Assert.That(group.localScale, Is.EqualTo(Vector3.one));
                Assert.That(group.GetComponent<LODGroup>(), Is.Null);
                Assert.That(batch.lods, Has.Length.EqualTo(1));
                Assert.That(batch.lods[0].level, Is.EqualTo("FIXED"));
                Assert.That(batch.lods[0].mesh_mode,
                    Is.EqualTo("CLOSED_TRUE_FOOTPRINT_SHELL_FIXED_RENDERER"));
                Renderer[] batchRenderers = group.GetComponentsInChildren<MeshRenderer>(true);
                Assert.That(batchRenderers, Has.Length.EqualTo(1));
                Bounds expectedBounds = StableBounds(batch.stable_bounds_min_max_xyz_meters);
                for (int lod = 0; lod < batch.lods.Length; lod++)
                {
                    Renderer renderer = batchRenderers[lod];
                    Assert.That(renderer.transform.localPosition, Is.EqualTo(Vector3.zero));
                    Assert.That(renderer.transform.localRotation, Is.EqualTo(Quaternion.identity));
                    Assert.That(renderer.transform.localScale, Is.EqualTo(Vector3.one));
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    Assert.That((mesh.bounds.center - expectedBounds.center).sqrMagnitude, Is.LessThan(0.000001f));
                    Assert.That((mesh.bounds.size - expectedBounds.size).sqrMagnitude, Is.LessThan(0.000001f));
                    Assert.That(mesh.vertexCount, Is.EqualTo(batch.lods[lod].vertices));
                    Assert.That(mesh.GetIndexCount(0) / 3L, Is.EqualTo(batch.lods[lod].triangles));
                    Assert.That(mesh.uv, Has.Length.EqualTo(mesh.vertexCount));
                    Assert.That(mesh.uv2, Has.Length.EqualTo(mesh.vertexCount));
                    Assert.That(mesh.colors32, Has.Length.EqualTo(mesh.vertexCount));
                    Assert.That(mesh.bounds.min.y,
                        Is.EqualTo(batch.stable_bounds_min_max_xyz_meters[1]).Within(0.0001f),
                        "Closed shells must extend to their stable foundation instead of floating as roof cards.");
                    StringAssert.DoesNotContain("cluster", mesh.name.ToLowerInvariant());
                    StringAssert.DoesNotContain("box", mesh.name.ToLowerInvariant());
                }
                int representedFar = batch.represented_tier_counts
                    .Single(row => row.tier == "far_orthophoto").count;
                Assert.That(representedFar, Is.Zero);
            }
        }

        [Test]
        public void ContextShellMaterialReusesResidentAirportCorePbrAtlasWithoutNewDependency()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                AerialSceneryBuildingAssetAuthoring.PrefabPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                AerialSceneryBuildingAssetAuthoring.BuildingMaterialPath);
            Material residentMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                AerialSceneryBuildingAssetAuthoring.ExistingAirportCoreMaterialPath);
            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(
                AerialSceneryBuildingAssetAuthoring.BuildingAlbedoAtlasPath);
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                AerialSceneryBuildingAssetAuthoring.BuildingNormalAtlasPath);
            Texture2D mrao = AssetDatabase.LoadAssetAtPath<Texture2D>(
                AerialSceneryBuildingAssetAuthoring.BuildingMraoAtlasPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(material, Is.Not.Null);
            Assert.That(residentMaterial, Is.Not.Null);
            Assert.That(albedo, Is.Not.Null);
            Assert.That(normal, Is.Not.Null);
            Assert.That(mrao, Is.Not.Null);
            Assert.That(material.shader.name,
                Is.EqualTo(AerialSceneryBuildingAssetAuthoring.BuildingShaderName));
            Assert.That(material.GetTexture("_MainTex"), Is.SameAs(albedo));
            Assert.That(material.GetTexture("_NormalTex"), Is.SameAs(normal));
            Assert.That(material.GetTexture("_MRAOTex"), Is.SameAs(mrao));
            Assert.That(residentMaterial.GetTexture("_MainTex"), Is.SameAs(albedo));
            Assert.That(residentMaterial.GetTexture("_NormalTex"), Is.SameAs(normal));
            Assert.That(residentMaterial.GetTexture("_MRAOTex"), Is.SameAs(mrao));
            Assert.That(material.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Geometry));
            Assert.That(material.enableInstancing, Is.True);
            Assert.That(material.HasProperty("_LightMap"), Is.False);
            Assert.That(material.GetFloat("_DetailFlattenStart"), Is.EqualTo(3.00f).Within(0.0001f));
            Assert.That(material.GetFloat("_DetailFlattenEnd"), Is.EqualTo(8.00f).Within(0.0001f));
            Assert.That(material.GetFloat("_AmbientFloor"), Is.EqualTo(0.12f).Within(0.0001f));
            Assert.That(material.GetFloat("_NearAlbedoLift"), Is.EqualTo(0.12f).Within(0.0001f));

            string[] dependencies = AssetDatabase.GetDependencies(
                AerialSceneryBuildingAssetAuthoring.PrefabPath, true);
            Assert.That(dependencies,
                Does.Contain(AerialSceneryBuildingAssetAuthoring.BuildingAlbedoAtlasPath));
            Assert.That(dependencies,
                Does.Contain(AerialSceneryBuildingAssetAuthoring.BuildingNormalAtlasPath));
            Assert.That(dependencies,
                Does.Contain(AerialSceneryBuildingAssetAuthoring.BuildingMraoAtlasPath));
            Assert.That(dependencies,
                Does.Not.Contain(AerialSceneryBuildingAssetAuthoring.ObsoleteContextAtlasPath));
            Assert.That(dependencies.Count(path =>
                    path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)),
                Is.EqualTo(1), "The standalone context prefab must retain one shared material.");
            Assert.That(AssetDatabase.GetDependencies(
                    AerialSceneryBuildingAssetAuthoring.ProductEnvironmentRootPath, true),
                Does.Contain(AerialSceneryBuildingAssetAuthoring.ExistingAirportCoreMaterialPath),
                "The PBR triplet must already be resident through the active AirportCore V4 material.");

            HashSet<Vector2> allowedAtlasCells = new HashSet<Vector2>
            {
                new Vector2(0f, 0f), // brick
                new Vector2(1f, 0f), // light metal
                new Vector2(1f, 1f), // concrete
                new Vector2(0f, 2f), // hangar cladding
                new Vector2(2f, 2f), // weathered corrugated
                new Vector2(3f, 0f), // dark roof
            };
            MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            foreach (MeshRenderer renderer in renderers)
            {
                Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { material }));
                Assert.That(renderer.lightmapIndex, Is.LessThan(0));
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.uv2, Is.Not.Empty);
                Assert.That(mesh.uv2.All(allowedAtlasCells.Contains), Is.True, mesh.name);
            }

            TextureImporter normalImporter = AssetImporter.GetAtPath(
                AerialSceneryBuildingAssetAuthoring.BuildingNormalAtlasPath) as TextureImporter;
            TextureImporter mraoImporter = AssetImporter.GetAtPath(
                AerialSceneryBuildingAssetAuthoring.BuildingMraoAtlasPath) as TextureImporter;
            Assert.That(normalImporter, Is.Not.Null);
            Assert.That(normalImporter.textureType, Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(mraoImporter, Is.Not.Null);
            Assert.That(mraoImporter.sRGBTexture, Is.False);
            Assert.That(normalImporter.mipmapEnabled, Is.True);
            Assert.That(mraoImporter.mipmapEnabled, Is.True);

            string shaderPath = AssetDatabase.GetAssetPath(material.shader);
            string shaderSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", shaderPath)));
            StringAssert.DoesNotContain("LIGHTMAP_ON", shaderSource);
            StringAssert.DoesNotContain("unity_Lightmap", shaderSource);
            StringAssert.Contains("_NormalTex", shaderSource);
            StringAssert.Contains("_MRAOTex", shaderSource);
            StringAssert.Contains("ddx(input.detailUv)", shaderSource);
            StringAssert.Contains("ddy(input.detailUv)", shaderSource);
            StringAssert.Contains("tex2Dgrad", shaderSource);
            StringAssert.Contains("StableAtlasAverage", shaderSource);
        }

        private static T ReadJson<T>(string path)
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.That(asset, Is.Not.Null, path);
            T result = JsonUtility.FromJson<T>(asset.text);
            Assert.That(result, Is.Not.Null, path);
            return result;
        }

        private static int[] Unclosed(int[] source)
        {
            if (source.Length >= 4 && source[0] == source[source.Length - 2] &&
                source[1] == source[source.Length - 1])
                return source.Take(source.Length - 2).ToArray();
            return source.ToArray();
        }

        private static int[] Bounds(int[] points)
        {
            int[] x = points.Where((_, index) => index % 2 == 0).ToArray();
            int[] z = points.Where((_, index) => index % 2 == 1).ToArray();
            return new[] { x.Min(), z.Min(), x.Max(), z.Max() };
        }

        private static bool PolygonsIntersect(int[] first, int[] second)
        {
            int[] firstBounds = Bounds(first);
            int[] secondBounds = Bounds(second);
            if (firstBounds[2] < secondBounds[0] || secondBounds[2] < firstBounds[0] ||
                firstBounds[3] < secondBounds[1] || secondBounds[3] < firstBounds[1]) return false;
            for (int firstIndex = 0; firstIndex < first.Length; firstIndex += 2)
            {
                Vector2Int a = Point(first, firstIndex);
                Vector2Int b = Point(first, (firstIndex + 2) % first.Length);
                for (int secondIndex = 0; secondIndex < second.Length; secondIndex += 2)
                {
                    Vector2Int c = Point(second, secondIndex);
                    Vector2Int d = Point(second, (secondIndex + 2) % second.Length);
                    if (SegmentsIntersect(a, b, c, d)) return true;
                }
            }
            return PointInPolygon(Point(first, 0), second) || PointInPolygon(Point(second, 0), first);
        }

        private static Vector2Int Point(int[] points, int index) => new Vector2Int(points[index], points[index + 1]);

        private static long Orientation(Vector2Int a, Vector2Int b, Vector2Int c) =>
            (long)(b.x - a.x) * (c.y - a.y) - (long)(b.y - a.y) * (c.x - a.x);

        private static bool OnSegment(Vector2Int a, Vector2Int b, Vector2Int point) =>
            Orientation(a, b, point) == 0 && point.x >= Math.Min(a.x, b.x) && point.x <= Math.Max(a.x, b.x) &&
            point.y >= Math.Min(a.y, b.y) && point.y <= Math.Max(a.y, b.y);

        private static bool SegmentsIntersect(Vector2Int a, Vector2Int b, Vector2Int c, Vector2Int d)
        {
            long first = Orientation(a, b, c);
            long second = Orientation(a, b, d);
            long third = Orientation(c, d, a);
            long fourth = Orientation(c, d, b);
            if ((first > 0) != (second > 0) && first != 0 && second != 0 &&
                (third > 0) != (fourth > 0) && third != 0 && fourth != 0) return true;
            return first == 0 && OnSegment(a, b, c) || second == 0 && OnSegment(a, b, d) ||
                   third == 0 && OnSegment(c, d, a) || fourth == 0 && OnSegment(c, d, b);
        }

        private static bool PointInPolygon(Vector2Int point, int[] polygon)
        {
            bool inside = false;
            for (int index = 0; index < polygon.Length; index += 2)
            {
                Vector2Int a = Point(polygon, index);
                Vector2Int b = Point(polygon, (index + 2) % polygon.Length);
                if (OnSegment(a, b, point)) return true;
                if ((a.y > point.y) != (b.y > point.y))
                {
                    double intersection = (double)(b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
                    if (point.x < intersection) inside = !inside;
                }
            }
            return inside;
        }

        private static Bounds StableBounds(float[] value)
        {
            Vector3 minimum = new Vector3(value[0], value[1], value[2]);
            Vector3 maximum = new Vector3(value[3], value[4], value[5]);
            return new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
        }

        private sealed class DisjointSet
        {
            private readonly Dictionary<int, int> parent;
            public DisjointSet(IEnumerable<int> values) => parent = values.ToDictionary(value => value);
            public int Find(int value)
            {
                while (parent[value] != value)
                {
                    parent[value] = parent[parent[value]];
                    value = parent[value];
                }
                return value;
            }
            public void Union(int first, int second)
            {
                first = Find(first);
                second = Find(second);
                if (first != second) parent[second] = first;
            }
        }

        [Serializable]
        private sealed class ContextDocument
        {
            public OsmPayload openstreetmap;
        }

        [Serializable]
        private sealed class OsmPayload
        {
            public ContextFeature[] features;
        }

        [Serializable]
        private sealed class ContextFeature
        {
            public int[] points_q;
        }
    }
}
