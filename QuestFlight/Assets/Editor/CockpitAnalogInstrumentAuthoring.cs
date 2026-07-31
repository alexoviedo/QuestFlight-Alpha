using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.Analog;
using QuestFlightLab.PhotorealAerialSurfaceV1.Reflection.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Deterministically partitions the frozen imported Meter_01 topology into
    /// a neutralized static mesh plus one combined dynamic mesh. It never saves
    /// or modifies the product prefab; integration remains an explicit later step.
    /// </summary>
    public static class CockpitAnalogInstrumentAuthoring
    {
        public const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const string SourceGlbPath =
            "Assets/Resources/QuestFlightLab/ImportedAssets/" +
            "Cessna172KogThorns/cessna172.glb";
        public const string SourceMeterMaterialPath =
            "Assets/Production/Materials/Cockpit/" +
            "Cockpit_Meter_01_MAT_7515756384451954381.mat";
        public const string SourceMeshName =
            "Cessna_Interior_Meter_01_MAT_0_Meter_01_MAT_0";
        public const string MeterRendererPath =
            "AircraftVisualRoot/ImportedCockpitInterior/Cessna_Interior/" +
            "Cessna_Interior_Meter_01_MAT_0/" +
            "Cessna_Interior_Meter_01_MAT_0_Meter_01_MAT_0";
        public const string AircraftVisualRootPath = "AircraftVisualRoot";
        public const string NeutralizedMeterMeshPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/" +
            "Cockpit/Meshes/C172_Meter01_Neutralized.asset";
        public const string DynamicInstrumentMeshPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/" +
            "Cockpit/Meshes/C172_DynamicAnalogInstruments.asset";
        public const string LayoutAssetPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/" +
            "Cockpit/C172_AnalogInstrumentLayout.asset";
        public const string ManifestPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Reflection/" +
            "Cockpit/C172_DynamicAnalogInstrumentManifest.json";
        public const string DynamicRendererObjectName =
            "ProductionDynamicAnalogInstruments";

        public const int ExpectedSourceVertexCount = 4823;
        public const int ExpectedSourceTriangleCount = 6241;
        public const int ExpectedApertureCount = 13;
        public const int ExpectedDynamicTriangleCount = 40;
        public const int ExpectedNeutralizedTriangleCount =
            ExpectedSourceTriangleCount - ExpectedDynamicTriangleCount;
        public const string ExpectedSourceGuid =
            "d312be0fd0ba928458dd86cbe967956b";
        public const string ExpectedSourceSha256 =
            "a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872";

        public readonly struct InstrumentSourceDefinition
        {
            public readonly string Label;
            public readonly AnalogInstrumentFunction Function;
            public readonly int FaceSeed;
            public readonly int ExpectedFaceTriangles;
            public readonly int HubSeed;
            public readonly int TipSeed;
            public readonly int[] DynamicSeeds;
            public readonly int[] ExpectedDynamicSeedTriangleCounts;
            public readonly string Authority;
            public readonly string Calibration;

            public InstrumentSourceDefinition(
                string label,
                AnalogInstrumentFunction function,
                int faceSeed,
                int expectedFaceTriangles,
                int hubSeed,
                int tipSeed,
                int[] dynamicSeeds,
                int[] expectedDynamicSeedTriangleCounts,
                string authority,
                string calibration)
            {
                Label = label;
                Function = function;
                FaceSeed = faceSeed;
                ExpectedFaceTriangles = expectedFaceTriangles;
                HubSeed = hubSeed;
                TipSeed = tipSeed;
                DynamicSeeds = dynamicSeeds;
                ExpectedDynamicSeedTriangleCounts =
                    expectedDynamicSeedTriangleCounts;
                Authority = authority;
                Calibration = calibration;
            }
        }

        public sealed class SourceAudit
        {
            public int sourceVertexCount;
            public int sourceTriangleCount;
            public int connectedComponentCount;
            public int apertureCount;
            public int dynamicTriangleCount;
            public int neutralizedTriangleCount;
            public int supportedBindingCount;
            public string sourceMeshName = string.Empty;
            public string sourceAssetGuid = string.Empty;
            public string sourceSha256 = string.Empty;
            public string deterministicSignature = string.Empty;
            public int[] apertureSeeds = Array.Empty<int>();
            public int[] removedTriangleSeeds = Array.Empty<int>();
        }

        private sealed class MeshComponent
        {
            public int Seed;
            public readonly List<int> Triangles = new List<int>();
            public readonly HashSet<int> Vertices = new HashSet<int>();
            public Bounds LocalBounds;
            public Bounds AircraftBounds;
        }

        private sealed class SourceContext : IDisposable
        {
            public GameObject PrefabRoot;
            public Transform AircraftVisual;
            public MeshRenderer MeterRenderer;
            public Mesh SourceMesh;
            public Matrix4x4 SourceToAircraft;

            public void Dispose()
            {
                if (PrefabRoot != null)
                    PrefabUtility.UnloadPrefabContents(PrefabRoot);
            }
        }

        [Serializable]
        private sealed class Manifest
        {
            public string schema = "questflight.cockpit.dynamic_analog.v1";
            public string sourceGlb = SourceGlbPath;
            public string sourceGuid = ExpectedSourceGuid;
            public string sourceSha256 = string.Empty;
            public string sourceMesh = string.Empty;
            public int sourceVertices;
            public int sourceTriangles;
            public int connectedComponents;
            public int geometryDerivedRoundApertures;
            public ManifestGauge[] supported = Array.Empty<ManifestGauge>();
            public ManifestExclusion[] exclusions = Array.Empty<ManifestExclusion>();
            public string neutralizedMesh = NeutralizedMeterMeshPath;
            public string combinedDynamicMesh = DynamicInstrumentMeshPath;
            public string reusedMaterial = SourceMeterMaterialPath;
            public int neutralizedTriangles;
            public int dynamicTriangles;
            public int runtimeDrawDelta = 1;
            public int visibleMaterialDelta = 0;
            public int runtimeTextureDelta = 0;
            public int transparentSlotDelta = 0;
            public string[] preservedOverlays =
            {
                "ProductionPrincipalInstrumentLensArray",
                "ProductionInstrumentDisplayReadouts"
            };
        }

        [Serializable]
        private sealed class ManifestGauge
        {
            public string label = string.Empty;
            public string function = string.Empty;
            public string authority = string.Empty;
            public string calibration = string.Empty;
            public int faceTriangleSeed;
            public int[] dynamicTriangleSeeds = Array.Empty<int>();
            public int sourceTriangles;
        }

        [Serializable]
        private sealed class ManifestExclusion
        {
            public string item = string.Empty;
            public int[] faceTriangleSeeds = Array.Empty<int>();
            public string reason = string.Empty;
        }

        private static readonly InstrumentSourceDefinition[] Definitions =
        {
            new InstrumentSourceDefinition(
                "IAS / KNOTS",
                AnalogInstrumentFunction.IndicatedAirspeedKnots,
                2819, 30, 4483, 4157,
                new[] { 4147, 4151, 4153, 4157, 4159 },
                new[] { 4, 2, 4, 2, 4 },
                "Positively labeled IAS/KNOTS face driven by calibrated airspeed.",
                "Piecewise source-face marks through the normal C172 operating range; " +
                "no generic 270-degree gauge formula."),
            new InstrumentSourceDefinition(
                "Principal VERTICAL SPEED",
                AnalogInstrumentFunction.IndicatedVerticalSpeedFeetPerMinute,
                2541, 36, 5601, 5639,
                new[]
                {
                    2533, 2535, 2537, 2539,
                    5631, 5635, 5639, 5641, 6011
                },
                new[] { 2, 2, 2, 2, 4, 4, 2, 4, 2 },
                "Positively labeled VERTICAL SPEED / 1000 FPM face driven by " +
                "the filtered authoritative air-data state.",
                "Audited zero-at-left split scale: climb uses the upper arc and " +
                "descent uses the lower arc at 0.5/1/2/4/6 thousand FPM."),
        };

        private static readonly ManifestExclusion[] Exclusions =
        {
            Exclusion(
                "BRAKE PRESS face",
                new[] { 2906 },
                "Toe-brake command exists, but measured brake-system pressure does not. " +
                "A control command is not presented as hydraulic pressure."),
            Exclusion(
                "MACH/AS hybrid faces",
                new[] { 2501, 2469 },
                "Ambiguous fantasy/reused faces; not a positively identified airspeed target."),
            Exclusion(
                "Duplicate reused VSI faces",
                new[] { 2789, 2585 },
                "Duplicate fantasy presentation; only the principal labeled VSI is dynamic."),
            Exclusion(
                "ALT faces",
                new[] { 2667, 2621, 2713 },
                "Altitude is authoritative, but only one hand is separable while the " +
                "counter/drum geometry remains static. Partial animation would be misleading."),
            Exclusion(
                "Clock",
                new[] { 2849 },
                "Represented but simulation clock authority is not in the cockpit contract."),
            Exclusion(
                "FLAPS indicator",
                new[] { 2759 },
                "Only flap command is authoritative; actual flap-position feedback is unavailable."),
            Exclusion(
                "GYRO/LOC face",
                Array.Empty<int>(),
                "Ambiguous fantasy face with no authoritative localizer/gyro presentation contract."),
            Exclusion(
                "Directional gyro heading card",
                new[] { 2891 },
                "The card is identity-audited and heading is authoritative, but the " +
                "fixed lubber vector and absolute authored-card registration are not " +
                "proven. An arbitrary first-sample offset would be misleading."),
            Exclusion(
                "Magnetic compass tape",
                new[] { 77, 78 },
                "Geometry is separate, but magnetic variation/heading is unavailable; true " +
                "heading is not relabeled as magnetic heading."),
            Exclusion(
                "Attitude card",
                Array.Empty<int>(),
                "Pitch/bank state exists, but the combined lower atlas/card topology is not " +
                "deterministically separable without damaging unrelated panel art."),
            Exclusion(
                "Fuel, oil, tachometer and unlabeled faces",
                Array.Empty<int>(),
                "No positive face identity plus authoritative value pairing was proven.")
        };

        public static IReadOnlyList<InstrumentSourceDefinition>
            SupportedDefinitions => Definitions;

        public static IReadOnlyList<string> ExcludedItemNames =>
            Exclusions.Select(item => item.item).ToArray();

        [MenuItem("QuestFlight/Cockpit/Build Dynamic Analog Instrument Assets Only")]
        public static void BuildAssetsOnly()
        {
            using (SourceContext context = LoadSourceContext())
            {
                Dictionary<int, MeshComponent> components =
                    BuildComponents(
                        context.SourceMesh,
                        context.SourceToAircraft,
                        out List<MeshComponent> orderedComponents);
                SourceAudit audit = Audit(
                    context, components, orderedComponents);

                HashSet<int> removedTriangles = DynamicTriangleSet(components);
                Mesh neutralized = BuildNeutralizedMesh(
                    context.SourceMesh, removedTriangles);
                Mesh dynamic = BuildDynamicMeshAndBindings(
                    context,
                    components,
                    out AnalogInstrumentMeshBinding[] bindings);

                Mesh neutralizedAsset = UpsertMesh(
                    neutralized, NeutralizedMeterMeshPath);
                Mesh dynamicAsset = UpsertMesh(
                    dynamic, DynamicInstrumentMeshPath);
                Material sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    SourceMeterMaterialPath);
                Require(sourceMaterial != null,
                    "The existing source meter material could not be loaded.");
                Require(context.MeterRenderer.sharedMaterial == sourceMaterial,
                    "The product meter no longer uses the expected shared source material.");

                ProductionAnalogInstrumentLayout layout =
                    AssetDatabase.LoadAssetAtPath<ProductionAnalogInstrumentLayout>(
                        LayoutAssetPath);
                if (layout == null)
                {
                    layout = ScriptableObject.CreateInstance<
                        ProductionAnalogInstrumentLayout>();
                    AssetDatabase.CreateAsset(layout, LayoutAssetPath);
                }
                layout.ConfigureGeneratedAssets(
                    neutralizedAsset,
                    dynamicAsset,
                    sourceMaterial,
                    bindings,
                    audit.sourceTriangleCount,
                    audit.neutralizedTriangleCount,
                    audit.dynamicTriangleCount,
                    audit.sourceMeshName,
                    audit.sourceAssetGuid);
                Require(layout.Validate(out string layoutError), layoutError);
                EditorUtility.SetDirty(layout);

                WriteManifest(audit);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceUpdate);
                Debug.Log(
                    "[QuestFlight][AnalogInstruments] Built assets only: two " +
                    "identity-audited instruments, one future draw, existing material, " +
                    "no new texture. Product prefab was not modified.");
            }
        }

        [MenuItem(
            "QuestFlight/Cockpit/Apply Dynamic Analog Instruments To Product Prefab")]
        public static void ApplyToProductPrefab()
        {
            ProductionAnalogInstrumentLayout layout = LoadGeneratedLayout();
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                Transform meter = root.transform.Find(MeterRendererPath);
                Require(meter != null,
                    $"Required transform is missing: {MeterRendererPath}");
                MeshFilter meterFilter = meter.GetComponent<MeshFilter>();
                MeshRenderer meterRenderer = meter.GetComponent<MeshRenderer>();
                Require(meterFilter != null && meterRenderer != null,
                    "The authored Meter_01 renderer is incomplete.");

                Mesh frozenSource = LoadFrozenSourceMesh();
                Require(
                    meterFilter.sharedMesh == frozenSource ||
                    meterFilter.sharedMesh == layout.NeutralizedMeterMesh,
                    "Meter_01 is neither the frozen source nor the generated " +
                    "neutralized topology. Refusing to overwrite unrelated work.");
                Require(meterRenderer.sharedMaterials.Length == 1 &&
                        meterRenderer.sharedMaterial ==
                            layout.SharedSourceMeterMaterial,
                    "Meter_01 no longer uses exactly the audited source material.");

                ProductionCockpitSystemsController[] systemsControllers =
                    root.GetComponentsInChildren<
                        ProductionCockpitSystemsController>(true);
                Require(systemsControllers.Length == 1,
                    "Exactly one authored ProductionCockpitSystemsController must " +
                    $"exist before analog integration; found {systemsControllers.Length}.");

                Transform[] directDynamicChildren = meter
                    .Cast<Transform>()
                    .Where(child => child.name == DynamicRendererObjectName)
                    .ToArray();
                Require(directDynamicChildren.Length <= 1,
                    "Multiple authored dynamic-analog children exist under Meter_01.");
                Transform dynamicTransform;
                if (directDynamicChildren.Length == 0)
                {
                    var dynamicObject = new GameObject(
                        DynamicRendererObjectName,
                        typeof(MeshFilter),
                        typeof(MeshRenderer));
                    dynamicObject.layer = meter.gameObject.layer;
                    dynamicTransform = dynamicObject.transform;
                    dynamicTransform.SetParent(meter, false);
                }
                else
                {
                    dynamicTransform = directDynamicChildren[0];
                    Require(
                        dynamicTransform.GetComponents<Component>().Length == 3 &&
                        dynamicTransform.GetComponent<MeshFilter>() != null &&
                        dynamicTransform.GetComponent<MeshRenderer>() != null,
                        "The existing dynamic-analog child has unexpected components.");
                }
                dynamicTransform.localPosition = Vector3.zero;
                dynamicTransform.localRotation = Quaternion.identity;
                dynamicTransform.localScale = Vector3.one;

                MeshFilter dynamicFilter =
                    dynamicTransform.GetComponent<MeshFilter>();
                MeshRenderer dynamicRenderer =
                    dynamicTransform.GetComponent<MeshRenderer>();
                dynamicFilter.sharedMesh = layout.DynamicInstrumentMesh;
                dynamicRenderer.sharedMaterials =
                    new[] { layout.SharedSourceMeterMaterial };
                dynamicRenderer.shadowCastingMode = ShadowCastingMode.Off;
                dynamicRenderer.receiveShadows = false;
                dynamicRenderer.lightProbeUsage = LightProbeUsage.Off;
                dynamicRenderer.reflectionProbeUsage =
                    ReflectionProbeUsage.BlendProbesAndSkybox;
                dynamicRenderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.ForceNoMotion;
                dynamicRenderer.allowOcclusionWhenDynamic = false;
                EnsureInteriorVisibilityRenderer(root, dynamicRenderer);

                meterFilter.sharedMesh = layout.NeutralizedMeterMesh;

                ProductionAnalogInstrumentController[] controllers =
                    root.GetComponentsInChildren<
                        ProductionAnalogInstrumentController>(true);
                Require(controllers.Length <= 1,
                    "Multiple ProductionAnalogInstrumentController components exist.");
                ProductionAnalogInstrumentController controller =
                    controllers.Length == 1
                        ? controllers[0]
                        : root.AddComponent<ProductionAnalogInstrumentController>();
                controller.ConfigureAuthoredReferences(
                    systemsControllers[0],
                    layout,
                    meterFilter,
                    meterRenderer,
                    dynamicFilter,
                    dynamicRenderer);

                ValidateIntegratedPrefab(
                    root, layout, requireDirectionalVisibilityStreams: false);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    root, AircraftPrefabPath, out bool success);
                Require(success && saved != null,
                    "Unity failed to save the explicitly authored aircraft prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            CockpitDirectionalVisibilityAuthoring.AuthorAndIntegrate();
            ValidateProductPrefab();
            Debug.Log(
                "[QuestFlight][AnalogInstruments] Authored exactly two calibrated " +
                "analog channels into the product prefab with one combined renderer " +
                "and the existing zero-draw directional-visibility contract.");
        }

        [MenuItem(
            "QuestFlight/Cockpit/Validate Dynamic Analog Product Prefab")]
        public static void ValidateProductPrefab()
        {
            ProductionAnalogInstrumentLayout layout = LoadGeneratedLayout();
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                ValidateIntegratedPrefab(
                    root, layout, requireDirectionalVisibilityStreams: true);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log(
                "[QuestFlight][AnalogInstruments] Product prefab integration is valid.");
        }

        public static SourceAudit AuditFrozenSourceForTests()
        {
            using (SourceContext context = LoadSourceContext())
            {
                Dictionary<int, MeshComponent> components =
                    BuildComponents(
                        context.SourceMesh,
                        context.SourceToAircraft,
                        out List<MeshComponent> ordered);
                return Audit(context, components, ordered);
            }
        }

        private static void EnsureInteriorVisibilityRenderer(
            GameObject root,
            Renderer renderer)
        {
            AircraftVisibilityProfileController[] visibilityControllers =
                root.GetComponentsInChildren<
                    AircraftVisibilityProfileController>(true);
            Require(
                visibilityControllers.Length == 1,
                "The authored aircraft must contain exactly one visibility profile.");

            SerializedObject serialized =
                new SerializedObject(visibilityControllers[0]);
            SerializedProperty interiors =
                serialized.FindProperty("interiorRenderers");
            Require(
                interiors != null && interiors.isArray,
                "The visibility profile interior-renderer list is unavailable.");

            Renderer[] current = Enumerable.Range(0, interiors.arraySize)
                .Select(index =>
                    interiors.GetArrayElementAtIndex(index)
                        .objectReferenceValue as Renderer)
                .Where(item => item != null && item != renderer)
                .Append(renderer)
                .ToArray();
            interiors.arraySize = current.Length;
            for (int index = 0; index < current.Length; index++)
            {
                interiors.GetArrayElementAtIndex(index).objectReferenceValue =
                    current[index];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(visibilityControllers[0]);
        }

        private static SourceContext LoadSourceContext()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                Transform aircraftVisual = root.transform.Find(AircraftVisualRootPath);
                Transform meter = root.transform.Find(MeterRendererPath);
                Require(aircraftVisual != null,
                    $"Required transform is missing: {AircraftVisualRootPath}");
                Require(meter != null,
                    $"Required transform is missing: {MeterRendererPath}");
                MeshRenderer renderer = meter.GetComponent<MeshRenderer>();
                Mesh source = LoadFrozenSourceMesh();
                Require(renderer != null && source != null,
                    "The identity-audited Meter_01 renderer/mesh is missing.");
                return new SourceContext
                {
                    PrefabRoot = root,
                    AircraftVisual = aircraftVisual,
                    MeterRenderer = renderer,
                    SourceMesh = source,
                    SourceToAircraft =
                        aircraftVisual.worldToLocalMatrix *
                        meter.localToWorldMatrix
                };
            }
            catch
            {
                PrefabUtility.UnloadPrefabContents(root);
                throw;
            }
        }

        private static Mesh LoadFrozenSourceMesh()
        {
            Mesh[] meshes = AssetDatabase.LoadAllAssetsAtPath(SourceGlbPath)
                .OfType<Mesh>()
                .Where(mesh => mesh.name == SourceMeshName)
                .ToArray();
            Require(meshes.Length == 1,
                $"Expected one frozen source mesh named '{SourceMeshName}'; found " +
                $"{meshes.Length}.");
            return meshes[0];
        }

        private static ProductionAnalogInstrumentLayout LoadGeneratedLayout()
        {
            ProductionAnalogInstrumentLayout layout =
                AssetDatabase.LoadAssetAtPath<ProductionAnalogInstrumentLayout>(
                    LayoutAssetPath);
            Require(layout != null,
                "Generate the dynamic analog assets before prefab integration.");
            Require(layout.Validate(out string error), error);
            Require(layout.SourceAssetGuid == ExpectedSourceGuid,
                "The generated layout does not derive from the frozen C172 source.");
            return layout;
        }

        private static void ValidateIntegratedPrefab(
            GameObject root,
            ProductionAnalogInstrumentLayout layout,
            bool requireDirectionalVisibilityStreams)
        {
            Require(root != null, "The product prefab root is missing.");
            Transform meter = root.transform.Find(MeterRendererPath);
            Require(meter != null,
                $"Required transform is missing: {MeterRendererPath}");
            MeshFilter meterFilter = meter.GetComponent<MeshFilter>();
            MeshRenderer meterRenderer = meter.GetComponent<MeshRenderer>();
            Require(meterFilter != null && meterRenderer != null,
                "The authored Meter_01 renderer is incomplete.");
            Require(meterFilter.sharedMesh == layout.NeutralizedMeterMesh,
                "Meter_01 is not using the generated neutralized topology.");
            ValidateDirectionalVisibilityStream(
                meterRenderer,
                layout.NeutralizedMeterMesh,
                requireDirectionalVisibilityStreams,
                "Meter_01");

            Transform[] dynamicChildren = meter
                .Cast<Transform>()
                .Where(child => child.name == DynamicRendererObjectName)
                .ToArray();
            Require(dynamicChildren.Length == 1,
                "Exactly one combined dynamic-analog child must exist under Meter_01.");
            Transform dynamicTransform = dynamicChildren[0];
            Require(
                dynamicTransform.localPosition.sqrMagnitude < 1e-12f &&
                Quaternion.Angle(
                    dynamicTransform.localRotation, Quaternion.identity) < 0.001f &&
                (dynamicTransform.localScale - Vector3.one).sqrMagnitude < 1e-12f,
                "The dynamic-analog child must be identity-aligned with Meter_01.");
            MeshFilter dynamicFilter =
                dynamicTransform.GetComponent<MeshFilter>();
            MeshRenderer dynamicRenderer =
                dynamicTransform.GetComponent<MeshRenderer>();
            Require(dynamicFilter != null && dynamicRenderer != null,
                "The combined dynamic-analog renderer is incomplete.");
            Require(dynamicFilter.sharedMesh == layout.DynamicInstrumentMesh,
                "The combined renderer is not using the generated dynamic mesh.");
            ValidateDirectionalVisibilityStream(
                dynamicRenderer,
                layout.DynamicInstrumentMesh,
                requireDirectionalVisibilityStreams,
                "The combined renderer");
            Require(dynamicRenderer.sharedMaterials.Length == 1 &&
                    dynamicRenderer.sharedMaterial ==
                        layout.SharedSourceMeterMaterial,
                "The combined renderer must reuse exactly the source meter material.");
            Require(
                dynamicRenderer.shadowCastingMode == ShadowCastingMode.Off &&
                !dynamicRenderer.receiveShadows &&
                dynamicRenderer.lightProbeUsage == LightProbeUsage.Off &&
                dynamicRenderer.reflectionProbeUsage ==
                    ReflectionProbeUsage.BlendProbesAndSkybox,
                "The combined renderer violates the bounded Quest lighting contract.");
            AircraftVisibilityProfileController[] visibilityControllers =
                root.GetComponentsInChildren<
                    AircraftVisibilityProfileController>(true);
            Require(
                visibilityControllers.Length == 1,
                "The authored aircraft must contain exactly one visibility profile.");
            SerializedProperty interiors =
                new SerializedObject(visibilityControllers[0])
                    .FindProperty("interiorRenderers");
            int dynamicVisibilityReferences =
                Enumerable.Range(0, interiors.arraySize).Count(index =>
                    interiors.GetArrayElementAtIndex(index)
                        .objectReferenceValue == dynamicRenderer);
            Require(
                dynamicVisibilityReferences == 1,
                "The combined dynamic-analog renderer must appear exactly once " +
                "in the cockpit visibility profile.");

            ProductionCockpitSystemsController[] systems =
                root.GetComponentsInChildren<
                    ProductionCockpitSystemsController>(true);
            ProductionAnalogInstrumentController[] controllers =
                root.GetComponentsInChildren<
                    ProductionAnalogInstrumentController>(true);
            Require(systems.Length == 1,
                $"Expected one cockpit systems controller; found {systems.Length}.");
            Require(controllers.Length == 1,
                $"Expected one analog instrument controller; found {controllers.Length}.");
            Require(controllers[0].Systems == systems[0] &&
                    controllers[0].Layout == layout,
                "The analog controller references the wrong systems source or layout.");
            Require(controllers[0].ValidateAuthoredReferences(out string error), error);

            string[] preserved =
            {
                "ProductionPrincipalInstrumentLensArray",
                "ProductionInstrumentDisplayReadouts"
            };
            Transform[] allTransforms =
                root.GetComponentsInChildren<Transform>(true);
            foreach (string objectName in preserved)
            {
                Require(
                    allTransforms.Count(item => item.name == objectName) == 1,
                    $"Required preserved cockpit overlay '{objectName}' is missing " +
                    "or duplicated.");
            }
        }

        private static void ValidateDirectionalVisibilityStream(
            MeshRenderer renderer,
            Mesh sourceMesh,
            bool required,
            string label)
        {
            Mesh stream = renderer.additionalVertexStreams;
            if (!required && stream == null)
            {
                return;
            }

            Require(stream != null,
                $"{label} is missing its geometry-derived directional-visibility stream.");
            Require(stream.vertexCount == sourceMesh.vertexCount,
                $"{label} directional-visibility stream no longer matches its source mesh.");
            Require(
                stream.HasVertexAttribute(VertexAttribute.Color) &&
                stream.GetVertexAttributeDimension(VertexAttribute.Color) == 4 &&
                stream.GetVertexAttributeFormat(VertexAttribute.Color) ==
                    VertexAttributeFormat.UNorm8,
                $"{label} directional-visibility stream must use Color UNorm8x4.");
        }

        private static SourceAudit Audit(
            SourceContext context,
            Dictionary<int, MeshComponent> components,
            List<MeshComponent> ordered)
        {
            Mesh source = context.SourceMesh;
            Require(source.vertexCount == ExpectedSourceVertexCount,
                $"Frozen Meter_01 vertex count changed: {source.vertexCount}.");
            Require(source.triangles.Length / 3 == ExpectedSourceTriangleCount,
                $"Frozen Meter_01 triangle count changed: {source.triangles.Length / 3}.");
            Require(source.subMeshCount == 1,
                "Meter_01 must retain exactly one source submesh/material.");
            string sourceGuid = AssetDatabase.AssetPathToGUID(SourceGlbPath);
            Require(string.Equals(sourceGuid, ExpectedSourceGuid,
                    StringComparison.Ordinal),
                $"Frozen C172 source GUID changed: {sourceGuid}.");
            string sourceSha256 = Sha256(SourceGlbPath);
            Require(string.Equals(
                    sourceSha256, ExpectedSourceSha256,
                    StringComparison.Ordinal),
                $"Frozen C172 source bytes changed: {sourceSha256}.");

            int[] apertureSeeds = ordered
                .Where(component =>
                {
                    Vector3 size = component.AircraftBounds.size;
                    return size.x >= 0.045f && size.x <= 0.11f &&
                           size.y >= 0.045f && size.y <= 0.11f &&
                           size.z <= 0.012f;
                })
                .Select(component => component.Seed)
                .OrderBy(value => value)
                .ToArray();
            Require(apertureSeeds.Length == ExpectedApertureCount,
                $"Expected {ExpectedApertureCount} geometry-derived apertures; found " +
                $"{apertureSeeds.Length}.");

            int dynamicTriangles = 0;
            var removedSeeds = new List<int>();
            foreach (InstrumentSourceDefinition definition in Definitions)
            {
                Require(components.TryGetValue(
                        definition.FaceSeed, out MeshComponent face),
                    $"Missing supported face seed {definition.FaceSeed}.");
                Require(face.Triangles.Count == definition.ExpectedFaceTriangles,
                    $"Face seed {definition.FaceSeed} topology changed.");
                Require(apertureSeeds.Contains(definition.FaceSeed),
                    $"Supported face seed {definition.FaceSeed} is no longer a round aperture.");
                Require(components.ContainsKey(definition.HubSeed),
                    $"Missing hub seed {definition.HubSeed}.");
                if (definition.TipSeed >= 0)
                    Require(components.ContainsKey(definition.TipSeed),
                        $"Missing pointer tip seed {definition.TipSeed}.");
                Require(
                    definition.DynamicSeeds.Length ==
                    definition.ExpectedDynamicSeedTriangleCounts.Length,
                    $"Definition '{definition.Label}' has mismatched seed metadata.");

                for (int index = 0;
                     index < definition.DynamicSeeds.Length;
                     index++)
                {
                    int seed = definition.DynamicSeeds[index];
                    Require(components.TryGetValue(seed, out MeshComponent component),
                        $"Missing dynamic component seed {seed}.");
                    int expected =
                        definition.ExpectedDynamicSeedTriangleCounts[index];
                    Require(component.Triangles.Count == expected,
                        $"Dynamic seed {seed} has {component.Triangles.Count} triangles; " +
                        $"expected {expected}.");
                    dynamicTriangles += component.Triangles.Count;
                    removedSeeds.Add(seed);
                }
            }
            Require(dynamicTriangles == ExpectedDynamicTriangleCount,
                $"Expected {ExpectedDynamicTriangleCount} dynamic triangles; found " +
                $"{dynamicTriangles}.");

            int[] sortedRemoved = removedSeeds.OrderBy(value => value).ToArray();
            string signature = string.Join(
                "|",
                source.vertexCount,
                source.triangles.Length / 3,
                ordered.Count,
                string.Join(",", apertureSeeds),
                string.Join(",", sortedRemoved));

            return new SourceAudit
            {
                sourceVertexCount = source.vertexCount,
                sourceTriangleCount = source.triangles.Length / 3,
                connectedComponentCount = ordered.Count,
                apertureCount = apertureSeeds.Length,
                dynamicTriangleCount = dynamicTriangles,
                neutralizedTriangleCount =
                    source.triangles.Length / 3 - dynamicTriangles,
                supportedBindingCount = Definitions.Length,
                sourceMeshName = source.name,
                sourceAssetGuid = sourceGuid,
                sourceSha256 = sourceSha256,
                deterministicSignature = signature,
                apertureSeeds = apertureSeeds,
                removedTriangleSeeds = sortedRemoved
            };
        }

        private static Dictionary<int, MeshComponent> BuildComponents(
            Mesh source,
            Matrix4x4 sourceToAircraft,
            out List<MeshComponent> ordered)
        {
            Vector3[] vertices = source.vertices;
            int[] triangles = source.triangles;
            var trianglesByVertex = new List<int>[vertices.Length];
            for (int triangle = 0; triangle < triangles.Length / 3; triangle++)
            {
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = triangles[triangle * 3 + corner];
                    List<int> adjacent = trianglesByVertex[vertex];
                    if (adjacent == null)
                    {
                        adjacent = new List<int>();
                        trianglesByVertex[vertex] = adjacent;
                    }
                    adjacent.Add(triangle);
                }
            }

            var result = new Dictionary<int, MeshComponent>();
            ordered = new List<MeshComponent>();
            bool[] visited = new bool[triangles.Length / 3];
            var queue = new Queue<int>();
            for (int triangle = 0; triangle < visited.Length; triangle++)
            {
                if (visited[triangle]) continue;
                var component = new MeshComponent { Seed = triangle };
                visited[triangle] = true;
                queue.Enqueue(triangle);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Triangles.Add(current);
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = triangles[current * 3 + corner];
                        component.Vertices.Add(vertex);
                        foreach (int neighbor in trianglesByVertex[vertex])
                        {
                            if (visited[neighbor]) continue;
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                bool initialized = false;
                foreach (int vertex in component.Vertices)
                {
                    Vector3 local = vertices[vertex];
                    Vector3 aircraft = sourceToAircraft.MultiplyPoint3x4(local);
                    if (!initialized)
                    {
                        component.LocalBounds = new Bounds(local, Vector3.zero);
                        component.AircraftBounds =
                            new Bounds(aircraft, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        component.LocalBounds.Encapsulate(local);
                        component.AircraftBounds.Encapsulate(aircraft);
                    }
                }
                component.Triangles.Sort();
                result.Add(component.Seed, component);
                ordered.Add(component);
            }
            return result;
        }

        private static HashSet<int> DynamicTriangleSet(
            Dictionary<int, MeshComponent> components)
        {
            var result = new HashSet<int>();
            foreach (InstrumentSourceDefinition definition in Definitions)
            foreach (int seed in definition.DynamicSeeds)
            foreach (int triangle in components[seed].Triangles)
                Require(result.Add(triangle),
                    $"Triangle {triangle} was assigned to multiple analog instruments.");
            return result;
        }

        private static Mesh BuildNeutralizedMesh(
            Mesh source,
            HashSet<int> removedTriangles)
        {
            int[] sourceTriangles = source.triangles;
            var triangles = new List<int>(
                sourceTriangles.Length - removedTriangles.Count * 3);
            for (int triangle = 0;
                 triangle < sourceTriangles.Length / 3;
                 triangle++)
            {
                if (removedTriangles.Contains(triangle)) continue;
                triangles.Add(sourceTriangles[triangle * 3]);
                triangles.Add(sourceTriangles[triangle * 3 + 1]);
                triangles.Add(sourceTriangles[triangle * 3 + 2]);
            }
            Require(
                triangles.Count / 3 == ExpectedNeutralizedTriangleCount,
                "Neutralized Meter_01 triangle count is not deterministic.");

            Mesh generated = UnityEngine.Object.Instantiate(source);
            generated.name = "C172_Meter01_Neutralized";
            generated.SetTriangles(triangles, 0, false);
            generated.bounds = source.bounds;
            return generated;
        }

        private static Mesh BuildDynamicMeshAndBindings(
            SourceContext context,
            Dictionary<int, MeshComponent> components,
            out AnalogInstrumentMeshBinding[] bindings)
        {
            Mesh source = context.SourceMesh;
            int[] sourceTriangles = source.triangles;
            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector4[] sourceTangents = source.tangents;
            Vector2[] sourceUv = source.uv;
            Vector2[] sourceUv2 = source.uv2;
            Color[] sourceColors = source.colors;
            Require(sourceNormals.Length == sourceVertices.Length &&
                    sourceTangents.Length == sourceVertices.Length &&
                    sourceUv.Length == sourceVertices.Length,
                "Meter_01 position/normal/tangent/UV0 channels are incomplete.");

            var vertices = new List<Vector3>(ExpectedDynamicTriangleCount * 3);
            var normals = new List<Vector3>(vertices.Capacity);
            var tangents = new List<Vector4>(vertices.Capacity);
            var uv = new List<Vector2>(vertices.Capacity);
            var uv2 = new List<Vector2>(vertices.Capacity);
            var colors = new List<Color>(vertices.Capacity);
            var indices = new List<int>(vertices.Capacity);
            bindings = new AnalogInstrumentMeshBinding[Definitions.Length];

            Vector3 right = context.MeterRenderer.transform
                .InverseTransformDirection(context.AircraftVisual.right).normalized;
            Vector3 upCandidate = context.MeterRenderer.transform
                .InverseTransformDirection(context.AircraftVisual.up).normalized;
            Vector3 axis = Vector3.Cross(right, upCandidate).normalized;
            Vector3 up = Vector3.Cross(axis, right).normalized;
            Require(Vector3.Dot(up, upCandidate) > 0.99f,
                "Meter dial basis no longer aligns with AircraftVisualRoot.");

            for (int definitionIndex = 0;
                 definitionIndex < Definitions.Length;
                 definitionIndex++)
            {
                InstrumentSourceDefinition definition =
                    Definitions[definitionIndex];
                int firstVertex = vertices.Count;
                int sourceTriangleCount = 0;
                foreach (int seed in definition.DynamicSeeds)
                {
                    MeshComponent component = components[seed];
                    sourceTriangleCount += component.Triangles.Count;
                    foreach (int triangle in component.Triangles)
                    {
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int sourceVertex =
                                sourceTriangles[triangle * 3 + corner];
                            indices.Add(vertices.Count);
                            vertices.Add(sourceVertices[sourceVertex]);
                            normals.Add(sourceNormals[sourceVertex]);
                            tangents.Add(sourceTangents[sourceVertex]);
                            uv.Add(sourceUv[sourceVertex]);
                            if (sourceUv2.Length == sourceVertices.Length)
                                uv2.Add(sourceUv2[sourceVertex]);
                            if (sourceColors.Length == sourceVertices.Length)
                                colors.Add(sourceColors[sourceVertex]);
                        }
                    }
                }

                Vector3 pivot = components[definition.HubSeed].LocalBounds.center;
                Vector3 authoredDirection = right;
                if (definition.TipSeed >= 0)
                {
                    authoredDirection =
                        components[definition.TipSeed].LocalBounds.center - pivot;
                    authoredDirection =
                        Vector3.ProjectOnPlane(authoredDirection, axis).normalized;
                    Require(authoredDirection.sqrMagnitude > 0.99f,
                        $"Pointer tip for '{definition.Label}' is degenerate.");
                }
                bindings[definitionIndex] = new AnalogInstrumentMeshBinding
                {
                    label = definition.Label,
                    function = definition.Function,
                    firstVertex = firstVertex,
                    vertexCount = vertices.Count - firstVertex,
                    pivotLocal = pivot,
                    rotationAxisLocal = axis,
                    dialRightLocal = right,
                    dialUpLocal = up,
                    authoredIndicatorDirectionLocal = authoredDirection,
                    sourceFaceTriangleSeed = definition.FaceSeed,
                    sourceTipTriangleSeed = definition.TipSeed,
                    sourceTriangleCount = sourceTriangleCount
                };
            }

            Require(vertices.Count == ExpectedDynamicTriangleCount * 3,
                "Combined analog mesh vertex count is not deterministic.");
            var generated = new Mesh
            {
                name = "C172_DynamicAnalogInstruments",
                indexFormat = IndexFormat.UInt16
            };
            generated.SetVertices(vertices);
            generated.SetNormals(normals);
            generated.SetTangents(tangents);
            generated.SetUVs(0, uv);
            if (uv2.Count == vertices.Count) generated.SetUVs(1, uv2);
            if (colors.Count == vertices.Count) generated.SetColors(colors);
            generated.SetTriangles(indices, 0, false);
            // The whole source bounds safely contains every possible needle/card
            // rotation and avoids per-frame bounds work on Quest.
            generated.bounds = source.bounds;
            return generated;
        }

        private static Mesh UpsertMesh(Mesh generated, string path)
        {
            Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (asset == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            EditorUtility.CopySerialized(generated, asset);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void WriteManifest(SourceAudit audit)
        {
            var manifest = new Manifest
            {
                sourceGuid = audit.sourceAssetGuid,
                sourceSha256 = audit.sourceSha256,
                sourceMesh = audit.sourceMeshName,
                sourceVertices = audit.sourceVertexCount,
                sourceTriangles = audit.sourceTriangleCount,
                connectedComponents = audit.connectedComponentCount,
                geometryDerivedRoundApertures = audit.apertureCount,
                neutralizedTriangles = audit.neutralizedTriangleCount,
                dynamicTriangles = audit.dynamicTriangleCount,
                supported = Definitions.Select(definition =>
                    new ManifestGauge
                    {
                        label = definition.Label,
                        function = definition.Function.ToString(),
                        authority = definition.Authority,
                        calibration = definition.Calibration,
                        faceTriangleSeed = definition.FaceSeed,
                        dynamicTriangleSeeds = definition.DynamicSeeds,
                        sourceTriangles =
                            definition.ExpectedDynamicSeedTriangleCounts.Sum()
                    }).ToArray(),
                exclusions = Exclusions
            };
            string absolute = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", ManifestPath));
            File.WriteAllText(absolute, JsonUtility.ToJson(manifest, true) + "\n");
        }

        private static string Sha256(string assetPath)
        {
            string absolute = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", assetPath));
            using (FileStream stream = File.OpenRead(absolute))
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
        }

        private static ManifestExclusion Exclusion(
            string item,
            int[] faceTriangleSeeds,
            string reason)
        {
            return new ManifestExclusion
            {
                item = item,
                faceTriangleSeeds = faceTriangleSeeds,
                reason = reason
            };
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
