using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using QuestFlight.Editor;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    /// <summary>Bounded existing-data compiler. Never authors or replaces the product hierarchy.</summary>
    public static class TerrainFloat32IntegrationRepairAuthoring
    {
        public const string NearPath = "Assets/Production/Environment/PhotorealAerialSurfaceV1/Terrain/Native/KBDU_ProductAeqd_BareEarth_Fixed.asset";
        public const string QueryPath = "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset";
        public const string RoadSourcesPath = "Assets/Production/Environment/TrafficV2/Data/KBDU_VisibleRoadLaneSurfaces_Source.json";
        public const string RoadPatchId = "airport_road_repair_visible_triangles";
        public const string BeforeNearSha256 = "1977c3c42179b20087078cd07eb684e55f9990ec5495150b966135fd5255175e";
        public const string BeforeQuerySha256 = "2ecea7ef92d8fd2df0107230733fe54af4abe003c404b46245c5fd61930ccb52";
        public const string BeforeAirportSha256 = "e433a7f174c67f05d5ef951c86bc7147a16b78592597c9e49b89838fa66b1975";
        public const string BeforeRoadReceiptSha256 = "83cfd9a6235e5ba827219cb8ad34d7d785d1093fc1585c86cac32ece099f39d0";
        public const string BeforeAirportReceiptSha256 = "4127155aa7b853757b2257e13c4b6cd14d4272959d73306ab4bf4385b19377a2";
        // Actual pinned Unity6000.3.8f1 read-only plan9625. Two extra NumPy
        // candidates (130,8878) were one-ULP-only roundoff; Unity retains their
        // original exact Y. All4404 actual boundary probes agree at0m.
        public static int[] ReviewedBoundaryVertexIndices => new[] {
            74,75,76,131,132,133,292,298,299,300,301,304,305,306,307,
            8290,8291,8292,8293,8294,8295,8296,8297,8298,8299,8300,8301,
            8418,8419,8420,8421,8422,8423,8424,8425,8426,8427,8428,8429,
            8818,8819,8820,8834,8835,8836,8837,8838,8839,8840,8841,8842,8843,
            8844,8845,8846,8847,8848,8849,8850,8851,8852,8853,8854,8855,8856,
            8857,8858,8859,8860,8861,8862,8863,8864,8865,8879,8880,8881 };

        public sealed class Plan : IDisposable
        {
            public Mesh near;
            public ProductionTerrainSurfaceAsset query;
            public AirportPatchLidarConformanceAuthoring.ExistingBoundaryResult boundary;
            public TerrainFloat32HeightfieldPolicy.Result nearCorrection, queryCorrection;
            public void Dispose()
            {
                if (near != null) UnityEngine.Object.DestroyImmediate(near);
                if (query != null) UnityEngine.Object.DestroyImmediate(query);
                if (boundary?.mesh != null) UnityEngine.Object.DestroyImmediate(boundary.mesh);
            }
        }

        public static Plan Prepare(Mesh near, ProductionTerrainSurfaceAsset query, Mesh airport)
        {
            if (near == null || query == null || airport == null) throw new InvalidDataException("Authored terrain assets are required.");
            var plan = new Plan();
            try
            {
                plan.nearCorrection = TerrainFloat32HeightfieldPolicy.CorrectIndices(near.vertices, near.triangles, true);
                plan.near = UnityEngine.Object.Instantiate(near);
                plan.near.name = near.name;
                plan.near.triangles = plan.nearCorrection.indices;
                plan.near.bounds = near.bounds;
                // Valid existing smoothed normals stay fixed. This repair changes
                // front-face winding, not native contact normals or height data.
                plan.near.normals = near.normals;
                plan.query = UnityEngine.Object.Instantiate(query);
                plan.query.name = query.name;
                var patch = plan.query.transitionPatches.Single(item => item.stableId == RoadPatchId);
                plan.queryCorrection = TerrainFloat32HeightfieldPolicy.CorrectIndices(patch.vertices, patch.triangles, true);
                patch.triangles = plan.queryCorrection.indices;
                plan.boundary = AirportPatchLidarConformanceAuthoring.ReconcileExistingRoadBoundary(airport, plan.near);
                return plan;
            }
            catch { plan.Dispose(); throw; }
        }

        [Serializable] public sealed class BackupFile { public string projectRelativePath, sourceSha256, backupPath, backupSha256; }
        [Serializable] public sealed class BackupManifest { public BackupFile[] files; }
        [Serializable] public sealed class RepairReceipt
        {
            public string schema = "questflight.terrain.float32-existing-integration-repair.v1";
            public string operation = "existing_index_and_boundary_only_correction_no_full_terrain_regeneration";
            public string generatedUtc;
            public string beforeNearSha256, afterNearSha256, beforeQuerySha256, afterQuerySha256, beforeAirportSha256, afterAirportSha256;
            public string originalRoadSourceReceiptJson, originalAirportCompilerReceiptJson;
            public string canonicalNearSupportGeometrySha256;
            public int[] invertedNearFaces, zeroFootprintNearFaces, invertedQueryFaces, zeroFootprintQueryFaces, changedAirportBoundaryVertices;
            public int[] affectedAirportNormalVertices;
            public float[] originalAirportBoundaryY, correctedAirportBoundaryY;
            public int nearTrianglesBefore, nearTrianglesAfter, queryTrianglesBefore, queryTrianglesAfter;
            public int boundaryProbeCount;
            public float maximumBoundaryMismatchMeters, maximumBoundaryHeightAdjustmentMeters;
            public float meanBoundaryMismatchMeters, p95BoundaryMismatchMeters;
            public bool nearVertexNormalUvAndBoundsDataPreserved = true;
            public bool queryVerticesNormalsOrderingAndOriginalFourPatchesPreserved = true;
            public bool retainedBankBytesAndProjectedSupportGeometryPreserved = true;
            public string license = "Existing USGS public-domain terrain and project-authored OSM ODbL road derivative; existing source/license records retained.";
            public string limitations = "Index-only repair retains the accepted bank; this is not a new full support bake, native landing scenario, stereo simulator inspection or physical Quest acceptance.";
        }

        public static string[] WrittenPaths => new[] { NearPath, QueryPath,
            AirportPatchLidarConformanceAuthoring.OutputMeshPath, RoadSourcesPath,
            AirportPatchLidarConformanceAuthoring.OutputReportPath };

        [Serializable] public sealed class BoundaryProbeDiagnostic
        {
            public string operation = "read_only_actual_Unity_clone_plan_no_asset_writes";
            public RepairReceipt plan;
            public int[] roadSampledBoundaryIndices;
            public Vector3[] originalRoadBoundaryVertices, candidateRoadBoundaryVertices;
        }

        public static void DiagnoseBatch()
        {
            try
            {
                string path = CanonicalExternalPath(System.Environment.GetEnvironmentVariable("QFL_TERRAIN_REPAIR_DIAGNOSTIC"));
                if (!Directory.Exists(Path.GetDirectoryName(path)) || File.Exists(path) || Directory.Exists(path))
                    throw new InvalidDataException("Read-only diagnostic requires a new owned external output.");
                Mesh near = AssetDatabase.LoadAssetAtPath<Mesh>(NearPath);
                var query = AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(QueryPath);
                Mesh airport = AssetDatabase.LoadAssetAtPath<Mesh>(AirportPatchLidarConformanceAuthoring.OutputMeshPath);
                using Plan plan = Prepare(near, query, airport);
                var diagnostic = new BoundaryProbeDiagnostic {
                    roadSampledBoundaryIndices = plan.boundary.roadSampledBoundaryVertexIndices,
                    originalRoadBoundaryVertices = plan.boundary.roadSampledBoundaryVertexIndices.Select(index => airport.vertices[index]).ToArray(),
                    candidateRoadBoundaryVertices = plan.boundary.roadSampledBoundaryVertexIndices.Select(index => plan.boundary.mesh.vertices[index]).ToArray(),
                    plan = new RepairReceipt { beforeNearSha256 = Sha(NearPath), beforeQuerySha256 = Sha(QueryPath),
                        beforeAirportSha256 = Sha(AirportPatchLidarConformanceAuthoring.OutputMeshPath),
                        invertedNearFaces = plan.nearCorrection.invertedFaces, zeroFootprintNearFaces = plan.nearCorrection.zeroFootprintFaces,
                        invertedQueryFaces = plan.queryCorrection.invertedFaces, zeroFootprintQueryFaces = plan.queryCorrection.zeroFootprintFaces,
                        changedAirportBoundaryVertices = plan.boundary.changedVertexIndices,
                        originalAirportBoundaryY = plan.boundary.changedVertexIndices.Select(index => airport.vertices[index].y).ToArray(),
                        correctedAirportBoundaryY = plan.boundary.changedVertexIndices.Select(index => plan.boundary.mesh.vertices[index].y).ToArray(),
                        affectedAirportNormalVertices = plan.boundary.affectedNormalIndices,
                        nearTrianglesBefore = near.triangles.Length / 3, nearTrianglesAfter = plan.near.triangles.Length / 3,
                        queryTrianglesBefore = query.transitionPatches.Single(item => item.stableId == RoadPatchId).triangles.Length / 3,
                        queryTrianglesAfter = plan.queryCorrection.indices.Length / 3,
                        boundaryProbeCount = plan.boundary.probeCount, maximumBoundaryMismatchMeters = plan.boundary.maximumBoundaryMismatchMeters,
                        meanBoundaryMismatchMeters = plan.boundary.meanBoundaryMismatchMeters, p95BoundaryMismatchMeters = plan.boundary.p95BoundaryMismatchMeters,
                        maximumBoundaryHeightAdjustmentMeters = plan.boundary.maximumHeightAdjustmentMeters } };
                File.WriteAllText(path, JsonUtility.ToJson(diagnostic, true) + "\n");
                Debug.Log("[QuestFlight][TerrainFloat32Repair] Read-only actual plan captured.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        /// <summary>A pre-write backup is required for every modified asset/record and paired meta.</summary>
        public static void RunBatch()
        {
            try
            {
                string output = System.Environment.GetEnvironmentVariable("QFL_TERRAIN_REPAIR_REPORT");
                ValidatePreWritePaths(System.Environment.GetEnvironmentVariable("QFL_TERRAIN_REPAIR_BACKUP_MANIFEST"), output);
                RequireSha(NearPath, BeforeNearSha256); RequireSha(QueryPath, BeforeQuerySha256);
                RequireSha(AirportPatchLidarConformanceAuthoring.OutputMeshPath, BeforeAirportSha256);
                Mesh near = AssetDatabase.LoadAssetAtPath<Mesh>(NearPath);
                ValidateCurrentRoadTerrainIdentity(near);
                var query = AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(QueryPath);
                Mesh airport = AssetDatabase.LoadAssetAtPath<Mesh>(AirportPatchLidarConformanceAuthoring.OutputMeshPath);
                string originalRoadJson = File.ReadAllText(Absolute(RoadSourcesPath));
                string originalAirportJson = File.ReadAllText(Absolute(AirportPatchLidarConformanceAuthoring.OutputReportPath));
                ValidateSourceReceipts(originalRoadJson, originalAirportJson);
                using Plan plan = Prepare(near, query, airport);
                RequireExactScope(plan);
                string beforeGeometry = CanonicalSupportGeometrySha256(near.vertices, near.triangles);
                if (beforeGeometry != CanonicalSupportGeometrySha256(plan.near.vertices, plan.near.triangles))
                    throw new InvalidDataException("Retained bank support geometry changed.");
                var receipt = new RepairReceipt {
                    generatedUtc = DateTime.UtcNow.ToString("o"), beforeNearSha256 = Sha(NearPath), beforeQuerySha256 = Sha(QueryPath),
                    beforeAirportSha256 = Sha(AirportPatchLidarConformanceAuthoring.OutputMeshPath),
                    originalRoadSourceReceiptJson = originalRoadJson, originalAirportCompilerReceiptJson = originalAirportJson,
                    canonicalNearSupportGeometrySha256 = beforeGeometry,
                    invertedNearFaces = plan.nearCorrection.invertedFaces, zeroFootprintNearFaces = plan.nearCorrection.zeroFootprintFaces,
                    invertedQueryFaces = plan.queryCorrection.invertedFaces, zeroFootprintQueryFaces = plan.queryCorrection.zeroFootprintFaces,
                    changedAirportBoundaryVertices = plan.boundary.changedVertexIndices, affectedAirportNormalVertices = plan.boundary.affectedNormalIndices,
                    originalAirportBoundaryY = plan.boundary.changedVertexIndices.Select(index => airport.vertices[index].y).ToArray(),
                    correctedAirportBoundaryY = plan.boundary.changedVertexIndices.Select(index => plan.boundary.mesh.vertices[index].y).ToArray(),
                    nearTrianglesBefore = near.triangles.Length / 3, nearTrianglesAfter = plan.near.triangles.Length / 3,
                    queryTrianglesBefore = query.transitionPatches.Single(item => item.stableId == RoadPatchId).triangles.Length / 3,
                    queryTrianglesAfter = plan.queryCorrection.indices.Length / 3,
                    boundaryProbeCount = plan.boundary.probeCount, maximumBoundaryMismatchMeters = plan.boundary.maximumBoundaryMismatchMeters,
                    maximumBoundaryHeightAdjustmentMeters = plan.boundary.maximumHeightAdjustmentMeters,
                    meanBoundaryMismatchMeters = plan.boundary.meanBoundaryMismatchMeters,
                    p95BoundaryMismatchMeters = plan.boundary.p95BoundaryMismatchMeters };
                EditorUtility.CopySerialized(plan.near, near); EditorUtility.SetDirty(near);
                EditorUtility.CopySerialized(plan.query, query); EditorUtility.SetDirty(query);
                EditorUtility.CopySerialized(plan.boundary.mesh, airport); EditorUtility.SetDirty(airport);
                AssetDatabase.SaveAssetIfDirty(near);
                AssetDatabase.SaveAssetIfDirty(query);
                AssetDatabase.SaveAssetIfDirty(airport);
                receipt.afterNearSha256 = Sha(NearPath); receipt.afterQuerySha256 = Sha(QueryPath);
                receipt.afterAirportSha256 = Sha(AirportPatchLidarConformanceAuthoring.OutputMeshPath);
                string roadJson = ReplaceStringField(originalRoadJson, "nearMeshSha256", receipt.afterNearSha256);
                roadJson = ReplaceStringField(roadJson, "groundQuerySha256", receipt.afterQuerySha256);
                roadJson = Regex.Replace(roadJson, "(\"nearTriangles\"\\s*:\\s*)104078", "${1}104073");
                File.WriteAllText(Absolute(RoadSourcesPath), AppendReceipt(roadJson, receipt));
                string airportJson = ReplaceStringField(originalAirportJson, "sourceLidarAssetSha256", receipt.afterNearSha256);
                airportJson = ReplaceStringField(airportJson, "outputMeshAssetSha256", receipt.afterAirportSha256);
                airportJson = ReplaceStringField(airportJson, "generatedUtc", receipt.generatedUtc);
                airportJson = ReplaceStringField(airportJson, "method", $"Existing accepted-road boundary-only reconciliation; {receipt.changedAirportBoundaryVertices.Length} existing boundary Y values changed, XZ/indices/UV/interior retained. Historical full compiler receipt is preserved in terrainFloat32Repair.");
                airportJson = ReplaceFloatField(airportJson, "maximumBoundaryMismatchMeters", receipt.maximumBoundaryMismatchMeters);
                airportJson = ReplaceFloatField(airportJson, "meanBoundaryMismatchMeters", receipt.meanBoundaryMismatchMeters);
                airportJson = ReplaceFloatField(airportJson, "p95BoundaryMismatchMeters", receipt.p95BoundaryMismatchMeters);
                airportJson = ReplaceBooleanField(airportJson, "sourceLidarMeshUnchanged", false);
                airportJson = ReplaceBooleanField(airportJson, "authoritativeTerrainQueryUnchanged", false);
                airportJson = airportJson.Replace("The airport patch remains the operational-core visual override; physics and the authoritative V2 terrain query asset are unchanged.",
                    "The near mesh and native query serialized indices changed in the recorded bounded repair; their vertex/normal coordinates and nonzero projected native height footprint are preserved. The airport remains a visual-only operational-core override.");
                airportJson = airportJson.Replace("The frozen lidar source OBJ, runway geometry/collider, water, and terrain outside the airport patch are not modified.",
                    "The frozen lidar source OBJ, runway geometry/collider and water are unchanged. Outside the airport visual boundary, only the explicitly recorded near/query index corrections occurred; their coordinates and sampled native height footprint are preserved.");
                airportJson = airportJson.TrimEnd();
                airportJson = airportJson.Substring(0, airportJson.Length - 1) + ",\n  \"sampledSupportAndNativeHeightFootprintPreserved\": true\n}";
                File.WriteAllText(Absolute(AirportPatchLidarConformanceAuthoring.OutputReportPath), AppendReceipt(airportJson, receipt));
                AssetDatabase.ImportAsset(RoadSourcesPath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(AirportPatchLidarConformanceAuthoring.OutputReportPath, ImportAssetOptions.ForceSynchronousImport);
                RoadSurfaceBuildGuard.Validate();
                File.WriteAllText(output, JsonUtility.ToJson(receipt, true) + "\n");
                Debug.Log($"[QuestFlight][TerrainFloat32Repair] SUCCESS: four windings, five recorded zero-footprint exclusions, {receipt.changedAirportBoundaryVertices.Length} boundary heights.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        public static void RequireExactScope(Plan plan)
        {
            if (!plan.nearCorrection.invertedFaces.SequenceEqual(new[] { 19097, 36471, 49396, 49400 }) ||
                !plan.nearCorrection.zeroFootprintFaces.SequenceEqual(new[] { 35598, 36470, 38881, 38882, 38942 }) ||
                !plan.queryCorrection.invertedFaces.SequenceEqual(new[] { 1466, 6032, 9606, 9610 }) ||
                !plan.queryCorrection.zeroFootprintFaces.SequenceEqual(new[] { 5584, 6031, 6760, 6761, 6821 }) ||
                !plan.boundary.changedVertexIndices.SequenceEqual(ReviewedBoundaryVertexIndices) || plan.boundary.probeCount != 4404 ||
                plan.boundary.maximumBoundaryMismatchMeters > AirportPatchLidarConformanceAuthoring.MaximumBoundaryMismatchMeters)
                throw new InvalidDataException("Current terrain correction differs from the reviewed four/five/exact77-index scope.");
        }

        public static string CanonicalSupportGeometrySha256(Vector3[] vertices, int[] triangles)
        {
            using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
            foreach (Vector3 vertex in vertices) { writer.Write(vertex.x); writer.Write(vertex.y); writer.Write(vertex.z); }
            for (int offset = 0; offset < triangles.Length; offset += 3)
            {
                int[] indices = { triangles[offset], triangles[offset + 1], triangles[offset + 2] };
                if (TerrainFloat32HeightfieldPolicy.ProjectedCrossY(vertices[indices[0]], vertices[indices[1]], vertices[indices[2]]) == 0d) continue;
                Array.Sort(indices); foreach (int index in indices) writer.Write(index);
            }
            writer.Flush(); using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
        }

        [Serializable] public sealed class RoadSourceIdentity
        {
            public int nearTriangles;
            public string nearMeshSha256, groundQuerySha256, bankSha256;
            public RepairReceipt terrainFloat32Repair;
        }

        public static RoadSourceIdentity ParseRoadSourceIdentity(string json)
        {
            var record = JsonUtility.FromJson<RoadSourceIdentity>(json);
            if (record == null) throw new InvalidDataException("Road source identity JSON is missing.");
            // JsonUtility may materialize a default nested object when its
            // field is absent. The actual serialized top-level field governs.
            int token = TopLevelPropertyValueOffset(json, "terrainFloat32Repair");
            if (token < 0) record.terrainFloat32Repair = null;
            else if (token >= json.Length || json[token] != '{' || record.terrainFloat32Repair == null)
                throw new InvalidDataException("Current terrain repair must be an explicit top-level receipt object.");
            return record;
        }

        public static int TopLevelPropertyValueOffset(string json, string property)
        {
            if (json == null) return -1;
            int depth = 0, found = -1;
            for (int index = 0; index < json.Length; index++)
            {
                char value = json[index];
                if (value == '{' || value == '[') { depth++; continue; }
                if (value == '}' || value == ']') { depth--; continue; }
                if (value != '"') continue;
                int start = ++index;
                for (; index < json.Length; index++)
                {
                    if (json[index] == '\\') { index++; continue; }
                    if (json[index] == '"') break;
                }
                if (index >= json.Length) throw new InvalidDataException("Unterminated JSON receipt string.");
                string token = json.Substring(start, index - start);
                int following = index + 1;
                while (following < json.Length && char.IsWhiteSpace(json[following])) following++;
                if (depth != 1 || token != property || following >= json.Length || json[following] != ':') continue;
                following++;
                while (following < json.Length && char.IsWhiteSpace(json[following])) following++;
                if (found >= 0) throw new InvalidDataException("Duplicate top-level source receipt field: " + property);
                found = following;
            }
            return found;
        }

        public static RoadSourceIdentity ValidateCurrentRoadTerrainIdentity(Mesh mesh)
        {
            if (mesh == null || AssetDatabase.GetAssetPath(mesh) != NearPath)
                throw new InvalidDataException("Expected the serialized shipping near terrain asset.");
            string sourceJson = File.ReadAllText(Absolute(RoadSourcesPath));
            var record = ParseRoadSourceIdentity(sourceJson);
            if (record.nearMeshSha256 != Sha(NearPath) || record.groundQuerySha256 != Sha(QueryPath) ||
                record.bankSha256 != Sha("Assets/Production/Environment/TrafficV2/Data/KBDU_VisibleRoadLaneSurfaces.bytes") ||
                record.nearTriangles != mesh.triangles.Length / 3)
                throw new InvalidDataException("Accepted local road terrain/source/bank identities differ.");
            if (record.terrainFloat32Repair == null)
            {
                if (record.nearMeshSha256 != BeforeNearSha256 || record.nearTriangles != 104078)
                    throw new InvalidDataException("Unrecognized pre-correction road repair receipt.");
            }
            else
            {
                RepairReceipt repair = record.terrainFloat32Repair;
                RequireSerializedRepairReceiptFields(sourceJson.Substring(TopLevelPropertyValueOffset(sourceJson, "terrainFloat32Repair")));
                ValidateRepairReceiptScope(repair);
                if (repair.zeroFootprintNearFaces == null || repair.invertedNearFaces == null ||
                    repair.beforeNearSha256 != BeforeNearSha256 || repair.afterNearSha256 != record.nearMeshSha256 ||
                    repair.afterQuerySha256 != record.groundQuerySha256 || repair.nearTrianglesBefore != 104078 ||
                    repair.nearTrianglesAfter != repair.nearTrianglesBefore - repair.zeroFootprintNearFaces.Length ||
                    repair.zeroFootprintNearFaces.Length != 5 || repair.invertedNearFaces.Length != 4 ||
                    repair.canonicalNearSupportGeometrySha256 != CanonicalSupportGeometrySha256(mesh.vertices, mesh.triangles))
                    throw new InvalidDataException("Current explicit float32 repair receipt does not bind terrain geometry.");
            }
            return record;
        }

        /// <summary>Pure receipt validation shared by all serialized terrain consumers.</summary>
        public static void ValidateRepairReceiptScope(RepairReceipt repair)
        {
            if (repair == null || repair.schema != "questflight.terrain.float32-existing-integration-repair.v1" ||
                repair.operation != "existing_index_and_boundary_only_correction_no_full_terrain_regeneration" ||
                repair.beforeNearSha256 != BeforeNearSha256 || repair.beforeQuerySha256 != BeforeQuerySha256 ||
                repair.beforeAirportSha256 != BeforeAirportSha256 ||
                !IsSha(repair.afterNearSha256) || !IsSha(repair.afterQuerySha256) || !IsSha(repair.afterAirportSha256) ||
                !IsSha(repair.canonicalNearSupportGeometrySha256) ||
                repair.invertedNearFaces == null || !repair.invertedNearFaces.SequenceEqual(new[] {19097,36471,49396,49400}) ||
                repair.zeroFootprintNearFaces == null || !repair.zeroFootprintNearFaces.SequenceEqual(new[] {35598,36470,38881,38882,38942}) ||
                repair.invertedQueryFaces == null || !repair.invertedQueryFaces.SequenceEqual(new[] {1466,6032,9606,9610}) ||
                repair.zeroFootprintQueryFaces == null || !repair.zeroFootprintQueryFaces.SequenceEqual(new[] {5584,6031,6760,6761,6821}) ||
                repair.changedAirportBoundaryVertices == null || !repair.changedAirportBoundaryVertices.SequenceEqual(ReviewedBoundaryVertexIndices) ||
                repair.originalAirportBoundaryY == null || repair.originalAirportBoundaryY.Length != 77 ||
                repair.correctedAirportBoundaryY == null || repair.correctedAirportBoundaryY.Length != 77 ||
                repair.originalAirportBoundaryY.Any(value => !float.IsFinite(value)) ||
                repair.correctedAirportBoundaryY.Any(value => !float.IsFinite(value)) ||
                repair.affectedAirportNormalVertices == null || repair.affectedAirportNormalVertices.Length != 92 ||
                repair.affectedAirportNormalVertices.Distinct().Count() != 92 ||
                repair.affectedAirportNormalVertices.Any(index => index < 0 || index >= 11618) ||
                repair.nearTrianglesBefore != 104078 || repair.nearTrianglesAfter != 104073 ||
                repair.queryTrianglesBefore != 13110 || repair.queryTrianglesAfter != 13105 || repair.boundaryProbeCount != 4404 ||
                !float.IsFinite(repair.maximumBoundaryMismatchMeters) || repair.maximumBoundaryMismatchMeters < 0 ||
                repair.maximumBoundaryMismatchMeters > AirportPatchLidarConformanceAuthoring.MaximumBoundaryMismatchMeters ||
                !float.IsFinite(repair.meanBoundaryMismatchMeters) || repair.meanBoundaryMismatchMeters < 0 ||
                repair.meanBoundaryMismatchMeters > repair.maximumBoundaryMismatchMeters ||
                !float.IsFinite(repair.p95BoundaryMismatchMeters) || repair.p95BoundaryMismatchMeters < 0 ||
                repair.p95BoundaryMismatchMeters > repair.maximumBoundaryMismatchMeters ||
                !float.IsFinite(repair.maximumBoundaryHeightAdjustmentMeters) || repair.maximumBoundaryHeightAdjustmentMeters <= .30f ||
                repair.maximumBoundaryHeightAdjustmentMeters > AirportPatchLidarConformanceAuthoring.MaximumOriginalBoundaryAdjustmentMeters ||
                !repair.nearVertexNormalUvAndBoundsDataPreserved || !repair.queryVerticesNormalsOrderingAndOriginalFourPatchesPreserved ||
                !repair.retainedBankBytesAndProjectedSupportGeometryPreserved)
                throw new InvalidDataException("Terrain repair receipt differs from the reviewed four/five/exact77 bounded contract.");
            if (ShaText(repair.originalRoadSourceReceiptJson) != BeforeRoadReceiptSha256 ||
                ShaText(repair.originalAirportCompilerReceiptJson) != BeforeAirportReceiptSha256)
                throw new InvalidDataException("Verbatim original terrain receipt history differs from the backed-up source identities.");
            ValidateSourceReceipts(repair.originalRoadSourceReceiptJson, repair.originalAirportCompilerReceiptJson);
            var historical = AirportPatchLidarConformanceAuthoring.ParseConformanceReport(repair.originalAirportCompilerReceiptJson);
            if (!historical.sourceLidarMeshUnchanged || !historical.authoritativeTerrainQueryUnchanged || historical.terrainFloat32Repair != null)
                throw new InvalidDataException("Original terrain compiler receipt history changed.");
        }

        static bool IsSha(string value) => value != null && Regex.IsMatch(value, "^[0-9a-f]{64}$");
        static string ShaText(string value)
        {
            if (value == null) return null;
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }

        public static void RequireSerializedRepairReceiptFields(string receiptJson)
        {
            // JsonUtility supplies field initializers for absent fields. The
            // serialized object itself must explicitly supply each claim.
            foreach (string field in new[] { "schema", "operation", "beforeNearSha256", "afterNearSha256", "beforeQuerySha256", "afterQuerySha256",
                "beforeAirportSha256", "afterAirportSha256", "originalRoadSourceReceiptJson", "originalAirportCompilerReceiptJson",
                "canonicalNearSupportGeometrySha256", "invertedNearFaces", "zeroFootprintNearFaces", "invertedQueryFaces", "zeroFootprintQueryFaces",
                "changedAirportBoundaryVertices", "affectedAirportNormalVertices", "originalAirportBoundaryY", "correctedAirportBoundaryY",
                "nearTrianglesBefore", "nearTrianglesAfter", "queryTrianglesBefore", "queryTrianglesAfter", "boundaryProbeCount",
                "maximumBoundaryMismatchMeters", "meanBoundaryMismatchMeters", "p95BoundaryMismatchMeters", "maximumBoundaryHeightAdjustmentMeters",
                "nearVertexNormalUvAndBoundsDataPreserved", "queryVerticesNormalsOrderingAndOriginalFourPatchesPreserved", "retainedBankBytesAndProjectedSupportGeometryPreserved" })
                if (TopLevelPropertyValueOffset(receiptJson, field) < 0)
                    throw new InvalidDataException("Missing explicit terrain repair field: " + field);
        }

        static string AppendReceipt(string json, RepairReceipt receipt)
        {
            string value = json.TrimEnd();
            if (!value.EndsWith("}", StringComparison.Ordinal) || value.Contains("\"terrainFloat32Repair\""))
                throw new InvalidDataException("Expected the original source receipt without a previous correction.");
            return value.Substring(0, value.Length - 1) + ",\n  \"terrainFloat32Repair\": " + JsonUtility.ToJson(receipt, true) + "\n}\n";
        }
        static string ReplaceStringField(string json, string field, string value)
        {
            string pattern = "(\"" + Regex.Escape(field) + "\"\\s*:\\s*\")[^\"]*(\")";
            if (Regex.Matches(json, pattern).Count != 1) throw new InvalidDataException("Expected one source receipt field: " + field);
            return Regex.Replace(json, pattern, match => match.Groups[1].Value + value + match.Groups[2].Value);
        }

        static string ReplaceFloatField(string json, string field, float value)
        {
            string pattern = "(\"" + Regex.Escape(field) + "\"\\s*:\\s*)[-+0-9.eE]+";
            if (Regex.Matches(json, pattern).Count != 1) throw new InvalidDataException("Expected one numeric source receipt field: " + field);
            return Regex.Replace(json, pattern, match => match.Groups[1].Value + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        static string ReplaceBooleanField(string json, string field, bool value)
        {
            string pattern = "(\"" + Regex.Escape(field) + "\"\\s*:\\s*)(true|false)";
            if (Regex.Matches(json, pattern).Count != 1) throw new InvalidDataException("Expected one boolean source receipt field: " + field);
            return Regex.Replace(json, pattern, match => match.Groups[1].Value + (value ? "true" : "false"));
        }

        public static void ValidateSourceReceipts(string roadJson, string airportJson)
        {
            if (string.IsNullOrEmpty(roadJson) || string.IsNullOrEmpty(airportJson)) throw new InvalidDataException("Original source receipts are required.");
            // Exercise every later transformation before any asset mutation.
            foreach (string field in new[] { "nearMeshSha256", "groundQuerySha256" }) ReplaceStringField(roadJson, field, "preflight");
            if (Regex.Matches(roadJson, "\"nearTriangles\"\\s*:\\s*104078\\b").Count != 1 ||
                Regex.Matches(roadJson, "\"nearTriangles\"\\s*:").Count != 1)
                throw new InvalidDataException("Expected one original nearTriangles=104078 receipt field.");
            foreach (string field in new[] { "sourceLidarAssetSha256", "outputMeshAssetSha256", "generatedUtc", "method" }) ReplaceStringField(airportJson, field, "preflight");
            foreach (string field in new[] { "maximumBoundaryMismatchMeters", "meanBoundaryMismatchMeters", "p95BoundaryMismatchMeters" }) ReplaceFloatField(airportJson, field, 0);
            foreach (string field in new[] { "sourceLidarMeshUnchanged", "authoritativeTerrainQueryUnchanged" }) ReplaceBooleanField(airportJson, field, false);
            if (!airportJson.Contains("The airport patch remains the operational-core visual override; physics and the authoritative V2 terrain query asset are unchanged."))
                throw new InvalidDataException("Expected the original compiler limitation to be superseded explicitly.");
            if (!airportJson.Contains("The frozen lidar source OBJ, runway geometry/collider, water, and terrain outside the airport patch are not modified."))
                throw new InvalidDataException("Expected the original outer-terrain limitation to be superseded explicitly.");
            if (airportJson.Contains("\"sampledSupportAndNativeHeightFootprintPreserved\""))
                throw new InvalidDataException("Current repair state already present in original airport receipt.");
            AppendReceipt(roadJson, new RepairReceipt()); AppendReceipt(airportJson, new RepairReceipt());
            try
            {
                var road = JsonUtility.FromJson<RoadSourceIdentity>(roadJson);
                var airport = JsonUtility.FromJson<AirportPatchLidarConformanceAuthoring.ConformanceReport>(airportJson);
                if (road.nearMeshSha256 != BeforeNearSha256 || road.groundQuerySha256 != BeforeQuerySha256 ||
                    airport.outputMeshAssetSha256 != BeforeAirportSha256)
                    throw new InvalidDataException("Original source receipt does not bind reviewed inputs.");
            }
            catch (ArgumentException exception) { throw new InvalidDataException("Malformed original source receipt JSON.", exception); }
        }

        public static void ValidatePreWritePaths(string path, string reportPath)
        {
            string manifestPath = CanonicalExternalPath(path);
            string outputPath = CanonicalExternalPath(reportPath);
            if (!Directory.Exists(Path.GetDirectoryName(outputPath))) throw new InvalidDataException("External report parent directory is missing.");
            if (File.Exists(outputPath) || Directory.Exists(outputPath)) throw new InvalidDataException("External repair report must be a new owned output, not existing work.");
            var seen = new HashSet<string>(WrittenPaths.SelectMany(value => new[] { Absolute(value), Absolute(value + ".meta") }), StringComparer.OrdinalIgnoreCase);
            if (!seen.Add(manifestPath) || !seen.Add(outputPath)) throw new InvalidDataException("Backup manifest/report paths alias another repair input or output.");
            var manifest = JsonUtility.FromJson<BackupManifest>(File.ReadAllText(path));
            var required = WrittenPaths.SelectMany(value => new[] { value, value + ".meta" }).ToArray();
            if (manifest?.files == null || manifest.files.Length != required.Length ||
                manifest.files.Select(item => item.projectRelativePath).Distinct(StringComparer.Ordinal).Count() != required.Length)
                throw new InvalidDataException("Exactly one backup for each reviewed asset/record/meta is required.");
            // Resolve/check every path before hashing any file. Reparse ancestors
            // are rejected, so a junction cannot turn an external path into a
            // live asset or another output after textual canonicalization.
            foreach (string asset in required)
            {
                BackupFile entry = manifest.files.SingleOrDefault(item => item.projectRelativePath == asset);
                if (entry == null || !seen.Add(CanonicalExternalPath(entry.backupPath)))
                    throw new InvalidDataException("Backup paths must be distinct external files without aliases: " + asset);
            }
            foreach (string asset in required)
            {
                BackupFile entry = manifest.files?.SingleOrDefault(item => item.projectRelativePath == asset);
                if (entry == null || entry.sourceSha256 != Sha(asset) || entry.backupSha256 != ShaAbsolute(entry.backupPath) || entry.sourceSha256 != entry.backupSha256)
                    throw new InvalidDataException("Missing or mismatched pre-write backup: " + asset);
            }
        }

        static string CanonicalExternalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new InvalidDataException("A fully qualified external repair path is required.");
            string canonical = Path.GetFullPath(path);
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (canonical.Equals(project, StringComparison.OrdinalIgnoreCase) || canonical.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Repair backups and reports must remain outside the project: " + canonical);
            string ancestor = canonical;
            while (!string.IsNullOrEmpty(ancestor))
            {
                if ((File.Exists(ancestor) || Directory.Exists(ancestor)) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Repair paths may not traverse filesystem links: " + canonical);
                ancestor = Path.GetDirectoryName(ancestor);
            }
            return canonical;
        }
        static void RequireSha(string path, string expected) { if (Sha(path) != expected) throw new InvalidDataException("Reviewed source identity changed: " + path); }
        static string Absolute(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        static string Sha(string path) => ShaAbsolute(Absolute(path));
        static string ShaAbsolute(string path) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    }
}
