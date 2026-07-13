using UnityEngine;

namespace QuestFlightLab.Flight
{
    public class AircraftState : MonoBehaviour
    {
        public C172StyleAircraftConfig config;
        public Vector3 velocityWorld;
        public Vector3 angularVelocityDeg;
        public float airspeedKts;
        public float altitudeFt;
        public float verticalSpeedFpm;
        public float headingDeg;
        public float pitchDeg;
        public float bankDeg;
        public float angleOfAttackDeg;
        public float stallIntensity;
        public float slipSkid;
        public float referenceSpeedKts;
        public float targetSpeedErrorKts;
        public float engineRpm;
        public float powerPercent;
        public float flapDegrees;
        public float trimPercent;
        public float loadFactorG = 1f;
        public float groundRollMeters;
        public float runwayLateralOffsetMeters;
        public bool stallWarning;
        public bool onGround;

        public void ResetState(Vector3 velocity)
        {
            velocityWorld = velocity;
            angularVelocityDeg = Vector3.zero;
            stallWarning = false;
            stallIntensity = 0f;
            slipSkid = 0f;
            referenceSpeedKts = 0f;
            targetSpeedErrorKts = 0f;
            onGround = true;
            engineRpm = 0f;
            powerPercent = 0f;
            flapDegrees = 0f;
            trimPercent = 0f;
            loadFactorG = 1f;
            groundRollMeters = 0f;
            runwayLateralOffsetMeters = 0f;
            RefreshFromTransform(transform);
        }

        public void RefreshFromTransform(Transform aircraftTransform)
        {
            airspeedKts = velocityWorld.magnitude * 1.943844f;
            altitudeFt = aircraftTransform.position.y * 3.28084f;
            verticalSpeedFpm = velocityWorld.y * 196.8504f;
            headingDeg = Mathf.Repeat(aircraftTransform.eulerAngles.y, 360f);
            pitchDeg = -NormalizeAngle(aircraftTransform.eulerAngles.x);
            bankDeg = NormalizeAngle(aircraftTransform.eulerAngles.z);
        }

        public static float NormalizeAngle(float angle)
        {
            angle = Mathf.Repeat(angle + 180f, 360f) - 180f;
            return angle;
        }
    }
}
