using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Environment.Editor
{
    /// <summary>
    /// Imports the optimized CC0 vehicle derivatives, reduces them to six
    /// single-submesh meshes, and authors one renderer-free TrafficRoot.  Both
    /// parked and moving records are submitted through the same bounded instanced
    /// path, so visual variety does not add GameObjects or material clones.
    /// </summary>
    public static class KbduTrafficAuthoring
    {
        public const string Root = "Assets/Production/Environment/TrafficV1";
        public const string ManifestPath =
            Root + "/Data/KBDU_TrafficManifestV1.json";
        public const string PrefabPath = Root + "/KBDU_TrafficRoot.prefab";
        public const string MaterialPath =
            Root + "/Materials/KBDU_TrafficVehicle.mat";
        public const string TexturePath =
            Root + "/Textures/KBDU_TrafficSurfaceDetail.png";
        public const string ProductEnvironmentPath =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        public const string ImportRoot = "Assets/__TrafficVehicleImportTemp";
        public const string PassengerProxyPath =
            Root + "/Models/KBDU_TrafficPassengerProxy.asset";
        public const string SemiProxyPath =
            Root + "/Models/KBDU_TrafficSemiProxy.asset";

        private const string ShaderName = "QuestFlight/KBDU Traffic Vehicle";

        private static readonly VehicleAsset[] VehicleAssets =
        {
            new VehicleAsset(
                "KBDU_TrafficSedan",
                "KBDU_TrafficSedan.fbx",
                1.82f,
                4.60f,
                1.45f),
            new VehicleAsset(
                "KBDU_TrafficHatchback",
                "KBDU_TrafficHatchback.fbx",
                1.78f,
                4.35f,
                1.50f),
            new VehicleAsset(
                "KBDU_TrafficCrossover",
                "KBDU_TrafficCrossover.fbx",
                1.92f,
                4.70f,
                1.75f),
            new VehicleAsset(
                "KBDU_TrafficPickup",
                "KBDU_TrafficPickup.fbx",
                2.00f,
                5.35f,
                1.82f),
            new VehicleAsset(
                "KBDU_TrafficCargoVan",
                "KBDU_TrafficCargoVan.fbx",
                1.98f,
                5.05f,
                2.10f),
            new VehicleAsset(
                "KBDU_TrafficSemi",
                "KBDU_TrafficSemi.fbx",
                2.55f,
                16.20f,
                4.00f)
        };

        private static readonly string[] ObsoleteRuntimeAssets =
        {
            Root + "/Models/KBDU_TrafficSedan_Blue.asset",
            Root + "/Models/KBDU_TrafficSuv_Red.asset",
            Root + "/Models/KBDU_TrafficPickup_White.asset",
            Root + "/Models/KBDU_TrafficMidProxy.asset",
            Root + "/Models/KBDU_TrafficParkedCombined.asset"
        };

        [MenuItem("QuestFlight/Traffic V1/Build And Integrate")]
        public static void BuildAndIntegrate()
        {
            EnsureFolder(Root);
            EnsureFolder(Root + "/Models");
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Textures");

            TextAsset manifest =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (manifest == null)
            {
                throw new InvalidOperationException(
                    "Compiled traffic manifest is missing: " + ManifestPath);
            }

            Material material = BuildMaterial();
            Mesh[] vehicleMeshes = new Mesh[VehicleAssets.Length];
            for (int index = 0; index < VehicleAssets.Length; index++)
            {
                VehicleAsset definition = VehicleAssets[index];
                vehicleMeshes[index] = LoadOrUpdateVehicleMesh(definition);
                ValidateVehicleMesh(vehicleMeshes[index], definition);
            }
            if (vehicleMeshes.Length != KbduTrafficRuntime.ArchetypeCount)
            {
                throw new InvalidOperationException(
                    "Traffic archetype count does not match the runtime contract.");
            }
            Mesh[] proxyMeshes = BuildProxyMeshes();

            GameObject trafficRoot = new GameObject("TrafficRoot");
            try
            {
                KbduTrafficRuntime runtime =
                    trafficRoot.AddComponent<KbduTrafficRuntime>();
                runtime.Configure(
                    manifest,
                    material,
                    vehicleMeshes,
                    proxyMeshes);
                if (!runtime.TryValidateContract(out string report))
                {
                    throw new InvalidOperationException(
                        "Authored traffic runtime failed: " + report);
                }
                PrefabUtility.SaveAsPrefabAsset(trafficRoot, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(trafficRoot);
            }

            IntegrateProductEnvironment();
            DeleteObsoleteAssets();
            if (AssetDatabase.IsValidFolder(ImportRoot))
            {
                AssetDatabase.DeleteAsset(ImportRoot);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            TrafficManifest records =
                JsonUtility.FromJson<TrafficManifest>(manifest.text);
            Debug.Log(
                $"Authored and integrated {PrefabPath}: " +
                $"parked={records?.parked?.Length ?? 0}, " +
                $"moving={records?.moving?.Length ?? 0}, " +
                $"routes={records?.routes?.Length ?? 0}, " +
                "six CC0 near body styles, three far batches sharing two project-owned proxies, " +
                "eight instanced paint colors, draws<=9, " +
                "serialized renderers=0, transparent=0, shadows=0.");
        }

        private static Mesh[] BuildProxyMeshes()
        {
            Color32 body = new Color32(255, 0, 0, 255);
            Color32 glass = new Color32(0, 255, 0, 255);
            Color32 trailer = new Color32(255, 255, 255, 255);

            List<Vector3> passengerVertices = new List<Vector3>(24);
            List<int> passengerTriangles = new List<int>(36);
            List<Color32> passengerColors = new List<Color32>(24);
            AddBox(
                passengerVertices,
                passengerTriangles,
                passengerColors,
                new Vector3(0f, 0.45f, 0f),
                new Vector3(1.90f, 0.70f, 4.70f),
                body);
            AddBox(
                passengerVertices,
                passengerTriangles,
                passengerColors,
                new Vector3(0f, 1.05f, -0.10f),
                new Vector3(1.65f, 0.65f, 2.40f),
                glass);
            AddBox(
                passengerVertices,
                passengerTriangles,
                passengerColors,
                new Vector3(0f, 1.42f, -0.10f),
                new Vector3(1.55f, 0.12f, 2.15f),
                body);

            List<Vector3> semiVertices = new List<Vector3>(24);
            List<int> semiTriangles = new List<int>(36);
            List<Color32> semiColors = new List<Color32>(24);
            AddBox(
                semiVertices,
                semiTriangles,
                semiColors,
                new Vector3(0f, 2.30f, -2.45f),
                new Vector3(2.55f, 3.40f, 11.30f),
                trailer);
            AddBox(
                semiVertices,
                semiTriangles,
                semiColors,
                new Vector3(0f, 0.75f, 5.65f),
                new Vector3(2.55f, 1.50f, 4.90f),
                body);
            AddBox(
                semiVertices,
                semiTriangles,
                semiColors,
                new Vector3(0f, 2.625f, 5.25f),
                new Vector3(2.45f, 2.75f, 3.70f),
                body);

            Mesh passengerProxy = WriteMesh(
                PassengerProxyPath,
                CreateProxyMesh(
                    "KBDU_TrafficPassengerProxy",
                    passengerVertices,
                    passengerTriangles,
                    passengerColors));
            Mesh semiProxy = WriteMesh(
                SemiProxyPath,
                CreateProxyMesh(
                    "KBDU_TrafficSemiProxy",
                    semiVertices,
                    semiTriangles,
                    semiColors));
            return new[] { passengerProxy, passengerProxy, semiProxy };
        }

        private static Mesh CreateProxyMesh(
            string name,
            List<Vector3> vertices,
            List<int> triangles,
            List<Color32> colors)
        {
            Mesh mesh = new Mesh
            {
                name = name,
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static void AddBox(
            List<Vector3> vertices,
            List<int> triangles,
            List<Color32> colors,
            Vector3 center,
            Vector3 size,
            Color32 semanticColor)
        {
            Vector3 half = size * 0.5f;
            int start = vertices.Count;
            vertices.Add(center + new Vector3(-half.x, -half.y, -half.z));
            vertices.Add(center + new Vector3(half.x, -half.y, -half.z));
            vertices.Add(center + new Vector3(half.x, half.y, -half.z));
            vertices.Add(center + new Vector3(-half.x, half.y, -half.z));
            vertices.Add(center + new Vector3(-half.x, -half.y, half.z));
            vertices.Add(center + new Vector3(half.x, -half.y, half.z));
            vertices.Add(center + new Vector3(half.x, half.y, half.z));
            vertices.Add(center + new Vector3(-half.x, half.y, half.z));
            for (int index = 0; index < 8; index++)
            {
                colors.Add(semanticColor);
            }
            int[] boxTriangles =
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5,
                0, 1, 5, 0, 5, 4,
                3, 7, 6, 3, 6, 2
            };
            for (int index = 0; index < boxTriangles.Length; index++)
            {
                triangles.Add(start + boxTriangles[index]);
            }
        }

        private static Material BuildMaterial()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Traffic vehicle shader is missing: " + ShaderName);
            }

            ConfigureSurfaceTexture();
            Texture2D detail =
                AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (detail == null)
            {
                throw new InvalidOperationException(
                    "Traffic surface detail texture is missing: " + TexturePath);
            }

            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "KBDU_TrafficVehicle",
                    enableInstancing = true
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.enableInstancing = true;
            material.SetColor(
                "_BodyTint",
                new Color(0.35f, 0.38f, 0.42f, 1f));
            material.SetColor(
                "_TrailerTint",
                new Color(0.72f, 0.73f, 0.72f, 1f));
            material.SetTexture("_SurfaceDetail", detail);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureSurfaceTexture()
        {
            AssetDatabase.ImportAsset(
                TexturePath,
                ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer =
                AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    "Traffic surface texture importer is unavailable.");
            }
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.maxTextureSize = 128;
            TextureImporterPlatformSettings android =
                importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 128;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = 50;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static Mesh LoadOrUpdateVehicleMesh(VehicleAsset definition)
        {
            string finalPath =
                Root + "/Models/" + definition.name + ".asset";
            string sourcePath = ImportRoot + "/" + definition.sourceFile;
            GameObject imported =
                AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (imported == null)
            {
                Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(finalPath);
                if (existing != null)
                {
                    return existing;
                }
                throw new InvalidOperationException(
                    "Neither prepared CC0 derivative nor serialized mesh exists: " +
                    sourcePath);
            }

            Mesh generated = CombineImportedMeshes(imported, definition.name);
            return WriteMesh(finalPath, generated);
        }

        private static Mesh CombineImportedMeshes(
            GameObject imported,
            string meshName)
        {
            GameObject instance =
                UnityEngine.Object.Instantiate(imported);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                MeshFilter[] filters =
                    instance.GetComponentsInChildren<MeshFilter>(true);
                List<CombineInstance> combines = new List<CombineInstance>();
                Matrix4x4 rootInverse = instance.transform.worldToLocalMatrix;
                for (int filterIndex = 0;
                    filterIndex < filters.Length;
                    filterIndex++)
                {
                    Mesh source = filters[filterIndex].sharedMesh;
                    if (source == null)
                    {
                        continue;
                    }
                    Matrix4x4 transform =
                        rootInverse * filters[filterIndex].transform.localToWorldMatrix;
                    for (int submesh = 0;
                        submesh < source.subMeshCount;
                        submesh++)
                    {
                        combines.Add(new CombineInstance
                        {
                            mesh = source,
                            subMeshIndex = submesh,
                            transform = transform
                        });
                    }
                }
                if (combines.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Prepared CC0 vehicle contains no mesh geometry: " +
                        imported.name);
                }

                Mesh mesh = new Mesh
                {
                    name = meshName,
                    indexFormat = UnityEngine.Rendering.IndexFormat.UInt16
                };
                mesh.CombineMeshes(
                    combines.ToArray(),
                    true,
                    true,
                    false);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(false);
                return mesh;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void ValidateVehicleMesh(
            Mesh mesh,
            VehicleAsset definition)
        {
            if (mesh == null || mesh.subMeshCount != 1)
            {
                throw new InvalidOperationException(
                    definition.name + " must be one single-submesh mesh.");
            }
            Vector3 size = mesh.bounds.size;
            if (Mathf.Abs(size.x - definition.width) > 0.08f ||
                Mathf.Abs(size.y - definition.height) > 0.08f ||
                Mathf.Abs(size.z - definition.length) > 0.08f)
            {
                throw new InvalidOperationException(
                    $"{definition.name} bounds are {size}; expected " +
                    $"{definition.width:F2} x {definition.height:F2} x " +
                    $"{definition.length:F2} meters.");
            }
            uint triangles = mesh.GetIndexCount(0) / 3u;
            uint minimumTriangles = definition.length > 10f ? 400u : 500u;
            if (triangles < minimumTriangles || triangles > 1100u)
            {
                throw new InvalidOperationException(
                    $"{definition.name} has {triangles} triangles; " +
                    $"expected a {minimumTriangles}-1100 triangle optimized derivative.");
            }
            if (mesh.colors32 == null || mesh.colors32.Length != mesh.vertexCount)
            {
                throw new InvalidOperationException(
                    definition.name +
                    " lost its semantic surface-color data during import.");
            }
        }

        private static void IntegrateProductEnvironment()
        {
            GameObject productRoot =
                PrefabUtility.LoadPrefabContents(ProductEnvironmentPath);
            try
            {
                Transform existing = productRoot.transform.Find("TrafficRoot");
                if (existing != null)
                {
                    UnityEngine.Object.DestroyImmediate(existing.gameObject);
                }
                GameObject trafficPrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    trafficPrefab,
                    productRoot.transform);
                instance.name = "TrafficRoot";
                instance.transform.SetLocalPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                ProductionEnvironmentRoot environment =
                    productRoot.GetComponent<ProductionEnvironmentRoot>();
                if (environment == null)
                {
                    throw new InvalidOperationException(
                        "Production environment marker is missing.");
                }
                environment.trafficRoot = instance.transform;
                ProductionEnvironmentBudget budget = environment.CalculateBudget();
                environment.bakedRendererCount = budget.rendererCount;
                environment.bakedTriangleCount = budget.triangleCount;
                environment.bakedMaterialCount = budget.materialCount;
                if (!environment.TryValidateContract(
                    out string environmentReport))
                {
                    throw new InvalidOperationException(
                        "Traffic integration leaves the product environment invalid: " +
                        environmentReport);
                }
                EditorUtility.SetDirty(environment);
                PrefabUtility.SaveAsPrefabAsset(
                    productRoot,
                    ProductEnvironmentPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(productRoot);
            }
        }

        private static void DeleteObsoleteAssets()
        {
            for (int index = 0; index < ObsoleteRuntimeAssets.Length; index++)
            {
                if (AssetDatabase.LoadMainAssetAtPath(
                    ObsoleteRuntimeAssets[index]) != null)
                {
                    AssetDatabase.DeleteAsset(ObsoleteRuntimeAssets[index]);
                }
            }
        }

        private static Mesh WriteMesh(string path, Mesh generated)
        {
            UnityEngine.Object existing =
                AssetDatabase.LoadMainAssetAtPath(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            Mesh mesh = existing as Mesh;
            if (mesh == null)
            {
                UnityEngine.Object.DestroyImmediate(generated);
                throw new InvalidOperationException(
                    "Traffic mesh path is not a Mesh: " + path);
            }
            EditorUtility.CopySerialized(generated, mesh);
            mesh.name = generated.name;
            EditorUtility.SetDirty(mesh);
            UnityEngine.Object.DestroyImmediate(generated);
            return mesh;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            string parent =
                Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(parent) ||
                string.IsNullOrWhiteSpace(leaf))
            {
                throw new InvalidOperationException(
                    "Invalid asset folder: " + path);
            }
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        [Serializable]
        private sealed class TrafficManifest
        {
            public ParkedRecord[] parked;
            public RouteRecord[] routes;
            public MovingRecord[] moving;
        }

        [Serializable]
        private sealed class ParkedRecord
        {
            public string id;
        }

        [Serializable]
        private sealed class RouteRecord
        {
            public string id;
        }

        [Serializable]
        private sealed class MovingRecord
        {
            public string id;
        }

        private readonly struct VehicleAsset
        {
            public readonly string name;
            public readonly string sourceFile;
            public readonly float width;
            public readonly float length;
            public readonly float height;

            public VehicleAsset(
                string nameValue,
                string sourceFileValue,
                float widthValue,
                float lengthValue,
                float heightValue)
            {
                name = nameValue;
                sourceFile = sourceFileValue;
                width = widthValue;
                length = lengthValue;
                height = heightValue;
            }
        }
    }
}
