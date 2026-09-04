using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Aircraft.CockpitDynamics.Analog;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Compiles every positively identified Meter_01 presentation into one
    /// opaque mesh/material. The frozen source remains the truth for IAS/VSI.
    /// Complete project-authored faces are overlaid only where face identity
    /// and authoritative state are proven; unidentified fantasy apertures are
    /// deliberately left static.
    /// </summary>
    public static class CockpitInstrumentV2Authoring
    {
        public const string LayoutPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/C172_InstrumentV2Layout.asset";
        public const string CombinedMeshPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Meshes/C172_InstrumentV2Combined.asset";
        public const string NeutralizedSourceMeshPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Meshes/C172_InstrumentV2Neutralized.asset";
        public const string AtlasPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Textures/C172_InstrumentV2Atlas.png";
        public const string MaterialPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Materials/ProductionCockpitInstrumentV2.mat";
        public const string ShaderPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Shaders/ProductionCockpitInstrumentV2.shader";
        public const string PresentationManifestPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/C172_InstrumentV2PresentationManifest.json";

        private const int AtlasWidth = 1024;
        private const int AtlasHeight = 512;
        private const int TileSize = 256;
        private const int TileColumns = 4;
        private const int SegmentCount = 48;
        // PhotorealAerialReflectionAuthoring places the physical instrument
        // lens 0.1 mm in front of each audited aperture in aircraft space.
        // Every generated face/card/pointer must remain strictly behind that
        // plane. The former millimetre-class offsets put the V2 overlays in
        // front of the lens, so their opaque depth prevented the later glass
        // pass from compositing over them.
        private const float InstrumentLensOffset = 0.0001f;
        private const float FaceOffset = 0.000001f;
        private const float CardOffset = 0.000001f;
        private const float PointerOffset = 0.000001f;
        private const float LayerDepthBias = 0.00004f;

        private const int AltimeterTile = 0;
        private const int DirectionTile = 1;
        private const int TachometerTile = 2;
        private const int FlapTile = 3;
        private const int BrakeTile = 4;
        private const int TurnCoordinatorTile = 5;
        private const int WhiteTile = 6;
        private const int BlackTile = 7;
        private const int PrincipalAltimeterApertureSeed = 4593;
        private const int TurnCoordinatorFaceSeed = 2667;
        private const int TachometerFaceSeed = 2621;

        private static readonly int[] AltimeterCandidates =
            { 2667, 2621, 2713 };

        private sealed class Component
        {
            public int Seed;
            public readonly List<int> Triangles = new List<int>();
            public readonly HashSet<int> Vertices = new HashSet<int>();
        }

        private readonly struct FaceFrame
        {
            public readonly int Seed;
            public readonly Vector3 Center;
            public readonly Vector3 Normal;
            public readonly Vector3 Right;
            public readonly Vector3 Up;
            public readonly float Radius;

            public FaceFrame(
                int seed,
                Vector3 center,
                Vector3 normal,
                Vector3 right,
                Vector3 up,
                float radius)
            {
                Seed = seed;
                Center = center;
                Normal = normal;
                Right = right;
                Up = up;
                Radius = radius;
            }
        }

        private readonly struct SphereFit
        {
            public readonly Vector3 Center;
            public readonly float Radius;
            public readonly float Rms;
            public readonly float MaximumError;

            public SphereFit(
                Vector3 center,
                float radius,
                float rms,
                float maximumError)
            {
                Center = center;
                Radius = radius;
                Rms = rms;
                MaximumError = maximumError;
            }
        }

        [Serializable]
        private sealed class Manifest
        {
            public string schema =
                "questflight.cockpit.instrument_presentation.v2";
            public string authority = "ProductionCockpitSystemsController";
            public string source_guid =
                CockpitInstrumentV2EditorContract.ExpectedSourceGuid;
            public string source_sha256 =
                CockpitInstrumentV2EditorContract.ExpectedSourceSha256;
            public string renderer_contract =
                "One combined opaque renderer and material; original Meter_01 " +
                "texture plus one ASTC project-authored face atlas; no per-gauge objects.";
            public int selected_principal_altimeter_seed;
            public ActiveGauge[] active = Array.Empty<ActiveGauge>();
            public Blocker[] blocked = Array.Empty<Blocker>();
        }

        [Serializable]
        private sealed class ActiveGauge
        {
            public string id = string.Empty;
            public int[] source_face_seeds = Array.Empty<int>();
            public string[] functions = Array.Empty<string>();
            public string authority = string.Empty;
            public string calibration = string.Empty;
        }

        [Serializable]
        private sealed class Blocker
        {
            public string id = string.Empty;
            public string source_identity = string.Empty;
            public string blocker = string.Empty;
            public string policy =
                "Preserve authored static presentation; never synthesize an identity.";
        }

        [MenuItem("QuestFlight/Cockpit/Build Batched InstrumentV2 Layout")]
        public static void BuildAssetsOnly()
        {
            ValidatePhysicalLensClearance();
            ProductionAnalogInstrumentLayout source =
                AssetDatabase.LoadAssetAtPath<ProductionAnalogInstrumentLayout>(
                    CockpitAnalogInstrumentAuthoring.LayoutAssetPath);
            Require(source != null, "The audited analog source layout is missing.");
            Require(source.Validate(out string sourceError), sourceError);
            Require(source.Bindings.Length == 2,
                "Only two frozen source pointers are identity/calibration proven.");

            Mesh frozen = AssetDatabase.LoadAllAssetsAtPath(
                    CockpitInstrumentV2EditorContract.SourceGlbPath)
                .OfType<Mesh>()
                .Single(mesh =>
                    mesh.name == CockpitInstrumentV2EditorContract.SourceMeshName);
            Require(frozen.vertexCount ==
                    CockpitAnalogInstrumentAuthoring.ExpectedSourceVertexCount &&
                    frozen.triangles.Length / 3 ==
                    CockpitAnalogInstrumentAuthoring.ExpectedSourceTriangleCount,
                "The frozen Meter_01 topology changed.");

            Dictionary<int, Component> components = BuildComponents(frozen);
            Vector3 dialRight = source.Bindings[0].dialRightLocal.normalized;
            Vector3 dialUp = source.Bindings[0].dialUpLocal.normalized;
            Vector3 dialNormal =
                source.Bindings[0].rotationAxisLocal.normalized;
            Component[] attitudeComponents =
                CockpitInstrumentV2EditorContract
                    .AttitudeSphereComponentSeeds
                    .Select(seed =>
                    {
                        Require(components.ContainsKey(seed),
                            $"Frozen attitude sphere component {seed} is missing.");
                        return components[seed];
                    })
                    .ToArray();
            var attitudeVertices = new HashSet<int>(
                attitudeComponents.SelectMany(item => item.Vertices));
            int attitudeTriangleCount = attitudeComponents.Sum(
                item => item.Triangles.Count);
            Require(attitudeVertices.Count ==
                    CockpitInstrumentV2EditorContract
                        .ExpectedAttitudeSphereVertexCount &&
                    attitudeTriangleCount ==
                    CockpitInstrumentV2EditorContract
                        .ExpectedAttitudeSphereTriangleCount,
                "Frozen attitude sphere topology changed.");
            SphereFit attitudeSphere = FitSphere(
                frozen.vertices,
                attitudeVertices);
            Require(attitudeSphere.Rms <=
                    CockpitInstrumentV2EditorContract
                        .MaximumAttitudeSphereFitRms &&
                    attitudeSphere.MaximumError <= 0.0001f,
                "Frozen attitude sphere no longer fits its audited gyro ball.");
            int principalAltimeterSeed = SelectPrincipalAltimeter(
                frozen, components, dialRight, dialUp, dialNormal);

            Texture2D atlas = BuildAtlas();
            Material material = UpsertMaterial(
                source.SharedSourceMeterMaterial, atlas);

            var vertices = new List<Vector3>(2048);
            var normals = new List<Vector3>(2048);
            var tangents = new List<Vector4>(2048);
            var uv = new List<Vector2>(2048);
            var selector = new List<Vector2>(2048);
            var indices = new List<int>(3072);
            var bindings = new List<CockpitInstrumentV2MeshBinding>(16);
            FaceFrame altimeter = Frame(
                frozen, components[principalAltimeterSeed],
                dialRight, dialUp, dialNormal);
            FaceFrame turnCoordinator = Frame(
                frozen, components[TurnCoordinatorFaceSeed],
                dialRight, dialUp, dialNormal);
            FaceFrame tachometer = Frame(
                frozen, components[TachometerFaceSeed],
                dialRight, dialUp, dialNormal);
            FaceFrame dg = Frame(
                frozen, components[2891], dialRight, dialUp, dialNormal);
            Component magneticCompassCard = components[
                CockpitInstrumentV2EditorContract.MagneticCompassCardSeed];
            Component magneticCompassAuxiliary = components[
                CockpitInstrumentV2EditorContract.MagneticCompassAuxiliaryCardSeed];
            Require(magneticCompassCard.Vertices.Count ==
                    CockpitInstrumentV2EditorContract
                        .ExpectedMagneticCompassCardVertexCount &&
                    magneticCompassCard.Triangles.Count ==
                    CockpitInstrumentV2EditorContract
                        .ExpectedMagneticCompassCardTriangleCount,
                "The frozen magnetic-compass card topology changed.");
            Require(magneticCompassAuxiliary.Vertices.Count ==
                    CockpitInstrumentV2EditorContract
                        .ExpectedMagneticCompassAuxiliaryVertexCount &&
                    magneticCompassAuxiliary.Triangles.Count ==
                    CockpitInstrumentV2EditorContract
                        .ExpectedMagneticCompassAuxiliaryTriangleCount,
                "The frozen magnetic-compass auxiliary island topology changed.");
            FaceFrame flap = Frame(
                frozen, components[2759], dialRight, dialUp, dialNormal);
            FaceFrame brake = Frame(
                frozen, components[2906], dialRight, dialUp, dialNormal);

            Component[] replacementOwnedComponents =
                CollectReplacementOwnedComponents(
                    frozen,
                    components,
                    new[]
                    {
                        altimeter, turnCoordinator, tachometer,
                        dg, flap, brake
                    },
                    dialRight,
                    dialUp,
                    dialNormal)
                .Concat(new[] { magneticCompassCard, magneticCompassAuxiliary })
                .Distinct()
                .OrderBy(item => item.Seed)
                .ToArray();

            // A complete opaque disc is required here. The frozen ALT faces
            // contain literal holes for static odometer meshes; copying those
            // shells after neutralization exposed the outside world through
            // the panel on-device.
            AddSealedGaugeBackplate(altimeter,
                vertices, normals, tangents, uv, selector, indices);
            AddDiscScaled(altimeter, AltimeterTile, 0f, 1.02f,
                vertices, normals, tangents, uv, selector, indices);
            Component altitudeAperture = components[
                PrincipalAltimeterApertureSeed];
            Require(IsChildOfFace(
                    frozen, altitudeAperture, altimeter,
                    dialRight, dialUp),
                "Frozen principal-altimeter odometer aperture 4593 moved.");
            Rect altitudeApertureUv = CalculateAltitudeApertureUv(
                frozen, altitudeAperture, altimeter);
            material.SetVector(
                "_AltitudeApertureUv",
                new Vector4(
                    altitudeApertureUv.xMin,
                    altitudeApertureUv.yMin,
                    altitudeApertureUv.xMax,
                    altitudeApertureUv.yMax));
            material.SetVector("_AltitudeDigits", new Vector4(5f, 2f, 8f, 0f));
            EditorUtility.SetDirty(material);

            AddSealedGaugeBackplate(tachometer,
                vertices, normals, tangents, uv, selector, indices);
            AddDiscScaled(tachometer, TachometerTile, 0f, 1.02f,
                vertices, normals, tangents, uv, selector, indices);
            AddSealedGaugeBackplate(turnCoordinator,
                vertices, normals, tangents, uv, selector, indices);
            AddDiscScaled(turnCoordinator, TurnCoordinatorTile, 0f, 1.02f,
                vertices, normals, tangents, uv, selector, indices);
            int flapFaceFirst = vertices.Count;
            AddSourceComponent(
                frozen, components[flap.Seed], false,
                vertices, normals, tangents, uv, selector, indices);
            for (int vertex = flapFaceFirst; vertex < vertices.Count; vertex++)
                selector[vertex] = new Vector2(0f, 1f);
            int brakeFaceFirst = vertices.Count;
            AddSourceComponent(
                frozen, components[brake.Seed], false,
                vertices, normals, tangents, uv, selector, indices);
            for (int vertex = brakeFaceFirst; vertex < vertices.Count; vertex++)
                selector[vertex] = new Vector2(0f, 1f);
            AddLubber(dg, vertices, normals, tangents, uv, selector, indices);
            int staticOverlayVertexCount = vertices.Count;

            AddFrozenBindings(
                source, vertices, normals, tangents, uv, selector, indices,
                bindings);

            AddSourceComponentBinding(
                frozen,
                components[dg.Seed],
                dg,
                CockpitInstrumentV2Function.DirectionalGyroHeadingDegrees,
                -1f,
                "directional_gyro_card",
                vertices, normals, tangents, uv, selector, indices, bindings);
            AddNeedleBinding(altimeter, 0.84f, 0.028f, 0f,
                CockpitInstrumentV2Function.AltimeterHundredsNeedleDegrees,
                0f, 1f, "altimeter_hundreds", bindings,
                vertices, normals, tangents, uv, selector, indices);
            AddNeedleBinding(altimeter, 0.66f, 0.038f, 0f,
                CockpitInstrumentV2Function.AltimeterThousandsNeedleDegrees,
                0f, 1f, "altimeter_thousands", bindings,
                vertices, normals, tangents, uv, selector, indices);
            AddNeedleBinding(altimeter, 0.48f, 0.05f, 0f,
                CockpitInstrumentV2Function.AltimeterTenThousandsNeedleDegrees,
                0f, 1f, "altimeter_ten_thousands", bindings,
                vertices, normals, tangents, uv, selector, indices);
            AddNeedleBinding(altimeter, 0.22f, 0.028f, 155f,
                CockpitInstrumentV2Function.AltimeterBarometricDrumInHg,
                29.92f, 45f, "altimeter_baro_setting", bindings,
                vertices, normals, tangents, uv, selector, indices);

            AddNeedleBinding(flap, 0.7f, 0.04f, -55f,
                CockpitInstrumentV2Function.ActualFlapDegrees,
                0f, 4f, "actual_flap_position", bindings,
                vertices, normals, tangents, uv, selector, indices);
            AddNeedleBinding(brake, 0.7f, 0.04f, -120f,
                CockpitInstrumentV2Function.AverageBrakePressurePsi,
                0f, 0.24f, "brake_pressure", bindings,
                vertices, normals, tangents, uv, selector, indices);
            AddNeedleBinding(tachometer, 0.76f, 0.035f, -150f,
                CockpitInstrumentV2Function.EngineRpmNeedleDegrees,
                0f, 1f, "engine_rpm", bindings,
                vertices, normals, tangents, uv, selector, indices);
            AddTurnCoordinatorBindings(
                turnCoordinator, bindings,
                vertices, normals, tangents, uv, selector, indices);

            Mesh combined = UpsertMesh(
                BuildMesh(
                    frozen.bounds, vertices, normals, tangents, uv,
                    selector, indices));
            Mesh neutralizedSource =
                UpsertNeutralizedSourceMesh(
                    source.NeutralizedMeterMesh,
                    replacementOwnedComponents,
                    attitudeVertices);
            CockpitInstrumentV2Layout layout =
                AssetDatabase.LoadAssetAtPath<CockpitInstrumentV2Layout>(
                    LayoutPath);
            if (layout == null)
            {
                layout =
                    ScriptableObject.CreateInstance<CockpitInstrumentV2Layout>();
                AssetDatabase.CreateAsset(layout, LayoutPath);
            }
            layout.ConfigureGenerated(
                neutralizedSource,
                combined,
                material,
                bindings.ToArray(),
                source.SourceTriangleCount,
                neutralizedSource.triangles.Length / 3,
                indices.Count / 3,
                CockpitInstrumentV2EditorContract.ExpectedSourceGuid,
                CockpitInstrumentV2EditorContract.ExpectedSourceSha256,
                staticOverlayVertexCount,
                true,
                Array.Empty<CockpitInstrumentV2AltitudeDigitSlot>(),
                Array.Empty<Rect>());
            layout.ConfigureAttitudeContract(
                new CockpitInstrumentV2AttitudeContract
                {
                    enabled = true,
                    sourceComponentSeeds =
                        CockpitInstrumentV2EditorContract
                            .AttitudeSphereComponentSeeds.ToArray(),
                    sourceVertexCount = attitudeVertices.Count,
                    sourceTriangleCount = attitudeTriangleCount,
                    pivotLocal = attitudeSphere.Center,
                    pitchAxisLocal = dialRight,
                    bankAxisLocal = dialNormal,
                    sphereRadiusLocal = attitudeSphere.Radius,
                    sphereFitRmsLocal = attitudeSphere.Rms
                });
            EditorUtility.SetDirty(layout);
            WriteManifest(principalAltimeterSeed);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Require(layout.Validate(out string error), error);
            Debug.Log(
                "[QuestFlight][CockpitInstrumentV2] Built one opaque batch: " +
                    $"{bindings.Count} bindings, {combined.vertexCount} vertices, " +
                    $"principal ALT seed {principalAltimeterSeed}; live tachometer, " +
                "turn/slip and the shader-driven attitude gyro sphere share " +
                "the existing two-renderer partition; the compass is isolated.");
        }

        public static void AuditGaugeTopologyBatch()
        {
            ProductionAnalogInstrumentLayout source =
                AssetDatabase.LoadAssetAtPath<ProductionAnalogInstrumentLayout>(
                    CockpitAnalogInstrumentAuthoring.LayoutAssetPath);
            Require(source != null, "The audited analog layout is missing.");
            Require(source.Validate(out string sourceError), sourceError);
            Texture sourceTexture = source.SharedSourceMeterMaterial.mainTexture;
            Require(sourceTexture != null,
                "The audited Meter_01 base-color texture is missing.");
            Debug.Log(
                $"[QuestFlight][GaugeTopology] SOURCE_TEXTURE " +
                $"name={sourceTexture.name} size={sourceTexture.width}x" +
                $"{sourceTexture.height}");
            Mesh frozen = AssetDatabase.LoadAllAssetsAtPath(
                    CockpitInstrumentV2EditorContract.SourceGlbPath)
                .OfType<Mesh>()
                .Single(mesh =>
                    mesh.name == CockpitInstrumentV2EditorContract.SourceMeshName);
            Dictionary<int, Component> components = BuildComponents(frozen);
            Vector3 right = source.Bindings[0].dialRightLocal.normalized;
            Vector3 up = source.Bindings[0].dialUpLocal.normalized;
            Vector3 normal = source.Bindings[0].rotationAxisLocal.normalized;
            int altimeter = SelectPrincipalAltimeter(
                frozen, components, right, up, normal);
            GameObject product = PrefabUtility.LoadPrefabContents(
                CockpitDynamicsIntegrationAuthoring.AircraftPrefabPath);
            try
            {
                Transform meter = product.transform.Find(
                    CockpitAnalogInstrumentAuthoring.MeterRendererPath);
                Camera camera = product.GetComponentInChildren<Camera>(true);
                Require(meter != null && camera != null,
                    "The product meter/camera projection contract is missing.");
                foreach (int candidateSeed in AltimeterCandidates)
                {
                    FaceFrame candidateFrame = Frame(
                        frozen, components[candidateSeed], right, up, normal);
                    Vector3 viewport = camera.WorldToViewportPoint(
                        meter.TransformPoint(candidateFrame.Center));
                    Debug.Log(
                        $"[QuestFlight][GaugeTopology] ALT_CANDIDATE " +
                        $"seed={candidateSeed} center={candidateFrame.Center:F7} " +
                        $"right={Vector3.Dot(candidateFrame.Center, right):F7} " +
                        $"up={Vector3.Dot(candidateFrame.Center, up):F7} " +
                        $"viewport={viewport:F5} " +
                        $"selected={candidateSeed == altimeter}");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(product);
            }
            int[] targets = AltimeterCandidates.Concat(
                    new[] { 2819, 2541, 2891, 2759, 2906 })
                .Distinct().ToArray();
            foreach (int seed in targets)
            {
                FaceFrame target = Frame(
                    frozen, components[seed], right, up, normal);
                Debug.Log(
                    $"[QuestFlight][GaugeTopology] TARGET seed={seed} " +
                    $"triangles={components[seed].Triangles.Count} " +
                    $"vertices={components[seed].Vertices.Count} " +
                    $"center={target.Center:F7} radius={target.Radius:F7}");
                foreach (Component candidate in components.Values)
                {
                    if (candidate.Seed == seed) continue;
                    FaceFrame frame = Frame(
                        frozen, candidate, right, up, normal);
                    Vector3 delta = frame.Center - target.Center;
                    float planar = Mathf.Sqrt(
                        Mathf.Pow(Vector3.Dot(delta, right), 2f) +
                        Mathf.Pow(Vector3.Dot(delta, up), 2f));
                    float depth = Mathf.Abs(Vector3.Dot(delta, target.Normal));
                    if (planar <= target.Radius * 1.05f &&
                        depth <= target.Radius * 0.35f &&
                        frame.Radius <= target.Radius * 0.85f)
                    {
                        Debug.Log(
                            $"[QuestFlight][GaugeTopology] CHILD target={seed} " +
                            $"seed={candidate.Seed} triangles={candidate.Triangles.Count} " +
                            $"vertices={candidate.Vertices.Count} " +
                            $"planar={planar:F7} depth={depth:F7} " +
                            $"radius={frame.Radius:F7} " +
                            $"center={frame.Center:F7}");
                    }
                }
            }
        }

        private static void ValidatePhysicalLensClearance()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CockpitDynamicsIntegrationAuthoring.AircraftPrefabPath);
            Require(prefab != null,
                "The product aircraft prefab is required to validate lens clearance.");
            Transform meter = prefab.transform.Find(
                CockpitAnalogInstrumentAuthoring.MeterRendererPath);
            Require(meter != null,
                "The product meter transform is required to validate lens clearance.");
            Vector3 scale = meter.lossyScale;
            float maximumScale = Mathf.Max(
                Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            float maximumDynamicOffsetAircraftSpace =
                (FaceOffset + PointerOffset + CardOffset * 2f) * maximumScale;
            Require(
                maximumDynamicOffsetAircraftSpace < InstrumentLensOffset * 0.5f,
                "Generated InstrumentV2 layers do not retain the required " +
                "aircraft-space clearance behind the physical lens.");
        }

        private static void AddFrozenBindings(
            ProductionAnalogInstrumentLayout source,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices,
            List<CockpitInstrumentV2MeshBinding> result)
        {
            Mesh mesh = source.DynamicInstrumentMesh;
            Vector3[] sourceVertices = mesh.vertices;
            Vector3[] sourceNormals = mesh.normals;
            Vector4[] sourceTangents = mesh.tangents;
            Vector2[] sourceUv = mesh.uv;
            foreach (AnalogInstrumentMeshBinding authored in source.Bindings)
            {
                CockpitInstrumentV2Function function;
                switch (authored.function)
                {
                    case AnalogInstrumentFunction.IndicatedAirspeedKnots:
                        function = CockpitInstrumentV2Function
                            .IndicatedAirspeedNeedleDegrees;
                        break;
                    case AnalogInstrumentFunction
                        .IndicatedVerticalSpeedFeetPerMinute:
                        function = CockpitInstrumentV2Function
                            .IndicatedVerticalSpeedNeedleDegrees;
                        break;
                    default:
                        throw new InvalidOperationException(
                            "An unproven frozen pointer entered InstrumentV2.");
                }
                int first = vertices.Count;
                for (int offset = 0; offset < authored.vertexCount; offset++)
                {
                    int sourceVertex = authored.firstVertex + offset;
                    indices.Add(vertices.Count);
                    vertices.Add(sourceVertices[sourceVertex]);
                    normals.Add(sourceNormals[sourceVertex]);
                    tangents.Add(sourceTangents[sourceVertex]);
                    uv.Add(sourceUv[sourceVertex]);
                    selector.Add(new Vector2(0f, 2f));
                }
                float authoredAngle = Mathf.Atan2(
                    Vector3.Dot(
                        authored.authoredIndicatorDirectionLocal,
                        authored.dialUpLocal),
                    Vector3.Dot(
                        authored.authoredIndicatorDirectionLocal,
                        authored.dialRightLocal)) * Mathf.Rad2Deg;
                result.Add(new CockpitInstrumentV2MeshBinding
                {
                    label = authored.label,
                    sourceIdentity =
                        "Meter_01 face seed " +
                        authored.sourceFaceTriangleSeed,
                    sourceTriangleSeed = authored.sourceFaceTriangleSeed,
                    firstVertex = first,
                    vertexCount = vertices.Count - first,
                    pivotLocal = authored.pivotLocal,
                    rotationAxisLocal = authored.rotationAxisLocal,
                    rotationFunction = function,
                    authoredRotationValue = authoredAngle,
                    rotationDegreesPerUnit = 1f,
                    translationAxisLocal = Vector3.zero,
                    translationFunction = CockpitInstrumentV2Function.None,
                    absoluteRegistrationProven = true,
                    magneticVariationContractProven = false
                });
            }
        }

        private static Component[] CollectReplacementOwnedComponents(
            Mesh mesh,
            Dictionary<int, Component> components,
            FaceFrame[] targets,
            Vector3 right,
            Vector3 up,
            Vector3 normal)
        {
            Vector3[] positions = mesh.vertices;
            var owned = new HashSet<Component>();
            foreach (FaceFrame target in targets)
            {
                Component face = components[target.Seed];
                owned.Add(face);
                foreach (Component candidate in components.Values)
                {
                    if (candidate == face) continue;
                    float maximumPlanarRadius = 0f;
                    float maximumDepth = 0f;
                    foreach (int vertex in candidate.Vertices)
                    {
                        Vector3 delta = positions[vertex] - target.Center;
                        float horizontal = Vector3.Dot(delta, right);
                        float vertical = Vector3.Dot(delta, up);
                        maximumPlanarRadius = Mathf.Max(
                            maximumPlanarRadius,
                            Mathf.Sqrt(horizontal * horizontal +
                                       vertical * vertical));
                        maximumDepth = Mathf.Max(
                            maximumDepth,
                            Mathf.Abs(Vector3.Dot(delta, normal)));
                    }

                    // Retain the enclosure/bezel: only components completely
                    // contained by the audited face boundary and within its
                    // shallow instrument cavity belong to the replaced static
                    // indication. This captures pointers, hubs, drums, and
                    // separately modeled marks without touching the housing.
                    if (maximumPlanarRadius <= target.Radius * 0.96f &&
                        maximumDepth <= Mathf.Max(
                            target.Radius * 0.15f, 0.0006f))
                    {
                        owned.Add(candidate);
                    }
                }
            }
            Require(owned.Count >= targets.Length,
                "The replacement ownership audit selected no source components.");
            return owned.OrderBy(item => item.Seed).ToArray();
        }

        private static void AddSourceComponent(
            Mesh source,
            Component component,
            bool presentation,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices)
        {
            int[] sourceTriangles = source.triangles;
            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector4[] sourceTangents = source.tangents;
            Vector2[] sourceUv = source.uv;
            Require(
                sourceNormals.Length == sourceVertices.Length &&
                sourceTangents.Length == sourceVertices.Length &&
                sourceUv.Length == sourceVertices.Length,
                "The frozen source lacks complete presentation channels.");
            foreach (int triangle in component.Triangles)
            for (int corner = 0; corner < 3; corner++)
            {
                int sourceVertex = sourceTriangles[triangle * 3 + corner];
                indices.Add(vertices.Count);
                vertices.Add(sourceVertices[sourceVertex]);
                normals.Add(sourceNormals[sourceVertex]);
                tangents.Add(sourceTangents[sourceVertex]);
                uv.Add(sourceUv[sourceVertex]);
                selector.Add(presentation
                    ? new Vector2(1f, 1f)
                    : Vector2.zero);
            }
        }

        private static void AddSourceComponentBinding(
            Mesh source,
            Component component,
            FaceFrame frame,
            CockpitInstrumentV2Function function,
            float degreesPerUnit,
            string label,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices,
            List<CockpitInstrumentV2MeshBinding> bindings)
        {
            int first = vertices.Count;
            AddSourceComponent(
                source, component, false,
                vertices, normals, tangents, uv, selector, indices);
            for (int vertex = first; vertex < vertices.Count; vertex++)
                selector[vertex] = new Vector2(0f, 1f);
            bindings.Add(Binding(
                label,
                frame.Seed,
                first,
                vertices.Count - first,
                frame.Center,
                frame.Normal,
                function,
                0f,
                degreesPerUnit,
                true,
                false));
        }

        private static void AddMagneticCompassBinding(
            Mesh source,
            Component component,
            Vector3 pivot,
            Vector3 rotationAxis,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices,
            List<CockpitInstrumentV2MeshBinding> bindings)
        {
            int first = vertices.Count;
            AddSourceComponent(
                source, component, false,
                vertices, normals, tangents, uv, selector, indices);
            // The rotating compass card is face artwork, not an unclassified
            // source layer. Keep it on the existing explicit face-depth layer
            // so every generated triangle has deterministic mobile depth.
            for (int vertex = first; vertex < vertices.Count; vertex++)
                selector[vertex] = new Vector2(0f, 1f);
            bindings.Add(Binding(
                "magnetic_compass_card",
                component.Seed,
                first,
                vertices.Count - first,
                pivot,
                rotationAxis,
                CockpitInstrumentV2Function.MagneticCompassHeadingDegrees,
                CockpitInstrumentV2EditorContract
                    .MagneticCompassAuthoredHeadingDegrees,
                -1f,
                true,
                true));
        }

        private static void AddMappedSourceComponent(
            Mesh source,
            Component component,
            FaceFrame frame,
            int tile,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices)
        {
            int[] sourceTriangles = source.triangles;
            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector4[] sourceTangents = source.tangents;
            Require(
                sourceNormals.Length == sourceVertices.Length &&
                sourceTangents.Length == sourceVertices.Length,
                "The frozen source lacks complete mapped presentation channels.");

            float minRight = float.PositiveInfinity;
            float maxRight = float.NegativeInfinity;
            float minUp = float.PositiveInfinity;
            float maxUp = float.NegativeInfinity;
            foreach (int vertex in component.Vertices)
            {
                Vector3 delta = sourceVertices[vertex] - frame.Center;
                float rightValue = Vector3.Dot(delta, frame.Right);
                float upValue = Vector3.Dot(delta, frame.Up);
                minRight = Mathf.Min(minRight, rightValue);
                maxRight = Mathf.Max(maxRight, rightValue);
                minUp = Mathf.Min(minUp, upValue);
                maxUp = Mathf.Max(maxUp, upValue);
            }
            Require(maxRight - minRight > 0.000001f &&
                    maxUp - minUp > 0.000001f,
                "A mapped source aperture has a degenerate projection.");

            Rect tileRect = TileRect(tile);
            foreach (int triangle in component.Triangles)
            for (int corner = 0; corner < 3; corner++)
            {
                int sourceVertex = sourceTriangles[triangle * 3 + corner];
                Vector3 delta = sourceVertices[sourceVertex] - frame.Center;
                float normalizedRight = Mathf.InverseLerp(
                    minRight, maxRight, Vector3.Dot(delta, frame.Right));
                float normalizedUp = Mathf.InverseLerp(
                    minUp, maxUp, Vector3.Dot(delta, frame.Up));
                indices.Add(vertices.Count);
                vertices.Add(sourceVertices[sourceVertex] +
                             frame.Normal * FaceOffset);
                normals.Add(sourceNormals[sourceVertex]);
                tangents.Add(sourceTangents[sourceVertex]);
                uv.Add(new Vector2(
                    Mathf.Lerp(tileRect.xMin, tileRect.xMax, normalizedRight),
                    Mathf.Lerp(tileRect.yMin, tileRect.yMax, normalizedUp)));
                selector.Add(new Vector2(1f, 1f));
            }
        }

        private static void AddDiscBinding(
            FaceFrame frame,
            int tile,
            float offset,
            CockpitInstrumentV2Function function,
            float scale,
            bool absolute,
            bool magnetic,
            string label,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices,
            List<CockpitInstrumentV2MeshBinding> bindings)
        {
            int first = vertices.Count;
            AddDisc(frame, tile, offset,
                vertices, normals, tangents, uv, selector, indices);
            bindings.Add(Binding(
                label, frame.Seed, first, vertices.Count - first,
                frame.Center, frame.Normal, function, 0f, scale,
                absolute, magnetic));
        }

        private static void AddNeedleBinding(
            FaceFrame frame,
            float lengthScale,
            float widthScale,
            float baseAngle,
            CockpitInstrumentV2Function function,
            float authoredValue,
            float scale,
            string label,
            List<CockpitInstrumentV2MeshBinding> bindings,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices)
        {
            int first = vertices.Count;
            AddNeedle(
                frame, lengthScale, widthScale, baseAngle,
                vertices, normals, tangents, uv, selector, indices);
            for (int vertex = first; vertex < vertices.Count; vertex++)
                selector[vertex] = new Vector2(1f, 2f);
            bindings.Add(Binding(
                label, frame.Seed, first, vertices.Count - first,
                frame.Center, frame.Normal, function, authoredValue, scale,
                true, false));
        }

        private static void AddTurnCoordinatorBindings(
            FaceFrame frame,
            List<CockpitInstrumentV2MeshBinding> bindings,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices)
        {
            Vector3 center = frame.Center +
                frame.Normal * (PointerOffset + CardOffset);
            Rect white = TileRect(WhiteTile);

            int aircraftFirst = vertices.Count;
            float wingHalf = frame.Radius * 0.48f;
            float wingHalfHeight = frame.Radius * 0.045f;
            AddPresentationQuad(
                center - frame.Right * wingHalf - frame.Up * wingHalfHeight,
                center + frame.Right * wingHalf - frame.Up * wingHalfHeight,
                center + frame.Right * wingHalf + frame.Up * wingHalfHeight,
                center - frame.Right * wingHalf + frame.Up * wingHalfHeight,
                frame.Normal, frame.Right, white,
                vertices, normals, tangents, uv, selector, indices);
            float bodyHalfWidth = frame.Radius * 0.055f;
            float bodyHalfHeight = frame.Radius * 0.26f;
            AddPresentationQuad(
                center - frame.Right * bodyHalfWidth - frame.Up * bodyHalfHeight,
                center + frame.Right * bodyHalfWidth - frame.Up * bodyHalfHeight,
                center + frame.Right * bodyHalfWidth + frame.Up * bodyHalfHeight,
                center - frame.Right * bodyHalfWidth + frame.Up * bodyHalfHeight,
                frame.Normal, frame.Right, white,
                vertices, normals, tangents, uv, selector, indices);
            for (int vertex = aircraftFirst; vertex < vertices.Count; vertex++)
                selector[vertex] = new Vector2(1f, 2f);
            bindings.Add(Binding(
                "turn_coordinator_aircraft",
                frame.Seed,
                aircraftFirst,
                vertices.Count - aircraftFirst,
                frame.Center,
                frame.Normal,
                CockpitInstrumentV2Function.TurnCoordinatorBankDegrees,
                0f,
                1f,
                true,
                false));

            int ballFirst = vertices.Count;
            Vector3 ballCenter = center - frame.Up * (frame.Radius * 0.58f);
            float ballRadius = frame.Radius * 0.075f;
            Vector2 black = TileRect(BlackTile).center;
            const int ballSegments = 18;
            for (int segment = 0; segment < ballSegments; segment++)
            {
                float a0 = segment * Mathf.PI * 2f / ballSegments;
                float a1 = (segment + 1) * Mathf.PI * 2f / ballSegments;
                Vector3 p0 = ballCenter +
                    frame.Right * (Mathf.Cos(a0) * ballRadius) +
                    frame.Up * (Mathf.Sin(a0) * ballRadius);
                Vector3 p1 = ballCenter +
                    frame.Right * (Mathf.Cos(a1) * ballRadius) +
                    frame.Up * (Mathf.Sin(a1) * ballRadius);
                AddTriangle(
                    ballCenter, p0, p1,
                    frame.Normal, frame.Right,
                    black, black, black, true,
                    vertices, normals, tangents, uv, selector, indices);
            }
            for (int vertex = ballFirst; vertex < vertices.Count; vertex++)
                selector[vertex] = new Vector2(1f, 2f);
            bindings.Add(new CockpitInstrumentV2MeshBinding
            {
                label = "slip_skid_ball",
                sourceIdentity =
                    "Meter_01 positively identified face seed " + frame.Seed,
                sourceTriangleSeed = frame.Seed,
                firstVertex = ballFirst,
                vertexCount = vertices.Count - ballFirst,
                pivotLocal = frame.Center,
                rotationAxisLocal = Vector3.zero,
                rotationFunction = CockpitInstrumentV2Function.None,
                authoredRotationValue = 0f,
                rotationDegreesPerUnit = 0f,
                translationAxisLocal = frame.Right,
                translationFunction =
                    CockpitInstrumentV2Function.SlipSkidNormalized,
                authoredTranslationValue = 0f,
                translationMetersPerUnit = frame.Radius * 0.30f,
                absoluteRegistrationProven = true,
                magneticVariationContractProven = false
            });
        }

        private static bool IsChildOfFace(
            Mesh source,
            Component component,
            FaceFrame face,
            Vector3 right,
            Vector3 up)
        {
            FaceFrame child = Frame(source, component, right, up, face.Normal);
            Vector3 delta = child.Center - face.Center;
            float planar = Mathf.Sqrt(
                Mathf.Pow(Vector3.Dot(delta, face.Right), 2f) +
                Mathf.Pow(Vector3.Dot(delta, face.Up), 2f));
            float depth = Mathf.Abs(Vector3.Dot(delta, face.Normal));
            return component.Triangles.Count == 2 &&
                   component.Vertices.Count == 4 &&
                   planar <= face.Radius &&
                   depth <= face.Radius * 0.35f;
        }

        private static Rect CalculateAltitudeApertureUv(
            Mesh source,
            Component aperture,
            FaceFrame face)
        {
            Vector3[] positions = source.vertices;
            float minRight = float.PositiveInfinity;
            float maxRight = float.NegativeInfinity;
            float minUp = float.PositiveInfinity;
            float maxUp = float.NegativeInfinity;
            foreach (int vertex in aperture.Vertices)
            {
                Vector3 delta = positions[vertex] - face.Center;
                float r = Vector3.Dot(delta, face.Right);
                float u = Vector3.Dot(delta, face.Up);
                minRight = Mathf.Min(minRight, r);
                maxRight = Mathf.Max(maxRight, r);
                minUp = Mathf.Min(minUp, u);
                maxUp = Mathf.Max(maxUp, u);
            }
            float width = maxRight - minRight;
            float height = maxUp - minUp;
            Require(width > height * 1.5f &&
                    width < face.Radius * 1.9f &&
                    height < face.Radius,
                "Frozen ALT odometer aperture no longer has its audited bounds.");

            // Match the exact UV transform used by AddDiscScaled. The readout
            // is composited inside that single face in the shader, eliminating
            // the former backing/digit planes and their sub-pixel depth fight.
            float insetX = width * 0.04f;
            float insetY = height * 0.12f;
            Rect tile = TileRect(AltimeterTile);
            const float radiusScale = 1.02f;
            float uvScaleX = tile.width * 0.49f /
                             (face.Radius * radiusScale);
            float uvScaleY = tile.height * 0.49f /
                             (face.Radius * radiusScale);
            return Rect.MinMaxRect(
                tile.center.x + (minRight + insetX) * uvScaleX,
                tile.center.y + (minUp + insetY) * uvScaleY,
                tile.center.x + (maxRight - insetX) * uvScaleX,
                tile.center.y + (maxUp - insetY) * uvScaleY);
        }

        private static void AddPresentationQuad(
            Vector3 bottomLeft,
            Vector3 bottomRight,
            Vector3 topRight,
            Vector3 topLeft,
            Vector3 normal,
            Vector3 tangent,
            Rect rect,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices)
        {
            AddTriangle(
                bottomLeft, bottomRight, topRight, normal, tangent,
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax), true,
                vertices, normals, tangents, uv, selector, indices);
            AddTriangle(
                bottomLeft, topRight, topLeft, normal, tangent,
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMax, rect.yMax),
                new Vector2(rect.xMin, rect.yMax), true,
                vertices, normals, tangents, uv, selector, indices);
        }

        private static void AddSealedGaugeBackplate(
            FaceFrame frame,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices)
        {
            // Oversized and recessed behind the authored panel so the panel
            // masks its edge while the plate closes the entire stereo viewing
            // cone through the source model's hollow odometer cutouts.
            AddDiscScaled(
                frame, BlackTile, -frame.Radius * 0.32f, 1.30f,
                vertices, normals, tangents, uv, selector, indices);
        }

        private static CockpitInstrumentV2MeshBinding Binding(
            string label,
            int seed,
            int first,
            int count,
            Vector3 pivot,
            Vector3 axis,
            CockpitInstrumentV2Function function,
            float authoredValue,
            float scale,
            bool absolute,
            bool magnetic)
        {
            return new CockpitInstrumentV2MeshBinding
            {
                label = label,
                sourceIdentity = "Meter_01 positively identified face seed " + seed,
                sourceTriangleSeed = seed,
                firstVertex = first,
                vertexCount = count,
                pivotLocal = pivot,
                rotationAxisLocal = axis,
                rotationFunction = function,
                authoredRotationValue = authoredValue,
                rotationDegreesPerUnit = scale,
                translationAxisLocal = Vector3.zero,
                translationFunction = CockpitInstrumentV2Function.None,
                absoluteRegistrationProven = absolute,
                magneticVariationContractProven = magnetic
            };
        }

        private static void AddDisc(
            FaceFrame frame,
            int tile,
            float offset,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices)
        {
            AddDiscScaled(
                frame, tile, offset, 1f,
                vertices, normals, tangents, uv, selector, indices);
        }

        private static void AddDiscScaled(
            FaceFrame frame,
            int tile,
            float offset,
            float radiusScale,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices)
        {
            Vector3 center = frame.Center + frame.Normal * offset;
            Rect rect = TileRect(tile);
            float radius = frame.Radius * radiusScale;
            for (int segment = 0; segment < SegmentCount; segment++)
            {
                float a0 = segment * Mathf.PI * 2f / SegmentCount;
                float a1 = (segment + 1) * Mathf.PI * 2f / SegmentCount;
                Vector2 p0 = new Vector2(Mathf.Sin(a0), Mathf.Cos(a0));
                Vector2 p1 = new Vector2(Mathf.Sin(a1), Mathf.Cos(a1));
                AddTriangle(
                    center,
                    center + frame.Right * (p0.x * radius) +
                    frame.Up * (p0.y * radius),
                    center + frame.Right * (p1.x * radius) +
                    frame.Up * (p1.y * radius),
                    frame.Normal, frame.Right,
                    new Vector2(rect.center.x, rect.center.y),
                    new Vector2(
                        rect.center.x + p0.x * rect.width * 0.49f,
                        rect.center.y + p0.y * rect.height * 0.49f),
                    new Vector2(
                        rect.center.x + p1.x * rect.width * 0.49f,
                        rect.center.y + p1.y * rect.height * 0.49f),
                    true,
                    vertices, normals, tangents, uv, selector, indices);
            }
        }

        private static void AddNeedle(
            FaceFrame frame,
            float lengthScale,
            float widthScale,
            float baseAngle,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices)
        {
            Vector3 center =
                frame.Center + frame.Normal * (PointerOffset + CardOffset);
            Vector3 direction =
                Quaternion.AngleAxis(baseAngle, frame.Normal) * frame.Up;
            Vector3 side = Vector3.Cross(frame.Normal, direction).normalized;
            float width = frame.Radius * widthScale;
            Vector3 tip =
                center + direction * (frame.Radius * lengthScale);
            Vector3 tail = center - direction * (frame.Radius * 0.12f);
            Vector2 white = TileRect(WhiteTile).center;
            AddTriangle(
                tail - side * width, tip, tail + side * width,
                frame.Normal, frame.Right, white, white, white, true,
                vertices, normals, tangents, uv, selector, indices);
            AddTriangle(
                center - frame.Right * width * 1.8f,
                center + frame.Up * width * 1.8f,
                center + frame.Right * width * 1.8f,
                frame.Normal, frame.Right, white, white, white, true,
                vertices, normals, tangents, uv, selector, indices);
            AddTriangle(
                center - frame.Right * width * 1.8f,
                center - frame.Up * width * 1.8f,
                center + frame.Right * width * 1.8f,
                frame.Normal, frame.Right, white, white, white, true,
                vertices, normals, tangents, uv, selector, indices);
        }

        private static void AddLubber(
            FaceFrame frame,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices)
        {
            int first = vertices.Count;
            Vector3 center =
                frame.Center + frame.Normal * (PointerOffset + CardOffset * 2f);
            float width = frame.Radius * 0.1f;
            float top = frame.Radius * 0.94f;
            Vector2 white = TileRect(WhiteTile).center;
            AddTriangle(
                center + frame.Up * top,
                center + frame.Up * (top - frame.Radius * 0.18f) -
                frame.Right * width,
                center + frame.Up * (top - frame.Radius * 0.18f) +
                frame.Right * width,
                frame.Normal, frame.Right, white, white, white, true,
                vertices, normals, tangents, uv, selector, indices);
            for (int vertex = first; vertex < vertices.Count; vertex++)
                selector[vertex] = new Vector2(1f, 2f);
        }

        private static void AddTriangle(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 normal,
            Vector3 tangentDirection,
            Vector2 uvA,
            Vector2 uvB,
            Vector2 uvC,
            bool presentation,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
            {
                Vector3 swapPosition = b;
                b = c;
                c = swapPosition;
                Vector2 swapUv = uvB;
                uvB = uvC;
                uvC = swapUv;
            }
            Vector4 tangent = new Vector4(
                tangentDirection.x, tangentDirection.y,
                tangentDirection.z, 1f);
            Vector2 source = presentation
                ? new Vector2(1f, 1f)
                : Vector2.zero;
            int first = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            tangents.Add(tangent); tangents.Add(tangent); tangents.Add(tangent);
            uv.Add(uvA); uv.Add(uvB); uv.Add(uvC);
            selector.Add(source); selector.Add(source); selector.Add(source);
            indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
        }

        private static Rect TileRect(int tile)
        {
            int x = tile % TileColumns;
            int y = tile / TileColumns;
            return new Rect(
                (x + 0.01f) / TileColumns,
                (y + 0.01f) / 2f,
                0.98f / TileColumns,
                0.98f / 2f);
        }

        private static Mesh BuildMesh(
            Bounds bounds,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> selector,
            List<int> indices)
        {
            Require(vertices.Count < ushort.MaxValue,
                "InstrumentV2 mesh exceeds the UInt16 Quest contract.");
            var mesh = new Mesh
            {
                name = "C172_InstrumentV2Combined",
                indexFormat = IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(3, selector);
            mesh.SetTriangles(indices, 0, false);
            mesh.bounds = bounds;
            return mesh;
        }

        private static Mesh UpsertMesh(Mesh generated)
        {
            Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>(CombinedMeshPath);
            if (asset == null)
            {
                AssetDatabase.CreateAsset(generated, CombinedMeshPath);
                return generated;
            }
            EditorUtility.CopySerialized(generated, asset);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static Mesh UpsertNeutralizedSourceMesh(
            Mesh source,
            Component[] replacementOwnedComponents,
            HashSet<int> attitudeVertices)
        {
            Require(source != null, "The neutralized Meter_01 mesh is missing.");
            Require(attitudeVertices != null &&
                    attitudeVertices.Count ==
                    CockpitInstrumentV2EditorContract
                        .ExpectedAttitudeSphereVertexCount,
                "The attitude gyro-ball vertex mask is incomplete.");
            Mesh generated = UnityEngine.Object.Instantiate(source);
            generated.name = "C172_InstrumentV2Neutralized";
            var removeVertices = new HashSet<int>(
                replacementOwnedComponents.SelectMany(item => item.Vertices));
            int[] sourceTriangles = generated.triangles;
            var retainedTriangles = new List<int>(sourceTriangles.Length);
            int removedTriangleCount = 0;
            for (int triangle = 0;
                 triangle < sourceTriangles.Length / 3;
                 triangle++)
            {
                int a = sourceTriangles[triangle * 3];
                int b = sourceTriangles[triangle * 3 + 1];
                int c = sourceTriangles[triangle * 3 + 2];
                if (removeVertices.Contains(a) &&
                    removeVertices.Contains(b) &&
                    removeVertices.Contains(c))
                {
                    removedTriangleCount++;
                    continue;
                }
                retainedTriangles.Add(a);
                retainedTriangles.Add(b);
                retainedTriangles.Add(c);
            }
            Require(removedTriangleCount > 0,
                "No static target triangles were removed from Meter_01.");
            generated.SetTriangles(retainedTriangles, 0, false);
            generated.bounds = source.bounds;
            var selector = new List<Vector4>(generated.vertexCount);
            for (int vertex = 0; vertex < generated.vertexCount; vertex++)
                selector.Add(attitudeVertices.Contains(vertex)
                    ? new Vector4(0f, 0f, 1f, 0f)
                    : Vector4.zero);
            generated.SetUVs(3, selector);

            Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>(
                NeutralizedSourceMeshPath);
            if (asset == null)
            {
                AssetDatabase.CreateAsset(generated, NeutralizedSourceMeshPath);
                return generated;
            }
            EditorUtility.CopySerialized(generated, asset);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static SphereFit FitSphere(
            Vector3[] vertices,
            HashSet<int> selectedVertices)
        {
            Require(vertices != null && selectedVertices != null &&
                    selectedVertices.Count >= 4,
                "A sphere fit requires at least four frozen vertices.");

            // Solve 2*x*cx + 2*y*cy + 2*z*cz + k = |p|^2 using
            // pivoted Gaussian elimination. This derives the authored pivot
            // from frozen topology rather than guessing it from the aperture.
            var normal = new double[4, 4];
            var rhs = new double[4];
            foreach (int index in selectedVertices)
            {
                Vector3 point = vertices[index];
                double[] row =
                    { 2.0 * point.x, 2.0 * point.y, 2.0 * point.z, 1.0 };
                double value = point.sqrMagnitude;
                for (int column = 0; column < 4; column++)
                {
                    rhs[column] += row[column] * value;
                    for (int other = 0; other < 4; other++)
                        normal[column, other] += row[column] * row[other];
                }
            }

            double[] solution = SolveLinear4(normal, rhs);
            var center = new Vector3(
                (float)solution[0],
                (float)solution[1],
                (float)solution[2]);
            float radius = Mathf.Sqrt(Mathf.Max(
                0f,
                (float)solution[3] + center.sqrMagnitude));
            double squaredError = 0.0;
            float maximumError = 0f;
            foreach (int index in selectedVertices)
            {
                float error = Mathf.Abs(
                    Vector3.Distance(vertices[index], center) - radius);
                squaredError += error * error;
                maximumError = Mathf.Max(maximumError, error);
            }
            return new SphereFit(
                center,
                radius,
                (float)Math.Sqrt(squaredError / selectedVertices.Count),
                maximumError);
        }

        private static double[] SolveLinear4(double[,] matrix, double[] rhs)
        {
            var augmented = new double[4, 5];
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                    augmented[row, column] = matrix[row, column];
                augmented[row, 4] = rhs[row];
            }

            for (int pivot = 0; pivot < 4; pivot++)
            {
                int best = pivot;
                for (int row = pivot + 1; row < 4; row++)
                {
                    if (Math.Abs(augmented[row, pivot]) >
                        Math.Abs(augmented[best, pivot]))
                        best = row;
                }
                Require(Math.Abs(augmented[best, pivot]) > 1e-18,
                    "The frozen attitude sphere fit is singular.");
                if (best != pivot)
                {
                    for (int column = pivot; column < 5; column++)
                    {
                        double temporary = augmented[pivot, column];
                        augmented[pivot, column] = augmented[best, column];
                        augmented[best, column] = temporary;
                    }
                }
                double divisor = augmented[pivot, pivot];
                for (int column = pivot; column < 5; column++)
                    augmented[pivot, column] /= divisor;
                for (int row = 0; row < 4; row++)
                {
                    if (row == pivot) continue;
                    double scale = augmented[row, pivot];
                    for (int column = pivot; column < 5; column++)
                        augmented[row, column] -=
                            scale * augmented[pivot, column];
                }
            }

            return new[]
            {
                augmented[0, 4], augmented[1, 4],
                augmented[2, 4], augmented[3, 4]
            };
        }

        private static Dictionary<int, Component> BuildComponents(Mesh mesh)
        {
            int[] triangles = mesh.triangles;
            var adjacent = new List<int>[mesh.vertexCount];
            for (int triangle = 0; triangle < triangles.Length / 3; triangle++)
            for (int corner = 0; corner < 3; corner++)
            {
                int vertex = triangles[triangle * 3 + corner];
                if (adjacent[vertex] == null)
                    adjacent[vertex] = new List<int>();
                adjacent[vertex].Add(triangle);
            }
            var result = new Dictionary<int, Component>();
            var visited = new bool[triangles.Length / 3];
            var queue = new Queue<int>();
            for (int seed = 0; seed < visited.Length; seed++)
            {
                if (visited[seed]) continue;
                var component = new Component { Seed = seed };
                visited[seed] = true;
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    int triangle = queue.Dequeue();
                    component.Triangles.Add(triangle);
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = triangles[triangle * 3 + corner];
                        component.Vertices.Add(vertex);
                        foreach (int neighbor in adjacent[vertex])
                        {
                            if (visited[neighbor]) continue;
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }
                result.Add(seed, component);
            }
            return result;
        }

        private static Component ComponentContainingTriangle(
            Dictionary<int, Component> components,
            int triangle)
        {
            Component[] matches = components.Values
                .Where(component => component.Triangles.Contains(triangle))
                .ToArray();
            Require(matches.Length == 1,
                $"Triangle {triangle} is not owned by exactly one frozen component.");
            return matches[0];
        }

        private static int SelectPrincipalAltimeter(
            Mesh mesh,
            Dictionary<int, Component> components,
            Vector3 right,
            Vector3 up,
            Vector3 normal)
        {
            // The imported panel's local X axis does not match pilot-view
            // left/right. Select the real six-pack upper-right aperture in the
            // authored pilot camera projection, not in mirrored model space.
            // This excludes the off-panel fantasy ALT and the lower duplicate.
            GameObject product = PrefabUtility.LoadPrefabContents(
                CockpitDynamicsIntegrationAuthoring.AircraftPrefabPath);
            try
            {
                Transform meter = product.transform.Find(
                    CockpitAnalogInstrumentAuthoring.MeterRendererPath);
                Camera camera = product.GetComponentInChildren<Camera>(true);
                Require(meter != null && camera != null,
                    "The product meter/camera projection contract is missing.");
                var candidates = AltimeterCandidates.Select(seed =>
                {
                    FaceFrame frame = Frame(
                        mesh, components[seed], right, up, normal);
                    Vector3 viewport = camera.WorldToViewportPoint(
                        meter.TransformPoint(frame.Center));
                    return new { frame, viewport };
                }).Where(item => item.viewport.z > 0f &&
                                 item.viewport.x >= 0.45f &&
                                 item.viewport.x <= 0.9f)
                  .OrderByDescending(item => item.viewport.y)
                  .ThenBy(item => Mathf.Abs(item.viewport.x - 0.65f))
                  .ToArray();
                Require(candidates.Length > 0,
                    "No ALT candidate occupies the authored six-pack pilot view.");
                return candidates[0].frame.Seed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(product);
            }
        }

        private static FaceFrame Frame(
            Mesh mesh,
            Component component,
            Vector3 right,
            Vector3 up,
            Vector3 normal)
        {
            return Frame(mesh, new[] { component }, right, up, normal);
        }

        private static Vector3 ComponentBoundsCenter(
            Mesh mesh,
            Component component,
            Vector3 right,
            Vector3 up,
            Vector3 normal)
        {
            Vector3[] positions = mesh.vertices;
            float minRight = float.PositiveInfinity;
            float maxRight = float.NegativeInfinity;
            float minUp = float.PositiveInfinity;
            float maxUp = float.NegativeInfinity;
            float minNormal = float.PositiveInfinity;
            float maxNormal = float.NegativeInfinity;
            foreach (int vertex in component.Vertices)
            {
                Vector3 point = positions[vertex];
                float rightValue = Vector3.Dot(point, right);
                float upValue = Vector3.Dot(point, up);
                float normalValue = Vector3.Dot(point, normal);
                minRight = Mathf.Min(minRight, rightValue);
                maxRight = Mathf.Max(maxRight, rightValue);
                minUp = Mathf.Min(minUp, upValue);
                maxUp = Mathf.Max(maxUp, upValue);
                minNormal = Mathf.Min(minNormal, normalValue);
                maxNormal = Mathf.Max(maxNormal, normalValue);
            }
            return right * ((minRight + maxRight) * 0.5f) +
                   up * ((minUp + maxUp) * 0.5f) +
                   normal * ((minNormal + maxNormal) * 0.5f);
        }

        private static FaceFrame Frame(
            Mesh mesh,
            IEnumerable<Component> components,
            Vector3 right,
            Vector3 up,
            Vector3 normal)
        {
            Component[] selected = components.ToArray();
            int seed = selected.Min(item => item.Seed);
            Vector3[] positions = mesh.vertices;
            Vector3[] sourceNormals = mesh.normals;
            HashSet<int> unique = new HashSet<int>(
                selected.SelectMany(item => item.Vertices));
            float minR = float.PositiveInfinity, maxR = float.NegativeInfinity;
            float minU = float.PositiveInfinity, maxU = float.NegativeInfinity;
            float minN = float.PositiveInfinity, maxN = float.NegativeInfinity;
            Vector3 averageNormal = Vector3.zero;
            foreach (int vertex in unique)
            {
                Vector3 point = positions[vertex];
                float r = Vector3.Dot(point, right);
                float u = Vector3.Dot(point, up);
                float n = Vector3.Dot(point, normal);
                minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
                minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
                minN = Mathf.Min(minN, n); maxN = Mathf.Max(maxN, n);
                if (sourceNormals.Length == positions.Length)
                    averageNormal += sourceNormals[vertex];
            }
            Vector3 faceNormal =
                Vector3.Dot(averageNormal, normal) >= 0f ? normal : -normal;
            float front = faceNormal == normal ? maxN : minN;
            Vector3 center =
                right * ((minR + maxR) * 0.5f) +
                up * ((minU + maxU) * 0.5f) +
                normal * front +
                faceNormal * FaceOffset;
            float spanR = maxR - minR;
            float spanU = maxU - minU;
            float smallerSpan = Mathf.Min(spanR, spanU);
            float largerSpan = Mathf.Max(spanR, spanU);
            // The source asset is authored in centimetre-class local units
            // under the aircraft import scale. The former 12 mm lower clamp
            // was therefore over three times larger than every audited
            // 3.5 mm source-face radius and caused the generated faces to
            // overlap. Preserve the measured source aperture. A strongly
            // rectangular component is the magnetic-compass card inside its
            // round housing, so use its width rather than collapsing it to
            // the card's narrow visible band.
            float measuredSpan =
                smallerSpan / Mathf.Max(largerSpan, 0.000001f) < 0.35f
                    ? largerSpan
                    : smallerSpan;
            float radius = Mathf.Clamp(
                measuredSpan * 0.47f,
                0.00065f, 0.00375f);
            Vector3 faceUp =
                Vector3.Cross(faceNormal, right).normalized;
            if (Vector3.Dot(faceUp, up) < 0f) faceUp = -faceUp;
            return new FaceFrame(
                seed, center, faceNormal, right, faceUp, radius);
        }

        private static Texture2D BuildAtlas()
        {
            var texture = new Texture2D(
                AtlasWidth, AtlasHeight, TextureFormat.RGBA32, false, false);
            var pixels = new Color32[AtlasWidth * AtlasHeight];
            Color32 black = new Color32(5, 7, 7, 255);
            for (int index = 0; index < pixels.Length; index++)
                pixels[index] = black;
            DrawRoundFace(pixels, AltimeterTile, "ALT",
                new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" });
            DrawRoundFace(pixels, DirectionTile, "DG",
                new[] { "N", "3", "6", "E", "12", "15", "S", "21", "24", "W", "30", "33" });
            DrawTachometerFace(pixels, TachometerTile);
            DrawRoundFace(pixels, FlapTile, "FLAP",
                new[] { "0", "", "", "10", "", "", "20", "", "", "30" });
            DrawRoundFace(pixels, BrakeTile, "BRAKE",
                new[] { "0", "", "2", "", "4", "", "6", "", "8", "", "10", "" });
            DrawTurnCoordinatorFace(pixels, TurnCoordinatorTile);
            FillTile(pixels, WhiteTile, new Color32(232, 238, 226, 255));
            FillTile(pixels, BlackTile, black);
            texture.SetPixels32(pixels);
            byte[] png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            string absolute = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", AtlasPath));
            File.WriteAllBytes(absolute, png);
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);
            var importer =
                (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 2;
            importer.maxTextureSize = 1024;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = 50;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        }

        private static Material UpsertMaterial(
            Material source,
            Texture2D atlas)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Require(shader != null, "InstrumentV2 shader is missing.");
            Require(source != null && source.mainTexture != null,
                "Frozen Meter_01 source material/texture is missing.");
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "ProductionCockpitInstrumentV2"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.SetTexture("_SourceTex", source.mainTexture);
            material.SetTexture("_PresentationTex", atlas);
            material.SetFloat("_LayerDepthBias", LayerDepthBias);
            material.SetVector("_AttitudePivot", Vector4.zero);
            material.SetVector(
                "_AttitudeRotation", new Vector4(0f, 0f, 0f, 1f));
            material.renderQueue = 2003;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void DrawRoundFace(
            Color32[] pixels,
            int tile,
            string label,
            string[] marks)
        {
            int originX = tile % TileColumns * TileSize;
            int originY = tile / TileColumns * TileSize;
            int cx = originX + TileSize / 2;
            int cy = originY + TileSize / 2;
            Color32 face = new Color32(9, 13, 13, 255);
            Color32 ink = new Color32(225, 232, 218, 255);
            for (int y = 0; y < TileSize; y++)
            for (int x = 0; x < TileSize; x++)
            {
                int dx = x - TileSize / 2;
                int dy = y - TileSize / 2;
                if (dx * dx + dy * dy <= 122 * 122)
                    pixels[(originY + y) * AtlasWidth + originX + x] = face;
            }
            int count = marks.Length;
            for (int index = 0; index < count; index++)
            {
                float angle = index * Mathf.PI * 2f / count;
                int x0 = cx + Mathf.RoundToInt(Mathf.Sin(angle) * 104f);
                int y0 = cy + Mathf.RoundToInt(Mathf.Cos(angle) * 104f);
                int x1 = cx + Mathf.RoundToInt(Mathf.Sin(angle) * 116f);
                int y1 = cy + Mathf.RoundToInt(Mathf.Cos(angle) * 116f);
                DrawLine(pixels, x0, y0, x1, y1, ink, 2);
                if (!string.IsNullOrEmpty(marks[index]))
                {
                    int tx = cx + Mathf.RoundToInt(Mathf.Sin(angle) * 82f);
                    int ty = cy + Mathf.RoundToInt(Mathf.Cos(angle) * 82f);
                    DrawTextCentered(pixels, tx, ty, marks[index], 2, ink);
                }
            }
            DrawTextCentered(pixels, cx, cy - 48, label, 3, ink);
        }

        private static void FillTile(
            Color32[] pixels,
            int tile,
            Color32 color)
        {
            int originX = tile % TileColumns * TileSize;
            int originY = tile / TileColumns * TileSize;
            for (int y = 0; y < TileSize; y++)
            for (int x = 0; x < TileSize; x++)
                pixels[(originY + y) * AtlasWidth + originX + x] = color;
        }

        private static void DrawTachometerFace(Color32[] pixels, int tile)
        {
            Color32 background = new Color32(9, 13, 13, 255);
            Color32 ink = new Color32(235, 241, 228, 255);
            FillTile(pixels, tile, background);
            int originX = tile % TileColumns * TileSize;
            int originY = tile / TileColumns * TileSize;
            int cx = originX + TileSize / 2;
            int cy = originY + TileSize / 2;
            for (int mark = 0; mark <= 30; mark++)
            {
                float angle = (-150f + mark * 10f) * Mathf.Deg2Rad;
                int x0 = cx + Mathf.RoundToInt(Mathf.Sin(angle) * 104f);
                int y0 = cy + Mathf.RoundToInt(Mathf.Cos(angle) * 104f);
                int length = mark % 5 == 0 ? 14 : 7;
                int x1 = cx + Mathf.RoundToInt(Mathf.Sin(angle) * (104f + length));
                int y1 = cy + Mathf.RoundToInt(Mathf.Cos(angle) * (104f + length));
                Color32 color = mark == 27
                    ? new Color32(220, 50, 36, 255)
                    : ink;
                DrawLine(pixels, x0, y0, x1, y1, color, mark % 5 == 0 ? 2 : 1);
                if (mark % 5 == 0)
                {
                    int tx = cx + Mathf.RoundToInt(Mathf.Sin(angle) * 79f);
                    int ty = cy + Mathf.RoundToInt(Mathf.Cos(angle) * 79f);
                    DrawTextCentered(pixels, tx, ty, mark.ToString(), 2, ink);
                }
            }
            DrawTextCentered(pixels, cx, cy - 34, "RPM", 3, ink);
            DrawTextCentered(pixels, cx, cy - 51, "X100", 2, ink);
        }

        private static void DrawTurnCoordinatorFace(Color32[] pixels, int tile)
        {
            Color32 background = new Color32(9, 13, 13, 255);
            Color32 ink = new Color32(235, 241, 228, 255);
            FillTile(pixels, tile, background);
            int originX = tile % TileColumns * TileSize;
            int originY = tile / TileColumns * TileSize;
            int cx = originX + TileSize / 2;
            int cy = originY + TileSize / 2;
            DrawTextCentered(pixels, cx, cy + 76, "TURN", 3, ink);
            DrawTextCentered(pixels, cx - 70, cy + 30, "L", 3, ink);
            DrawTextCentered(pixels, cx + 70, cy + 30, "R", 3, ink);
            DrawLine(pixels, cx - 66, cy - 43, cx + 66, cy - 43, ink, 3);
            DrawLine(pixels, cx - 30, cy - 54, cx - 30, cy - 32, ink, 2);
            DrawLine(pixels, cx + 30, cy - 54, cx + 30, cy - 32, ink, 2);
            DrawTextCentered(pixels, cx, cy - 80, "2 MIN", 2, ink);
        }

        private static void DrawTextCentered(
            Color32[] pixels,
            int centerX,
            int centerY,
            string text,
            int scale,
            Color32 color)
        {
            int width = text.Length * 6 * scale - scale;
            int startX = centerX - width / 2;
            int startY = centerY - 7 * scale / 2;
            for (int character = 0; character < text.Length; character++)
            {
                uint bits =
                    CockpitAvionicsV2PresentationAuthoring.GlyphBits(
                        text[character]);
                for (int row = 0; row < 7; row++)
                for (int column = 0; column < 5; column++)
                {
                    if ((bits & (1u << (row * 5 + column))) == 0) continue;
                    for (int py = 0; py < scale; py++)
                    for (int px = 0; px < scale; px++)
                    {
                        int x = startX + character * 6 * scale +
                                column * scale + px;
                        int y = startY + (6 - row) * scale + py;
                        if (x >= 0 && x < AtlasWidth &&
                            y >= 0 && y < AtlasHeight)
                            pixels[y * AtlasWidth + x] = color;
                    }
                }
            }
        }

        private static void DrawLine(
            Color32[] pixels,
            int x0,
            int y0,
            int x1,
            int y1,
            Color32 color,
            int radius)
        {
            int dx = Mathf.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    int px = x0 + x;
                    int py = y0 + y;
                    if (px >= 0 && px < AtlasWidth &&
                        py >= 0 && py < AtlasHeight)
                        pixels[py * AtlasWidth + px] = color;
                }
                if (x0 == x1 && y0 == y1) break;
                int twice = error * 2;
                if (twice >= dy) { error += dy; x0 += sx; }
                if (twice <= dx) { error += dx; y0 += sy; }
            }
        }

        private static void WriteManifest(int principalAltimeterSeed)
        {
            var manifest = new Manifest
            {
                selected_principal_altimeter_seed = principalAltimeterSeed,
                active = new[]
                {
                    Active("airspeed_indicator", new[] { 2819 },
                        new[] { "IndicatedAirspeedNeedleDegrees" },
                        "calibrated airspeed", "Existing audited piecewise KNOTS scale"),
                    Active("vertical_speed_indicator", new[] { 2541 },
                        new[] { "IndicatedVerticalSpeedNeedleDegrees" },
                        "filtered vertical speed", "Existing audited split FPM scale"),
                    Active("principal_altimeter", new[] { principalAltimeterSeed },
                        new[]
                        {
                            "AltimeterHundredsNeedleDegrees",
                            "AltimeterThousandsNeedleDegrees",
                            "AltimeterTenThousandsNeedleDegrees",
                            "AltimeterBarometricDrumInHg"
                        },
                        "pressure altitude and barometric setting",
                        "Three standard 10/1/0.1-revolution hands plus bounded baro indication"),
                    Active("directional_gyro", new[] { 2891 },
                        new[] { "DirectionalGyroHeadingDegrees" },
                        "true heading", "Project-authored card/lubber zero registration"),
                    Active("magnetic_compass",
                        new[]
                        {
                            CockpitInstrumentV2EditorContract
                                .MagneticCompassCardSeed
                        },
                        new[] { "MagneticCompassHeadingDegrees" },
                        "true heading minus frozen KBDU east magnetic variation",
                        "Complete curved source card rotates behind a fixed lubber " +
                        "line with shortest-path liquid-style visual damping"),
                    Active("flap_position", new[] { 2759 },
                        new[] { "ActualFlapDegrees" },
                        "actual flap position", "0/10/20/30-degree bounded scale"),
                    Active("brake_pressure", new[] { 2906 },
                        new[] { "AverageBrakePressurePsi" },
                        "measured left/right hydraulic pressure average",
                        "0-1000 PSI bounded indication"),
                    Active("tachometer", new[] { TachometerFaceSeed },
                        new[] { "EngineRpmNeedleDegrees" },
                        "authoritative engine RPM",
                        "0-3000 RPM, 300-degree C172-style sweep with 2700 RPM red line"),
                    Active("turn_coordinator", new[] { TurnCoordinatorFaceSeed },
                        new[]
                        {
                            "TurnCoordinatorBankDegrees",
                            "SlipSkidNormalized"
                        },
                        "authoritative turn-rate and slip/skid state",
                        "standard-rate reference at 3 degrees/second; bounded slip ball"),
                    Active("attitude_indicator",
                        CockpitInstrumentV2EditorContract
                            .AttitudeSphereComponentSeeds.ToArray(),
                        new[]
                        {
                            "AttitudePitchDegrees",
                            "AttitudeBankDegrees"
                        },
                        "filtered aircraft pitch and bank",
                        "Frozen 998-vertex gyro sphere rotates about its fitted " +
                        "center opposite aircraft pitch/bank; fixed miniature " +
                        "aircraft remains case-mounted")
                },
                blocked = new[]
                {
                    Block("clock", "candidate component 2849",
                        "Full-resolution QA proved that candidate 2849 occupies the " +
                        "attitude/turn cluster rather than an independently represented " +
                        "clock. The failed identity was removed instead of relabeling art."),
                    Block("fuel_oil_electrical_vacuum",
                        "remaining unlabeled/fantasy apertures",
                        "Authority exists, but no remaining face/pointer has a positive " +
                        "system identity, units, topology, and calibration."),
                    Block("mach_as_duplicate_vsi_gyro_loc",
                        "2501,2469,2789,2585",
                        "Fantasy/duplicate source art is not relabeled as a real instrument.")
                }
            };
            string absolute = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", PresentationManifestPath));
            File.WriteAllText(
                absolute, JsonUtility.ToJson(manifest, true) + "\n");
        }

        private static ActiveGauge Active(
            string id,
            int[] seeds,
            string[] functions,
            string authority,
            string calibration)
        {
            return new ActiveGauge
            {
                id = id,
                source_face_seeds = seeds,
                functions = functions,
                authority = authority,
                calibration = calibration
            };
        }

        private static Blocker Block(
            string id,
            string source,
            string reason)
        {
            return new Blocker
            {
                id = id,
                source_identity = source,
                blocker = reason
            };
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
