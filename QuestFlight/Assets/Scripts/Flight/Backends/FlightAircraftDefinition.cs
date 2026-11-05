using System;
using AlexOviedo.QuestFlight.JSBSim;
using UnityEngine;

namespace QuestFlightLab.Flight.Backends
{
    public enum FlightAircraftKind { C172, Sgs233Reference, Pa18TugReference }

    /// <summary>Authored selection contract. Native XML owns forces and contacts.</summary>
    [CreateAssetMenu(menuName = "QuestFlight/Flight aircraft definition")]
    public sealed class FlightAircraftDefinition : ScriptableObject
    {
        public FlightAircraftKind kind;
        public string displayName;
        public JSBSimAircraftConfiguration nativeConfiguration;
        public bool airbrake, wheelBrake, flaps, towAttachment, differentialAirflow;
        public int contactCount;
        public Vector3 towHookStructuralInches;
        public Vector3 eyeFromCgMeters;
        public double minimumWeightLb, maximumWeightLb, minimumCgXIn, maximumCgXIn;
        public double minimumReferenceCasKnots, maximumReferenceCasKnots;
        public FlightDynamicsInitialConditions airborneReference;
        public float referenceTrimNoseUp;
        [TextArea] public string provenance;
        public bool referenceHandlingAccepted;
        public bool IsGlider => kind == FlightAircraftKind.Sgs233Reference;
        public bool Powered => nativeConfiguration != null && nativeConfiguration.engineCount > 0;

        public bool IsValid(out string error)
        {
            error = "Aircraft definition is incomplete or inconsistent.";
            if (nativeConfiguration == null || !nativeConfiguration.IsValid(out _) ||
                string.IsNullOrWhiteSpace(displayName) || contactCount < 1 || contactCount > 16 ||
                (IsGlider && (Powered || nativeConfiguration.tankCount != 0 || flaps || !airbrake || !wheelBrake)) ||
                (kind == FlightAircraftKind.C172 && !nativeConfiguration.legacyC172Loading) ||
                (kind != FlightAircraftKind.C172 && nativeConfiguration.legacyC172Loading) ||
                minimumWeightLb <= 0 || maximumWeightLb < minimumWeightLb || maximumCgXIn < minimumCgXIn ||
                maximumReferenceCasKnots <= minimumReferenceCasKnots) return false;
            error = string.Empty; return true;
        }

        public bool Matches(JSBSimNativeCapabilities actual, out string error)
        {
            error = "Native model capabilities differ from the serialized aircraft definition.";
            if (actual.contactCount != contactCount || (actual.hasAirbrake != 0) != airbrake ||
                (actual.hasTowForce != 0) != towAttachment ||
                (actual.hasAirflowForce != 0 && actual.hasAirflowMoment != 0) != differentialAirflow) return false;
            error = string.Empty; return true;
        }

        public const string DefinitionFolder = "Assets/Production/SoaringV1/Definitions";
#if UNITY_EDITOR
        public static FlightAircraftDefinition EditorC172 => UnityEditor.AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(DefinitionFolder + "/C172.asset");
#endif
    }
}
