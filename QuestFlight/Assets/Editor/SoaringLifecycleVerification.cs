using System;
using System.IO;
using System.Linq;
using System.Reflection;
using QuestFlight.Product;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        [Serializable] private sealed class LifecycleEvidence
        {public bool passed,pauseBoth,focusClearsRelease,resetAttached,calibrationPreserved,ordinaryRelease,noRetether,faultStopsPublication,c172Restored;public string error,expectedNativeFault;}
        public static void RunSoaringLifecycle()
        {
            var report=new LifecycleEvidence();string output=System.Environment.GetEnvironmentVariable("QF_SOARING_QA");Directory.CreateDirectory(output);
            try
            {
                var c=LoadContext(640,480,new ProductionVisualQaReport());Time.timeScale=0;
                var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                InvokePrivate(mode,"BeginSelection");var flight=mode.AuthoredFlightCoordinator;
                if(!flight.InitializeSelectedBackend()||!mode.PrepareGroundTow())throw new InvalidOperationException(flight.LastError+mode.LastError);
                var offset=c.rig.UserViewCalibrationOffset.localPosition;var rotation=c.rig.UserViewCalibrationOffset.localRotation;
                mode.SelectFreeFlight();mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                var controls=new AircraftControlState();controls.SetGliderNeutral();controls.trim=flight.aircraftDefinition.referenceTrimNoseUp;
                for(int tick=0;tick<40*120;tick++)
                {SoaringTestPilot.Follow(flight.CurrentState,flight.TowSession.TugState,controls,false);if(!flight.StepForTest(controls,1d/120))throw new InvalidOperationException(flight.LastError);}
                var oldPair=flight.TowSession;double time=flight.CurrentState.simulationTimeSeconds;
                Time.timeScale=0;InvokePrivate(flight,"FixedUpdate");InvokePrivate(flight,"FixedUpdate");
                report.pauseBoth=flight.CurrentState.simulationTimeSeconds==time&&oldPair.TugState.simulationTimeSeconds==time;
                flight.controls.SampleTowRelease(false,true);flight.controls.SampleTowRelease(true,true);
                typeof(QuestFlightLab.Input.ProductControlInput).GetMethod("OnApplicationFocus",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(flight.controls,new object[]{false});
                report.focusClearsRelease=!flight.controls.ConsumeTowRelease();
                bool reset=flight.TryPrepareGroundTow();
                report.resetAttached=reset&&!ReferenceEquals(oldPair,flight.TowSession)&&flight.CurrentState.simulationTimeSeconds==0&&flight.TowSession.TugState.simulationTimeSeconds==0;
                report.calibrationPreserved=offset==c.rig.UserViewCalibrationOffset.localPosition&&rotation==c.rig.UserViewCalibrationOffset.localRotation;
                if(!reset)throw new InvalidOperationException(flight.LastError);
                controls.SetGliderNeutral();controls.trim=flight.aircraftDefinition.referenceTrimNoseUp;Time.timeScale=1;
                flight.controls.SampleTowRelease(false,true);flight.controls.SampleTowRelease(true,true);
                if(!flight.StepForTest(controls,1d/120))throw new InvalidOperationException(flight.LastError);
                report.ordinaryRelease=flight.TowSession.Line.state==TowlineState.ReleasedByGlider;
                for(int tick=0;tick<120;tick++){flight.controls.SampleTowRelease(true,true);if(!flight.StepForTest(controls,1d/120))throw new InvalidOperationException(flight.LastError);}
                report.noRetether=flight.TowSession.Line.state==TowlineState.ReleasedByGlider;
                time=flight.CurrentState.simulationTimeSeconds;double tugTime=flight.TowSession.TugState.simulationTimeSeconds;
                flight.TowSession.Dispose();bool advanced=flight.StepForTest(controls,1d/120);
                report.expectedNativeFault=flight.LastError;
                report.faultStopsPublication=!advanced&&flight.CurrentState.simulationTimeSeconds==time&&flight.TowSession.TugState.simulationTimeSeconds==tugTime;
                mode.ReturnToMainMenu();report.c172Restored=mode.SelectAircraft(false)&&FlightDynamicsAuthority.ActiveAuthorityCount==1&&!flight.aircraftDefinition.IsGlider;
                report.passed=report.pauseBoth&&report.focusClearsRelease&&report.resetAttached&&report.calibrationPreserved&&report.ordinaryRelease&&report.noRetether&&report.faultStopsPublication&&report.c172Restored;
            }
            catch(Exception e){report.error=e.ToString();Debug.LogException(e);}
            File.WriteAllText(Path.Combine(output,"lifecycle.json"),JsonUtility.ToJson(report,true));Time.timeScale=1;EditorApplication.Exit(report.passed?0:1);
        }
    }
}
