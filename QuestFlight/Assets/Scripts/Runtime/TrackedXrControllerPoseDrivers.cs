using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace QuestFlightLab.Runtime
{
    /// <summary>
    /// Validates the authored left/right controller transforms beneath the one
    /// active XR Origin. OpenXR owns both poses; calibration never writes them.
    /// </summary>
    public static class TrackedXrControllerPoseDrivers
    {
        public const string LeftControllerName = "Left Touch Controller";
        public const string RightControllerName = "Right Touch Controller";
        public const string LegacyLeftControllerName = "LeftHand Controller";
        public const string LegacyRightControllerName = "RightHand Controller";

        public static bool HasRequiredHierarchy(Transform xrOrigin)
        {
            if (xrOrigin == null) return false;
            Transform left = FindController(xrOrigin, true);
            Transform right = FindController(xrOrigin, false);
            return HasRequiredBindings(left, true) && HasRequiredBindings(right, false);
        }

        public static int ResolvedControlCount(Transform controller)
        {
            TrackedPoseDriver driver = controller != null ? controller.GetComponent<TrackedPoseDriver>() : null;
            if (driver == null) return 0;
            return ControlCount(driver.positionInput) +
                   ControlCount(driver.rotationInput) +
                   ControlCount(driver.trackingStateInput);
        }

        public static bool HasRequiredBindings(Transform controller, bool left)
        {
            if (controller == null) return false;
            if (IsOwnedByOvrControllerAnchor(controller, left)) return true;
            TrackedPoseDriver driver = controller.GetComponent<TrackedPoseDriver>();
            if (driver == null || driver.trackingType != TrackedPoseDriver.TrackingType.RotationAndPosition) return false;
            string hand = left ? "LeftHand" : "RightHand";
            return HasBinding(driver.positionInput, $"<XRController>{{{hand}}}/devicePosition") &&
                   HasBinding(driver.rotationInput, $"<XRController>{{{hand}}}/deviceRotation") &&
                   HasBinding(driver.trackingStateInput, $"<XRController>{{{hand}}}/trackingState");
        }

        public static bool IsOwnedByOvrControllerAnchor(
            Transform controller,
            bool left)
        {
            string anchorName = left
                ? "LeftControllerAnchor"
                : "RightControllerAnchor";
            bool foundAnchor = false;
            for (Transform current = controller;
                 current != null;
                 current = current.parent)
            {
                if (string.Equals(
                        current.name,
                        anchorName,
                        StringComparison.Ordinal))
                    foundAnchor = true;
                if (current.GetComponent<OVRCameraRig>() != null)
                    return foundAnchor;
            }
            return false;
        }

        private static bool HasBinding(InputActionProperty property, string binding)
        {
            InputAction action = property.action;
            return action != null && action.bindings.Any(candidate =>
                string.Equals(candidate.path, binding, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.effectivePath, binding, StringComparison.OrdinalIgnoreCase));
        }

        private static int ControlCount(InputActionProperty property)
        {
            InputAction action = property.action;
            return action != null ? action.controls.Count : 0;
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
            }

            return null;
        }

        private static Transform FindController(Transform xrOrigin, bool left)
        {
            string currentName = left ? LeftControllerName : RightControllerName;
            string legacyName = left ? LegacyLeftControllerName : LegacyRightControllerName;
            return FindDirectChild(xrOrigin, currentName) ??
                   FindDirectChild(xrOrigin, legacyName) ??
                   FindDescendant(xrOrigin, currentName) ??
                   FindDescendant(xrOrigin, legacyName);
        }

        private static Transform FindDescendant(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                    return child;
                Transform nested = FindDescendant(child, name);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
