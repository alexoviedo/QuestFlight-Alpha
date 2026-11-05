using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Editor
{
    /// <summary>
    /// Deterministically authors one serialized sparse volumetric cloud field.
    /// External VDBs are compiled offline into compact project-owned textures;
    /// no runtime hierarchy or cloud geometry is generated.
    /// </summary>
    public static class BoulderCloudAssetAuthoring
    {
        private const string Root = "Assets/Production/Environment/CloudsV1";
        private const string Generated = Root + "/Generated";
        private const string ProfilePath = Root + "/KBDU_BoulderCloudProfile.asset";
        private const string DensityPath = Root + "/KBDU_CloudDensitySlices_RGBA.png";
        private const string CoveragePath = Root + "/KBDU_CloudCoverage_RGBA.png";
        private const string MaterialPath = Root + "/KBDU_BoulderCloudShell.mat";
        private const string ShaderPath = Root + "/BoulderCloudShell.shader";
        private const string ManifestPath = Root + "/SOURCE_MANIFEST.json";
        private const string MeshPath = Generated + "/KBDU_BoulderCloudSector_0.asset";
        private const string CloudRootName = "BoulderCloudFieldRoot";
        private const string CloudRendererName = "SparseVolumetricCloudLayer";
        private const float AirportMslMeters = 1612.6f;
        private const float FieldWidthMeters = 16000f;
        // Reference low cumulus; retain the photographic high cloud backdrop.
        private const float FieldBaseAglMeters = 550f;
        private const float FieldTopAglMeters = 1450f;
        private const float FormationHorizontalScale = 0.55f;

        private readonly struct Formation
        {
            public readonly string id;
            public readonly float x;
            public readonly float z;
            public readonly float radiusX;
            public readonly float radiusZ;
            public readonly float density;
            public readonly float shapeBlend;

            public Formation(string authoredId, float normalizedX, float normalizedZ,
                float normalizedRadiusX, float normalizedRadiusZ, float authoredDensity,
                float authoredShapeBlend)
            {
                id = authoredId;
                x = normalizedX;
                z = normalizedZ;
                radiusX = normalizedRadiusX;
                radiusZ = normalizedRadiusZ;
                density = authoredDensity;
                shapeBlend = authoredShapeBlend;
            }
        }

        [MenuItem("QuestFlight/Environment/Author Sparse Volumetric Cloud Field V4")]
        public static void BuildInteractive() => Build(exitWhenComplete: false);

        public static void RunBatch()
        {
            try
            {
                Build(exitWhenComplete: true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Build(bool exitWhenComplete)
        {
            EnsureFolder("Assets/Production/Environment", "CloudsV1");
            EnsureFolder(Root, "Generated");
            Texture2D density = ImportDataTexture(DensityPath, 512, mipmaps: false);
            ImportDataTexture(CoveragePath, 256, mipmaps: true);
            Material material = BuildMaterial(density);
            BoulderCloudVolumeAuthoring.Build();
            Formation[] formations = BuildFormations();
            BoulderCloudProfile profile = BuildProfile(formations);
            Mesh volumeMesh = BuildVolumeMesh(profile, formations);
            AuthorEnvironmentPrefab(profile, material, volumeMesh);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "[QuestFlight][BoulderClouds] Authored eight bounded VDB cloud volumes " +
                "as one 96-triangle mesh, one material, one renderer, no cloud camera, " +
                "and no temporal reprojection.");
            if (exitWhenComplete)
                EditorApplication.Exit(0);
        }

        public static void RebuildFormationGeometryForVisualRepair()
        {
            var formations = BuildFormations();
            var profile = BuildProfile(formations);
            BuildVolumeMesh(profile, formations);
            AssetDatabase.SaveAssets();
        }

        private static Formation[] BuildFormations() => new[]
        {
            new Formation("VOLUME_WNW_CONNECTED", 0.20f, 0.22f, 0.125f, 0.100f, 0.88f, 0.15f),
            new Formation("VOLUME_NORTHWEST", 0.34f, 0.20f, 0.120f, 0.095f, 0.70f, 0.32f),
            new Formation("VOLUME_NORTH", 0.58f, 0.18f, 0.130f, 0.100f, 0.82f, 0.55f),
            new Formation("VOLUME_NORTHEAST", 0.78f, 0.29f, 0.125f, 0.105f, 0.74f, 0.72f),
            new Formation("VOLUME_WEST", 0.18f, 0.60f, 0.130f, 0.105f, 0.82f, 0.25f),
            new Formation("VOLUME_RESERVOIR_WEST", 0.38f, 0.72f, 0.125f, 0.100f, 0.72f, 0.48f),
            new Formation("VOLUME_RESERVOIR_EAST", 0.64f, 0.68f, 0.130f, 0.105f, 0.86f, 0.66f),
            new Formation("VOLUME_SOUTHEAST", 0.84f, 0.78f, 0.120f, 0.095f, 0.68f, 0.82f),
        };

        private static BoulderCloudProfile BuildProfile(Formation[] formations)
        {
            BoulderCloudProfile profile =
                AssetDatabase.LoadAssetAtPath<BoulderCloudProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<BoulderCloudProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            float centerAgl = (FieldBaseAglMeters + FieldTopAglMeters) * 0.5f;
            float halfHeight = (FieldTopAglMeters - FieldBaseAglMeters) * 0.5f;
            BoulderCloudProfile.Volume[] volumes = formations.Select(formation =>
            {
                Vector3 center = new Vector3(
                    (formation.x - 0.5f) * FieldWidthMeters,
                    centerAgl,
                    (formation.z - 0.5f) * FieldWidthMeters);
                Vector3 radii = new Vector3(
                    formation.radiusX * FieldWidthMeters * FormationHorizontalScale,
                    halfHeight,
                    formation.radiusZ * FieldWidthMeters * FormationHorizontalScale);
                return new BoulderCloudProfile.Volume(
                    formation.id,
                    center,
                    radii,
                    AirportMslMeters + FieldBaseAglMeters,
                    AirportMslMeters + FieldTopAglMeters,
                    formation.density);
            }).ToArray();

            profile.Configure(
                "KBDU_20260814_1055Z_SCT018_V4",
                "Reference low cumulus with retained photographic high cloud backdrop",
                AirportMslMeters,
                FieldBaseAglMeters,
                FieldTopAglMeters,
                0.28f,
                24000f,
                new Color(0.66f, 0.71f, 0.77f, 1f),
                0.022f,
                0.42f,
                0.76f,
                volumes);
            EditorUtility.SetDirty(profile);
            if (!profile.TryValidate(out string report))
                throw new InvalidOperationException("Cloud profile invalid: " + report);
            return profile;
        }

        private static Texture2D ImportDataTexture(string path, int maximumSize, bool mipmaps)
        {
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "../" + path));
            if (!File.Exists(absolute))
                throw new FileNotFoundException("Compiled cloud data texture is missing.", path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Cloud texture importer is missing: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = mipmaps;
            importer.streamingMipmaps = mipmaps;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 0;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = maximumSize;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = 80;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                throw new InvalidOperationException("Cloud data did not import: " + path);
            return texture;
        }

        private static Material BuildMaterial(Texture2D density)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
                throw new InvalidOperationException("Sparse cloud shader is missing: " + ShaderPath);
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Material authored = new Material(shader) { name = "KBDU_BoulderCloudShell" };
            authored.SetTexture("_DensitySlices", density);
            authored.SetColor("_LitColor", new Color(0.94f, 0.95f, 0.97f, 1f));
            authored.SetColor("_AmbientColor", new Color(0.68f, 0.72f, 0.77f, 1f));
            authored.SetColor("_UndersideColor", new Color(0.40f, 0.44f, 0.50f, 1f));
            authored.SetFloat("_DensityGain", 1.90f);
            authored.SetFloat("_Extinction", 4.6f);
            authored.SetFloat("_SilverLining", 0.40f);
            authored.SetFloat("_MaximumDistance", 18000f);
            authored.SetFloat("_MinimumStepMetres", 60f);
            // Same-route Quest measurements favor hardware volume filtering; empty
            // skipping was slower in both SpaceWarp modes. Keep regeneration aligned.
            authored.SetFloat("_CloudVolumeSampling", 1f);
            authored.SetFloat("_CloudEmptySkipping", 0f);
            authored.enableInstancing = false;
            authored.renderQueue = (int)RenderQueue.Transparent - 20;
            if (existing == null)
            {
                AssetDatabase.CreateAsset(authored, MaterialPath);
                return authored;
            }
            EditorUtility.CopySerialized(authored, existing);
            UnityEngine.Object.DestroyImmediate(authored);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Mesh BuildVolumeMesh(
            BoulderCloudProfile profile,
            Formation[] formations)
        {
            Mesh mesh = new Mesh { name = "KBDU_BoulderCloudSector_0" };
            Vector3[] corners =
            {
                new Vector3(-1f, -1f, -1f), new Vector3(1f, -1f, -1f),
                new Vector3(1f, 1f, -1f), new Vector3(-1f, 1f, -1f),
                new Vector3(-1f, -1f, 1f), new Vector3(1f, -1f, 1f),
                new Vector3(1f, 1f, 1f), new Vector3(-1f, 1f, 1f),
            };
            int[] cubeTriangles =
            {
                0, 2, 1, 0, 3, 2, 5, 6, 4, 6, 7, 4,
                4, 7, 0, 7, 3, 0, 1, 2, 5, 2, 6, 5,
                3, 7, 2, 7, 6, 2, 4, 0, 5, 0, 1, 5,
            };
            List<Vector3> vertices = new List<Vector3>(formations.Length * 8);
            List<Vector3> centers = new List<Vector3>(formations.Length * 8);
            List<Vector3> radii = new List<Vector3>(formations.Length * 8);
            List<Vector2> parameters = new List<Vector2>(formations.Length * 8);
            List<int> triangles = new List<int>(formations.Length * cubeTriangles.Length);
            for (int formationIndex = 0; formationIndex < formations.Length; formationIndex++)
            {
                BoulderCloudProfile.Volume volume = profile.Volumes[formationIndex];
                int vertexBase = vertices.Count;
                foreach (Vector3 corner in corners)
                {
                    vertices.Add(volume.centerLocalMeters + Vector3.Scale(corner, volume.radiiMeters));
                    centers.Add(volume.centerLocalMeters);
                    radii.Add(volume.radiiMeters);
                    parameters.Add(new Vector2(
                        formations[formationIndex].shapeBlend,
                        formations[formationIndex].density));
                }
                foreach (int triangleIndex in cubeTriangles)
                    triangles.Add(vertexBase + triangleIndex);
            }
            mesh.SetVertices(vertices);
            mesh.SetUVs(1, centers);
            mesh.SetUVs(2, radii);
            mesh.SetUVs(3, parameters);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false); // Readable for index-only back-to-front transparency ordering.
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, MeshPath);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void AuthorEnvironmentPrefab(
            BoulderCloudProfile profile,
            Material material,
            Mesh volumeMesh)
        {
            GameObject environment = PrefabUtility.LoadPrefabContents(
                ProductionEnvironmentRoot.PrefabAssetPath);
            try
            {
                ProductionEnvironmentRoot contract =
                    environment.GetComponent<ProductionEnvironmentRoot>();
                if (contract == null)
                    throw new InvalidOperationException("Production environment contract is missing.");

                Transform existingRoot = environment.transform.Find(CloudRootName);
                GameObject rootObject = existingRoot == null
                    ? new GameObject(CloudRootName)
                    : existingRoot.gameObject;
                if (existingRoot == null)
                    rootObject.transform.SetParent(environment.transform, false);
                rootObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                rootObject.transform.localScale = Vector3.one;

                BoulderCloudField field = rootObject.GetComponent<BoulderCloudField>();
                if (field == null)
                    field = rootObject.AddComponent<BoulderCloudField>();
                Transform existingRenderer = rootObject.transform.Find(CloudRendererName);
                GameObject rendererObject = existingRenderer == null
                    ? new GameObject(CloudRendererName)
                    : existingRenderer.gameObject;
                if (existingRenderer == null)
                    rendererObject.transform.SetParent(rootObject.transform, false);
                rendererObject.transform.localPosition = Vector3.zero;
                rendererObject.transform.localRotation = Quaternion.identity;
                rendererObject.transform.localScale = Vector3.one;
                MeshFilter filter = rendererObject.GetComponent<MeshFilter>();
                if (filter == null)
                    filter = rendererObject.AddComponent<MeshFilter>();
                filter.sharedMesh = volumeMesh;
                MeshRenderer renderer = rendererObject.GetComponent<MeshRenderer>();
                if (renderer == null)
                    renderer = rendererObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.allowOcclusionWhenDynamic = false;

                for (int index = rootObject.transform.childCount - 1; index >= 0; index--)
                {
                    Transform child = rootObject.transform.GetChild(index);
                    if (child == rendererObject.transform)
                        continue;
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }

                TextAsset manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
                if (manifest == null)
                    throw new InvalidOperationException("Cloud source manifest is missing.");
                field.Configure(profile, new[] { renderer }, material, manifest, null, null);
                contract.boulderCloudField = rootObject.transform;
                ProductionEnvironmentBudget budget = contract.CalculateBudget();
                contract.bakedRendererCount = budget.rendererCount;
                contract.bakedTriangleCount = budget.triangleCount;
                contract.bakedMaterialCount = budget.materialCount;
                contract.bakedTextureCount = Mathf.Max(contract.bakedTextureCount, 2);
                EditorUtility.SetDirty(field);
                EditorUtility.SetDirty(contract);
                PrefabUtility.SaveAsPrefabAsset(environment, ProductionEnvironmentRoot.PrefabAssetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(environment);
            }
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
