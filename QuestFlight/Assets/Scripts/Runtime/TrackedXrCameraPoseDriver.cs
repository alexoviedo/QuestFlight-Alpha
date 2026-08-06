using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace QuestFlightLab.Runtime
{
    public static class TrackedXrCameraPoseDriver
    {
        public const string PositionBinding = "<XRHMD>/centerEyePosition";
        public const string RotationBinding = "<XRHMD>/centerEyeRotation";
        public const string TrackingStateBinding = "<XRHMD>/trackingState";

        public static bool HasRequiredBindings(Camera camera)
        {
            if (camera == null) return false;
            if (IsOwnedByOvrCameraRig(camera.transform)) return true;
            TrackedPoseDriver driver = camera.GetComponent<TrackedPoseDriver>();
            return driver != null &&
                   driver.trackingType == TrackedPoseDriver.TrackingType.RotationAndPosition &&
                   driver.updateType == TrackedPoseDriver.UpdateType.UpdateAndBeforeRender &&
                   HasBinding(driver.positionInput, PositionBinding) &&
                   HasBinding(driver.rotationInput, RotationBinding) &&
                   HasBinding(driver.trackingStateInput, TrackingStateBinding);
        }

        public static bool IsOwnedByOvrCameraRig(Transform value)
        {
            for (Transform current = value;
                 current != null;
                 current = current.parent)
            {
                if (current.GetComponent<OVRCameraRig>() != null)
                    return true;
            }
            return false;
        }

        public static string PositionBindingPath(Camera camera)
        {
            TrackedPoseDriver driver = camera != null ? camera.GetComponent<TrackedPoseDriver>() : null;
            return BindingPath(driver != null ? driver.positionInput : default);
        }

        public static string RotationBindingPath(Camera camera)
        {
            TrackedPoseDriver driver = camera != null ? camera.GetComponent<TrackedPoseDriver>() : null;
            return BindingPath(driver != null ? driver.rotationInput : default);
        }

        public static int ResolvedControlCount(Camera camera)
        {
            TrackedPoseDriver driver = camera != null ? camera.GetComponent<TrackedPoseDriver>() : null;
            if (driver == null) return 0;
            return ControlCount(driver.positionInput) +
                   ControlCount(driver.rotationInput) +
                   ControlCount(driver.trackingStateInput);
        }

        private static bool HasBinding(InputActionProperty property, string requiredPath)
        {
            InputAction action = property.action;
            return action != null && action.bindings.Any(binding =>
                string.Equals(binding.effectivePath, requiredPath, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(binding.path, requiredPath, System.StringComparison.OrdinalIgnoreCase));
        }

        private static string BindingPath(InputActionProperty property)
        {
            InputAction action = property.action;
            return action == null || action.bindings.Count == 0 ? string.Empty : action.bindings[0].effectivePath;
        }

        private static int ControlCount(InputActionProperty property)
        {
            InputAction action = property.action;
            return action != null ? action.controls.Count : 0;
        }
    }
}
