using System;
using System.IO;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static class SoaringDefinitionAuthoring
    {
        [MenuItem("QuestFlight/Soaring/Author aircraft definitions")]
        public static void Run()
        {
            Directory.CreateDirectory(FlightAircraftDefinition.DefinitionFolder);
            AssetDatabase.Refresh();
            var c172 = Definition("C172", FlightAircraftKind.C172, "C172P", JSBSimAircraftConfiguration.C172, 6);
            c172.flaps = true; c172.minimumWeightLb = 1454; c172.maximumWeightLb = 2400;
            c172.minimumCgXIn = 35; c172.maximumCgXIn = 47.3;
            c172.minimumReferenceCasKnots = 48; c172.maximumReferenceCasKnots = 126;
            c172.provenance = "Preserved product c172x and legacy loading: 1454 lb empty, 170/170 front seats, 130/130 fuel. Existing C172 fidelity limitations remain.";
            var glider = Definition("SGS233A_Reference", FlightAircraftKind.Sgs233Reference, "SGS 2-33A reference", new JSBSimAircraftConfiguration
            {
                modelName = "qfl_sgs233_ref", engineCount = 0, tankCount = 0,
                stations = new[] { Load(0,170), Load(1,170) }
            }, 5);
            glider.airbrake = glider.wheelBrake = glider.towAttachment = glider.differentialAirflow = true;
            glider.minimumWeightLb = 610; glider.maximumWeightLb = 1040; glider.minimumCgXIn = 78.2; glider.maximumCgXIn = 86.1;
            glider.minimumReferenceCasKnots = 35; glider.maximumReferenceCasKnots = 85.1592;
            glider.towHookStructuralInches = new Vector3(20,0,-12);
            glider.eyeFromCgMeters = new Vector3(0,.65f,.70f);
            glider.provenance = "REFERENCE_CONFIGURATION. G2EA Rev4 geometry/loading; older Schweizer flight manual performance and controls. Empty CG/inertia/vertical contacts/hook estimated. Native source and downloaded visual restricted to local development. Not an identified operator airframe.";
            var tug = Definition("PA18_Tug_Reference", FlightAircraftKind.Pa18TugReference, "PA-18 150 hp reference tug", new JSBSimAircraftConfiguration
            {
                modelName = "qfl_pa18_tug_ref", engineCount = 1, tankCount = 2,
                stations = new[] { Load(0,0), Load(1,170) }, tanks = new[] { Load(0,60), Load(1,60) }
            }, 8);
            tug.flaps = tug.towAttachment = tug.differentialAirflow = true;
            tug.minimumWeightLb = 983; tug.maximumWeightLb = 1750; tug.minimumCgXIn = 10; tug.maximumCgXIn = 23;
            tug.minimumReferenceCasKnots = 40; tug.maximumReferenceCasKnots = 90;
            tug.towHookStructuralInches = new Vector3(210,0,-20);
            tug.provenance = "REFERENCE_CONFIGURATION. Isolated 150 hp PA-18 data from wlbragg/J3Cub 7d9c8d34; GPL3. Fixed-pitch 75-inch propeller, 20-degree source table. CG limits/inertia/hook are development estimates, no verified Boulder operator assignment.";
            foreach (var d in new[] { c172, glider, tug })
            {
                d.airborneReference = FlightDynamicsInitialConditions.KbduRunway();
                d.airborneReference.altitudeMslMeters += 600;
                d.airborneReference.calibratedAirspeedKnots = d.IsGlider ? 50 : 65;
                d.airborneReference.flightPathAngleDegrees = d.IsGlider ? -2.862400417 : 0;
                d.airborneReference.pitchDegrees = d.IsGlider ? .168303819 : 0;
                d.referenceTrimNoseUp = d.IsGlider ? .1062608793f : 0;
                d.airborneReference.engineRunning = d.Powered;
                d.referenceHandlingAccepted = false;
                if (!d.IsValid(out string error)) throw new InvalidOperationException(d.name + ": " + error);
                EditorUtility.SetDirty(d);
            }
            AssetDatabase.SaveAssets();
            foreach (string path in new[] { "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab", "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab" })
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var flight in root.GetComponentsInChildren<FlightDynamicsCoordinator>(true)) flight.aircraftDefinition = c172;
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[QuestFlight][Soaring] Three authored native definitions; existing player remains C172.");
        }

        private static JSBSimNativeStationLoad Load(int index,double pounds) => new JSBSimNativeStationLoad { index=index,pounds=pounds };
        private static FlightAircraftDefinition Definition(string name,FlightAircraftKind kind,string display,JSBSimAircraftConfiguration config,int contacts)
        {
            string path = FlightAircraftDefinition.DefinitionFolder + "/" + name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(path);
            if (asset == null) { asset = ScriptableObject.CreateInstance<FlightAircraftDefinition>();AssetDatabase.CreateAsset(asset,path); }
            asset.kind=kind;asset.displayName=display;asset.nativeConfiguration=config;asset.contactCount=contacts;
            return asset;
        }
    }
}
