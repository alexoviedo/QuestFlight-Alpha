using System;
using System.Collections.Generic;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Explicitly owns mip residency for the seven fixed ENU orthophoto textures.
    /// It reads the authored OpenXR camera pose but never writes the camera or hierarchy.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AircraftReferenceFrameRig))]
    public sealed class GeospatialMipController : MonoBehaviour
    {
        public const string ContractVersion = "photoreal_aerial_surface_v1_explicit_mips";

        [Header("Authored camera authority")]
        public AircraftReferenceFrameRig trackedRig;

        [Header("Fixed ENU texture pyramid")]
        public Texture2D near4Km;
        public Texture2D midNorthWest;
        public Texture2D midNorthEast;
        public Texture2D midSouthWest;
        public Texture2D midSouthEast;
        public Texture2D midBase12Km;
        public Texture2D far24Km;

        [Header("Deterministic preload contract")]
        [Min(0f)] public float predictionSeconds = 2f;
        [Min(0f)] public float preloadMarginMeters = 650f;
        [Range(0.1f, 2f)] public float projectedTexelSafetyFactor = 0.72f;
        public bool logMipChanges = true;
        public string authoredContractVersion = ContractVersion;

        private Vector3 previousCameraPosition;
        private bool hasPreviousCameraPosition;
        private int nearLevel = -1;
        private int midNwLevel = -1;
        private int midNeLevel = -1;
        private int midSwLevel = -1;
        private int midSeLevel = -1;
        private int midBaseLevel = -1;
        private int farLevel = -1;

        public int NearLevel => nearLevel;
        public int MidNorthWestLevel => midNwLevel;
        public int MidNorthEastLevel => midNeLevel;
        public int MidSouthWestLevel => midSwLevel;
        public int MidSouthEastLevel => midSeLevel;
        public int MidBaseLevel => midBaseLevel;
        public int FarLevel => farLevel;

        private void Awake()
        {
            if (trackedRig == null) trackedRig = GetComponent<AircraftReferenceFrameRig>();
            if (!TryValidateContract(out string report))
                Debug.LogError("[QuestFlight][GeospatialMipController] Authored contract failed: " + report, this);
        }

        private void LateUpdate()
        {
            Camera camera = trackedRig != null ? trackedRig.TrackedCamera : null;
            if (camera == null || !camera.isActiveAndEnabled) return;

            Vector3 current = camera.transform.position;
            Vector3 velocity = hasPreviousCameraPosition && Time.unscaledDeltaTime > 0.00001f
                ? (current - previousCameraPosition) / Time.unscaledDeltaTime
                : Vector3.zero;
            Vector3 predicted = current + velocity * predictionSeconds;
            previousCameraPosition = current;
            hasPreviousCameraPosition = true;

            ApplyDeterministicRequest(current, predicted, camera.fieldOfView, camera.pixelHeight);
        }

        public void ApplyDeterministicRequest(
            Vector3 current,
            Vector3 predicted,
            float verticalFieldOfViewDegrees,
            int viewportPixelHeight)
        {
            verticalFieldOfViewDegrees = Mathf.Clamp(verticalFieldOfViewDegrees, 1f, 179f);

            float verticalDistance = Mathf.Max(1f, Mathf.Abs(current.y));
            float viewportHeight = Mathf.Max(1f, viewportPixelHeight);
            float metersPerPixel = 2f * verticalDistance *
                                   Mathf.Tan(verticalFieldOfViewDegrees * Mathf.Deg2Rad * 0.5f) /
                                   viewportHeight;
            metersPerPixel *= projectedTexelSafetyFactor;

            int desiredNear = DesiredMip(near4Km, metersPerPixel, 0.48828125f);
            int desiredPatternTile = DesiredMip(midNorthWest, metersPerPixel, 1.984375f);
            int desiredPatternBase = DesiredMip(midBase12Km, metersPerPixel, 1.46484375f);
            int desiredFar = DesiredMip(far24Km, metersPerPixel, 5.859375f);
            float dynamicPreload = Mathf.Max(preloadMarginMeters, verticalDistance * 2.25f);
            bool patternTilesUseful = Mathf.Max(Mathf.Abs(predicted.x), Mathf.Abs(predicted.z)) <= 5000f;

            bool changed = false;
            changed |= RequestForBounds(near4Km, predicted, -2000f, -2000f, 2000f, 2000f,
                dynamicPreload, desiredNear, ref nearLevel);
            changed |= RequestTexture(midNorthWest,
                patternTilesUseful ? desiredPatternTile : CoarsestMip(midNorthWest), ref midNwLevel);
            changed |= RequestTexture(midNorthEast,
                patternTilesUseful ? desiredPatternTile : CoarsestMip(midNorthEast), ref midNeLevel);
            changed |= RequestTexture(midSouthWest,
                patternTilesUseful ? desiredPatternTile : CoarsestMip(midSouthWest), ref midSwLevel);
            changed |= RequestTexture(midSouthEast,
                patternTilesUseful ? desiredPatternTile : CoarsestMip(midSouthEast), ref midSeLevel);
            changed |= RequestForBounds(midBase12Km, predicted, -6000f, -6000f, 6000f, 6000f,
                dynamicPreload, desiredPatternBase, ref midBaseLevel);
            changed |= RequestForBounds(far24Km, predicted, -12000f, -12000f, 12000f, 12000f,
                dynamicPreload, desiredFar, ref farLevel);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (changed && logMipChanges)
                Debug.Log($"[QuestFlight][GeospatialMipController] mips near={nearLevel}, " +
                          $"mid={midNwLevel}/{midNeLevel}/{midSwLevel}/{midSeLevel}/{midBaseLevel}, " +
                          $"far={farLevel}, predictedENU=({predicted.x:0.0},{predicted.z:0.0})", this);
#endif
        }

        private bool RequestForBounds(
            Texture2D texture,
            Vector3 predicted,
            float minimumX,
            float minimumZ,
            float maximumX,
            float maximumZ,
            float preloadMargin,
            int usefulLevel,
            ref int currentLevel)
        {
            if (texture == null) return false;
            float outsideX = Mathf.Max(0f, Mathf.Max(minimumX - predicted.x, predicted.x - maximumX));
            float outsideZ = Mathf.Max(0f, Mathf.Max(minimumZ - predicted.z, predicted.z - maximumZ));
            float outsideDistance = Mathf.Sqrt(outsideX * outsideX + outsideZ * outsideZ);
            int target = outsideDistance <= preloadMargin
                ? usefulLevel
                : CoarsestMip(texture);
            return RequestTexture(texture, target, ref currentLevel);
        }

        private static bool RequestTexture(Texture2D texture, int target, ref int currentLevel)
        {
            if (texture == null) return false;
            target = Mathf.Clamp(target, 0, CoarsestMip(texture));
            if (target == currentLevel) return false;
            texture.requestedMipmapLevel = target;
            currentLevel = target;
            return true;
        }

        private static int CoarsestMip(Texture2D texture) =>
            texture == null ? 0 : Mathf.Max(0, texture.mipmapCount - 1);

        private static int DesiredMip(Texture2D texture, float metersPerPixel, float sourceMetersPerTexel)
        {
            if (texture == null || texture.mipmapCount <= 1) return 0;
            float ratio = Mathf.Max(1f, metersPerPixel / Mathf.Max(0.0001f, sourceMetersPerTexel));
            return Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(ratio, 2f)), 0, texture.mipmapCount - 1);
        }

        private void OnDisable()
        {
            ClearAllRequests();
        }

        public void ClearAllRequests()
        {
            Clear(near4Km);
            Clear(midNorthWest);
            Clear(midNorthEast);
            Clear(midSouthWest);
            Clear(midSouthEast);
            Clear(midBase12Km);
            Clear(far24Km);
            hasPreviousCameraPosition = false;
            nearLevel = midNwLevel = midNeLevel = midSwLevel = midSeLevel = midBaseLevel = farLevel = -1;
        }

        private static void Clear(Texture2D texture)
        {
            if (texture != null) texture.ClearRequestedMipmapLevel();
        }

        public bool TryValidateContract(out string report)
        {
            List<string> failures = new List<string>();
            if (trackedRig == null) failures.Add("tracked aircraft rig is missing");
            if (trackedRig != null && trackedRig.gameObject != gameObject)
                failures.Add("controller must be authored on the aircraft-rig root");
            Texture2D[] textures =
            {
                near4Km, midNorthWest, midNorthEast, midSouthWest, midSouthEast, midBase12Km, far24Km
            };
            if (Array.Exists(textures, texture => texture == null)) failures.Add("one or more pyramid textures are missing");
            if (textures.Length != 7) failures.Add("exactly seven fixed texture domains are required");
            if (predictionSeconds < 0f || preloadMarginMeters < 0f || projectedTexelSafetyFactor <= 0f)
                failures.Add("prediction/preload parameters are invalid");
            if (!string.Equals(authoredContractVersion, ContractVersion, StringComparison.Ordinal))
                failures.Add("contract version is stale");
            report = failures.Count == 0
                ? "PASS explicit requested-mip ownership for seven fixed ENU textures"
                : string.Join("; ", failures);
            return failures.Count == 0;
        }
    }
}
