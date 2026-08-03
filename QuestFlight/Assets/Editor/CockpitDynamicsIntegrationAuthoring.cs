using System;
using System.Collections.Generic;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.Presentation;
using QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2;
using QuestFlightLab.Aircraft.CockpitDynamics.TouchInputV2;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Editor-only integration recipe for cockpit controls proven to exist as
    /// separate source-model parts. Runtime code never creates these pivots,
    /// discovers transforms, or repairs the hierarchy.
    /// </summary>
    public static class CockpitDynamicsIntegrationAuthoring
    {
        public const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";

        public const string AnimatedControlsPath =
            "AircraftVisualRoot/AnimatedControlSurfaces";
        public const string CopilotYokeSourcePath =
            "AircraftVisualRoot/ImportedCockpitInterior/Cessna_Interior/" +
            "Cessna_Interior_Steering_MAT_0.001";
        public const string ThrottlePath =
            "AircraftVisualRoot/ImportedCockpitInterior/Dynamic_Switch_005";
        public const string MixturePath =
            "AircraftVisualRoot/ImportedCockpitInterior/Dynamic_Switch_006";
        public const string AltBatMasterPath =
            "AircraftVisualRoot/ImportedCockpitInterior/Dynamic_Switch_001";
        public const string PrimerPath =
            "AircraftVisualRoot/ImportedCockpitInterior/Dynamic_Switch_002";
        public const string IgnitionSwitchPath =
            "AircraftVisualRoot/ImportedCockpitInterior/Dynamic_Switch_003";
        public const string AvionicsMasterPath =
            "AircraftVisualRoot/ImportedCockpitInterior/Dynamic_Switch_004";
        public const string WingFlapSelectorSourcePath =
            "AircraftVisualRoot/ImportedCockpitInterior/Dynamic_Switch_007";
        public const string FuelSelectorPath =
            "AircraftVisualRoot/ImportedCockpitInterior/Dynamic_Switch_008";
        public const string ImportedCockpitPath =
            "AircraftVisualRoot/ImportedCockpitInterior";
        public const string RudderSourcePath =
            "AircraftVisualRoot/ImportedAircraftExterior/Cessna_Exterior/" +
            "Cessna_Exterior_Body_MAT_0.008";

        public const string CopilotYokePitchPivotName = "CopilotYokePitchPivot";
        public const string CopilotYokeRollPivotName = "CopilotYokeRollPivot";
        public const string ThrottleTravelPivotName = "ThrottleTravelPivot";
        public const string MixtureTravelPivotName = "MixtureTravelPivot";
        public const string RudderPivotName = "RudderPivot";

        public const string CopilotYokePitchPivotPath =
            AnimatedControlsPath + "/" + CopilotYokePitchPivotName;
        public const string CopilotYokeRollPivotPath =
            CopilotYokePitchPivotPath + "/" + CopilotYokeRollPivotName;
        public const string ThrottleTravelPivotPath =
            ImportedCockpitPath + "/" + ThrottleTravelPivotName;
        public const string MixtureTravelPivotPath =
            ImportedCockpitPath + "/" + MixtureTravelPivotName;
        public const string RudderPivotPath =
            AnimatedControlsPath + "/" + RudderPivotName;

        public const float CopilotYokeMaximumRollDegrees = 55f;
        public const float CopilotYokePitchHalfTravelMeters = 0.04f;
        // 45 mm total is deliberately bounded below the imported 91.7/104.6 mm
        // throttle/mixture shaft lengths so neither handle can leave its panel.
        public const float EngineControlHalfTravelMeters = 0.0225f;
        // Cessna 172R/S maintenance data: 16 degrees 10 minutes parallel
        // to the water line. The imported rudder's vertical hinge is parent-local
        // +Y; its leading edge is the maximum parent-local Z boundary.
        public const float RudderMaximumTravelDegrees = 16f + 10f / 60f;

        public const int ExpectedPresentationBindingCount = 18;
        public const int ExpectedLegacyAnimatorBindingCountBeforeRudder = 8;
        public const int ExpectedAnimatorBindingCountAfterRudder = 9;
        public const long ProductScenarioUtcUnixSeconds = 1785434400L;

        private const string RudderBindingLabel = "Exterior rudder";
        private const string LegacyWingFlapSelectorPivotName =
            "WingFlapSelectorTravelPivot";

        [MenuItem("QuestFlight/Cockpit/Apply Represented Cockpit Dynamics")]
        public static void ApplyToProductPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                AuthorPrefabContents(root);
                if (!ValidateAuthoredPrefabContents(root, out string error))
                    throw new InvalidOperationException(error);
                PrefabUtility.SaveAsPrefabAsset(root, AircraftPrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "[QuestFlight][CockpitDynamics] Serialized identity-audited " +
                    "flight controls, engine controls, six represented switches, " +
                    "single systems authority, and centralized Touch interaction.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void AuthorPrefabContents(GameObject root)
        {
            Require(root != null, "Aircraft prefab contents are required.");
            Require(root.name == "ProductionC172AircraftRig",
                "Cockpit dynamics may only author ProductionC172AircraftRig.");

            Transform animatedControls = RequirePath(root, AnimatedControlsPath);
            FlightDynamicsCoordinator coordinator = RequireExactlyOne<FlightDynamicsCoordinator>(root);
            ProductionAircraftControlAnimator legacyAnimator =
                RequireExactlyOne<ProductionAircraftControlAnimator>(root);

            EnsureCopilotYokePivots(
                root, animatedControls,
                out Transform copilotPitchPivot,
                out Transform copilotRollPivot);
            Transform importedCockpit = RequirePath(root, ImportedCockpitPath);
            Transform throttlePivot = EnsureEngineControlTravelPivot(
                root,
                importedCockpit,
                ThrottlePath,
                ThrottleTravelPivotPath,
                ThrottleTravelPivotName,
                out Vector3 throttleFullForwardAxis);
            Transform mixturePivot = EnsureEngineControlTravelPivot(
                root,
                importedCockpit,
                MixturePath,
                MixtureTravelPivotPath,
                MixtureTravelPivotName,
                out Vector3 mixtureFullRichAxis);
            EnsureWingFlapSelectorDirectBinding(root, importedCockpit);
            Transform altBatMaster = RequirePath(root, AltBatMasterPath);
            Transform primer = RequirePath(root, PrimerPath);
            Transform ignitionSwitch = RequirePath(root, IgnitionSwitchPath);
            Transform avionicsMaster = RequirePath(root, AvionicsMasterPath);
            Transform flapSelector =
                RequirePath(root, WingFlapSelectorSourcePath);
            Transform fuelSelector = RequirePath(root, FuelSelectorPath);
            Transform rudderPivot = EnsureRudderPivot(root, animatedControls);
            Transform pedalRoot = RequirePath(
                root,
                CockpitPedalCompletionAuthoring.RootPath);

            ProductionCockpitSystemsController systems =
                RequireOrAddSingle<ProductionCockpitSystemsController>(animatedControls.gameObject);
            systems.ConfigureAuthoredSource(
                coordinator,
                ProductElectricalPowerSource(),
                ProductSystemsScenario());

            ProductionCockpitDynamicsController presentation =
                RequireOrAddSingle<ProductionCockpitDynamicsController>(animatedControls.gameObject);
            presentation.ConfigureAuthoredReferences(
                coordinator,
                systems,
                BuildPresentationBindings(
                    copilotPitchPivot,
                    copilotRollPivot,
                    throttlePivot,
                    mixturePivot,
                    throttleFullForwardAxis,
                    mixtureFullRichAxis,
                    altBatMaster,
                    primer,
                    ignitionSwitch,
                    avionicsMaster,
                    flapSelector,
                    fuelSelector,
                    pedalRoot));

            AuthorRudderBinding(legacyAnimator, rudderPivot);

            CockpitTouchInputV2Bridge touchInputBridge =
                RequireOrAddSingle<CockpitTouchInputV2Bridge>(
                    animatedControls.gameObject);
            touchInputBridge.ConfigureAuthoredReferences(
                coordinator,
                animatedControls,
                importedCockpit,
                importedCockpit,
                CockpitTouchInputV2Bridge.AuthoredYokeAxisLocal,
                CockpitTouchInputV2Bridge.AuthoredYokeAxisLocal,
                throttleFullForwardAxis,
                mixtureFullRichAxis);
            touchInputBridge.ConfigureSystemsAuthority(systems);
            CockpitTouchInteractionV2Authoring.AuthorPrefabContents(
                root,
                touchInputBridge);

            EditorUtility.SetDirty(copilotPitchPivot);
            EditorUtility.SetDirty(copilotRollPivot);
            EditorUtility.SetDirty(throttlePivot);
            EditorUtility.SetDirty(mixturePivot);
            EditorUtility.SetDirty(rudderPivot);
            EditorUtility.SetDirty(systems);
            EditorUtility.SetDirty(presentation);
            EditorUtility.SetDirty(touchInputBridge);
            EditorUtility.SetDirty(legacyAnimator);
        }

        public static bool ValidateAuthoredPrefabContents(
            GameObject root,
            out string error)
        {
            try
            {
                Require(root != null, "Aircraft prefab contents are required.");
                Transform animatedControls = RequirePath(root, AnimatedControlsPath);
                ProductionCockpitSystemsController systems =
                    RequireExactlyOne<ProductionCockpitSystemsController>(root);
                ProductionCockpitDynamicsController presentation =
                    RequireExactlyOne<ProductionCockpitDynamicsController>(root);
                ProductionAircraftControlAnimator animator =
                    RequireExactlyOne<ProductionAircraftControlAnimator>(root);
                FlightDynamicsCoordinator coordinator =
                    RequireExactlyOne<FlightDynamicsCoordinator>(root);
                CockpitTouchInputV2Bridge touchInputBridge =
                    RequireExactlyOne<CockpitTouchInputV2Bridge>(root);

                Require(systems.transform == animatedControls,
                    "Cockpit systems must be serialized on AnimatedControlSurfaces.");
                Require(presentation.transform == animatedControls,
                    "Cockpit presentation must be serialized on AnimatedControlSurfaces.");
                Require(systems.HasAuthoredReferences(out string systemsError), systemsError);
                SerializedObject systemsObject = new SerializedObject(systems);
                SerializedProperty authoredPower =
                    systemsObject.FindProperty("electricalPowerInput");
                Require(
                    authoredPower.FindPropertyRelative("hasAuthoritativeState").boolValue,
                    "Product cockpit electrical source must be authoritative.");
                Require(
                    authoredPower.FindPropertyRelative("batteryCanPowerBus").boolValue,
                    "Product cockpit battery must be able to power the avionics bus.");
                Require(
                    !authoredPower.FindPropertyRelative("alternatorProducingPower").boolValue,
                    "Product startup must not claim unsupported alternator output.");
                Require(presentation.Coordinator == coordinator,
                    "Cockpit presentation coordinator reference is not authoritative.");
                Require(presentation.AuthoritativeStateProvider == systems,
                    "Cockpit presentation must use the serialized cockpit systems provider.");
                Require(presentation.ValidateAuthoredBindings(out string presentationError),
                    presentationError);
                Require(touchInputBridge.transform == animatedControls,
                    "Cockpit Touch input bridge must be serialized on AnimatedControlSurfaces.");
                Require(touchInputBridge.Coordinator == coordinator,
                    "Cockpit Touch input bridge must use the sole FlightDynamicsCoordinator.");
                Require(
                    touchInputBridge.ValidateAuthoredReferences(
                        out string touchBridgeError),
                    touchBridgeError);
                Require(
                    CockpitTouchInteractionV2Authoring.ValidatePrefabContents(
                        root,
                        out string touchAuthoringError),
                    touchAuthoringError);

                Dictionary<string, CockpitPresentationChannel> expected =
                    new Dictionary<string, CockpitPresentationChannel>(StringComparer.Ordinal)
                    {
                        [CopilotYokePitchPivotPath] =
                            CockpitPresentationChannel.YokePitchNormalized,
                        [CopilotYokeRollPivotPath] =
                            CockpitPresentationChannel.YokeRollNormalized,
                        [ThrottleTravelPivotPath] =
                            CockpitPresentationChannel.ThrottleNormalized,
                        [MixtureTravelPivotPath] =
                            CockpitPresentationChannel.MixtureNormalized,
                        [AltBatMasterPath] =
                            CockpitPresentationChannel.BatteryMasterOn,
                        [PrimerPath] =
                            CockpitPresentationChannel.PrimerPositionNormalized,
                        [IgnitionSwitchPath] =
                            CockpitPresentationChannel.IgnitionSwitchPosition,
                        [AvionicsMasterPath] =
                            CockpitPresentationChannel.AvionicsMasterOn,
                        [WingFlapSelectorSourcePath] =
                            CockpitPresentationChannel.FlapsCommandNormalized,
                        [FuelSelectorPath] =
                            CockpitPresentationChannel.FuelSelectorPosition,
                        [CockpitPedalCompletionAuthoring.RootPath + "/" +
                         CockpitPedalCompletionAuthoring.PilotLeftRudderName] =
                            CockpitPresentationChannel.RudderPedalsNormalized,
                        [CockpitPedalCompletionAuthoring.RootPath + "/" +
                         CockpitPedalCompletionAuthoring.PilotLeftRudderName + "/" +
                         CockpitPedalCompletionAuthoring.PilotLeftToeName] =
                            CockpitPresentationChannel.LeftToeBrakeNormalized,
                        [CockpitPedalCompletionAuthoring.RootPath + "/" +
                         CockpitPedalCompletionAuthoring.PilotRightRudderName] =
                            CockpitPresentationChannel.RudderPedalsNormalized,
                        [CockpitPedalCompletionAuthoring.RootPath + "/" +
                         CockpitPedalCompletionAuthoring.PilotRightRudderName + "/" +
                         CockpitPedalCompletionAuthoring.PilotRightToeName] =
                            CockpitPresentationChannel.RightToeBrakeNormalized,
                        [CockpitPedalCompletionAuthoring.RootPath + "/" +
                         CockpitPedalCompletionAuthoring.CopilotLeftRudderName] =
                            CockpitPresentationChannel.RudderPedalsNormalized,
                        [CockpitPedalCompletionAuthoring.RootPath + "/" +
                         CockpitPedalCompletionAuthoring.CopilotLeftRudderName + "/" +
                         CockpitPedalCompletionAuthoring.CopilotLeftToeName] =
                            CockpitPresentationChannel.LeftToeBrakeNormalized,
                        [CockpitPedalCompletionAuthoring.RootPath + "/" +
                         CockpitPedalCompletionAuthoring.CopilotRightRudderName] =
                            CockpitPresentationChannel.RudderPedalsNormalized,
                        [CockpitPedalCompletionAuthoring.RootPath + "/" +
                         CockpitPedalCompletionAuthoring.CopilotRightRudderName + "/" +
                         CockpitPedalCompletionAuthoring.CopilotRightToeName] =
                            CockpitPresentationChannel.RightToeBrakeNormalized
                    };

                SerializedObject presentationObject = new SerializedObject(presentation);
                SerializedProperty presentationBindings =
                    presentationObject.FindProperty("bindings");
                Require(
                    presentationBindings.arraySize == ExpectedPresentationBindingCount,
                    $"Expected {ExpectedPresentationBindingCount} represented presentation " +
                    $"bindings; found {presentationBindings.arraySize}.");

                HashSet<Transform> presentationTargets = new HashSet<Transform>();
                for (int index = 0; index < presentationBindings.arraySize; index++)
                {
                    SerializedProperty binding =
                        presentationBindings.GetArrayElementAtIndex(index);
                    Transform target = binding.FindPropertyRelative("target")
                        .objectReferenceValue as Transform;
                    Require(target != null,
                        $"Presentation binding {index} has no target.");
                    string path = RelativePath(root.transform, target);
                    Require(expected.TryGetValue(path, out CockpitPresentationChannel channel),
                        $"Unsupported cockpit target is bound: {path}.");
                    Require(presentationTargets.Add(target),
                        $"Cockpit target is double-bound in presentation: {path}.");
                    Require(target.GetComponent<Camera>() == null,
                        $"Cockpit presentation may not target a camera: {path}.");

                    SerializedProperty motions = binding.FindPropertyRelative("motions");
                    Require(motions.arraySize == 1,
                        $"Represented control '{path}' must have exactly one motion term.");
                    SerializedProperty motion = motions.GetArrayElementAtIndex(0);
                    int actualChannel =
                        motion.FindPropertyRelative("channel").intValue;
                    Require(actualChannel == (int)channel,
                        $"Cockpit target '{path}' uses the wrong channel.");

                    if (path == ThrottleTravelPivotPath ||
                        path == MixtureTravelPivotPath)
                    {
                        Vector3 derivedPanelwardAxis =
                            DerivePanelwardShaftAxis(
                                target.parent,
                                target,
                                path);
                        Vector3 authoredAxis =
                            motion.FindPropertyRelative("localTranslationAxis")
                                .vector3Value.normalized;
                        Require(Vector3.Dot(authoredAxis, derivedPanelwardAxis) >= 0.9999f,
                            $"Engine-control travel axis for '{path}' does not match " +
                            "its source-mesh shaft.");
                        Require(Vector3.Dot(authoredAxis, Vector3.forward) >= 0.95f,
                            $"Engine-control full-forward/full-rich travel for '{path}' " +
                            "must point predominantly toward aircraft/panel +Z.");
                    }
                }

                SerializedObject animatorObject = new SerializedObject(animator);
                SerializedProperty animatorBindings = animatorObject.FindProperty("bindings");
                Require(
                    animatorBindings.arraySize == ExpectedAnimatorBindingCountAfterRudder,
                    $"Expected {ExpectedAnimatorBindingCountAfterRudder} legacy/rudder animator " +
                    $"bindings; found {animatorBindings.arraySize}.");
                int rudderCount = 0;
                HashSet<Transform> animatorTargets = new HashSet<Transform>();
                for (int index = 0; index < animatorBindings.arraySize; index++)
                {
                    SerializedProperty binding =
                        animatorBindings.GetArrayElementAtIndex(index);
                    Transform target = binding.FindPropertyRelative("target")
                        .objectReferenceValue as Transform;
                    Require(target != null, $"Aircraft animator binding {index} has no target.");
                    Require(animatorTargets.Add(target),
                        $"Aircraft animator target is bound twice: {target.name}.");
                    Require(!presentationTargets.Contains(target),
                        $"Target '{target.name}' is double-written by cockpit and aircraft animators.");

                    int channel = binding.FindPropertyRelative("channel").enumValueIndex;
                    if (channel != (int)ProductionAircraftControlAnimator.ControlChannel.Rudder)
                        continue;
                    rudderCount++;
                    Require(RelativePath(root.transform, target) == RudderPivotPath,
                        "Rudder channel must target the identity-audited exterior rudder pivot.");
                }
                Require(rudderCount == 1,
                    $"Exactly one exterior rudder binding is required; found {rudderCount}.");

                Require(RequirePath(root, CopilotYokeRollPivotPath)
                        .GetComponentsInChildren<MeshRenderer>(true).Length == 1,
                    "Copilot yoke pivot must retain exactly its represented source renderer.");
                Require(RequirePath(root, WingFlapSelectorSourcePath)
                        .GetComponentsInChildren<MeshRenderer>(true).Length == 1,
                    "Flap selector must retain exactly its represented source renderer.");
                Require(
                    RequirePath(root, ImportedCockpitPath)
                        .Find(LegacyWingFlapSelectorPivotName) == null,
                    "Obsolete flap-selector wrapper must not remain in the authored hierarchy.");
                Require(RequirePath(root, RudderPivotPath)
                        .GetComponentsInChildren<MeshRenderer>(true).Length == 1,
                    "Rudder pivot must retain exactly its represented source renderer.");
                RequireRudderLeadingEdgeHinge(RequirePath(root, RudderPivotPath));

                string[] prohibitedNames =
                {
                    "master", "primer", "ignition", "key", "breaker",
                    "fuel selector", "propeller", "wheel", "instrument"
                };
                foreach (string prohibited in prohibitedNames)
                {
                    foreach (string path in expected.Keys)
                        Require(path.IndexOf(prohibited, StringComparison.OrdinalIgnoreCase) < 0,
                            $"Unsupported control was authored: {path}.");
                }

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static ProductionCockpitDynamicsController.TransformBinding[]
            BuildPresentationBindings(
                Transform copilotPitchPivot,
                Transform copilotRollPivot,
                Transform throttlePivot,
                Transform mixturePivot,
                Vector3 throttleFullForwardAxis,
                Vector3 mixtureFullRichAxis,
                Transform altBatMaster,
                Transform primer,
                Transform ignitionSwitch,
                Transform avionicsMaster,
                Transform flapSelector,
                Transform fuelSelector,
                Transform pedalRoot)
        {
            List<ProductionCockpitDynamicsController.TransformBinding> result =
                new List<ProductionCockpitDynamicsController.TransformBinding>
            {
                Binding(
                    "Copilot yoke pitch travel",
                    copilotPitchPivot,
                    Motion(
                        "Mechanically linked copilot yoke pitch",
                        CockpitPresentationChannel.YokePitchNormalized,
                        -1f, 1f,
                        Vector3.zero, 0f, 0f,
                        // Positive elevator is nose-up, so the linked yoke must
                        // travel aft toward the pilot rather than into the panel.
                        Vector3.back,
                        -CopilotYokePitchHalfTravelMeters,
                        CopilotYokePitchHalfTravelMeters)),
                Binding(
                    "Copilot yoke roll",
                    copilotRollPivot,
                    Motion(
                        "Mechanically linked copilot yoke roll",
                        CockpitPresentationChannel.YokeRollNormalized,
                        -1f, 1f,
                        Vector3.forward,
                        CopilotYokeMaximumRollDegrees,
                        -CopilotYokeMaximumRollDegrees,
                        Vector3.zero, 0f, 0f)),
                Binding(
                    "Throttle push-pull control",
                    throttlePivot,
                    Motion(
                        "Full aft to full forward throttle",
                        CockpitPresentationChannel.ThrottleNormalized,
                        0f, 1f,
                        Vector3.zero, 0f, 0f,
                        throttleFullForwardAxis,
                        -EngineControlHalfTravelMeters,
                        EngineControlHalfTravelMeters)),
                Binding(
                    "Mixture push-pull control",
                    mixturePivot,
                    Motion(
                        "Idle cutoff to full rich mixture",
                        CockpitPresentationChannel.MixtureNormalized,
                        0f, 1f,
                        Vector3.zero, 0f, 0f,
                        mixtureFullRichAxis,
                        -EngineControlHalfTravelMeters,
                        EngineControlHalfTravelMeters)),
                Binding(
                    "Combined alternator and battery master",
                    altBatMaster,
                    Motion(
                        "ALT/BAT master rocker state",
                        CockpitPresentationChannel.BatteryMasterOn,
                        0f, 1f,
                        Vector3.right,
                        -12f, 12f,
                        Vector3.zero, 0f, 0f)),
                Binding(
                    "Engine primer",
                    primer,
                    Motion(
                        "Primer plunger position",
                        CockpitPresentationChannel.PrimerPositionNormalized,
                        0f, 1f,
                        Vector3.zero, 0f, 0f,
                        Vector3.forward,
                        0f, -0.018f)),
                Binding(
                    "Ignition and starter switch",
                    ignitionSwitch,
                    Motion(
                        "OFF-R-L-BOTH-START selector",
                        CockpitPresentationChannel.IgnitionSwitchPosition,
                        0f, 4f,
                        Vector3.forward,
                        -70f, 70f,
                        Vector3.zero, 0f, 0f)),
                Binding(
                    "Avionics master",
                    avionicsMaster,
                    Motion(
                        "Avionics master rocker state",
                        CockpitPresentationChannel.AvionicsMasterOn,
                        0f, 1f,
                        Vector3.right,
                        -12f, 12f,
                        Vector3.zero, 0f, 0f)),
                Binding(
                    "Wing flap selector",
                    flapSelector,
                    Motion(
                        "0-10-20-30 degree flap selector",
                        CockpitPresentationChannel.FlapsCommandNormalized,
                        0f, 1f,
                        Vector3.right,
                        -16f, 24f,
                        Vector3.zero, 0f, 0f)),
                Binding(
                    "Fuel selector",
                    fuelSelector,
                    Motion(
                        "OFF-LEFT-BOTH-RIGHT fuel selector",
                        CockpitPresentationChannel.FuelSelectorPosition,
                        0f, 3f,
                        Vector3.forward,
                        -90f, 90f,
                        Vector3.zero, 0f, 0f))
            };
            result.AddRange(
                CockpitPedalCompletionAuthoring.BuildPedalPresentationBindings(
                    pedalRoot));
            return result.ToArray();
        }

        private static ProductionCockpitDynamicsController.TransformBinding Binding(
            string label,
            Transform target,
            ProductionCockpitDynamicsController.MotionTerm motion)
        {
            return new ProductionCockpitDynamicsController.TransformBinding
            {
                label = label,
                target = target,
                motions = new[] { motion }
            };
        }

        private static CockpitSystemsScenarioV2 ProductSystemsScenario()
        {
            return new CockpitSystemsScenarioV2
            {
                hasAuthoritativeConfiguration = true,
                scenarioUtcUnixSeconds = ProductScenarioUtcUnixSeconds,
                batteryMasterOn = true,
                alternatorMasterOn = true,
                avionicsMasterOn = true,
                ignitionSwitchPosition =
                    CockpitIgnitionSwitchPositionV2.Both,
                primerPositionNormalized = 0f,
                fuelSelectorPosition =
                    CockpitFuelSelectorPositionV2.Both
            };
        }

        /// <summary>
        /// Product startup has a serviceable battery but no authoritative engine-
        /// driven alternator model. Systems V2 owns the serialized master-switch
        /// positions; this source describes only the available electrical supply.
        /// </summary>
        public static CockpitElectricalPowerInput ProductElectricalPowerSource()
        {
            return new CockpitElectricalPowerInput
            {
                hasAuthoritativeState = true,
                batteryCanPowerBus = true,
                alternatorProducingPower = false
            };
        }

        private static ProductionCockpitDynamicsController.MotionTerm Motion(
            string label,
            CockpitPresentationChannel channel,
            float inputMinimum,
            float inputMaximum,
            Vector3 rotationAxis,
            float minimumAngle,
            float maximumAngle,
            Vector3 translationAxis,
            float minimumTranslation,
            float maximumTranslation)
        {
            return new ProductionCockpitDynamicsController.MotionTerm
            {
                label = label,
                channel = channel,
                inputRemap =
                    ProductionCockpitDynamicsController.InputRemapMode.Clamp,
                inputMinimum = inputMinimum,
                inputMaximum = inputMaximum,
                responseRatePerSecond = 0f,
                localRotationAxis = rotationAxis,
                minimumAngleDegrees = minimumAngle,
                maximumAngleDegrees = maximumAngle,
                localTranslationAxis = translationAxis,
                minimumTranslationMeters = minimumTranslation,
                maximumTranslationMeters = maximumTranslation
            };
        }

        private static void EnsureCopilotYokePivots(
            GameObject root,
            Transform animatedControls,
            out Transform pitchPivot,
            out Transform rollPivot)
        {
            pitchPivot = root.transform.Find(CopilotYokePitchPivotPath);
            if (pitchPivot != null)
            {
                rollPivot = pitchPivot.Find(CopilotYokeRollPivotName);
                Require(rollPivot != null,
                    "Authored copilot pitch pivot is missing its roll pivot.");
                return;
            }

            Transform source = RequirePath(root, CopilotYokeSourcePath);
            Bounds bounds = RendererBounds(source);
            pitchPivot = CreatePivot(
                CopilotYokePitchPivotName, animatedControls, bounds.center);
            rollPivot = CreatePivot(
                CopilotYokeRollPivotName, pitchPivot, bounds.center);
            source.SetParent(rollPivot, true);
        }

        private static Transform EnsureEngineControlTravelPivot(
            GameObject root,
            Transform parent,
            string sourcePath,
            string authoredPivotPath,
            string authoredPivotName,
            out Vector3 panelwardAxis)
        {
            Transform existing = root.transform.Find(authoredPivotPath);
            string sourceName =
                sourcePath.Substring(sourcePath.LastIndexOf('/') + 1);
            Transform source;
            if (existing != null)
            {
                Require(existing.parent == parent,
                    $"Authored engine-control pivot '{authoredPivotPath}' has the wrong parent.");
                source = existing.Find(sourceName);
                Require(source != null,
                    $"Authored engine-control pivot '{authoredPivotPath}' lost '{sourceName}'.");
                panelwardAxis =
                    DerivePanelwardShaftAxis(parent, source, sourceName);
                return existing;
            }

            source = RequirePath(root, sourcePath);
            panelwardAxis =
                DerivePanelwardShaftAxis(parent, source, sourceName);
            Transform pivot = CreatePivot(authoredPivotName, parent, source.position);
            source.SetParent(pivot, true);
            return pivot;
        }

        private static Vector3 DerivePanelwardShaftAxis(
            Transform motionParent,
            Transform source,
            string identity)
        {
            MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true);
            Require(filters.Length == 1,
                $"Engine control '{identity}' must own exactly one source mesh.");
            Mesh mesh = filters[0].sharedMesh;
            Require(mesh != null,
                $"Engine control '{identity}' has no source mesh.");
            Require(mesh.isReadable,
                $"Engine control '{identity}' source mesh must be readable during Editor authoring.");

            Bounds bounds = mesh.bounds;
            Vector3 size = bounds.size;
            Require(size.z >= size.x * 2f && size.z >= size.y * 2f,
                $"Engine control '{identity}' no longer has a conclusive mesh-local shaft axis.");

            Vector3[] vertices = mesh.vertices;
            Require(vertices.Length > 0,
                $"Engine control '{identity}' source mesh has no vertices.");
            float endSlice = size.z * 0.15f;
            float minimumEndRadius = 0f;
            float maximumEndRadius = 0f;
            int minimumEndSamples = 0;
            int maximumEndSamples = 0;
            Vector3 center = bounds.center;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 vertex = vertices[index];
                float radius = new Vector2(
                    vertex.x - center.x,
                    vertex.y - center.y).magnitude;
                if (vertex.z <= bounds.min.z + endSlice)
                {
                    minimumEndRadius = Mathf.Max(minimumEndRadius, radius);
                    minimumEndSamples++;
                }
                if (vertex.z >= bounds.max.z - endSlice)
                {
                    maximumEndRadius = Mathf.Max(maximumEndRadius, radius);
                    maximumEndSamples++;
                }
            }
            Require(minimumEndSamples > 0 && maximumEndSamples > 0,
                $"Engine control '{identity}' shaft endpoints could not be sampled.");

            float thinRadius = Mathf.Min(minimumEndRadius, maximumEndRadius);
            float handleRadius = Mathf.Max(minimumEndRadius, maximumEndRadius);
            Require(thinRadius <= handleRadius * 0.6f,
                $"Engine control '{identity}' does not expose a conclusive thin panel shaft " +
                "and wider pilot handle.");

            // The thinner end is the shaft entering the instrument panel. Moving
            // toward that endpoint is therefore full-forward throttle/full-rich
            // mixture. Transform it from the actual MeshFilter frame into the
            // authored pivot parent's frame; never reuse a vector from mesh space.
            Vector3 meshPanelwardAxis =
                minimumEndRadius < maximumEndRadius
                    ? Vector3.back
                    : Vector3.forward;
            Vector3 panelwardWorld =
                filters[0].transform.TransformVector(meshPanelwardAxis);
            Vector3 panelwardParent =
                motionParent.InverseTransformVector(panelwardWorld).normalized;
            Require(Vector3.Dot(panelwardParent, Vector3.forward) >= 0.95f,
                $"Engine control '{identity}' panelward shaft must point predominantly " +
                "toward AircraftVisualRoot +Z.");
            return panelwardParent;
        }

        private static void EnsureWingFlapSelectorDirectBinding(
            GameObject root,
            Transform importedCockpit)
        {
            Transform stalePivot =
                importedCockpit.Find(LegacyWingFlapSelectorPivotName);
            if (stalePivot != null)
            {
                Transform selector = stalePivot.Find("Dynamic_Switch_007");
                Require(selector != null,
                    "Stale flap-selector pivot lost the represented source mesh.");
                selector.SetParent(importedCockpit, true);
                UnityEngine.Object.DestroyImmediate(stalePivot.gameObject);
            }

            RequirePath(root, WingFlapSelectorSourcePath);
        }

        private static Transform EnsureRudderPivot(
            GameObject root,
            Transform animatedControls)
        {
            Transform existing = root.transform.Find(RudderPivotPath);
            Transform source;
            if (existing != null)
            {
                Require(existing.parent == animatedControls,
                    "Authored rudder pivot has the wrong parent.");
                source = existing.Find("Cessna_Exterior_Body_MAT_0.008");
                Require(source != null,
                    "Authored rudder pivot lost its identity-audited source mesh.");
                source.SetParent(animatedControls, true);
            }
            else
            {
                source = RequirePath(root, RudderSourcePath);
            }
            Vector3 hinge = BoundsPointAtRudderLeadingEdge(
                animatedControls, source);
            Transform pivot = existing ??
                              CreatePivot(RudderPivotName, animatedControls, hinge);
            pivot.position = hinge;
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;
            source.SetParent(pivot, true);
            return pivot;
        }

        private static Transform CreatePivot(
            string name,
            Transform parent,
            Vector3 worldPosition)
        {
            Require(parent.Find(name) == null,
                $"Cannot create duplicate authored pivot '{name}'.");
            GameObject pivotObject = new GameObject(name);
            pivotObject.layer = parent.gameObject.layer;
            Transform pivot = pivotObject.transform;
            pivot.SetParent(parent, false);
            pivot.position = worldPosition;
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;
            GameObjectUtility.SetStaticEditorFlags(pivotObject, 0);
            return pivot;
        }

        private static Bounds RendererBounds(Transform source)
        {
            Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
            Require(renderers.Length == 1,
                $"Identity-audited source '{source.name}' must own exactly one renderer.");
            return renderers[0].bounds;
        }

        private static Vector3 BoundsPointAtRudderLeadingEdge(
            Transform parent,
            Transform rudderSource)
        {
            GetMeshBoundsInParent(
                parent, rudderSource, out Vector3 minimum, out Vector3 maximum);
            // Imported C172 convention after Unity conversion:
            //   X = rudder thickness, Y = vertical span, Z = chord/longitudinal.
            // A pivot at the X/Y centre and maximum Z therefore defines the
            // full-height forward/leading-edge hinge line when rotated about +Y.
            Vector3 hingeLocal = new Vector3(
                (minimum.x + maximum.x) * 0.5f,
                (minimum.y + maximum.y) * 0.5f,
                maximum.z);
            return parent.TransformPoint(hingeLocal);
        }

        private static void GetMeshBoundsInParent(
            Transform parent,
            Transform meshRoot,
            out Vector3 minimum,
            out Vector3 maximum)
        {
            MeshFilter[] filters = meshRoot.GetComponentsInChildren<MeshFilter>(true);
            Require(filters.Length == 1,
                "Identity-audited rudder must own exactly one mesh filter.");
            Require(filters[0].sharedMesh != null,
                "Identity-audited rudder mesh filter has no source mesh.");

            minimum = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            maximum = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            Bounds meshBounds = filters[0].sharedMesh.bounds;
            Vector3 center = meshBounds.center;
            Vector3 extents = meshBounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 meshLocalCorner = center + Vector3.Scale(
                    extents, new Vector3(x, y, z));
                Vector3 local = parent.InverseTransformPoint(
                    filters[0].transform.TransformPoint(meshLocalCorner));
                minimum = Vector3.Min(minimum, local);
                maximum = Vector3.Max(maximum, local);
            }
        }

        private static void RequireRudderLeadingEdgeHinge(Transform rudderPivot)
        {
            Require(rudderPivot != null, "Authored rudder pivot is required.");
            Require(rudderPivot.parent != null, "Authored rudder pivot requires a parent.");
            GetMeshBoundsInParent(
                rudderPivot.parent,
                rudderPivot,
                out Vector3 minimum,
                out Vector3 maximum);
            Vector3 size = maximum - minimum;
            Require(size.y > size.z && size.z > size.x,
                "Imported rudder geometry no longer matches the audited " +
                "vertical-Y, chord-Z, thickness-X convention.");
            Vector3 expected = new Vector3(
                (minimum.x + maximum.x) * 0.5f,
                (minimum.y + maximum.y) * 0.5f,
                maximum.z);
            Require(Vector3.Distance(rudderPivot.localPosition, expected) <= 0.0001f,
                "Rudder pivot is not on the vertical leading-edge hinge line.");
            Require(Quaternion.Angle(rudderPivot.localRotation, Quaternion.identity) <= 0.001f,
                "Rudder pivot axes must remain aligned to the authored aircraft parent.");
        }

        private static void AuthorRudderBinding(
            ProductionAircraftControlAnimator animator,
            Transform rudderPivot)
        {
            SerializedObject animatorObject = new SerializedObject(animator);
            SerializedProperty bindings = animatorObject.FindProperty("bindings");
            int rudderIndex = -1;
            int rudderCount = 0;
            for (int index = 0; index < bindings.arraySize; index++)
            {
                SerializedProperty candidate = bindings.GetArrayElementAtIndex(index);
                int channel = candidate.FindPropertyRelative("channel").enumValueIndex;
                if (channel != (int)ProductionAircraftControlAnimator.ControlChannel.Rudder)
                    continue;
                rudderIndex = index;
                rudderCount++;
            }
            Require(rudderCount <= 1,
                "Aircraft animator already has multiple rudder bindings.");
            if (rudderIndex < 0)
            {
                Require(bindings.arraySize == ExpectedLegacyAnimatorBindingCountBeforeRudder,
                    "Unexpected aircraft animator baseline; refusing a blind append.");
                rudderIndex = bindings.arraySize;
                bindings.InsertArrayElementAtIndex(rudderIndex);
            }

            SerializedProperty binding = bindings.GetArrayElementAtIndex(rudderIndex);
            binding.FindPropertyRelative("label").stringValue = RudderBindingLabel;
            binding.FindPropertyRelative("target").objectReferenceValue = rudderPivot;
            binding.FindPropertyRelative("channel").enumValueIndex =
                (int)ProductionAircraftControlAnimator.ControlChannel.Rudder;
            binding.FindPropertyRelative("localRotationAxis").vector3Value = Vector3.up;
            // Positive rudder input moves the trailing edge to aircraft right.
            binding.FindPropertyRelative("minimumAngleDegrees").floatValue =
                RudderMaximumTravelDegrees;
            binding.FindPropertyRelative("maximumAngleDegrees").floatValue =
                -RudderMaximumTravelDegrees;
            binding.FindPropertyRelative("localTranslationAxis").vector3Value = Vector3.zero;
            binding.FindPropertyRelative("minimumTranslationMeters").floatValue = 0f;
            binding.FindPropertyRelative("maximumTranslationMeters").floatValue = 0f;
            animatorObject.ApplyModifiedPropertiesWithoutUndo();
            animator.CaptureAuthoredRestPosesForValidation();
        }

        private static Transform RequirePath(GameObject root, string path)
        {
            Transform value = root.transform.Find(path);
            Require(value != null, $"Required authored transform is missing: {path}");
            return value;
        }

        private static T RequireExactlyOne<T>(GameObject root) where T : Component
        {
            T[] values = root.GetComponentsInChildren<T>(true);
            Require(values.Length == 1,
                $"Expected exactly one {typeof(T).Name}; found {values.Length}.");
            return values[0];
        }

        private static T RequireOrAddSingle<T>(GameObject target) where T : Component
        {
            T[] values = target.GetComponents<T>();
            Require(values.Length <= 1,
                $"Expected at most one {typeof(T).Name} on {target.name}; found {values.Length}.");
            return values.Length == 1 ? values[0] : target.AddComponent<T>();
        }

        private static string RelativePath(Transform root, Transform value)
        {
            if (value == root) return string.Empty;
            List<string> parts = new List<string>();
            Transform current = value;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            Require(current == root,
                $"Transform '{value.name}' is outside the product aircraft hierarchy.");
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
