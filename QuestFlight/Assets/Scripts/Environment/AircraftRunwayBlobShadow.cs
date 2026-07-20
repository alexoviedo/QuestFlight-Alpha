using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Moves one explicitly authored, texture-free blob shadow over the authoritative
    /// runway only. The aircraft, camera, XR Origin, and environment hierarchy are never
    /// modified. The reused property block keeps the steady-state path allocation-free.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AircraftRunwayBlobShadow : MonoBehaviour
    {
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        [SerializeField] private Transform aircraftSimulationRoot;
        [SerializeField] private ProductionEnvironmentRoot environment;
        [SerializeField] private MeshRenderer blobRenderer;
        [SerializeField, Min(0f)] private float surfaceOffsetMeters = 0.012f;
        [SerializeField, Min(0f)] private float fullOpacityHeightMeters = 2.5f;
        [SerializeField, Min(0.1f)] private float fadeOutHeightMeters = 24f;
        [SerializeField, Range(0f, 0.65f)] private float maximumOpacity = 0.42f;
        [SerializeField, Min(0f)] private float runwayEdgeToleranceMeters = 1.5f;

        private MaterialPropertyBlock _propertyBlock;

        public Transform AircraftSimulationRoot => aircraftSimulationRoot;
        public ProductionEnvironmentRoot Environment => environment;
        public MeshRenderer BlobRenderer => blobRenderer;
        public float FadeOutHeightMeters => fadeOutHeightMeters;

        public void ConfigureAuthoredReferences(
            Transform aircraftRoot,
            ProductionEnvironmentRoot authoredEnvironment,
            MeshRenderer authoredRenderer)
        {
            aircraftSimulationRoot = aircraftRoot;
            environment = authoredEnvironment;
            blobRenderer = authoredRenderer;
        }

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            UpdateShadowPoseNow();
        }

        private void LateUpdate() => UpdateShadowPoseNow();

        public void UpdateShadowPoseNow()
        {
            if (aircraftSimulationRoot == null || environment == null || blobRenderer == null)
            {
                if (blobRenderer != null) blobRenderer.enabled = false;
                return;
            }

            Vector3 runway08 = environment.transform.TransformPoint(environment.runway08EndLocal);
            Vector3 runway26 = environment.transform.TransformPoint(environment.runway26EndLocal);
            Vector3 runwayAxis = runway26 - runway08;
            float runwayLengthSquared = runwayAxis.sqrMagnitude;
            if (runwayLengthSquared < 1f)
            {
                blobRenderer.enabled = false;
                return;
            }

            Vector3 aircraftPosition = aircraftSimulationRoot.position;
            float longitudinal = Vector3.Dot(aircraftPosition - runway08, runwayAxis) / runwayLengthSquared;
            float boundedLongitudinal = Mathf.Clamp01(longitudinal);
            Vector3 runwayPoint = Vector3.Lerp(runway08, runway26, boundedLongitudinal);
            Vector3 runwayUp = environment.transform.up;
            Vector3 lateralDelta = Vector3.ProjectOnPlane(aircraftPosition - runwayPoint, runwayUp);
            float halfWidth = environment.runwayWidthMeters * 0.5f + runwayEdgeToleranceMeters;
            bool overRunway = longitudinal >= 0f && longitudinal <= 1f && lateralDelta.magnitude <= halfWidth;

            float height = Vector3.Dot(aircraftPosition - runwayPoint, runwayUp);
            float normalizedHeight = Mathf.InverseLerp(
                fullOpacityHeightMeters,
                fadeOutHeightMeters,
                Mathf.Max(0f, height));
            float fade = 1f - Mathf.SmoothStep(0f, 1f, normalizedHeight);
            float opacity = maximumOpacity * fade;
            if (!overRunway || opacity <= 0.001f)
            {
                blobRenderer.enabled = false;
                return;
            }

            Vector3 aircraftForward = Vector3.ProjectOnPlane(aircraftSimulationRoot.forward, runwayUp);
            if (aircraftForward.sqrMagnitude < 0.001f)
                aircraftForward = Vector3.ProjectOnPlane(runwayAxis, runwayUp);
            aircraftForward.Normalize();

            transform.SetPositionAndRotation(
                runwayPoint + runwayUp * surfaceOffsetMeters,
                Quaternion.LookRotation(aircraftForward, runwayUp));
            blobRenderer.enabled = true;
            if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();
            blobRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(OpacityId, opacity);
            blobRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
