using System;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.TouchInputV2;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Editor-only integration recipe for one centralized Touch interaction
    /// loop. It adds only bounded trigger colliders to already-authored,
    /// positively represented controls. A serialized authoritative command
    /// consumer is mandatory; authoring fails rather than creating a dead or
    /// speculative control path.
    /// </summary>
    public static class CockpitTouchInteractionV2Authoring
    {
        public const string HostPath =
            "AircraftVisualRoot/AnimatedControlSurfaces";
        public const string LeftControllerPath =
            "AircraftVisualRoot/PilotSeatAnchor/UserViewCalibrationOffset/" +
            "XR Origin/Left Touch Controller";
        public const string RightControllerPath =
            "AircraftVisualRoot/PilotSeatAnchor/UserViewCalibrationOffset/" +
            "XR Origin/Right Touch Controller";
        public const string PilotYokeTargetPath =
            HostPath + "/PilotYokePitchPivot/PilotYokeRollPivot";
        public const string ThrottleTargetPath =
            "AircraftVisualRoot/ImportedCockpitInterior/ThrottleTravelPivot";
        public const string MixtureTargetPath =
            "AircraftVisualRoot/ImportedCockpitInterior/MixtureTravelPivot";

        public const int ExpectedBindingCount = 9;
        public const int QueueCapacity = 32;
        public const float ColliderPaddingMeters = 0.008f;
        public const float MaximumRayDistanceMeters = 0.8f;

        public static void AuthorPrefabContents(
            GameObject root,
            MonoBehaviour authoritativeCommandConsumer)
        {
            Require(root != null, "Aircraft prefab contents are required.");
            Require(root.name == "ProductionC172AircraftRig",
                "Touch V2 authoring may only modify ProductionC172AircraftRig.");
            Require(
                authoritativeCommandConsumer is
                    ICockpitTouchInteractionV2CommandConsumer,
                "Touch V2 authoring requires a serialized authoritative command consumer.");
            Require(
                authoritativeCommandConsumer.transform.IsChildOf(root.transform) ||
                authoritativeCommandConsumer.transform == root.transform,
                "The Touch command consumer must belong to the authored aircraft prefab.");

            Transform host = RequirePath(root, HostPath);
            Transform leftController = RequirePath(root, LeftControllerPath);
            Transform rightController = RequirePath(root, RightControllerPath);
            Transform pilotYoke = RequirePath(root, PilotYokeTargetPath);
            Transform throttle = RequirePath(root, ThrottleTargetPath);
            Transform mixture = RequirePath(root, MixtureTargetPath);
            Transform altBatMaster = RequirePath(
                root, CockpitDynamicsIntegrationAuthoring.AltBatMasterPath);
            Transform primer = RequirePath(
                root, CockpitDynamicsIntegrationAuthoring.PrimerPath);
            Transform ignition = RequirePath(
                root, CockpitDynamicsIntegrationAuthoring.IgnitionSwitchPath);
            Transform avionics = RequirePath(
                root, CockpitDynamicsIntegrationAuthoring.AvionicsMasterPath);
            Transform flapSelector = RequirePath(
                root,
                CockpitDynamicsIntegrationAuthoring.WingFlapSelectorSourcePath);
            Transform fuelSelector = RequirePath(
                root, CockpitDynamicsIntegrationAuthoring.FuelSelectorPath);

            BoxCollider yokeCollider =
                EnsureBoundedInteractionCollider(pilotYoke);
            BoxCollider throttleCollider =
                EnsureBoundedInteractionCollider(throttle);
            BoxCollider mixtureCollider =
                EnsureBoundedInteractionCollider(mixture);
            BoxCollider altBatCollider =
                EnsureBoundedInteractionCollider(altBatMaster);
            BoxCollider primerCollider =
                EnsureBoundedInteractionCollider(primer);
            BoxCollider ignitionCollider =
                EnsureBoundedInteractionCollider(ignition);
            BoxCollider avionicsCollider =
                EnsureBoundedInteractionCollider(avionics);
            BoxCollider flapCollider =
                EnsureBoundedInteractionCollider(flapSelector);
            BoxCollider fuelCollider =
                EnsureBoundedInteractionCollider(fuelSelector);
            LayerMask layers =
                (1 << yokeCollider.gameObject.layer) |
                (1 << throttleCollider.gameObject.layer) |
                (1 << mixtureCollider.gameObject.layer) |
                (1 << altBatCollider.gameObject.layer) |
                (1 << primerCollider.gameObject.layer) |
                (1 << ignitionCollider.gameObject.layer) |
                (1 << avionicsCollider.gameObject.layer) |
                (1 << flapCollider.gameObject.layer) |
                (1 << fuelCollider.gameObject.layer);

            CockpitTouchInteractionV2Controller controller =
                RequireOrAddSingleController(root, host);
            controller.ConfigureAuthoredReferences(
                leftController,
                rightController,
                layers,
                MaximumRayDistanceMeters,
                new[]
                {
                    new CockpitTouchInteractionV2Controller.Binding
                    {
                        id = CockpitTouchInputV2Bridge.PilotYokeBindingId,
                        targetCollider = yokeCollider,
                        target = CockpitTouchInteractionV2Target.PilotYoke,
                        allowedHand = CockpitTouchInteractionV2Hand.Left,
                        mode = CockpitTouchInteractionV2Mode.GrabPose
                    },
                    new CockpitTouchInteractionV2Controller.Binding
                    {
                        id = CockpitTouchInputV2Bridge.ThrottleBindingId,
                        targetCollider = throttleCollider,
                        target = CockpitTouchInteractionV2Target.Throttle,
                        allowedHand = CockpitTouchInteractionV2Hand.Right,
                        mode = CockpitTouchInteractionV2Mode.GrabPose
                    },
                    new CockpitTouchInteractionV2Controller.Binding
                    {
                        id = CockpitTouchInputV2Bridge.MixtureBindingId,
                        targetCollider = mixtureCollider,
                        target = CockpitTouchInteractionV2Target.Mixture,
                        allowedHand = CockpitTouchInteractionV2Hand.Right,
                        mode = CockpitTouchInteractionV2Mode.GrabPose
                    },
                    new CockpitTouchInteractionV2Controller.Binding
                    {
                        id = CockpitTouchInputV2Bridge.AltBatMasterBindingId,
                        targetCollider = altBatCollider,
                        target = CockpitTouchInteractionV2Target.AltBatMaster,
                        allowedHand = CockpitTouchInteractionV2Hand.Either,
                        mode = CockpitTouchInteractionV2Mode.Toggle
                    },
                    new CockpitTouchInteractionV2Controller.Binding
                    {
                        id = CockpitTouchInputV2Bridge.PrimerBindingIdPrefix,
                        targetCollider = primerCollider,
                        target = CockpitTouchInteractionV2Target.Primer,
                        allowedHand = CockpitTouchInteractionV2Hand.Either,
                        mode = CockpitTouchInteractionV2Mode.Momentary
                    },
                    new CockpitTouchInteractionV2Controller.Binding
                    {
                        id = CockpitTouchInputV2Bridge.IgnitionBindingIdPrefix,
                        targetCollider = ignitionCollider,
                        target = CockpitTouchInteractionV2Target.IgnitionStart,
                        allowedHand = CockpitTouchInteractionV2Hand.Either,
                        mode = CockpitTouchInteractionV2Mode.Toggle
                    },
                    new CockpitTouchInteractionV2Controller.Binding
                    {
                        id = CockpitTouchInputV2Bridge.AvionicsPowerBindingId,
                        targetCollider = avionicsCollider,
                        target = CockpitTouchInteractionV2Target.AvionicsPower,
                        allowedHand = CockpitTouchInteractionV2Hand.Either,
                        mode = CockpitTouchInteractionV2Mode.Toggle
                    },
                    new CockpitTouchInteractionV2Controller.Binding
                    {
                        id = CockpitTouchInputV2Bridge.FlapSelectorBindingIdPrefix,
                        targetCollider = flapCollider,
                        target = CockpitTouchInteractionV2Target.FlapSelector,
                        allowedHand = CockpitTouchInteractionV2Hand.Either,
                        mode = CockpitTouchInteractionV2Mode.Toggle
                    },
                    new CockpitTouchInteractionV2Controller.Binding
                    {
                        id = CockpitTouchInputV2Bridge.FuelSelectorBindingIdPrefix,
                        targetCollider = fuelCollider,
                        target = CockpitTouchInteractionV2Target.FuelSelector,
                        allowedHand = CockpitTouchInteractionV2Hand.Either,
                        mode = CockpitTouchInteractionV2Mode.Toggle
                    }
                },
                authoritativeCommandConsumer,
                QueueCapacity);

            EditorUtility.SetDirty(yokeCollider);
            EditorUtility.SetDirty(throttleCollider);
            EditorUtility.SetDirty(mixtureCollider);
            EditorUtility.SetDirty(altBatCollider);
            EditorUtility.SetDirty(primerCollider);
            EditorUtility.SetDirty(ignitionCollider);
            EditorUtility.SetDirty(avionicsCollider);
            EditorUtility.SetDirty(flapCollider);
            EditorUtility.SetDirty(fuelCollider);
            EditorUtility.SetDirty(controller);

            if (!ValidatePrefabContents(root, out string error))
                throw new InvalidOperationException(error);
        }

        public static bool ValidatePrefabContents(
            GameObject root,
            out string error)
        {
            try
            {
                Require(root != null, "Aircraft prefab contents are required.");
                Transform host = RequirePath(root, HostPath);
                Transform leftController =
                    RequirePath(root, LeftControllerPath);
                Transform rightController =
                    RequirePath(root, RightControllerPath);
                Transform pilotYoke =
                    RequirePath(root, PilotYokeTargetPath);
                Transform throttle =
                    RequirePath(root, ThrottleTargetPath);
                Transform mixture =
                    RequirePath(root, MixtureTargetPath);
                Transform altBatMaster = RequirePath(
                    root, CockpitDynamicsIntegrationAuthoring.AltBatMasterPath);
                Transform primer = RequirePath(
                    root, CockpitDynamicsIntegrationAuthoring.PrimerPath);
                Transform ignition = RequirePath(
                    root,
                    CockpitDynamicsIntegrationAuthoring.IgnitionSwitchPath);
                Transform avionics = RequirePath(
                    root, CockpitDynamicsIntegrationAuthoring.AvionicsMasterPath);
                Transform flapSelector = RequirePath(
                    root,
                    CockpitDynamicsIntegrationAuthoring.WingFlapSelectorSourcePath);
                Transform fuelSelector = RequirePath(
                    root, CockpitDynamicsIntegrationAuthoring.FuelSelectorPath);

                CockpitTouchInteractionV2Controller[] controllers =
                    root.GetComponentsInChildren<
                        CockpitTouchInteractionV2Controller>(true);
                Require(controllers.Length == 1,
                    $"Expected one centralized Touch V2 controller; found " +
                    $"{controllers.Length}.");
                CockpitTouchInteractionV2Controller controller =
                    controllers[0];
                Require(controller.transform == host,
                    "The Touch V2 controller must be serialized on the control host.");
                Require(controller.LeftRayOrigin == leftController &&
                        controller.RightRayOrigin == rightController,
                    "The Touch V2 controller must reference both authored OpenXR poses.");
                Require(controller.BindingCount == ExpectedBindingCount,
                    $"Expected {ExpectedBindingCount} represented Touch bindings.");
                Require(controller.CommandConsumer is
                        ICockpitTouchInteractionV2CommandConsumer,
                    "The Touch V2 controller has no authoritative command consumer.");
                Require(
                    controller.ValidateAuthoredReferences(
                        out string controllerError),
                    controllerError);

                RequireBoundedCollider(pilotYoke);
                RequireBoundedCollider(throttle);
                RequireBoundedCollider(mixture);
                RequireBoundedCollider(altBatMaster);
                RequireBoundedCollider(primer);
                RequireBoundedCollider(ignition);
                RequireBoundedCollider(avionics);
                RequireBoundedCollider(flapSelector);
                RequireBoundedCollider(fuelSelector);

                Require(
                    root.GetComponentsInChildren<
                        CockpitTouchInteractionV2Controller>(true).Length == 1,
                    "Per-control Touch behaviours are prohibited.");
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static CockpitTouchInteractionV2Controller
            RequireOrAddSingleController(
                GameObject root,
                Transform host)
        {
            CockpitTouchInteractionV2Controller[] values =
                root.GetComponentsInChildren<
                    CockpitTouchInteractionV2Controller>(true);
            Require(values.Length <= 1,
                "Multiple Touch V2 controllers are prohibited.");
            if (values.Length == 1)
            {
                Require(values[0].transform == host,
                    "Existing Touch V2 controller is on the wrong host.");
                return values[0];
            }
            return host.gameObject
                .AddComponent<CockpitTouchInteractionV2Controller>();
        }

        private static BoxCollider EnsureBoundedInteractionCollider(
            Transform target)
        {
            BoxCollider[] colliders =
                target.GetComponents<BoxCollider>();
            Require(colliders.Length <= 1,
                $"Represented control '{target.name}' has multiple BoxColliders.");
            BoxCollider collider =
                colliders.Length == 1
                    ? colliders[0]
                    : target.gameObject.AddComponent<BoxCollider>();

            Bounds bounds = RendererBoundsInFrame(target, target);
            collider.center = bounds.center;
            collider.size =
                bounds.size +
                Vector3.one * (ColliderPaddingMeters * 2f);
            collider.isTrigger = true;
            collider.enabled = true;
            return collider;
        }

        private static void RequireBoundedCollider(Transform target)
        {
            BoxCollider[] colliders =
                target.GetComponents<BoxCollider>();
            Require(colliders.Length == 1,
                $"Represented control '{target.name}' requires one bounded collider.");
            Require(colliders[0].enabled &&
                    colliders[0].isTrigger &&
                    colliders[0].size.x > 0f &&
                    colliders[0].size.y > 0f &&
                    colliders[0].size.z > 0f,
                $"Represented control '{target.name}' collider is invalid.");
        }

        private static Bounds RendererBoundsInFrame(
            Transform source,
            Transform frame)
        {
            Renderer[] renderers =
                source.GetComponentsInChildren<Renderer>(true);
            Require(renderers.Length > 0,
                $"Represented control '{source.name}' has no renderer.");

            Bounds result = default;
            bool initialized = false;
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Bounds world = renderers[rendererIndex].bounds;
                Vector3 center = world.center;
                Vector3 extents = world.extents;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = frame.InverseTransformPoint(
                        center +
                        Vector3.Scale(
                            extents,
                            new Vector3(x, y, z)));
                    if (!initialized)
                    {
                        result = new Bounds(point, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        result.Encapsulate(point);
                    }
                }
            }
            Require(initialized,
                $"Represented control '{source.name}' bounds are unavailable.");
            return result;
        }

        private static Transform RequirePath(
            GameObject root,
            string path)
        {
            Transform value = root.transform.Find(path);
            Require(value != null,
                $"Required authored Touch target is missing: {path}");
            return value;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
