using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Meta.XR;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using Oculus.Interaction.OVR;
using Oculus.Interaction.Surfaces;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.TouchInputV2;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.OpenXR;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Deterministic, Editor-only authoring for the product OVR concurrent
    /// hierarchy. The product player never creates or repairs XR hierarchy.
    /// </summary>
    public static class OvrConcurrentProductRigAuthoring
    {
        private const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        private const string OvrCameraRigPath =
            "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        private const string OvrInteractionRigPath =
            "Packages/com.meta.xr.sdk.interaction.ovr/Runtime/Prefabs/" +
            "OVRComprehensiveInteractionRig.prefab";
        private const string HandGrabInteractablePath =
            "Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/HandGrab/" +
            "HandGrabInteractable.prefab";
        private const string GrabInteractablePath =
            "Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/Grab/" +
            "GrabInteractable.prefab";
        private const string MetaFeatureSetId =
            "com.meta.openxr.featureset.metaxr";

        public static void Apply()
        {
            ConfigureProjectForConcurrentInput();

            GameObject root = PrefabUtility.LoadPrefabContents(
                AircraftPrefabPath);
            try
            {
                AuthorProductRig(root);
                if (!ValidateProductRig(root, out string error))
                    throw new InvalidOperationException(error);
                PrefabUtility.SaveAsPrefabAsset(root, AircraftPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                "[QuestFlight][OVRInput] Serialized product OVR concurrent " +
                "rig authored and validated.");
        }

        public static void Validate()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(
                AircraftPrefabPath);
            try
            {
                if (!ValidateProductRig(root, out string error))
                    throw new InvalidOperationException(error);
                Debug.Log(
                    "[QuestFlight][OVRInput] Serialized OVR concurrent rig " +
                    "validation passed.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Narrow recovery authoring pass. It replaces only the pilot-yoke
        /// pointable beneath the already-proven OVR concurrent rig.
        /// </summary>
        public static void ApplyConstrainedPilotYokeGrab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(
                AircraftPrefabPath);
            try
            {
                AuthorConstrainedPilotYokeGrab(root);
                if (!ValidateProductRig(root, out string error))
                    throw new InvalidOperationException(error);
                PrefabUtility.SaveAsPrefabAsset(root, AircraftPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                "[QuestFlight][OVRInput] Constrained pilot-yoke grab " +
                "authored without rebuilding the concurrent rig.");
        }

        public static void DumpRigContracts()
        {
            string output = Environment.GetEnvironmentVariable(
                "QUESTFLIGHT_OVR_DUMP");
            if (string.IsNullOrWhiteSpace(output))
            {
                output = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "../../ovr_rig_contracts.txt"));
            }

            var builder = new StringBuilder(65536);
            AppendPrefab(builder, OvrCameraRigPath);
            AppendPrefab(builder, OvrInteractionRigPath);
            AppendPrefab(builder, AircraftPrefabPath);
            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
            File.WriteAllText(output, builder.ToString());
            Debug.Log($"OVR rig contract written to {output}");
        }

        public static void DumpProductMaterialPaths()
        {
            string output = Environment.GetEnvironmentVariable(
                "QUESTFLIGHT_OVR_MATERIAL_DUMP");
            if (string.IsNullOrWhiteSpace(output))
                output = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "../../ovr_product_material_paths.txt"));
            GameObject root = PrefabUtility.LoadPrefabContents(
                AircraftPrefabPath);
            try
            {
                var lines = root.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => renderer.enabled &&
                        !renderer.forceRenderingOff &&
                        renderer.gameObject.activeInHierarchy)
                    .SelectMany(renderer => renderer.sharedMaterials.Select(
                        material => (material, renderer)))
                    .Where(item => item.material != null)
                    .OrderBy(item => item.material.name,
                        StringComparer.Ordinal)
                    .ThenBy(item => HierarchyPath(item.renderer.transform),
                        StringComparer.Ordinal)
                    .Select(item => item.material.name + " | " +
                        HierarchyPath(item.renderer.transform));
                File.WriteAllLines(output, lines);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log("OVR product material paths written to " + output);
        }

        private static void ConfigureProjectForConcurrentInput()
        {
            OVRProjectConfig config = OVRProjectConfig.CachedProjectConfig;
            if (config == null)
                throw new InvalidOperationException(
                    "OVRProjectConfig is unavailable after package import.");
            config.handTrackingSupport =
                OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            if (!config.targetDeviceTypes.Contains(
                    OVRProjectConfig.DeviceType.Quest3))
                config.targetDeviceTypes.Add(
                    OVRProjectConfig.DeviceType.Quest3);
            OVRProjectConfig.CommitProjectConfig(config);

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var featureSet =
                OpenXRFeatureSetManager.GetFeatureSetWithId(
                    BuildTargetGroup.Android,
                    MetaFeatureSetId);
            if (featureSet == null)
                throw new InvalidOperationException(
                    "Meta XR OpenXR feature set is unavailable.");
            featureSet.isEnabled = true;
            OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(
                BuildTargetGroup.Android);
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);

            EnableRequiredFeature(MetaXRFeature.featureId);
            EnableRequiredFeature(
                DetachedMetaQuestTouchPlusControllerProfile.featureId);
            EnableRequiredFeature(
                DetachedOculusTouchControllerProfile.featureId);

            OpenXRSettings settings =
                OpenXRSettings.GetSettingsForBuildTargetGroup(
                    BuildTargetGroup.Android);
            if (settings == null)
                throw new InvalidOperationException(
                    "Android OpenXR settings are unavailable.");
            settings.renderMode =
                OpenXRSettings.RenderMode.SinglePassInstanced;
            settings.latencyOptimization =
                OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static void EnableRequiredFeature(string featureId)
        {
            var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(
                BuildTargetGroup.Android,
                featureId);
            if (feature == null)
                throw new InvalidOperationException(
                    "Required Android OpenXR feature is missing: " +
                    featureId);
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        private static void AuthorProductRig(GameObject root)
        {
            Require(root != null &&
                    root.name == "ProductionC172AircraftRig",
                "The authored C172 product prefab is required.");
            AircraftReferenceFrameRig referenceFrame =
                root.GetComponent<AircraftReferenceFrameRig>();
            Require(referenceFrame != null,
                "AircraftReferenceFrameRig is missing.");

            Transform visualRoot = RequirePath(
                root.transform,
                "AircraftVisualRoot");
            Transform seat = RequirePath(
                visualRoot,
                "PilotSeatAnchor");
            Transform calibration = RequirePath(
                seat,
                "UserViewCalibrationOffset");
            Transform oldOrigin = calibration.Find("XR Origin");
            Transform existingOvr = calibration.Find("ProductOVRCameraRig");
            Require(oldOrigin != null || existingOvr != null,
                "Neither the accepted XR Origin nor product OVR rig exists.");

            Camera trackedCamera = root.GetComponentsInChildren<Camera>(true)
                .Single(camera => camera.name == "Main Camera");
            Transform leftProxy = FindDescendant(root.transform,
                TrackedXrControllerPoseDrivers.LeftControllerName);
            Transform rightProxy = FindDescendant(root.transform,
                TrackedXrControllerPoseDrivers.RightControllerName);
            Require(leftProxy != null && rightProxy != null,
                "Accepted left/right Touch proxy transforms are missing.");

            Transform ovrRoot;
            if (existingOvr == null)
            {
                GameObject prefab = RequireAsset<GameObject>(
                    OvrCameraRigPath);
                GameObject instance = (GameObject)
                    PrefabUtility.InstantiatePrefab(
                        prefab,
                        calibration);
                PrefabUtility.UnpackPrefabInstance(
                    instance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                instance.name = "ProductOVRCameraRig";
                instance.transform.SetLocalPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                ovrRoot = instance.transform;

                RemoveStockCameraComponents(ovrRoot);
                Transform centerEye = RequirePath(
                    ovrRoot,
                    "TrackingSpace/CenterEyeAnchor");
                trackedCamera.transform.SetParent(centerEye, false);
                trackedCamera.transform.SetLocalPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                trackedCamera.transform.localScale = Vector3.one;
                RemoveTrackedPoseDriver(trackedCamera.gameObject);

                Transform leftAnchor = RequirePath(
                    ovrRoot,
                    "TrackingSpace/LeftHandAnchor/LeftControllerAnchor");
                Transform rightAnchor = RequirePath(
                    ovrRoot,
                    "TrackingSpace/RightHandAnchor/RightControllerAnchor");
                leftProxy.SetParent(leftAnchor, false);
                leftProxy.SetLocalPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                leftProxy.localScale = Vector3.one;
                rightProxy.SetParent(rightAnchor, false);
                rightProxy.SetLocalPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                rightProxy.localScale = Vector3.one;
                RemoveTrackedPoseDriver(leftProxy.gameObject);
                RemoveTrackedPoseDriver(rightProxy.gameObject);

                if (oldOrigin != null)
                    UnityEngine.Object.DestroyImmediate(
                        oldOrigin.gameObject);
            }
            else
            {
                ovrRoot = existingOvr;
            }

            OVRCameraRig cameraRig =
                RequireSingle<OVRCameraRig>(ovrRoot.gameObject);
            // The accepted product Main Camera remains a child of the official
            // center-eye anchor. Prevent OVRCameraRig from enabling its own
            // automatically-created anchor cameras, which would double-render
            // the scene while retaining normal OVR anchor tracking.
            cameraRig.disableEyeAnchorCameras = true;
            EditorUtility.SetDirty(cameraRig);
            OVRManager manager =
                RequireSingle<OVRManager>(ovrRoot.gameObject);
            manager.trackingOriginType =
                OVRManager.TrackingOrigin.EyeLevel;
            manager.usePositionTracking = true;
            manager.SimultaneousHandsAndControllersEnabled = true;
            manager.launchSimultaneousHandsControllersOnStartup = true;
            manager.controllerDrivenHandPosesType =
                OVRManager.ControllerDrivenHandPosesType.Natural;
            EditorUtility.SetDirty(manager);

            Transform priorInteraction =
                ovrRoot.Find("OVRComprehensiveInteractionRig");
            if (priorInteraction != null)
                UnityEngine.Object.DestroyImmediate(
                    priorInteraction.gameObject);
            GameObject interactionPrefab = RequireAsset<GameObject>(
                OvrInteractionRigPath);
            GameObject interaction = (GameObject)
                PrefabUtility.InstantiatePrefab(
                    interactionPrefab,
                    ovrRoot);
            PrefabUtility.UnpackPrefabInstance(
                interaction,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            interaction.name = "OVRComprehensiveInteractionRig";
            interaction.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            interaction.transform.localScale = Vector3.one;
            InjectCameraRigWithoutClearingHands(interaction, cameraRig);
            ConfigureSampleConcurrentControllerStates(interaction.transform);
            DisableAircraftIncompatibleInteractions(interaction.transform);

            OvrPointerSourceRegistry registry =
                ovrRoot.GetComponent<OvrPointerSourceRegistry>();
            if (registry == null)
                registry = ovrRoot.gameObject
                    .AddComponent<OvrPointerSourceRegistry>();
            registry.ConfigureAuthoredSources(
                CollectProductSources(interaction.transform));

            OvrConcurrentInputTrace trace =
                ovrRoot.GetComponent<OvrConcurrentInputTrace>();
            if (trace == null)
                trace = ovrRoot.gameObject
                    .AddComponent<OvrConcurrentInputTrace>();
            trace.ConfigureAuthoredReferences(manager);

            Transform centerEyeFinal = RequirePath(
                ovrRoot,
                "TrackingSpace/CenterEyeAnchor");
            Transform leftFinal = RequirePath(
                ovrRoot,
                "TrackingSpace/LeftHandAnchor/LeftControllerAnchor/" +
                TrackedXrControllerPoseDrivers.LeftControllerName);
            Transform rightFinal = RequirePath(
                ovrRoot,
                "TrackingSpace/RightHandAnchor/RightControllerAnchor/" +
                TrackedXrControllerPoseDrivers.RightControllerName);
            Camera finalCamera = centerEyeFinal
                .GetComponentsInChildren<Camera>(true)
                .Single(camera => camera.name == "Main Camera");
            Require(finalCamera == trackedCamera,
                "The accepted Main Camera identity was not preserved.");

            referenceFrame.ConfigureAuthoredHierarchy(
                referenceFrame.CenterOfGravityReference,
                visualRoot,
                seat,
                calibration,
                ovrRoot,
                leftFinal,
                rightFinal,
                finalCamera);

            CockpitTouchInteractionV2Controller controller =
                root.GetComponentInChildren<
                    CockpitTouchInteractionV2Controller>(true);
            Require(controller != null &&
                    controller.BindingCount ==
                    CockpitTouchInteractionV2Authoring.ExpectedBindingCount,
                "The accepted nine-control cockpit binding catalog is missing.");
            AuthorCockpitPointables(
                controller,
                registry,
                controller.CommandConsumer);
            controller.enabled = false;

            EditorUtility.SetDirty(referenceFrame);
            EditorUtility.SetDirty(registry);
            EditorUtility.SetDirty(trace);
            EditorUtility.SetDirty(cameraRig);
        }

        private static void AuthorCockpitPointables(
            CockpitTouchInteractionV2Controller controller,
            OvrPointerSourceRegistry registry,
            MonoBehaviour commandConsumer)
        {
            CockpitTouchInteractionV2Controller.Binding[] bindings =
                controller.AuthoredBindings;
            Require(bindings != null,
                "The serialized cockpit binding array is missing.");
            foreach (CockpitTouchInteractionV2Controller.Binding binding
                     in bindings)
            {
                Transform target = binding.targetCollider.transform;
                string objectName = "OVR_" + binding.id;
                Transform prior = target.Find(objectName);
                if (prior != null)
                    UnityEngine.Object.DestroyImmediate(prior.gameObject);

                var root = new GameObject(objectName);
                root.transform.SetParent(target, false);
                root.transform.SetLocalPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                root.transform.localScale = Vector3.one;
                var pointables = new List<MonoBehaviour>(2);

                if (binding.mode ==
                    CockpitTouchInteractionV2Mode.GrabPose)
                {
                    Rigidbody body = target.GetComponent<Rigidbody>();
                    if (body == null)
                        body = target.gameObject.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    body.useGravity = false;
                    body.interpolation = RigidbodyInterpolation.None;

                    GameObject handPrefab = RequireAsset<GameObject>(
                        HandGrabInteractablePath);
                    GameObject handObject = (GameObject)
                        PrefabUtility.InstantiatePrefab(
                            handPrefab,
                            root.transform);
                    PrefabUtility.UnpackPrefabInstance(
                        handObject,
                        PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);
                    handObject.transform.SetLocalPositionAndRotation(
                        Vector3.zero,
                        Quaternion.identity);
                    handObject.transform.localScale = Vector3.one;
                    HandGrabInteractable handGrab =
                        handObject.GetComponent<HandGrabInteractable>();
                    handGrab.InjectRigidbody(body);
                    handGrab.InjectSupportedGrabTypes(GrabTypeFlags.All);
                    HandGrabPose grabPose = AuthorControlHandGrabPose(
                        root.transform,
                        target,
                        binding.targetCollider);
                    handGrab.InjectOptionalHandGrabPoses(
                        new List<HandGrabPose> { grabPose });
                    handGrab.InjectOptionalScoreModifier(
                        PoseMeasureParameters.DEFAULT);
                    pointables.Add(handGrab);

                    GameObject grabPrefab = RequireAsset<GameObject>(
                        GrabInteractablePath);
                    GameObject grabObject = (GameObject)
                        PrefabUtility.InstantiatePrefab(
                            grabPrefab,
                            root.transform);
                    PrefabUtility.UnpackPrefabInstance(
                        grabObject,
                        PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);
                    grabObject.transform.SetLocalPositionAndRotation(
                        Vector3.zero,
                        Quaternion.identity);
                    grabObject.transform.localScale = Vector3.one;
                    GrabInteractable grab =
                        grabObject.GetComponent<GrabInteractable>();
                    grab.InjectRigidbody(body);
                    pointables.Add(grab);
                }
                else
                {
                    BoxCollider box =
                        binding.targetCollider as BoxCollider;
                    Require(box != null,
                        "Cockpit OVR pointables require the accepted bounded BoxCollider catalog.");
                    var pokeObject = new GameObject("PokeInteractable");
                    pokeObject.transform.SetParent(root.transform, false);
                    pokeObject.transform.localPosition =
                        box.center;
                    PlaneSurface plane =
                        pokeObject.AddComponent<PlaneSurface>();
                    plane.InjectAllPlaneSurface(
                        PlaneSurface.NormalFacing.Backward,
                        true);
                    BoundsClipper clipper =
                        pokeObject.AddComponent<BoundsClipper>();
                    clipper.Size = box.size;
                    ClippedPlaneSurface clipped =
                        pokeObject.AddComponent<ClippedPlaneSurface>();
                    clipped.InjectAllClippedPlaneSurface(
                        plane,
                        new IBoundsClipper[] { clipper });
                    PokeInteractable poke =
                        pokeObject.AddComponent<PokeInteractable>();
                    poke.InjectAllPokeInteractable(clipped);
                    pointables.Add(poke);

                    var rayObject = new GameObject("RayInteractable");
                    rayObject.transform.SetParent(root.transform, false);
                    ColliderSurface surface =
                        rayObject.AddComponent<ColliderSurface>();
                    surface.InjectAllColliderSurface(
                        binding.targetCollider);
                    RayInteractable ray =
                        rayObject.AddComponent<RayInteractable>();
                    ray.InjectAllRayInteractable(surface);
                    pointables.Add(ray);
                }

                // Meta's interaction prefabs include editor/debug renderers.
                // They are not part of cockpit contact presentation and would
                // consume a runtime material without adding product feedback.
                foreach (Transform child in root.transform
                             .GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == "InteractableDebugVisual")
                        child.gameObject.SetActive(false);
                }

                OvrCockpitPointableAdapter adapter =
                    root.AddComponent<OvrCockpitPointableAdapter>();
                adapter.ConfigureAuthoredReferences(
                    binding.id,
                    binding.target,
                    binding.allowedHand,
                    binding.mode,
                    binding.normalizedValue,
                    binding.targetCollider,
                    registry,
                    commandConsumer,
                    pointables.ToArray());
                EditorUtility.SetDirty(adapter);
            }
        }

        private static void AuthorConstrainedPilotYokeGrab(
            GameObject root)
        {
            CockpitTouchInteractionV2Controller controller =
                RequireSingle<CockpitTouchInteractionV2Controller>(root);
            CockpitTouchInteractionV2Controller.Binding binding =
                controller.AuthoredBindings.Single(value =>
                    value.target ==
                    CockpitTouchInteractionV2Target.PilotYoke);
            Transform yoke = binding.targetCollider.transform;
            string objectName = "OVR_" + binding.id;
            Transform prior = yoke.Find(objectName);
            if (prior != null)
                UnityEngine.Object.DestroyImmediate(prior.gameObject);

            OvrPointerSourceRegistry registry =
                RequireSingle<OvrPointerSourceRegistry>(root);
            CockpitTouchInputV2Bridge bridge =
                controller.CommandConsumer as CockpitTouchInputV2Bridge;
            Require(bridge != null,
                "The serialized Touch V2 bridge is missing.");

            Bounds leftBounds;
            Bounds rightBounds;
            DerivePilotYokeGripBounds(
                yoke,
                out leftBounds,
                out rightBounds);

            var interactionRoot = new GameObject(objectName);
            interactionRoot.layer = yoke.gameObject.layer;
            interactionRoot.transform.SetParent(yoke, false);
            interactionRoot.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            interactionRoot.transform.localScale = Vector3.one;

            Rigidbody body = interactionRoot.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;

            BoxCollider leftGrip = AuthorYokeGripCollider(
                interactionRoot.transform,
                "LeftVerticalGrip",
                leftBounds);
            BoxCollider rightGrip = AuthorYokeGripCollider(
                interactionRoot.transform,
                "RightVerticalGrip",
                rightBounds);

            OvrConstrainedYokeGrabTransformer transformer =
                interactionRoot.AddComponent<
                    OvrConstrainedYokeGrabTransformer>();
            transformer.ConfigureAuthoredReferences(
                bridge.Coordinator,
                controller.transform,
                yoke,
                CockpitTouchInputV2Bridge.AuthoredYokeAxisLocal,
                CockpitTouchInputV2Bridge.AuthoredYokeAxisLocal,
                CockpitTouchInputV2Bridge.YokePitchHalfTravelMeters,
                CockpitTouchInputV2Bridge.YokeRollMaximumDegrees);

            Grabbable grabbable =
                interactionRoot.AddComponent<Grabbable>();
            grabbable.MaxGrabPoints = 1;
            grabbable.InjectOptionalOneGrabTransformer(transformer);
            grabbable.InjectOptionalTargetTransform(
                interactionRoot.transform);
            grabbable.InjectOptionalRigidbody(body);
            grabbable.InjectOptionalThrowWhenUnselected(false);
            grabbable.InjectOptionalKinematicWhileSelected(false);

            GameObject handPrefab = RequireAsset<GameObject>(
                HandGrabInteractablePath);
            GameObject handObject = (GameObject)
                PrefabUtility.InstantiatePrefab(
                    handPrefab,
                    interactionRoot.transform);
            PrefabUtility.UnpackPrefabInstance(
                handObject,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            handObject.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            handObject.transform.localScale = Vector3.one;
            HandGrabInteractable handGrab =
                handObject.GetComponent<HandGrabInteractable>();
            handGrab.InjectRigidbody(body);
            handGrab.InjectSupportedGrabTypes(GrabTypeFlags.All);
            handGrab.InjectOptionalPointableElement(grabbable);
            handGrab.InjectOptionalHandGrabPoses(
                new List<HandGrabPose>
                {
                    AuthorYokeHandGrabCandidate(
                        interactionRoot.transform,
                        yoke,
                        leftGrip,
                        "LeftGripCandidate"),
                    AuthorYokeHandGrabCandidate(
                        interactionRoot.transform,
                        yoke,
                        rightGrip,
                        "RightGripCandidate")
                });
            handGrab.InjectOptionalScoreModifier(
                PoseMeasureParameters.DEFAULT);
            handGrab.HandAlignment = HandAlignType.None;
            MoveAtSourceProvider handMovement =
                handObject.AddComponent<MoveAtSourceProvider>();
            handGrab.InjectOptionalMovementProvider(handMovement);

            GameObject grabPrefab = RequireAsset<GameObject>(
                GrabInteractablePath);
            GameObject grabObject = (GameObject)
                PrefabUtility.InstantiatePrefab(
                    grabPrefab,
                    interactionRoot.transform);
            PrefabUtility.UnpackPrefabInstance(
                grabObject,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            grabObject.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            grabObject.transform.localScale = Vector3.one;
            GrabInteractable grab =
                grabObject.GetComponent<GrabInteractable>();
            grab.InjectRigidbody(body);
            grab.InjectOptionalPointableElement(grabbable);
            grab.UseClosestPointAsGrabSource = false;
            grab.ResetGrabOnGrabsUpdated = false;

            foreach (Transform child in interactionRoot.transform
                         .GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "InteractableDebugVisual")
                    child.gameObject.SetActive(false);
            }

            OvrCockpitPointableAdapter adapter =
                interactionRoot.AddComponent<
                    OvrCockpitPointableAdapter>();
            adapter.ConfigureAuthoredReferences(
                binding.id,
                binding.target,
                binding.allowedHand,
                binding.mode,
                binding.normalizedValue,
                binding.targetCollider,
                registry,
                controller.CommandConsumer,
                new MonoBehaviour[] { handGrab, grab });
            // The Grabbable transformer is the sole yoke command path. Keep
            // the adapter serialized for catalog parity, but prevent the old
            // raw-pointer-orientation bridge from competing for authority.
            adapter.enabled = false;

            EditorUtility.SetDirty(body);
            EditorUtility.SetDirty(leftGrip);
            EditorUtility.SetDirty(rightGrip);
            EditorUtility.SetDirty(transformer);
            EditorUtility.SetDirty(grabbable);
            EditorUtility.SetDirty(handGrab);
            EditorUtility.SetDirty(grab);
            EditorUtility.SetDirty(adapter);
        }

        private static BoxCollider AuthorYokeGripCollider(
            Transform parent,
            string name,
            Bounds bounds)
        {
            var grip = new GameObject(name);
            grip.layer = parent.gameObject.layer;
            grip.transform.SetParent(parent, false);
            grip.transform.localPosition = bounds.center;
            grip.transform.localRotation = Quaternion.identity;
            grip.transform.localScale = Vector3.one;
            BoxCollider collider = grip.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = bounds.size + Vector3.one * 0.012f;
            collider.isTrigger = true;
            return collider;
        }

        private static HandGrabPose AuthorYokeHandGrabCandidate(
            Transform parent,
            Transform yoke,
            BoxCollider grip,
            string name)
        {
            var poseObject = new GameObject(name);
            poseObject.transform.SetParent(parent, false);
            poseObject.transform.localPosition =
                grip.transform.localPosition + grip.center;
            poseObject.transform.localRotation = Quaternion.identity;
            poseObject.transform.localScale = Vector3.one;
            HandGrabPose pose = poseObject.AddComponent<HandGrabPose>();
            pose.InjectAllHandGrabPose(yoke);
            // The candidate is positional only. It makes either real handle
            // reliably discoverable without imposing a wrist/finger pose.
            pose.InjectOptionalHandPose(null);
            return pose;
        }

        private static void DerivePilotYokeGripBounds(
            Transform yoke,
            out Bounds left,
            out Bounds right)
        {
            var points = new List<Vector3>(2048);
            foreach (MeshFilter filter in yoke
                         .GetComponentsInChildren<MeshFilter>(true))
            {
                if (RelativePath(yoke, filter.transform).StartsWith(
                        "OVR_", StringComparison.Ordinal) ||
                    filter.sharedMesh == null)
                    continue;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    points.Add(yoke.InverseTransformPoint(
                        filter.transform.TransformPoint(vertex)));
                }
            }
            Require(points.Count > 0,
                "Pilot-yoke grip bounds require render geometry.");

            float minimumX = points.Min(point => point.x);
            float maximumX = points.Max(point => point.x);
            float centerX = (minimumX + maximumX) * 0.5f;
            float halfWidth = (maximumX - minimumX) * 0.5f;
            float innerOffset = halfWidth * 0.68f;
            List<Vector3> leftPoints = points.Where(point =>
                point.x <= centerX - innerOffset).ToList();
            List<Vector3> rightPoints = points.Where(point =>
                point.x >= centerX + innerOffset).ToList();
            Require(leftPoints.Count >= 8 && rightPoints.Count >= 8,
                "Pilot-yoke vertical handle geometry is incomplete.");

            left = BoundsFromPoints(leftPoints);
            right = BoundsFromPoints(rightPoints);
            Require(left.size.y > left.size.x &&
                    right.size.y > right.size.x &&
                    left.center.x < centerX &&
                    right.center.x > centerX,
                "Derived pilot-yoke grips are not vertical left/right handles.");
        }

        private static Bounds BoundsFromPoints(
            IReadOnlyList<Vector3> points)
        {
            Bounds bounds = new Bounds(points[0], Vector3.zero);
            for (int i = 1; i < points.Count; i++)
                bounds.Encapsulate(points[i]);
            return bounds;
        }

        private static HandGrabPose AuthorControlHandGrabPose(
            Transform pointableRoot,
            Transform control,
            Collider targetCollider)
        {
            BoxCollider box = targetCollider as BoxCollider;
            Require(box != null,
                $"Control '{control.name}' requires its audited BoxCollider.");

            var poseObject = new GameObject("HandGrabPose");
            poseObject.transform.SetParent(pointableRoot, false);
            // The product aircraft faces local +Z. Place the wrist target just
            // aft of the visible control, toward the seated pilot. Candidate
            // scoring is translation-only so natural wrist rotation remains
            // unconstrained while acquisition stays tied to the real handle.
            poseObject.transform.localPosition = box.center +
                Vector3.back * (box.size.z * 0.5f + 0.025f);
            poseObject.transform.localRotation = Quaternion.identity;
            poseObject.transform.localScale = Vector3.one;
            HandGrabPose pose = poseObject.AddComponent<HandGrabPose>();
            pose.InjectAllHandGrabPose(control);
            pose.InjectOptionalHandPose(null);
            return pose;
        }

        private static void ConfigureSampleConcurrentControllerStates(
            Transform interactionRoot)
        {
            FromOVRControllerDataSource[] sources = interactionRoot
                .GetComponentsInChildren<FromOVRControllerDataSource>(true);
            Require(sources.Length == 2,
                "Meta's left/right OVR controller data sources are required.");

            foreach (FromOVRControllerDataSource source in sources)
            {
                bool isLeft = source.name.EndsWith(
                    "Left", StringComparison.Ordinal);
                bool isRight = source.name.EndsWith(
                    "Right", StringComparison.Ordinal);
                Require(isLeft ^ isRight,
                    "OVR controller data-source handedness is ambiguous: " +
                    source.name);

                OVRControllerInHandActiveState state = source
                    .GetComponent<OVRControllerInHandActiveState>();
                if (state == null)
                    state = source.gameObject
                        .AddComponent<OVRControllerInHandActiveState>();
                state.HandType = isLeft
                    ? OVRInput.Hand.HandLeft
                    : OVRInput.Hand.HandRight;
                state.ShowState =
                    OVRInput.InputDeviceShowState.ControllerInHand;

                ActiveStateTracker tracker = source
                    .GetComponent<ActiveStateTracker>();
                if (tracker == null)
                    tracker = source.gameObject
                        .AddComponent<ActiveStateTracker>();
                tracker.InjectAllActiveStateTracker(state);
                tracker.InjectOptionalIncludeChildrenAsDependents(false);
                tracker.InjectOptionalGameObjects(new List<GameObject>());
                tracker.InjectOptionalMonoBehaviours(
                    new List<MonoBehaviour> { source });
                EditorUtility.SetDirty(state);
                EditorUtility.SetDirty(tracker);
            }
        }

        private static OvrPointerSourceRegistry.Source[]
            CollectProductSources(Transform interactionRoot)
        {
            var results = new List<(
                string path,
                OvrPointerSourceRegistry.Source source)>();
            foreach (MonoBehaviour behaviour in
                     interactionRoot.GetComponentsInChildren<
                         MonoBehaviour>(true))
            {
                if (!(behaviour is IInteractorView)) continue;
                string path = RelativePath(
                    interactionRoot,
                    behaviour.transform);
                CockpitInputSourceKind kind;
                if (behaviour is PokeInteractor ||
                    (behaviour is HandGrabInteractor &&
                     path.Contains(
                         "/Hand and No Controller/",
                         StringComparison.Ordinal)))
                {
                    kind = CockpitInputSourceKind.OvrHand;
                }
                else if (behaviour is RayInteractor ||
                         behaviour is GrabInteractor ||
                         (behaviour is HandGrabInteractor &&
                          path.Contains(
                              "/Controller and Hand/",
                              StringComparison.Ordinal)))
                {
                    kind = CockpitInputSourceKind.OvrTouchController;
                }
                else
                {
                    continue;
                }

                CockpitTouchInteractionV2Hand hand;
                if (path.StartsWith(
                        "ComprehensiveInteractorsLeft/",
                        StringComparison.Ordinal))
                    hand = CockpitTouchInteractionV2Hand.Left;
                else if (path.StartsWith(
                             "ComprehensiveInteractorsRight/",
                             StringComparison.Ordinal))
                    hand = CockpitTouchInteractionV2Hand.Right;
                else
                    continue;

                results.Add((path,
                    new OvrPointerSourceRegistry.Source
                    {
                        interactor = behaviour,
                        hand = hand,
                        kind = kind
                    }));
            }

            OvrPointerSourceRegistry.Source[] sources = results
                .OrderBy(item => item.path, StringComparer.Ordinal)
                .Select(item => item.source)
                .ToArray();
            Require(sources.Count(source =>
                    source.kind == CockpitInputSourceKind.OvrHand) >= 4,
                "Official left/right Meta hand poke/grab sources are missing.");
            Require(sources.Count(source =>
                    source.kind ==
                    CockpitInputSourceKind.OvrTouchController) >= 4,
                "Official left/right Meta controller ray/grab sources are missing.");
            return sources;
        }

        private static void DisableAircraftIncompatibleInteractions(
            Transform interactionRoot)
        {
            string[] disabledNames =
            {
                "Locomotor",
                "LocomotionControllerInteractorGroup",
                "LocomotionOutput",
                "MicroGesturesLocomotionHandInteractorGroup",
                "DistanceHandGrabInteractor",
                "DistanceControllerHandGrabInteractor",
                "TouchHandGrabInteractor",
                "PokeLocation",
                "MetaQuestTouchPro_Left",
                "MetaQuestTouchPro_Right",
                "OculusTouchForQuest2LeftModel",
                "OculusTouchForQuest2RightModel",
                "OculusTouchForQuestAndRiftS_Left",
                "OculusTouchForQuestAndRiftS_Right",
                "left_touch_controller_model_skel",
                "right_touch_controller_model_skel"
            };
            foreach (Transform transform in
                     interactionRoot.GetComponentsInChildren<Transform>(true))
            {
                if (disabledNames.Contains(
                        transform.name,
                        StringComparer.Ordinal))
                    transform.gameObject.SetActive(false);
            }

            PruneLegacyControllerModels(interactionRoot);
        }

        private static void PruneLegacyControllerModels(
            Transform interactionRoot)
        {
            string[] legacyModelNames =
            {
                "MetaQuestTouchPro_Left",
                "MetaQuestTouchPro_Right",
                "OculusTouchForQuest2LeftModel",
                "OculusTouchForQuest2RightModel",
                "OculusTouchForQuestAndRiftS_Left",
                "OculusTouchForQuestAndRiftS_Right",
                "left_touch_controller_model_skel",
                "right_touch_controller_model_skel"
            };

            foreach (OVRControllerHelper helper in interactionRoot
                         .GetComponentsInChildren<OVRControllerHelper>(true))
            {
                Transform leftTransform = helper.transform
                    .GetComponentsInChildren<Transform>(true)
                    .Single(value =>
                        value.name == "MetaQuestTouchPlus_Left");
                Transform rightTransform = helper.transform
                    .GetComponentsInChildren<Transform>(true)
                    .Single(value =>
                        value.name == "MetaQuestTouchPlus_Right");
                GameObject left = leftTransform.gameObject;
                GameObject right = rightTransform.gameObject;

                // Standalone Quest 3 is the only shipping target. Point every
                // fallback slot at the retained Touch Plus pair so Meta's
                // helper remains null-safe while legacy device meshes and
                // textures leave the Android dependency closure.
                helper.m_modelOculusTouchQuestAndRiftSLeftController = left;
                helper.m_modelOculusTouchQuestAndRiftSRightController = right;
                helper.m_modelOculusTouchRiftLeftController = left;
                helper.m_modelOculusTouchRiftRightController = right;
                helper.m_modelOculusTouchQuest2LeftController = left;
                helper.m_modelOculusTouchQuest2RightController = right;
                helper.m_modelMetaTouchProLeftController = left;
                helper.m_modelMetaTouchProRightController = right;
                helper.m_modelMetaTouchPlusLeftController = left;
                helper.m_modelMetaTouchPlusRightController = right;
                EditorUtility.SetDirty(helper);

                Transform[] obsolete = helper.transform
                    .GetComponentsInChildren<Transform>(true)
                    .Where(value => legacyModelNames.Contains(
                        value.name, StringComparer.Ordinal))
                    .ToArray();
                foreach (Transform value in obsolete)
                    UnityEngine.Object.DestroyImmediate(value.gameObject);
            }
        }

        private static void InjectCameraRigWithoutClearingHands(
            GameObject interaction,
            OVRCameraRig cameraRig)
        {
            OVRCameraRigRef cameraRigRef =
                RequireSingle<OVRCameraRigRef>(interaction);
            var serialized = new SerializedObject(cameraRigRef);
            SerializedProperty property =
                serialized.FindProperty("_ovrCameraRig");
            Require(property != null,
                "Meta OVRCameraRigRef serialized field is unavailable.");
            property.objectReferenceValue = cameraRig;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Require(
                serialized.FindProperty("_leftHand")
                    .objectReferenceValue != null &&
                serialized.FindProperty("_rightHand")
                    .objectReferenceValue != null,
                "Official OVR hand references were unexpectedly cleared.");
            EditorUtility.SetDirty(cameraRigRef);
        }

        private static void RemoveStockCameraComponents(Transform ovrRoot)
        {
            foreach (Camera camera in
                     ovrRoot.GetComponentsInChildren<Camera>(true))
                UnityEngine.Object.DestroyImmediate(camera);
            foreach (AudioListener listener in
                     ovrRoot.GetComponentsInChildren<AudioListener>(true))
                UnityEngine.Object.DestroyImmediate(listener);
        }

        private static void RemoveTrackedPoseDriver(GameObject value)
        {
            foreach (TrackedPoseDriver driver in
                     value.GetComponents<TrackedPoseDriver>())
                UnityEngine.Object.DestroyImmediate(driver);
        }

        public static bool ValidateProductRig(
            GameObject root,
            out string error)
        {
            try
            {
                Require(root != null,
                    "The product aircraft prefab is unavailable.");
                AircraftReferenceFrameRig reference =
                    RequireSingle<AircraftReferenceFrameRig>(root);
                Require(reference.XrOrigin != null &&
                        reference.XrOrigin.name ==
                        "ProductOVRCameraRig",
                    "AircraftReferenceFrameRig does not reference ProductOVRCameraRig.");
                Require(reference.ValidateHierarchy(),
                    "The authored aircraft/seat/OVR reference frame is invalid.");
                Require(root.GetComponentsInChildren<OVRCameraRig>(true)
                        .Length == 1,
                    "Exactly one OVRCameraRig is required.");
                Require(root.GetComponentsInChildren<OVRManager>(true)
                        .Length == 1,
                    "Exactly one OVRManager is required.");
                OVRManager manager =
                    root.GetComponentInChildren<OVRManager>(true);
                OVRCameraRig cameraRig =
                    root.GetComponentInChildren<OVRCameraRig>(true);
                Require(cameraRig.disableEyeAnchorCameras,
                    "OVR anchor cameras must remain disabled in favor of the accepted Main Camera.");
                Require(manager.trackingOriginType ==
                            OVRManager.TrackingOrigin.EyeLevel &&
                        manager.SimultaneousHandsAndControllersEnabled &&
                        manager.launchSimultaneousHandsControllersOnStartup &&
                        manager.controllerDrivenHandPosesType ==
                            OVRManager.ControllerDrivenHandPosesType.Natural,
                    "OVR seated concurrent settings are not serialized.");
                Camera[] productCameras = root
                    .GetComponentsInChildren<Camera>(true)
                    .Where(value => value.name == "Main Camera")
                    .ToArray();
                Require(productCameras.Length == 1 &&
                        root.GetComponentsInChildren<AudioListener>(true)
                            .Length == 1,
                    "Exactly one tracked Main Camera/listener is required.");
                Camera camera = productCameras[0];
                Require(camera.name == "Main Camera" &&
                        camera.GetComponent<TrackedPoseDriver>() == null &&
                        TrackedXrCameraPoseDriver.IsOwnedByOvrCameraRig(
                            camera.transform),
                    "The accepted Main Camera is not solely OVR-owned.");

                OvrPointerSourceRegistry registry =
                    RequireSingle<OvrPointerSourceRegistry>(root);
                Require(registry.ValidateAuthoredSources(
                        out string registryError),
                    registryError);
                OvrCockpitPointableAdapter[] adapters =
                    root.GetComponentsInChildren<
                        OvrCockpitPointableAdapter>(true);
                Require(adapters.Length ==
                        CockpitTouchInteractionV2Authoring
                            .ExpectedBindingCount,
                    "Exactly nine represented OVR cockpit adapters are required.");
                foreach (OvrCockpitPointableAdapter adapter in adapters)
                {
                    Require(adapter.ValidateAuthoredReferences(
                        out string adapterError), adapterError);
                    bool continuous =
                        adapter.Target ==
                            CockpitTouchInteractionV2Target.PilotYoke ||
                        adapter.Target ==
                            CockpitTouchInteractionV2Target.Throttle ||
                        adapter.Target ==
                            CockpitTouchInteractionV2Target.Mixture;
                    Require(
                        adapter.PointableCount == 2,
                        $"OVR adapter '{adapter.name}' has an incomplete Meta pointable set.");
                    if (adapter.Target ==
                        CockpitTouchInteractionV2Target.PilotYoke)
                    {
                        HandGrabInteractable handGrab = adapter
                            .GetComponentInChildren<
                                HandGrabInteractable>(true);
                        Grabbable grabbable = adapter
                            .GetComponent<Grabbable>();
                        OvrConstrainedYokeGrabTransformer transformer =
                            adapter.GetComponent<
                                OvrConstrainedYokeGrabTransformer>();
                        Require(!adapter.enabled,
                            "The rejected raw-pose yoke adapter must remain disabled.");
                        Require(handGrab != null &&
                                handGrab.HandAlignment ==
                                    HandAlignType.None &&
                                handGrab.HandGrabPoses.Count == 2 &&
                                handGrab.HandGrabPoses.All(value =>
                                    value != null &&
                                    !value.UsesHandPose()) &&
                                handGrab.GetComponent<
                                    MoveAtSourceProvider>() != null,
                            "The pilot yoke requires two positional grip candidates while preserving the physical hand pose without an alignment tween.");
                        Require(grabbable != null &&
                                grabbable.MaxGrabPoints == 1,
                            "The pilot yoke requires one single-owner Meta Grabbable.");
                        Require(transformer != null,
                            "The constrained Meta yoke transformer is missing.");
                        Require(transformer.ValidateAuthoredReferences(
                                out string transformerError),
                            transformerError);
                        BoxCollider[] grips = adapter
                            .GetComponentsInChildren<BoxCollider>(true)
                            .Where(value =>
                                value.name == "LeftVerticalGrip" ||
                                value.name == "RightVerticalGrip")
                            .ToArray();
                        Require(grips.Length == 2 &&
                                grips.All(value =>
                                    value.enabled &&
                                    value.isTrigger &&
                                    value.size.y > value.size.x),
                            "The pilot yoke requires two derived vertical grip volumes.");
                    }
                    else if (continuous)
                    {
                        HandGrabInteractable handGrab = adapter
                            .GetComponentInChildren<HandGrabInteractable>(true);
                        Require(handGrab != null &&
                                handGrab.HandGrabPoses.Count == 1 &&
                                handGrab.HandGrabPoses[0] != null,
                            $"OVR adapter '{adapter.name}' requires one authored hand-grab pose.");
                    }
                }
                OVRControllerInHandActiveState[] inHandStates = root
                    .GetComponentsInChildren<OVRControllerInHandActiveState>(
                        true);
                Require(inHandStates.Length == 2 &&
                        inHandStates.All(value => value.ShowState ==
                            OVRInput.InputDeviceShowState.ControllerInHand),
                    "Meta's two controller-in-hand active states are required.");
                TouchHandGrabInteractor[] touchHandInteractors = root
                    .GetComponentsInChildren<TouchHandGrabInteractor>(true);
                Require(touchHandInteractors.Length == 2 &&
                        touchHandInteractors.All(value =>
                            !value.gameObject.activeInHierarchy),
                    "The rejected touch-hand grab experiment must stay disabled.");
                CockpitTouchInteractionV2Controller legacyController =
                    RequireSingle<CockpitTouchInteractionV2Controller>(root);
                Require(!legacyController.enabled,
                    "The superseded custom controller interaction loop must be disabled.");
                Require(FindDescendant(root.transform, "Locomotor") == null ||
                        !FindDescendant(root.transform, "Locomotor")
                            .gameObject.activeSelf,
                    "Meta locomotion must be disabled in the aircraft.");
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static T RequireAsset<T>(string path)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Require(asset != null, "Required asset is missing: " + path);
            return asset;
        }

        private static T RequireSingle<T>(GameObject root)
            where T : Component
        {
            T[] values = root.GetComponentsInChildren<T>(true);
            Require(values.Length == 1,
                $"Expected one {typeof(T).Name}; found {values.Length}.");
            return values[0];
        }

        private static Transform RequirePath(Transform root, string path)
        {
            Transform value = root.Find(path);
            Require(value != null,
                "Required authored path is missing: " + path);
            return value;
        }

        private static Transform FindDescendant(
            Transform root,
            string name)
        {
            foreach (Transform value in
                     root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(value.name, name,
                        StringComparison.Ordinal))
                    return value;
            }
            return null;
        }

        private static string RelativePath(
            Transform root,
            Transform value)
        {
            var parts = new Stack<string>();
            for (Transform current = value;
                 current != null && current != root;
                 current = current.parent)
                parts.Push(current.name);
            return string.Join("/", parts);
        }

        private static string HierarchyPath(Transform value)
        {
            var parts = new Stack<string>();
            for (Transform current = value; current != null;
                 current = current.parent)
                parts.Push(current.name);
            return string.Join("/", parts);
        }

        private static void AppendPrefab(
            StringBuilder builder,
            string assetPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                assetPath);
            if (prefab == null)
                throw new InvalidOperationException(
                    "Required prefab is missing: " + assetPath);
            builder.AppendLine(assetPath);
            AppendTransform(builder, prefab.transform, string.Empty);
            builder.AppendLine();
        }

        private static void AppendTransform(
            StringBuilder builder,
            Transform transform,
            string indent)
        {
            builder.Append(indent).Append(transform.name)
                .Append(" [active=")
                .Append(transform.gameObject.activeSelf).AppendLine("]");
            foreach (Component component in
                     transform.GetComponents<Component>())
            {
                if (component != null && !(component is Transform))
                    builder.Append(indent).Append("  - ")
                        .AppendLine(component.GetType().FullName);
            }
            for (int i = 0; i < transform.childCount; i++)
                AppendTransform(builder, transform.GetChild(i),
                    indent + "  ");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
