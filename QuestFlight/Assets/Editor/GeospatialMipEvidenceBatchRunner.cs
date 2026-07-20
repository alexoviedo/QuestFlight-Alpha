using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    public static class GeospatialMipEvidenceBatchRunner
    {
        private const string OutputVariable = "QFL_MIP_EVIDENCE_DIR";
        private const string AircraftPath = "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";

        [MenuItem("QuestFlight/Photoreal Aerial Surface V1/Record Mip Selection Evidence")]
        public static void RunInteractive() => Run(false);

        public static void RunBatch()
        {
            try { Run(true); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Run(bool exit)
        {
            string output = Environment.GetEnvironmentVariable(OutputVariable);
            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException(OutputVariable + " must name an external artifact directory.");
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);

            GameObject aircraft = AssetDatabase.LoadAssetAtPath<GameObject>(AircraftPath);
            GeospatialMipController controller = aircraft != null
                ? aircraft.GetComponent<GeospatialMipController>()
                : null;
            string contract = controller == null ? "controller missing" : string.Empty;
            if (controller == null || !controller.TryValidateContract(out contract))
                throw new InvalidOperationException("Authored mip controller is missing or invalid: " + contract);

            MipEvidenceReport report = new MipEvidenceReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                controllerContract = contract,
                streamingMipmapsActive = QualitySettings.streamingMipmapsActive,
                selectionMode = "explicit Texture2D.requestedMipmapLevel with two-second predicted ENU preload",
                loadedEvidenceBoundary =
                    "Editor loaded/calculated levels are API evidence only; physical Quest residency requires device capture."
            };

            try
            {
                foreach (PoseCase pose in Cases())
                {
                    controller.ApplyDeterministicRequest(pose.current, pose.predicted, 62f, 1832);
                    MipPoseEvidence evidence = new MipPoseEvidence
                    {
                        id = pose.id,
                        currentEnu = pose.current,
                        predictedEnu = pose.predicted,
                        textures = new List<MipTextureEvidence>()
                    };
                    Add(evidence, "near_4km", controller.near4Km, controller.NearLevel);
                    Add(evidence, "mid_nw", controller.midNorthWest, controller.MidNorthWestLevel);
                    Add(evidence, "mid_ne", controller.midNorthEast, controller.MidNorthEastLevel);
                    Add(evidence, "mid_sw", controller.midSouthWest, controller.MidSouthWestLevel);
                    Add(evidence, "mid_se", controller.midSouthEast, controller.MidSouthEastLevel);
                    Add(evidence, "mid_base_12km", controller.midBase12Km, controller.MidBaseLevel);
                    Add(evidence, "far_24km", controller.far24Km, controller.FarLevel);
                    int[] quadrants =
                    {
                        controller.MidNorthWestLevel, controller.MidNorthEastLevel,
                        controller.MidSouthWestLevel, controller.MidSouthEastLevel
                    };
                    evidence.quadrantAsymmetry = Mathf.Max(quadrants) - Mathf.Min(quadrants);
                    evidence.closeNearMipAccepted = !pose.requireNearMipZero || controller.NearLevel == 0;
                    evidence.passed = evidence.quadrantAsymmetry == 0 && evidence.closeNearMipAccepted;
                    report.poses.Add(evidence);
                }
            }
            finally
            {
                controller.ClearAllRequests();
            }

            report.passed = report.streamingMipmapsActive && report.poses.TrueForAll(item => item.passed);
            string path = Path.Combine(output, "terrain_mip_selection_report.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(output, "terrain_mip_selection_report.md"), Markdown(report));
            Debug.Log("[QuestFlight][GeospatialMipEvidence] " + (report.passed ? "PASS " : "FAIL ") + path);
            if (!report.passed) throw new InvalidOperationException("Geospatial mip evidence gate failed.");
            if (exit) EditorApplication.Exit(0);
        }

        private static void Add(MipPoseEvidence pose, string id, Texture2D texture, int level)
        {
            TextureImporter importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            pose.textures.Add(new MipTextureEvidence
            {
                id = id,
                requestedLevel = level,
                requestedApiLevel = texture.requestedMipmapLevel,
                calculatedLevel = ReadMipProperty(texture, "calculatedMipmapLevel"),
                desiredLevel = ReadMipProperty(texture, "desiredMipmapLevel"),
                loadedLevel = ReadMipProperty(texture, "loadedMipmapLevel"),
                requestedLevelLoaded = texture.IsRequestedMipmapLevelLoaded(),
                streamingImport = importer != null && importer.streamingMipmaps
            });
        }

        private static int ReadMipProperty(Texture2D texture, string name)
        {
            PropertyInfo property = typeof(Texture2D).GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            object value = property?.GetValue(texture);
            return value is int integer ? integer : -1;
        }

        private static IEnumerable<PoseCase> Cases()
        {
            yield return new PoseCase("taxi_center", new Vector3(0f, 2f, 0f), new Vector3(0f, 2f, 0f), true);
            yield return new PoseCase("left_100m", new Vector3(-1100f, 100f, -260f),
                new Vector3(-1250f, 100f, -280f), true);
            yield return new PoseCase("right_reservoir_100m", new Vector3(1100f, 100f, 320f),
                new Vector3(1260f, 100f, 350f), true);
            yield return new PoseCase("pattern_300m", new Vector3(850f, 300f, -650f),
                new Vector3(1050f, 300f, -780f), true);
            yield return new PoseCase("oblique_500m", new Vector3(-1650f, 500f, 900f),
                new Vector3(-1900f, 500f, 1040f), true);
            yield return new PoseCase("predicted_near_exit", new Vector3(1750f, 250f, 300f),
                new Vector3(2450f, 250f, 420f), false);
        }

        private static string Markdown(MipEvidenceReport report)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.AppendLine("# Geospatial mip-selection evidence");
            text.AppendLine();
            text.AppendLine("Status: **" + (report.passed ? "PASS" : "FAIL") + "**");
            text.AppendLine();
            text.AppendLine("| Pose | Near mip | Mid NW/NE/SW/SE | Mid base | Far | Asymmetry | Result |");
            text.AppendLine("|---|---:|---|---:|---:|---:|---|");
            foreach (MipPoseEvidence pose in report.poses)
                text.AppendLine($"| {pose.id} | {pose.textures[0].requestedLevel} | " +
                                $"{pose.textures[1].requestedLevel}/{pose.textures[2].requestedLevel}/" +
                                $"{pose.textures[3].requestedLevel}/{pose.textures[4].requestedLevel} | " +
                                $"{pose.textures[5].requestedLevel} | {pose.textures[6].requestedLevel} | " +
                                $"{pose.quadrantAsymmetry} | {(pose.passed ? "PASS" : "FAIL")} |");
            text.AppendLine();
            text.AppendLine(report.loadedEvidenceBoundary);
            return text.ToString();
        }

        private readonly struct PoseCase
        {
            public readonly string id;
            public readonly Vector3 current;
            public readonly Vector3 predicted;
            public readonly bool requireNearMipZero;
            public PoseCase(string id, Vector3 current, Vector3 predicted, bool requireNearMipZero)
            {
                this.id = id;
                this.current = current;
                this.predicted = predicted;
                this.requireNearMipZero = requireNearMipZero;
            }
        }

        [Serializable]
        private sealed class MipEvidenceReport
        {
            public string generatedUtc;
            public string unityVersion;
            public string controllerContract;
            public string selectionMode;
            public string loadedEvidenceBoundary;
            public bool streamingMipmapsActive;
            public bool passed;
            public List<MipPoseEvidence> poses = new List<MipPoseEvidence>();
        }

        [Serializable]
        private sealed class MipPoseEvidence
        {
            public string id;
            public Vector3 currentEnu;
            public Vector3 predictedEnu;
            public int quadrantAsymmetry;
            public bool closeNearMipAccepted;
            public bool passed;
            public List<MipTextureEvidence> textures;
        }

        [Serializable]
        private sealed class MipTextureEvidence
        {
            public string id;
            public int requestedLevel;
            public int requestedApiLevel;
            public int calculatedLevel;
            public int desiredLevel;
            public int loadedLevel;
            public bool requestedLevelLoaded;
            public bool streamingImport;
        }
    }
}
