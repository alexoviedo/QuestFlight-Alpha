#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Input.Visuals;
using QuestFlightLab.Environment;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    /// <summary>
    /// Plain EditMode capture does not run the SDK's tracking/pose/visibility
    /// updates. Keep those unposed input previews out of deterministic scenery
    /// and cockpit evidence; tracked input remains a separate player/device check.
    /// Every preview state is restored, including on a failed capture.
    /// </summary>
    public sealed class ProductionVisualQaSettledSdkPreview : IDisposable
    {
        public const string Policy = "EditMode without tracked input: authored SDK hand/controller/interactor renderers suppressed; exact handed TouchPlus model selected using serialized SDK references. No tracked controls acceptance; all states restored after capture.";
        private readonly Dictionary<Renderer, bool> rendererStates = new Dictionary<Renderer, bool>();
        private readonly Dictionary<GameObject, bool> modelStates = new Dictionary<GameObject, bool>();
        public int SuppressedRendererCount => rendererStates.Count;

        public ProductionVisualQaSettledSdkPreview(Transform authoredAircraft)
        {
            if (authoredAircraft == null) throw new ArgumentNullException(nameof(authoredAircraft));
            ControllerVisual[] controllers = authoredAircraft.GetComponentsInChildren<ControllerVisual>(true);
            HandVisual[] hands = authoredAircraft.GetComponentsInChildren<HandVisual>(true);
            var sdkRoots = controllers.Select(c => FindSdkRoot(c.transform))
                .Concat(hands.Select(h => FindSdkRoot(h.transform))).Distinct().ToArray();
            if (sdkRoots.Length != 1 || sdkRoots[0] == null)
                throw new InvalidOperationException("QA requires the existing authored SDK interaction rig.");
            try
            {
                foreach (Renderer renderer in sdkRoots[0].GetComponentsInChildren<Renderer>(true))
                {
                    rendererStates.Add(renderer, renderer.enabled);
                    renderer.enabled = false;
                }
                foreach (OVRControllerHelper helper in sdkRoots[0].GetComponentsInChildren<OVRControllerHelper>(true))
                {
                    GameObject left = helper.m_modelMetaTouchPlusLeftController;
                    GameObject right = helper.m_modelMetaTouchPlusRightController;
                    if (left == null || right == null || left == right ||
                        (helper.m_controller != OVRInput.Controller.LTouch && helper.m_controller != OVRInput.Controller.RTouch))
                        throw new InvalidOperationException("QA TouchPlus handed model references are invalid.");
                    foreach (GameObject model in Models(helper).Where(m => m != null).Distinct())
                    {
                        if (!modelStates.ContainsKey(model)) modelStates.Add(model, model.activeSelf);
                        model.SetActive(false);
                    }
                    (helper.m_controller == OVRInput.Controller.LTouch ? left : right).SetActive(true);
                }
            }
            catch { Dispose(); throw; }
        }

        private static Transform FindSdkRoot(Transform item)
        {
            for (Transform current = item; current != null; current = current.parent)
                if (current.name == "OVRComprehensiveInteractionRig") return current;
            return null;
        }

        private static IEnumerable<GameObject> Models(OVRControllerHelper helper)
        {
            yield return helper.m_modelOculusTouchQuestAndRiftSLeftController;
            yield return helper.m_modelOculusTouchQuestAndRiftSRightController;
            yield return helper.m_modelOculusTouchRiftLeftController;
            yield return helper.m_modelOculusTouchRiftRightController;
            yield return helper.m_modelOculusTouchQuest2LeftController;
            yield return helper.m_modelOculusTouchQuest2RightController;
            yield return helper.m_modelMetaTouchProLeftController;
            yield return helper.m_modelMetaTouchProRightController;
            yield return helper.m_modelMetaTouchPlusLeftController;
            yield return helper.m_modelMetaTouchPlusRightController;
        }

        public void Dispose()
        {
            foreach (var state in modelStates) if (state.Key != null) state.Key.SetActive(state.Value);
            foreach (var state in rendererStates) if (state.Key != null) state.Key.enabled = state.Value;
            modelStates.Clear(); rendererStates.Clear();
        }

        public static Pose GroundPresentationPose(ProductionEnvironmentRoot environment)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            Vector3 start = environment.transform.TransformPoint(environment.runway08EndLocal);
            Vector3 end = environment.transform.TransformPoint(environment.runway26EndLocal);
            Vector3 axis = end - start;
            if (axis.sqrMagnitude < 1f) throw new InvalidOperationException("QA runway endpoints are invalid.");
            Vector3 right = Vector3.Cross(environment.transform.up, axis.normalized).normalized;
            Vector3 up = Vector3.Cross(axis.normalized, right).normalized;
            return new Pose(Vector3.Lerp(start, end, .1f) + up * 1.25f, Quaternion.LookRotation(axis, up));
        }
    }
}
#endif
