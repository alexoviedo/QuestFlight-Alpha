using System.Linq;
using NUnit.Framework;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlight.Product;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    public sealed class GoldenEagleThermalEditModeTests
    {
        [Test]
        public void ThermalProfileIsSmoothBoundedAndUsesNedUpdraftSign()
        {
            ThermalFieldProfile profile = ScriptableObject.CreateInstance<ThermalFieldProfile>();
            try
            {
                profile.centerUnityMeters = new Vector3(100f, 0f, 200f);
                profile.coreRadiusMeters = 50f;
                profile.outerRadiusMeters = 200f;
                profile.baseHeightUnityMeters = 20f;
                profile.fullStrengthBottomUnityMeters = 100f;
                profile.fullStrengthTopUnityMeters = 600f;
                profile.capHeightUnityMeters = 900f;
                profile.peakUpdraftMetersPerSecond = 2.5f;
                Assert.That(profile.IsValid(out string failure), Is.True, failure);

                float center = profile.SampleUpdraftMetersPerSecond(new Vector3(100f, 300f, 200f));
                float shoulder = profile.SampleUpdraftMetersPerSecond(new Vector3(225f, 300f, 200f));
                Assert.That(center, Is.EqualTo(2.5f).Within(0.0001f));
                Assert.That(shoulder, Is.GreaterThan(0f).And.LessThan(center));
                Assert.That(profile.SampleUpdraftMetersPerSecond(new Vector3(301f, 300f, 200f)), Is.Zero);
                Assert.That(profile.SampleUpdraftMetersPerSecond(new Vector3(100f, 10f, 200f)), Is.Zero);
                Assert.That(profile.SampleUpdraftMetersPerSecond(new Vector3(100f, 901f, 200f)), Is.Zero);

                FlightDynamicsAtmosphere atmosphere =
                    FlightDynamicsCoordinator.AtmosphereFromUpdraft(center);
                Assert.That(atmosphere.windNorthMetersPerSecond, Is.Zero);
                Assert.That(atmosphere.windEastMetersPerSecond, Is.Zero);
                Assert.That(atmosphere.windDownMetersPerSecond, Is.EqualTo(-2.5d).Within(0.0001d));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ProductUsesOneSharedThermalAndOneAnimatedEagleRenderer()
        {
            ThermalFieldProfile profile = AssetDatabase.LoadAssetAtPath<ThermalFieldProfile>(
                GoldenEagleThermalAuthoring.ProfilePath);
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.IsValid(out string failure), Is.True, failure);

            GameObject eaglePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                GoldenEagleThermalAuthoring.PrefabPath);
            Assert.That(eaglePrefab, Is.Not.Null);
            GoldenEagleThermalSoaring eagle = eaglePrefab.GetComponent<GoldenEagleThermalSoaring>();
            Assert.That(eagle, Is.Not.Null);
            Assert.That(eagle.thermalField, Is.SameAs(profile));
            Assert.That(eagle.HasAuthoredReferences(out failure), Is.True, failure);
            Assert.That(eaglePrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, Is.EqualTo(1));
            Animator eagleAnimator = eaglePrefab.GetComponentInChildren<Animator>(true);
            Assert.That(eagleAnimator.cullingMode, Is.EqualTo(AnimatorCullingMode.AlwaysAnimate),
                "The encounter animation must not freeze when the skinned renderer briefly leaves an eye frustum.");
            string[] stateNames = { "SoarIdle", "FlapLoop", "BankLeft", "BankRight", "GlideTransition" };
            foreach (string stateName in stateNames)
                Assert.That(eagleAnimator.runtimeAnimatorController.animationClips
                    .Any(clip => clip.name.EndsWith(stateName, System.StringComparison.Ordinal)), Is.True,
                    $"The authored controller must include {stateName}.");
            Assert.That(eaglePrefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .Distinct().Count(), Is.EqualTo(1));

            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductionEnvironmentRoot.PrefabAssetPath);
            ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
            Assert.That(contract.sharedThermalField, Is.SameAs(profile));
            Assert.That(contract.goldenEagleThermalSoaring, Is.Not.Null);
            Assert.That(contract.goldenEagleThermalSoaring.thermalField, Is.SameAs(profile));

            GameObject aircraft = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab");
            FlightDynamicsCoordinator coordinator =
                aircraft.GetComponentInChildren<FlightDynamicsCoordinator>(true);
            Assert.That(coordinator.thermalField, Is.SameAs(profile));
            Assert.That(coordinator.requestedBackend, Is.EqualTo(FlightDynamicsBackendKind.UnityPrototype),
                "The release product must retain the Unity prototype authority until native promotion passes.");
            Assert.That(coordinator.preferNativeInDevelopment, Is.True,
                "Development builds should opt into the native JSBSim thermal path.");

            GameObject world = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
            GoldenEagleThermalSoaring worldEagle =
                world.GetComponentInChildren<GoldenEagleThermalSoaring>(true);
            FlightDynamicsCoordinator worldCoordinator =
                world.GetComponentInChildren<FlightDynamicsCoordinator>(true);
            ProductDemoPilot worldRoutePilot =
                world.GetComponentInChildren<ProductDemoPilot>(true);
            Assert.That(worldEagle, Is.Not.Null);
            Assert.That(worldCoordinator, Is.Not.Null);
            Assert.That(worldRoutePilot, Is.Not.Null);
            Assert.That(worldEagle.encounterObserver, Is.SameAs(worldCoordinator.simulationRoot),
                "The product bird encounter observer must be explicitly serialized to the aircraft root.");
            Assert.That(worldEagle.encounterRoutePilot, Is.SameAs(worldRoutePilot),
                "The product bird encounter must be explicitly serialized to the active lesson authority.");
            Assert.That(worldEagle.encounterRouteSegmentIndex, Is.EqualTo(1),
                "The encounter must arm on climb-to-reservoir before the former visible segment-handoff pop.");
            Assert.That(worldEagle.orbitRadiusMeters, Is.EqualTo(55f).Within(0.01f));
            Assert.That(worldEagle.soaringAltitudeUnityMeters, Is.EqualTo(578f).Within(0.01f));
            Assert.That(worldEagle.airspeedMetersPerSecond, Is.EqualTo(13.2f).Within(0.01f));
            float initialDistance = Mathf.Sqrt(
                worldEagle.encounterForwardOffsetMeters * worldEagle.encounterForwardOffsetMeters +
                worldEagle.encounterLateralOffsetMeters * worldEagle.encounterLateralOffsetMeters +
                worldEagle.encounterVerticalSeparationMeters * worldEagle.encounterVerticalSeparationMeters);
            float initialAngularWingspanDegrees =
                2f * Mathf.Atan(1.05f / initialDistance) * Mathf.Rad2Deg;
            Assert.That(initialAngularWingspanDegrees, Is.InRange(0.75f, 1.1f),
                "The initial 2.1 m silhouette must resolve from distance instead of popping in at close-pass scale.");
            Assert.That(worldEagle.encounterStagingDistanceMeters, Is.EqualTo(1250f).Within(0.01f));
            Assert.That(worldEagle.encounterForwardOffsetMeters, Is.EqualTo(120f).Within(0.01f));
            Assert.That(worldEagle.encounterLateralOffsetMeters, Is.EqualTo(22f).Within(0.01f));
            Assert.That(worldEagle.encounterGlideDurationSeconds, Is.EqualTo(7f).Within(0.01f));
            Assert.That(worldEagle.encounterGlideSpeedMetersPerSecond, Is.EqualTo(15f).Within(0.01f));
        }

        [Test]
        public void EncounterBezierHasExactEndpointsAndFiniteForwardTangents()
        {
            Vector3 start = new Vector3(10f, 20f, 30f);
            Vector3 controlOne = new Vector3(40f, 25f, 70f);
            Vector3 controlTwo = new Vector3(80f, 50f, 100f);
            Vector3 end = new Vector3(120f, 60f, 150f);
            Assert.That(GoldenEagleThermalSoaring.EvaluateCubicBezier(
                start, controlOne, controlTwo, end, 0f), Is.EqualTo(start));
            Assert.That(GoldenEagleThermalSoaring.EvaluateCubicBezier(
                start, controlOne, controlTwo, end, 1f), Is.EqualTo(end));
            Assert.That(GoldenEagleThermalSoaring.EvaluateCubicBezierTangent(
                start, controlOne, controlTwo, end, 0f), Is.EqualTo(3f * (controlOne - start)));
            Assert.That(GoldenEagleThermalSoaring.EvaluateCubicBezierTangent(
                start, controlOne, controlTwo, end, 1f), Is.EqualTo(3f * (end - controlTwo)));
        }

        [Test]
        public void ImportedEagleWingbeatActuallyDeformsTheSkinnedMesh()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
                GoldenEagleThermalAuthoring.ModelPath);
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(
                    GoldenEagleThermalAuthoring.ModelPath)
                .OfType<AnimationClip>()
                .First(candidate => candidate.name.EndsWith(
                    "FlapLoop", System.StringComparison.Ordinal));
            GameObject instance = Object.Instantiate(source);
            Mesh early = new Mesh();
            Mesh extended = new Mesh();
            try
            {
                SkinnedMeshRenderer renderer =
                    instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.That(renderer, Is.Not.Null);
                clip.SampleAnimation(instance, 0.10f);
                renderer.BakeMesh(early);
                clip.SampleAnimation(instance, 0.70f);
                renderer.BakeMesh(extended);
                Assert.That(early.vertexCount, Is.EqualTo(extended.vertexCount));
                float maximumVertexTravel = 0f;
                Vector3[] earlyVertices = early.vertices;
                Vector3[] extendedVertices = extended.vertices;
                for (int index = 0; index < earlyVertices.Length; index++)
                    maximumVertexTravel = Mathf.Max(maximumVertexTravel,
                        Vector3.Distance(earlyVertices[index], extendedVertices[index]));
                Assert.That(maximumVertexTravel, Is.GreaterThan(0.35f),
                    "The imported FlapLoop must visibly articulate the wings; a named but rigid clip is rejected.");
            }
            finally
            {
                Object.DestroyImmediate(early);
                Object.DestroyImmediate(extended);
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void AuthoredPrefabWingbeatUsesTheCorrectAnimationRoot()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
                GoldenEagleThermalAuthoring.PrefabPath);
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(
                    GoldenEagleThermalAuthoring.ModelPath)
                .OfType<AnimationClip>()
                .First(candidate => candidate.name.EndsWith(
                    "FlapLoop", System.StringComparison.Ordinal));
            GameObject instance = Object.Instantiate(source);
            Mesh early = new Mesh();
            Mesh extended = new Mesh();
            try
            {
                Transform animationRoot = instance.transform.Find("GoldenEagleVisual");
                SkinnedMeshRenderer renderer =
                    instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.That(animationRoot, Is.Not.Null);
                Assert.That(renderer, Is.Not.Null);
                clip.SampleAnimation(animationRoot.gameObject, 0.10f);
                renderer.BakeMesh(early);
                clip.SampleAnimation(animationRoot.gameObject, 0.70f);
                renderer.BakeMesh(extended);
                float maximumVertexTravel = 0f;
                for (int index = 0; index < early.vertexCount; index++)
                    maximumVertexTravel = Mathf.Max(maximumVertexTravel,
                        Vector3.Distance(early.vertices[index], extended.vertices[index]));
                Assert.That(maximumVertexTravel, Is.GreaterThan(0.35f),
                    "The authored prefab must bind FlapLoop at its actual imported model root.");
            }
            finally
            {
                Object.DestroyImmediate(early);
                Object.DestroyImmediate(extended);
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ThermalCenterLiesOnTheAuthoredClimbToReservoirCorridor()
        {
            ThermalFieldProfile profile = AssetDatabase.LoadAssetAtPath<ThermalFieldProfile>(
                GoldenEagleThermalAuthoring.ProfilePath);
            Assert.That(profile.centerUnityMeters.x, Is.EqualTo(1700f).Within(0.01f));
            Assert.That(profile.centerUnityMeters.z, Is.EqualTo(2600f).Within(0.01f));

            Vector2 start = new Vector2(2000f, 1700f);
            Vector2 end = new Vector2(1400f, 3500f);
            Vector2 point = new Vector2(profile.centerUnityMeters.x, profile.centerUnityMeters.z);
            float t = Mathf.Clamp01(Vector2.Dot(point - start, end - start) / (end - start).sqrMagnitude);
            float crossTrack = Vector2.Distance(point, Vector2.Lerp(start, end, t));
            Assert.That(crossTrack, Is.LessThan(1f));
        }
    }
}
