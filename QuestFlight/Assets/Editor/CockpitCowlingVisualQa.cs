using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using QuestFlightLab.Aircraft;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        public static void RunCockpitCowlingQa()
        {
            string output = System.Environment.GetEnvironmentVariable("QFL_COWL_QA");
            if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("QFL_COWL_QA is required.");
            Directory.CreateDirectory(Path.Combine(output, "screenshots"));
            const int width = 1440, height = 1080;
            var report = new ProductionVisualQaReport {
                generatedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                scenePath = ProductionScenePath, outputDirectory = output,
                renderWidth = width, renderHeight = height, humanHeadsetAcceptance = "PENDING" };
            try
            {
                CockpitEngineCowlAuthoring.ValidateExteriorBatch();
                VisualContext c = LoadContext(width, height, report);
                Transform aircraft = c.rig.transform;
                MeshRenderer cowl = c.rig.AircraftVisualRoot.GetComponentsInChildren<MeshRenderer>(true)
                    .Single(renderer => renderer.name == CockpitEngineCowlAuthoring.ExteriorCowlObjectName);
                MeshRenderer original = c.rig.AircraftVisualRoot.GetComponentsInChildren<MeshRenderer>(true)
                    .Single(renderer => renderer.name == CockpitEngineCowlAuthoring.SourceRendererName);
                c.visibility.SetCockpitView();
                if (!cowl.enabled || original.enabled || cowl.sharedMaterial != original.sharedMaterial ||
                    cowl.sharedMaterial.shader.name == ProductionAircraftLightingController.StableCockpitShaderName)
                    throw new InvalidOperationException("Cockpit visibility or lighting converted the exterior cowling incorrectly.");
                c.visibility.SetExternalView();
                if (cowl.enabled || !original.enabled)
                    throw new InvalidOperationException("External view duplicates the derived cowling or hides the original.");
                c.visibility.SetCockpitView();

                // Separate editor QA camera only; never write the tracked product camera.
                SetAircraftPose(c, c.originalAircraftPosition + Vector3.up * 250f, c.originalAircraftRotation);
                Vector3 eye = c.rig.PilotSeatAnchor.position;
                CaptureCowlingStereo(c, report, output, "seated_forward", "Normal seated forward view",
                    eye, aircraft.rotation, width, height);
                CaptureCowlingStereo(c, report, output, "pilot_over_nose", "Pilot leaning naturally over the glareshield",
                    eye + aircraft.TransformVector(new Vector3(0f, 0.14f, 0.08f)),
                    aircraft.rotation * Quaternion.Euler(12f, 0f, 0f), width, height);
                CaptureCowlingStereo(c, report, output, "passenger_over_nose", "Right seat over the nose",
                    eye + aircraft.TransformVector(new Vector3(0.56f, 0.14f, 0.08f)),
                    aircraft.rotation * Quaternion.Euler(12f, 0f, 0f), width, height);
                Capture(c, report, output, width, height, new ShotDefinition(
                    "footwell_liner", "Preserved dark footwell liner", "cockpit", eye,
                    aircraft.rotation * Quaternion.Euler(34f, 0f, 0f), 68f, ViewProfile.Cockpit));
                report.passed = report.errors.Count == 0 && report.shots.All(shot => shot.passed);
                report.classification = report.passed ? "PASS_COWLING_MACHINE_QA" : "REVIEW_COWLING_QA";
            }
            catch (Exception exception) { report.errors.Add(exception.ToString()); Debug.LogException(exception); }
            WriteReportFiles(report, output);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }

        private static void CaptureCowlingStereo(VisualContext c, ProductionVisualQaReport report, string output,
            string id, string title, Vector3 eye, Quaternion rotation, int width, int height)
        {
            for (int side = 0; side < 2; side++)
                Capture(c, report, output, width, height, new ShotDefinition(
                    id + (side == 0 ? "_left" : "_right"), title, "cockpit",
                    eye + rotation * Vector3.right * (side == 0 ? -0.032f : 0.032f),
                    rotation, 68f, ViewProfile.Cockpit));
            var left = new Texture2D(2, 2);
            var right = new Texture2D(2, 2);
            var stereo = new Texture2D(width * 2, height, TextureFormat.RGBA32, false);
            try
            {
                left.LoadImage(File.ReadAllBytes(Path.Combine(output, "screenshots", id + "_left.png")));
                right.LoadImage(File.ReadAllBytes(Path.Combine(output, "screenshots", id + "_right.png")));
                stereo.SetPixels32(0, 0, width, height, left.GetPixels32());
                stereo.SetPixels32(width, 0, width, height, right.GetPixels32());
                stereo.Apply();
                File.WriteAllBytes(Path.Combine(output, "screenshots", id + "_stereo.png"), stereo.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(left);
                UnityEngine.Object.DestroyImmediate(right);
                UnityEngine.Object.DestroyImmediate(stereo);
            }
        }
    }
}
