using System;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Environment.Editor
{
    /// <summary>
    /// One-time authored integration for the full-map headset candidate. It
    /// updates the serialized TrafficRoot prefab; no runtime bootstrap or
    /// hierarchy construction is involved.
    /// </summary>
    public static class TrafficV2Gate2Authoring
    {
        public const string TrafficPrefabPath =
            "Assets/Production/Environment/TrafficV1/KBDU_TrafficRoot.prefab";
        public const string ProductEnvironmentPath =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        public const string ManifestPath =
            "Assets/Production/Environment/TrafficV2/Data/" +
            "KBDU_TrafficV2FullMapManifest.json";
        public const string ProvenancePath =
            "Assets/Production/Environment/TrafficV2/Data/" +
            "KBDU_TrafficV2FullMapSourceAndLicense.json";

        [MenuItem("QuestFlight/Traffic V2/Integrate Full Map Headset Candidate")]
        public static void IntegrateFullMapHeadsetCandidate()
        {
            TextAsset manifest = RequireAsset<TextAsset>(ManifestPath);
            TextAsset provenance = RequireAsset<TextAsset>(ProvenancePath);
            GameObject environmentPrefab =
                RequireAsset<GameObject>(ProductEnvironmentPath);
            ProductionEnvironmentRoot environment =
                environmentPrefab.GetComponent<ProductionEnvironmentRoot>();
            if (environment == null || environment.authoritativeTerrainSurface == null)
            {
                throw new InvalidOperationException(
                    "ProductionEnvironmentRoot has no authoritative terrain surface binding.");
            }

            GameObject trafficRoot = PrefabUtility.LoadPrefabContents(TrafficPrefabPath);
            try
            {
                KbduTrafficRuntime v1 = trafficRoot.GetComponent<KbduTrafficRuntime>();
                if (v1 == null)
                {
                    throw new InvalidOperationException(
                        "The accepted serialized Traffic V1 runtime is missing.");
                }
                v1.ConfigureMovingTraffic(false);

                KbduTrafficV2Runtime v2 =
                    trafficRoot.GetComponent<KbduTrafficV2Runtime>() ??
                    trafficRoot.AddComponent<KbduTrafficV2Runtime>();
                v2.Configure(
                    manifest,
                    provenance,
                    v1,
                    environment.authoritativeTerrainSurface);
                if (!v1.TryValidateContract(out string v1Report))
                {
                    throw new InvalidOperationException(
                        "Parked Traffic V1 contract failed: " + v1Report);
                }
                if (!v2.TryValidateContract(out string v2Report))
                {
                    throw new InvalidOperationException(
                        "Moving Traffic V2 contract failed: " + v2Report);
                }
                PrefabUtility.SaveAsPrefabAsset(trafficRoot, TrafficPrefabPath);
                Debug.Log(
                    "[QuestFlight][TrafficV2] Serialized full-map integration complete: " +
                    v2Report + "; V1 moving traffic disabled, V1 parked traffic retained.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(trafficRoot);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException("Required asset is missing: " + path);
            }
            return asset;
        }
    }
}
