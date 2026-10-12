using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using QuestFlight.Product;
using QuestFlightLab.Aircraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace QuestFlight.Editor
{
    // A serialized, external-view-only derivative. Imported objects and their
    // identities remain intact; no runtime discovery or mesh construction.
    public static class ProductionAircraftExteriorBatchAuthoring
    {
        public const string PrefabPath = "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const string Folder = "Assets/Production/Rendering/ExteriorBatch";
        public const string MeshPath = Folder + "/C172ExteriorBatch.asset";
        public const string BatchName = "ProductionExteriorBatch";
        public const string SourcePath = "Assets/Resources/QuestFlightLab/ImportedAssets/Cessna172KogThorns/cessna172.glb";
        public static readonly string[] Suffixes =
        { "002", "003", "004", "005", "006", "007", "009", "010", "011", "018", "020", "022", "023", "024", "025", "026" };

        public static MeshRenderer[] GetSources(GameObject root)
        {
            Transform exterior = root.transform.Find("AircraftVisualRoot/ImportedAircraftExterior/Cessna_Exterior");
            Require(exterior != null, "Missing authored exterior.");
            return Suffixes.Select(suffix =>
            {
                string name = "Cessna_Exterior_Body_MAT_0." + suffix;
                Transform node = exterior.Find(name);
                Require(node != null, "Missing fixed exterior node " + name);
                MeshRenderer renderer = node.Find(name + "_Body_MAT_0")?.GetComponent<MeshRenderer>();
                Require(renderer != null, "Missing fixed exterior renderer " + name);
                Require(node.GetComponents<MonoBehaviour>().Length == 0 && renderer.GetComponents<MonoBehaviour>().Length == 0,
                    "A source node has independent behaviour: " + name);
                return renderer;
            }).ToArray();
        }

        public static Mesh CreateMesh(GameObject root)
        {
            // Match ProductSpaceWarpAudit.RunBatch: ShaderGraph exposes the
            // active pipeline's passes only after the pipeline is instantiated.
            if (RenderPipelineManager.currentPipeline == null)
            {
                var cameraObject = new GameObject("Exterior batch coverage camera") { hideFlags = HideFlags.HideAndDontSave };
                var camera = cameraObject.AddComponent<Camera>();
                var target = new RenderTexture(32, 32, 24);
                camera.targetTexture = target;
                try { QuestFlight.Rendering.ProductRenderCapture.Render(camera); }
                finally
                {
                    Object.DestroyImmediate(cameraObject);
                    target.Release();
                    Object.DestroyImmediate(target);
                }
            }
            Transform visual = root.transform.Find("AircraftVisualRoot");
            MeshRenderer[] sources = GetSources(root);
            Material material = sources[0].sharedMaterial;
            Require(material != null, "Missing source material.");
            // Raw glTF imports need the existing product conversion. Validate
            // that path on a temporary copy; do not mutate the source asset or
            // give the batch a different material from its original renderers.
            var converted = new Material(material);
            try
            {
                ProductSpaceWarpAudit.ConvertMaterial(converted);
                Require(converted.FindPass("XRMotionVectors") >= 0 && converted.GetShaderPassEnabled("XRMotionVectors"),
                    "Existing material conversion lacks motion coverage: " + material.shader.name);
            }
            finally { Object.DestroyImmediate(converted); }
            foreach (var renderer in sources)
            {
                Require(renderer.sharedMaterials.Length == 1 && renderer.sharedMaterial == material,
                    "Source material slots differ.");
                Require(!renderer.HasPropertyBlock(), "Source has a material property override: " + renderer.name);
                Require(renderer.probeAnchor == sources[0].probeAnchor && renderer.lightProbeUsage == sources[0].lightProbeUsage &&
                    renderer.reflectionProbeUsage == sources[0].reflectionProbeUsage && renderer.shadowCastingMode == ShadowCastingMode.Off &&
                    !renderer.receiveShadows && renderer.motionVectorGenerationMode == sources[0].motionVectorGenerationMode &&
                    renderer.gameObject.layer == sources[0].gameObject.layer && renderer.renderingLayerMask == sources[0].renderingLayerMask &&
                    renderer.lightmapIndex < 0 && renderer.realtimeLightmapIndex < 0,
                    "Source rendering settings are incompatible: " + renderer.name);
            }
            // Reject serialized animation/behaviour references to source nodes.
            var objects = sources.SelectMany(r => new Object[] { r, r.transform, r.gameObject, r.transform.parent, r.transform.parent.gameObject }).ToHashSet();
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour is AircraftVisibilityProfileController) continue;
                var so = new SerializedObject(behaviour);
                var property = so.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && objects.Contains(property.objectReferenceValue))
                    {
                        // Whole-aircraft showroom suppression is extended to the
                        // batch by Configure; it is not independent part motion.
                        if (behaviour is ProductFlightModeMenuView && property.objectReferenceValue is Renderer &&
                            property.propertyPath.StartsWith("aircraftPresentationRenderers.Array.data[", StringComparison.Ordinal)) continue;
                        throw new InvalidOperationException("Independent source binding: " + behaviour.GetType().Name + "." + property.propertyPath);
                    }
            }
            var combine = sources.Select(renderer =>
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                Require(mesh != null && mesh.isReadable && mesh.subMeshCount == 1 && mesh.GetTopology(0) == MeshTopology.Triangles,
                    "Source must be readable single-submesh triangles: " + renderer.name);
                Matrix4x4 transform = visual.worldToLocalMatrix * renderer.localToWorldMatrix;
                Require(transform.determinant > 0f, "Mirrored source needs separate winding treatment.");
                return new CombineInstance { mesh = mesh, subMeshIndex = 0, transform = transform };
            }).ToArray();
            Require(combine.Sum(c => (long)c.mesh.GetIndexCount(0) / 3) == 4457, "Audited source topology changed.");
            var result = new Mesh { name = "C172ExteriorBatch", indexFormat =
                combine.Sum(c => c.mesh.vertexCount) > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try
            {
                result.CombineMeshes(combine, true, true, false);
                ValidateGeometry(root, result);
                return result;
            }
            catch { Object.DestroyImmediate(result); throw; }
        }

        public static void ValidateGeometry(GameObject root, Mesh combined)
        {
            MeshRenderer[] sources = GetSources(root);
            Transform visual = root.transform.Find("AircraftVisualRoot");
            var positions = combined.vertices;
            var normals = combined.normals;
            var tangents = combined.tangents;
            var colors = combined.colors;
            int[] triangles = combined.triangles;
            var allUvs = Enumerable.Range(0, 8).Select(_ => new System.Collections.Generic.List<Vector4>()).ToArray();
            for (int channel = 0; channel < 8; channel++) combined.GetUVs(channel, allUvs[channel]);
            int vertexOffset = 0, indexOffset = 0;
            foreach (var renderer in sources)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Matrix4x4 matrix = visual.worldToLocalMatrix * renderer.localToWorldMatrix;
                Matrix4x4 normalMatrix = matrix.inverse.transpose;
                Vector3[] sourcePositions = mesh.vertices, sourceNormals = mesh.normals;
                Vector4[] sourceTangents = mesh.tangents;
                Color[] sourceColors = mesh.colors;
                Require(sourceNormals.Length == mesh.vertexCount && normals.Length == positions.Length, "Normals must be preserved.");
                Require(sourceTangents.Length == 0 ? tangents.Length == 0 : tangents.Length == positions.Length, "Tangent layout changed.");
                Require(sourceColors.Length == 0 ? colors.Length == 0 : colors.Length == positions.Length, "Color layout changed.");
                for (int i = 0; i < mesh.vertexCount; i++)
                {
                    // 10 micrometres in aircraft space; never a decimation tolerance.
                    Require(Vector3.Distance(positions[vertexOffset + i], matrix.MultiplyPoint3x4(sourcePositions[i])) <= 0.00001f, "Position changed.");
                    Require(Vector3.Distance(normals[vertexOffset + i].normalized, normalMatrix.MultiplyVector(sourceNormals[i]).normalized) <= 0.00001f, "Normal changed.");
                    if (sourceTangents.Length > 0)
                    {
                        Vector3 expected = matrix.MultiplyVector((Vector3)sourceTangents[i]).normalized;
                        Vector4 actual = tangents[vertexOffset + i];
                        Require(Vector3.Distance(((Vector3)actual).normalized, expected) <= 0.00001f && actual.w == sourceTangents[i].w, "Tangent changed.");
                    }
                    if (sourceColors.Length > 0) Require(colors[vertexOffset + i] == sourceColors[i], "Color changed.");
                }
                for (int channel = 0; channel < 8; channel++)
                {
                    var sourceUvs = new System.Collections.Generic.List<Vector4>();
                    mesh.GetUVs(channel, sourceUvs);
                    Require(sourceUvs.Count == 0 ? allUvs[channel].Count == 0 : allUvs[channel].Count == positions.Length, "UV layout changed.");
                    for (int i = 0; i < sourceUvs.Count; i++) Require(allUvs[channel][vertexOffset + i] == sourceUvs[i], "UV changed.");
                }
                foreach (int index in mesh.triangles)
                    Require(triangles[indexOffset++] == vertexOffset + index, "Index order/winding changed.");
                vertexOffset += mesh.vertexCount;
            }
            Require(vertexOffset == positions.Length && indexOffset == triangles.Length && triangles.Length / 3 == 4457,
                "Geometry count changed.");
        }

        public static void Configure(GameObject root, Mesh mesh)
        {
            MeshRenderer[] sources = GetSources(root);
            Transform visual = root.transform.Find("AircraftVisualRoot");
            Transform existing = visual.Find(BatchName);
            GameObject batch = existing != null ? existing.gameObject : new GameObject(BatchName);
            batch.transform.SetParent(visual, false);
            batch.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            batch.transform.localScale = Vector3.one;
            batch.layer = sources[0].gameObject.layer;
            var filter = batch.GetComponent<MeshFilter>();
            if (filter == null) filter = batch.AddComponent<MeshFilter>();
            var renderer = batch.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = batch.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            var source = sources[0];
            renderer.sharedMaterials = source.sharedMaterials;
            renderer.shadowCastingMode = source.shadowCastingMode;
            renderer.receiveShadows = source.receiveShadows;
            renderer.motionVectorGenerationMode = source.motionVectorGenerationMode;
            renderer.lightProbeUsage = source.lightProbeUsage;
            renderer.reflectionProbeUsage = source.reflectionProbeUsage;
            renderer.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            renderer.renderingLayerMask = source.renderingLayerMask;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder;
            renderer.rendererPriority = source.rendererPriority;
            renderer.enabled = false;
            renderer.probeAnchor = sources[0].probeAnchor;
            // This object is a rigid moving aircraft part, never Unity static batching.
            GameObjectUtility.SetStaticEditorFlags(batch, (StaticEditorFlags)0);
            root.GetComponentInChildren<AircraftVisibilityProfileController>(true)
                .ConfigureAuthoredExternalBatch(renderer, sources);
            var menu = root.GetComponentInChildren<ProductFlightModeMenuView>(true);
            Require(menu != null, "Missing authored showroom visibility owner.");
            var serializedMenu = new SerializedObject(menu);
            var presentation = serializedMenu.FindProperty("aircraftPresentationRenderers");
            bool included = false;
            for (int i = 0; i < presentation.arraySize; i++)
                included |= presentation.GetArrayElementAtIndex(i).objectReferenceValue == renderer;
            if (!included)
            {
                int index = presentation.arraySize++;
                presentation.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
                serializedMenu.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [MenuItem("QuestFlight/Aircraft/Author External Draw Batch")]
        public static void RunBatch()
        {
            string report = System.Environment.GetEnvironmentVariable("QFL_EXTERIOR_BATCH_REPORT");
            Require(!string.IsNullOrEmpty(report), "QFL_EXTERIOR_BATCH_REPORT is required.");
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            Mesh transient = null;
            try
            {
                var authoredVisibility = root.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => r.enabled);
                transient = CreateMesh(root);
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Production/Rendering", "ExteriorBatch");
                Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
                if (asset == null) { AssetDatabase.CreateAsset(transient, MeshPath); asset = transient; transient = null; }
                else EditorUtility.CopySerialized(transient, asset);
                Configure(root, asset);
                ValidateGeometry(root, asset);
                Require(authoredVisibility.All(entry => entry.Key.enabled == entry.Value),
                    "Authoring changed an existing renderer's enabled state.");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                string digest;
                using (var stream = File.OpenRead(SourcePath))
                using (var hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                string json = JsonUtility.ToJson(new Provenance { sourceSha256 = digest, vertexCount = asset.vertexCount }, true);
                File.WriteAllText(Folder + "/SOURCE_AND_LICENSE.json", json);
                File.WriteAllText(report, json);
                AssetDatabase.Refresh();
                Debug.Log("[ExteriorBatch] Authored 16 source draws as one 4457-triangle external-view-only mesh; original geometry and nodes retained.");
            }
            finally
            {
                if (transient != null) Object.DestroyImmediate(transient);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Serializable] private sealed class Provenance
        {
            public string source = SourcePath;
            public string sourceSha256;
            public string[] sourceSuffixes = Suffixes;
            public int sourceDraws = 16, combinedDraws = 1, triangles = 4457, vertexCount;
            public string generator = "Assets/Editor/ProductionAircraftExteriorBatchAuthoring.cs";
            public string rights = "Local derivative of existing Cessna172KogThorns source. Inherits source rights and unresolved distribution restrictions; no new permission or public-distribution claim. Keep local/uncommitted pending existing asset provenance review.";
            public string scope = "External profile only; cockpit retains original renderers. No simplification, removed faces, changed aircraft parameters or runtime hierarchy construction.";
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
