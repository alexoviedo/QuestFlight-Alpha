using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuestFlight.Editor;
using QuestFlightLab.Editor;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.EditorTests
{
    public sealed class TerrainFloat32IntegrationRepairEditModeTests
    {
        [Test]
        public void HistoricalAndCurrentConformanceReceiptsRetainTheirDistinctTruthfulContracts()
        {
            var current = LoadConformance();
            Assert.That(current.terrainFloat32Repair, Is.Not.Null, "This after-integration consumer test requires the actual bounded repair receipt.");
            var historical = AirportPatchLidarConformanceAuthoring.ParseConformanceReport(current.terrainFloat32Repair.originalAirportCompilerReceiptJson);
            Assert.That(historical.terrainFloat32Repair, Is.Null);
            Assert.DoesNotThrow(() => AirportPatchLidarConformanceAuthoring.ValidateConformanceReceiptScope(historical));
            Assert.DoesNotThrow(() => AirportPatchLidarConformanceAuthoring.ValidateCompiledAsset(LoadAirport(), current));
            Assert.That(current.sourceLidarMeshUnchanged || current.authoritativeTerrainQueryUnchanged, Is.False);
            Assert.That(current.sampledSupportAndNativeHeightFootprintPreserved, Is.True);
        }

        [Test]
        public void CurrentConformanceRejectsMissingNullAndContradictoryFlagsWithoutAssetMutation()
        {
            byte[] before = File.ReadAllBytes(Absolute(AirportPatchLidarConformanceAuthoring.OutputMeshPath));
            var missing = LoadConformance(); missing.terrainFloat32Repair = null;
            Assert.Catch(() => AirportPatchLidarConformanceAuthoring.ValidateConformanceReceiptScope(missing));
            missing.sourceLidarMeshUnchanged = true; missing.authoritativeTerrainQueryUnchanged = true;
            missing.sampledSupportAndNativeHeightFootprintPreserved = false;
            Assert.Catch(() => AirportPatchLidarConformanceAuthoring.ValidateConformanceReceiptScope(missing),
                "Current output cannot regain historical status by falsifying unchanged flags and dropping its receipt.");
            Assert.Catch(() => AirportPatchLidarConformanceAuthoring.ParseConformanceReport("{\"terrainFloat32Repair\":null}"));
            Assert.Catch(() => AirportPatchLidarConformanceAuthoring.ParseConformanceReport("{\"terrainFloat32Repair\":{},\"terrainFloat32Repair\":{}}"));
            string actualJson = File.ReadAllText(Absolute(AirportPatchLidarConformanceAuthoring.OutputReportPath));
            Assert.Catch(() => AirportPatchLidarConformanceAuthoring.ParseConformanceReport(actualJson.Replace(
                "\"nearVertexNormalUvAndBoundsDataPreserved\"", "\"missingExplicitPreservationClaim\"")));
            foreach (Action<AirportPatchLidarConformanceAuthoring.ConformanceReport> change in new Action<AirportPatchLidarConformanceAuthoring.ConformanceReport>[] {
                report => report.sourceLidarMeshUnchanged = true,
                report => report.authoritativeTerrainQueryUnchanged = true,
                report => report.sampledSupportAndNativeHeightFootprintPreserved = false,
                report => report.sourceLidarAssetSha256 = new string('0',64),
                report => report.outputMeshAssetSha256 = new string('0',64),
                report => report.maximumBoundaryMismatchMeters = float.NaN })
            {
                var report = LoadConformance(); change(report);
                Assert.Catch(() => AirportPatchLidarConformanceAuthoring.ValidateCompiledAsset(LoadAirport(), report));
            }
            Assert.That(File.ReadAllBytes(Absolute(AirportPatchLidarConformanceAuthoring.OutputMeshPath)), Is.EqualTo(before));
        }

        [Test]
        public void CurrentConformanceRejectsChangedFourFive77ScopeAndHistoricalIdentities()
        {
            foreach (Action<TerrainFloat32IntegrationRepairAuthoring.RepairReceipt> change in new Action<TerrainFloat32IntegrationRepairAuthoring.RepairReceipt>[] {
                receipt => receipt.invertedNearFaces[0]++,
                receipt => receipt.zeroFootprintNearFaces[0]++,
                receipt => receipt.invertedQueryFaces[0]++,
                receipt => receipt.zeroFootprintQueryFaces[0]++,
                receipt => receipt.changedAirportBoundaryVertices[0]++,
                receipt => receipt.originalAirportBoundaryY = null,
                receipt => receipt.correctedAirportBoundaryY[0] = float.NaN,
                receipt => receipt.beforeNearSha256 = new string('0',64),
                receipt => receipt.beforeQuerySha256 = new string('0',64),
                receipt => receipt.beforeAirportSha256 = new string('0',64),
                receipt => receipt.originalRoadSourceReceiptJson = "{}",
                receipt => receipt.originalAirportCompilerReceiptJson = "{}",
                receipt => receipt.retainedBankBytesAndProjectedSupportGeometryPreserved = false })
            {
                var report = LoadConformance(); change(report.terrainFloat32Repair);
                Assert.Catch(() => AirportPatchLidarConformanceAuthoring.ValidateConformanceReceiptScope(report));
            }
        }

        [Test]
        public void CurrentConformanceMustMatchActualCanonicalSourceReceiptAndBoundaryY()
        {
            foreach (Action<TerrainFloat32IntegrationRepairAuthoring.RepairReceipt> change in new Action<TerrainFloat32IntegrationRepairAuthoring.RepairReceipt>[] {
                receipt => receipt.canonicalNearSupportGeometrySha256 = new string('0',64),
                receipt => receipt.afterQuerySha256 = new string('0',64),
                receipt => receipt.correctedAirportBoundaryY[0] += .01f,
                receipt => receipt.originalAirportBoundaryY[0] += .01f })
            {
                var report = LoadConformance(); change(report.terrainFloat32Repair);
                Assert.Catch(() => AirportPatchLidarConformanceAuthoring.ValidateCompiledAsset(LoadAirport(), report));
            }
        }

        static AirportPatchLidarConformanceAuthoring.ConformanceReport LoadConformance() =>
            AirportPatchLidarConformanceAuthoring.ParseConformanceReport(File.ReadAllText(Absolute(AirportPatchLidarConformanceAuthoring.OutputReportPath)));

        [Test]
        public void StoredFloat32WindingIsCorrectedWithoutChangingCoordinates()
        {
            var vertices = new[] {
                new Vector3(-1601.00023f, 1.446399312f, -465.9519f),
                new Vector3(-1600.97006f, 1.579113835f, -468.66666f),
                new Vector3(-1601.00017f, 1.446325346f, -465.95935f) };
            var snapshot = (Vector3[])vertices.Clone();
            Assert.That(TerrainFloat32HeightfieldPolicy.ProjectedCrossY(vertices[0], vertices[1], vertices[2]), Is.LessThan(0));
            var corrected = TerrainFloat32HeightfieldPolicy.CorrectIndices(vertices, new[] { 0, 1, 2 }, false);
            Assert.That(corrected.indices, Is.EqualTo(new[] { 0, 2, 1 }));
            Assert.That(corrected.invertedFaces, Is.EqualTo(new[] { 0 }));
            Assert.That(vertices, Is.EqualTo(snapshot));
            var twice = TerrainFloat32HeightfieldPolicy.CorrectIndices(vertices, corrected.indices, false);
            Assert.That(twice.indices, Is.EqualTo(corrected.indices));
            Assert.That(twice.invertedFaces, Is.Empty);
        }

        [Test]
        public void CollapsedPayloadFailsClosedUnlessExplicitZeroFootprintPolicyIsUsed()
        {
            Vector3[] vertices = { new Vector3(1600f, 0, 0), new Vector3(1600.00003f, .23f, 2),
                new Vector3(1600.00002f, .1f, 1), new Vector3(1601f, 0, 0) };
            Assert.That(Vector3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]).sqrMagnitude, Is.GreaterThan(0));
            Assert.That(TerrainFloat32HeightfieldPolicy.ProjectedCrossY(vertices[0], vertices[1], vertices[2]), Is.Zero);
            Assert.Throws<InvalidDataException>(() => TerrainFloat32HeightfieldPolicy.CorrectIndices(vertices, new[] { 0, 1, 2 }, false));
            var result = TerrainFloat32HeightfieldPolicy.CorrectIndices(vertices, new[] { 0, 1, 2, 0, 1, 3 }, true);
            Assert.That(result.zeroFootprintFaces, Is.EqualTo(new[] { 0 }));
            Assert.That(result.indices, Is.EqualTo(new[] { 0, 1, 3 }));
            Assert.Throws<InvalidDataException>(() => TerrainFloat32HeightfieldPolicy.CorrectIndices(vertices, new[] { 0, 1, 2 }, true));
        }

        [Test]
        public void InvalidStoredCoordinatesAndIndicesAreRejected()
        {
            var good = new[] { Vector3.zero, Vector3.forward, Vector3.right };
            Assert.Throws<InvalidDataException>(() => TerrainFloat32HeightfieldPolicy.CorrectIndices(good, new[] { 0, 1, 3 }, false));
            Assert.Throws<InvalidDataException>(() => TerrainFloat32HeightfieldPolicy.CorrectIndices(good, new[] { -1, 1, 2 }, false));
            Assert.Throws<InvalidDataException>(() => TerrainFloat32HeightfieldPolicy.CorrectIndices(good, new[] { 0, 1 }, false));
            good[0].x = float.NaN;
            Assert.Throws<InvalidDataException>(() => TerrainFloat32HeightfieldPolicy.CorrectIndices(good, new[] { 0, 1, 2 }, false));
        }

        [Test]
        public void ReceiptPresenceComesFromSerializedTopLevelFieldAndRejectsNullOrDuplicate()
        {
            const string absent = "{\"nearTriangles\":104078,\"history\":{\"terrainFloat32Repair\":{}},\"note\":\"escaped \\\"terrainFloat32Repair\\\":{}\"}";
            Assert.That(TerrainFloat32IntegrationRepairAuthoring.ParseRoadSourceIdentity(absent).terrainFloat32Repair, Is.Null);
            Assert.That(TerrainFloat32IntegrationRepairAuthoring.TopLevelPropertyValueOffset(absent, "terrainFloat32Repair"), Is.EqualTo(-1));
            Assert.Throws<InvalidDataException>(() => TerrainFloat32IntegrationRepairAuthoring.ParseRoadSourceIdentity("{\"terrainFloat32Repair\":null}"));
            Assert.Throws<InvalidDataException>(() => TerrainFloat32IntegrationRepairAuthoring.ParseRoadSourceIdentity("{\"terrainFloat32Repair\":{},\"terrainFloat32Repair\":{}}"));
            const string present = "{\"terrainFloat32Repair\":{\"operation\":\"actual-current-receipt\"}}";
            Assert.That(TerrainFloat32IntegrationRepairAuthoring.ParseRoadSourceIdentity(present).terrainFloat32Repair.operation,
                Is.EqualTo("actual-current-receipt"));
        }

        [Test]
        public void LiveAssetBackupOrReportAliasesAreRejectedBeforeMutation()
        {
            WithExternalPathFixture((directory, manifest, entries) =>
            {
                byte[] original = File.ReadAllBytes(Absolute(TerrainFloat32IntegrationRepairAuthoring.NearPath));
                string output = Path.Combine(directory, "report.json");
                Assert.Throws<InvalidDataException>(() => TerrainFloat32IntegrationRepairAuthoring.ValidatePreWritePaths(manifest,
                    Absolute(TerrainFloat32IntegrationRepairAuthoring.NearPath)));
                entries[0].backupPath = Absolute(TerrainFloat32IntegrationRepairAuthoring.NearPath);
                File.WriteAllText(manifest, JsonUtility.ToJson(new TerrainFloat32IntegrationRepairAuthoring.BackupManifest { files = entries }));
                Assert.Throws<InvalidDataException>(() => TerrainFloat32IntegrationRepairAuthoring.ValidatePreWritePaths(manifest, output));
                Assert.That(File.ReadAllBytes(Absolute(TerrainFloat32IntegrationRepairAuthoring.NearPath)), Is.EqualTo(original));
                Assert.That(File.Exists(output), Is.False);
            });
        }

        [Test]
        public void DuplicateBackupAndManifestReportAliasesAreRejectedBeforeHashing()
        {
            WithExternalPathFixture((directory, manifest, entries) =>
            {
                Assert.Throws<InvalidDataException>(() => TerrainFloat32IntegrationRepairAuthoring.ValidatePreWritePaths(manifest, entries[0].backupPath));
                Assert.Throws<InvalidDataException>(() => TerrainFloat32IntegrationRepairAuthoring.ValidatePreWritePaths(manifest, manifest));
                entries[1].backupPath = entries[0].backupPath;
                File.WriteAllText(manifest, JsonUtility.ToJson(new TerrainFloat32IntegrationRepairAuthoring.BackupManifest { files = entries }));
                Assert.Throws<InvalidDataException>(() => TerrainFloat32IntegrationRepairAuthoring.ValidatePreWritePaths(manifest, Path.Combine(directory, "report.json")));
                Assert.That(File.Exists(Path.Combine(directory, "report.json")), Is.False);
            });
        }

        [Test]
        public void InvalidOrPreviouslyCorrectedReceiptsAreRejectedWithoutAssetMutation()
        {
            string road = File.ReadAllText(Absolute(TerrainFloat32IntegrationRepairAuthoring.RoadSourcesPath));
            string airport = File.ReadAllText(Absolute(AirportPatchLidarConformanceAuthoring.OutputReportPath));
            var current = TerrainFloat32IntegrationRepairAuthoring.ParseRoadSourceIdentity(road);
            if (current.terrainFloat32Repair != null)
            {
                airport = current.terrainFloat32Repair.originalAirportCompilerReceiptJson;
                road = current.terrainFloat32Repair.originalRoadSourceReceiptJson;
            }
            var bytes = TerrainFloat32IntegrationRepairAuthoring.WrittenPaths.ToDictionary(path => path, path => File.ReadAllBytes(Absolute(path)));
            Assert.DoesNotThrow(() => TerrainFloat32IntegrationRepairAuthoring.ValidateSourceReceipts(road, airport));
            Assert.Throws<InvalidDataException>(() => TerrainFloat32IntegrationRepairAuthoring.ValidateSourceReceipts(road.Replace("\"nearMeshSha256\"", "\"missingField\""), airport));
            string duplicate = road.TrimEnd(); duplicate = duplicate.Substring(0, duplicate.Length - 1) + ",\"nearTriangles\":104078}";
            Assert.Throws<InvalidDataException>(() => TerrainFloat32IntegrationRepairAuthoring.ValidateSourceReceipts(duplicate, airport));
            string corrected = road.TrimEnd(); corrected = corrected.Substring(0, corrected.Length - 1) + ",\"terrainFloat32Repair\":{}}";
            Assert.Throws<InvalidDataException>(() => TerrainFloat32IntegrationRepairAuthoring.ValidateSourceReceipts(corrected, airport));
            Assert.Throws<InvalidDataException>(() => TerrainFloat32IntegrationRepairAuthoring.ValidateSourceReceipts(road,
                airport.Replace("\"p95BoundaryMismatchMeters\"", "\"missingMetric\"")));
            foreach (var asset in bytes) Assert.That(File.ReadAllBytes(Absolute(asset.Key)), Is.EqualTo(asset.Value));
        }

        [Test]
        public void ActualCurrentPlanPreservesSerializedSourceGeometryAndMatchesReviewedScope()
        {
            Mesh near = LoadNear(); var query = LoadQuery(); Mesh airport = LoadAirport();
            var source = TerrainFloat32IntegrationRepairAuthoring.ValidateCurrentRoadTerrainIdentity(near);
            byte[] nearDisk = File.ReadAllBytes(Absolute(TerrainFloat32IntegrationRepairAuthoring.NearPath));
            byte[] queryDisk = File.ReadAllBytes(Absolute(TerrainFloat32IntegrationRepairAuthoring.QueryPath));
            byte[] airportDisk = File.ReadAllBytes(Absolute(AirportPatchLidarConformanceAuthoring.OutputMeshPath));
            using var plan = TerrainFloat32IntegrationRepairAuthoring.Prepare(near, query, airport);
            if (source.terrainFloat32Repair == null) TerrainFloat32IntegrationRepairAuthoring.RequireExactScope(plan);
            else
            {
                Assert.That(plan.nearCorrection.invertedFaces, Is.Empty);
                Assert.That(plan.nearCorrection.zeroFootprintFaces, Is.Empty);
                Assert.That(plan.boundary.changedVertexIndices, Is.Empty);
            }
            Assert.That(plan.near.vertices, Is.EqualTo(near.vertices));
            Assert.That(plan.near.normals, Is.EqualTo(near.normals));
            Assert.That(plan.near.uv, Is.EqualTo(near.uv));
            Assert.That(plan.near.bounds, Is.EqualTo(near.bounds));
            Assert.That(TerrainFloat32IntegrationRepairAuthoring.CanonicalSupportGeometrySha256(plan.near.vertices, plan.near.triangles),
                Is.EqualTo(TerrainFloat32IntegrationRepairAuthoring.CanonicalSupportGeometrySha256(near.vertices, near.triangles)));
            for (int index = 0; index < query.transitionPatches.Length; index++)
            {
                var before = query.transitionPatches[index]; var after = plan.query.transitionPatches[index];
                Assert.That(after.vertices, Is.EqualTo(before.vertices)); Assert.That(after.normals, Is.EqualTo(before.normals));
                if (before.stableId != TerrainFloat32IntegrationRepairAuthoring.RoadPatchId)
                    Assert.That(JsonUtility.ToJson(after), Is.EqualTo(JsonUtility.ToJson(before)));
            }
            Vector3[] oldVertices = airport.vertices, newVertices = plan.boundary.mesh.vertices;
            Vector3[] oldNormals = airport.normals, newNormals = plan.boundary.mesh.normals;
            for (int index = 0; index < oldVertices.Length; index++)
            {
                Assert.That(newVertices[index].x, Is.EqualTo(oldVertices[index].x));
                Assert.That(newVertices[index].z, Is.EqualTo(oldVertices[index].z));
                if (!plan.boundary.changedVertexIndices.Contains(index)) Assert.That(newVertices[index], Is.EqualTo(oldVertices[index]));
                else Assert.That(Mathf.Abs(newVertices[index].x) == 800f || Mathf.Abs(newVertices[index].z) == 300f, Is.True);
                if (!plan.boundary.affectedNormalIndices.Contains(index)) Assert.That(newNormals[index], Is.EqualTo(oldNormals[index]));
                Assert.That(float.IsFinite(newNormals[index].x) && float.IsFinite(newNormals[index].y) && float.IsFinite(newNormals[index].z), Is.True);
            }
            Assert.That(plan.boundary.mesh.triangles, Is.EqualTo(airport.triangles));
            Assert.That(plan.boundary.mesh.uv, Is.EqualTo(airport.uv));
            Assert.That(plan.boundary.mesh.bounds, Is.EqualTo(airport.bounds));
            Assert.That(plan.boundary.probeCount, Is.EqualTo(4404));
            Assert.That(plan.boundary.maximumBoundaryMismatchMeters, Is.LessThanOrEqualTo(.03f));
            Assert.That(File.ReadAllBytes(Absolute(TerrainFloat32IntegrationRepairAuthoring.NearPath)), Is.EqualTo(nearDisk));
            Assert.That(File.ReadAllBytes(Absolute(TerrainFloat32IntegrationRepairAuthoring.QueryPath)), Is.EqualTo(queryDisk));
            Assert.That(File.ReadAllBytes(Absolute(AirportPatchLidarConformanceAuthoring.OutputMeshPath)), Is.EqualTo(airportDisk));
            using var twice = TerrainFloat32IntegrationRepairAuthoring.Prepare(plan.near, plan.query, plan.boundary.mesh);
            Assert.That(twice.near.triangles, Is.EqualTo(plan.near.triangles));
            Assert.That(twice.boundary.changedVertexIndices, Is.Empty);
        }

        [Test]
        public void FourOriginalSeamCounterexamplesAreCorrectedWithoutChangingRunwayInterior()
        {
            Mesh near = LoadNear(); Mesh airport = LoadAirport();
            var source = TerrainFloat32IntegrationRepairAuthoring.ValidateCurrentRoadTerrainIdentity(near);
            Mesh original = UnityEngine.Object.Instantiate(airport);
            try
            {
                // After staging, replay the exact original77 heights recorded by
                // the real bounded compiler. Before staging, use actual bad data.
                if (source.terrainFloat32Repair != null)
                {
                    var receipt = source.terrainFloat32Repair;
                    Vector3[] vertices = original.vertices;
                    for (int index = 0; index < receipt.changedAirportBoundaryVertices.Length; index++)
                        vertices[receipt.changedAirportBoundaryVertices[index]].y = receipt.originalAirportBoundaryY[index];
                    original.vertices = vertices;
                }
                var result = AirportPatchLidarConformanceAuthoring.ReconcileExistingRoadBoundary(original, near);
                try
                {
                    Assert.That(result.changedVertexIndices, Is.EqualTo(TerrainFloat32IntegrationRepairAuthoring.ReviewedBoundaryVertexIndices));
                    Assert.That(result.changedVertexIndices.Contains(130) || result.changedVertexIndices.Contains(8878), Is.False,
                        "One-ULP NumPy-only candidates retain their already-correct original Unity Y.");
                    Assert.That(result.maximumHeightAdjustmentMeters, Is.GreaterThan(.30f));
                    Assert.That(result.maximumBoundaryMismatchMeters, Is.LessThanOrEqualTo(.03f));
                    Assert.That(result.probeCount, Is.EqualTo(4404));
                    Assert.That(result.mesh.vertices.Take(8098).Where(vertex => Mathf.Abs(vertex.x) < 799.99f && Mathf.Abs(vertex.z) < 299.99f),
                        Is.EqualTo(original.vertices.Take(8098).Where(vertex => Mathf.Abs(vertex.x) < 799.99f && Mathf.Abs(vertex.z) < 299.99f)));
                }
                finally { UnityEngine.Object.DestroyImmediate(result.mesh); }
            }
            finally { UnityEngine.Object.DestroyImmediate(original); }
        }

        [Test]
        public void NativeQueryAndRetainedRoadSamplerRemainEquivalentAfterWindingCorrection()
        {
            Mesh near = LoadNear(); var query = LoadQuery(); Mesh airport = LoadAirport();
            using var plan = TerrainFloat32IntegrationRepairAuthoring.Prepare(near, query, airport);
            var originalSampler = new TrafficRoadSurfaceAuthoring.MeshSurface(near, 32f);
            var correctedSampler = new TrafficRoadSurfaceAuthoring.MeshSurface(plan.near, 32f);
            var patch = query.transitionPatches.Single(item => item.stableId == TerrainFloat32IntegrationRepairAuthoring.RoadPatchId);
            int[] reviewedFaces = { 1466, 6032, 9606, 9610, 5584, 6031, 6760, 6761, 6821 };
            foreach (int face in Enumerable.Range(0, (patch.triangles.Length / 3 + 30) / 31).Select(index => index * 31)
                         .Where(index => index < patch.triangles.Length / 3).Concat(reviewedFaces).Distinct())
            {
                Vector3 a = patch.vertices[patch.triangles[face * 3]], b = patch.vertices[patch.triangles[face * 3 + 1]], c = patch.vertices[patch.triangles[face * 3 + 2]];
                Vector3 point = (a + b + c) / 3f;
                bool oldFound = originalSampler.TrySample(new Vector2(point.x, point.z), out float oldY, out Vector3 oldNormal);
                bool newFound = correctedSampler.TrySample(new Vector2(point.x, point.z), out float newY, out Vector3 newNormal);
                Assert.That(newFound, Is.EqualTo(oldFound));
                if (oldFound) { Assert.That(newY, Is.EqualTo(oldY).Within(.001f)); Assert.That(Vector3.Angle(newNormal, oldNormal), Is.LessThanOrEqualTo(.05f)); }
                bool oldQuery = query.TrySample(point.x, point.z, out var oldHit);
                bool newQuery = plan.query.TrySample(point.x, point.z, out var newHit);
                Assert.That(newQuery, Is.EqualTo(oldQuery));
                if (oldQuery) { Assert.That(newHit.heightMeters, Is.EqualTo(oldHit.heightMeters).Within(.001f)); Assert.That(Vector3.Angle(newHit.normal, oldHit.normal), Is.LessThanOrEqualTo(.05f)); }
            }
        }

        static Mesh LoadNear() => AssetDatabase.LoadAssetAtPath<Mesh>(TerrainFloat32IntegrationRepairAuthoring.NearPath);
        static ProductionTerrainSurfaceAsset LoadQuery() => AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(TerrainFloat32IntegrationRepairAuthoring.QueryPath);
        static Mesh LoadAirport() => AssetDatabase.LoadAssetAtPath<Mesh>(AirportPatchLidarConformanceAuthoring.OutputMeshPath);
        static string Absolute(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));

        static void WithExternalPathFixture(Action<string, string, TerrainFloat32IntegrationRepairAuthoring.BackupFile[]> assertion)
        {
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            string owned = Path.Combine(temporaryRoot, "QuestFlightTerrainGuard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(owned);
            try
            {
                var entries = TerrainFloat32IntegrationRepairAuthoring.WrittenPaths.SelectMany(path => new[] { path, path + ".meta" })
                    .Select((path, index) => new TerrainFloat32IntegrationRepairAuthoring.BackupFile { projectRelativePath = path,
                        sourceSha256 = "not-hashed-for-negative-path-case", backupSha256 = "not-hashed-for-negative-path-case",
                        backupPath = Path.Combine(owned, "backup-" + index) }).ToArray();
                string manifest = Path.Combine(owned, "backup-manifest.json");
                File.WriteAllText(manifest, JsonUtility.ToJson(new TerrainFloat32IntegrationRepairAuthoring.BackupManifest { files = entries }));
                assertion(owned, manifest, entries);
            }
            finally
            {
                string resolved = Path.GetFullPath(owned);
                if (!resolved.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("QuestFlightTerrainGuard-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Owned external fixture cleanup escaped the temporary directory.");
                Directory.Delete(resolved, true);
            }
        }
    }
}
