using System;
using System.IO;
using System.Collections.Generic;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    public static class TrainingNativeValidation
    {
        [Serializable] private sealed class Result { public string lesson; public bool checkpointFinite; public int stagesCompleted; public string failure; public float seconds; }
        [Serializable] private sealed class Report { public Result[] results; public string limitation = "Offline native scenario evidence; not hardware timing or pilot acceptance."; }
        public static void RunBatch()
        {
            try
            {
                string output=System.Environment.GetEnvironmentVariable("QFL_TRAINING_EVIDENCE");Directory.CreateDirectory(output);
                var world=AssetDatabase.LoadAssetAtPath<GameObject>(ProductTrainingAuthoring.World);
                var player=world.GetComponentInChildren<FlightDynamicsCoordinator>(true);
                var lessons=world.GetComponentInChildren<ProductTrainingController>(true).Lessons;
                var results=new List<Result>();
                foreach(var lesson in lessons)
                {
                    try { results.Add(Run(player,lesson,output)); }
                    catch(Exception e) { results.Add(new Result{lesson=lesson.lessonId,failure=e.Message});Debug.LogException(e); }
                }
                File.WriteAllText(Path.Combine(output,"native_training.json"),JsonUtility.ToJson(new Report{results=results.ToArray()},true));
                EditorApplication.Exit(results.TrueForAll(r=>r.checkpointFinite && string.IsNullOrEmpty(r.failure))?0:2);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        private static Result Run(FlightDynamicsCoordinator player,TrainingLessonDefinition lesson,string output)
        {
            var result=new Result{lesson=lesson.lessonId};
            using(var backend=new JSBSimNativeDevelopmentBackend(JSBSimRuntimeData.EditorPackageDataRoot))
            {
                if(!backend.Initialize(new FlightDynamicsBackendContext{aircraftDefinition=player.aircraftDefinition,simulationRoot=player.simulationRoot,aircraftState=player.aircraftState,terrainSurface=player.terrainSurface}))
                    throw new InvalidOperationException(backend.LastError);
                var point=FlightFrameConversions.GeodeticToUnity(lesson.start.latitudeDegrees,lesson.start.longitudeDegrees,lesson.start.altitudeMslMeters,GeodeticReference.Kbdu);
                backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(player.windField.Sample(point,0)));
                if(!backend.Reset(lesson.start))throw new InvalidOperationException(backend.LastError);
                result.checkpointFinite=backend.CurrentState.IsFinite;
                bool first=lesson.lessonId=="02_first_flight";
                var controls=AircraftControlState.Neutral(lesson.initialThrottle);controls.mixture=1;controls.flaps=lesson.initialFlaps;controls.trim=lesson.initialTrim;
                if(lesson.start.calibratedAirspeedKnots<1)controls.leftToeBrake=controls.rightToeBrake=1;
                var evaluator=new TrainingEvaluator();evaluator.BeginAttempt(backend.CurrentState);evaluator.BeginStage(controls.trim);
                var guidance=new FlightLessonGuidanceController();int index=0;float accumulator=0;
                var pilotSettings=FlightLessonGuidanceControllerSettings.Default;
                using(var csv=new StreamWriter(Path.Combine(output,lesson.lessonId+".csv")))
                {
                    csv.WriteLine("time,stage,altitude_ft,cas,heading,bank,pitch,vs,held,feedback");
                    for(int tick=0;tick<(first?240:8)*120;tick++)
                    {
                        var s=backend.CurrentState;
                        if(first)
                        {
                            var stage=lesson.stages[index];
                            var intent=new FlightLessonGuidanceIntent("native_validation",FlightLessonSegmentKind.Cruise,s.positionUnityMeters,stage.heading,0,stage.altitudeFeet,stage.speedKnots,15,400,0);
                            if(!guidance.TryStep(intent,ProductDemoPilot.BuildGuidanceAircraftState(s),1f/120,pilotSettings,out var command))throw new InvalidOperationException("Invalid guidance");
                            command.trim=lesson.initialTrim;
                            QuestFlightLab.Input.ProductControlInput.CopyFlightLessonCommandInto(command,controls);
                        }
                        backend.SetControls(controls);backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(player.windField.Sample(s.positionUnityMeters,s.simulationTimeSeconds)));
                        if(!backend.Advance(1.0/120))throw new InvalidOperationException(backend.LastError);
                        s=backend.CurrentState;result.seconds=(float)s.simulationTimeSeconds;
                        if(!s.IsFinite){result.failure="Nonfinite state";break;}
                        evaluator.ObserveNative(s);accumulator+=1f/120;
                        if(first&&accumulator>=.1f)
                        {
                            evaluator.Step(lesson.stages[index],s,FlightDynamicsControlSnapshot.From(controls),accumulator,true);accumulator=0;
                            if(evaluator.Complete){index++;result.stagesCompleted=index;if(index>=lesson.stages.Length)break;evaluator.BeginStage(controls.trim);guidance.Reset();}
                            if(evaluator.TimedOut){result.failure="Stage timed out: "+lesson.stages[index].title;break;}
                        }
                        if(tick%120==0)csv.WriteLine(FormattableString.Invariant($"{s.simulationTimeSeconds:F1},{index},{s.altitudeMslMeters/.3048:F1},{s.calibratedAirspeedKnots:F1},{s.headingDegrees:F1},{s.bankDegrees:F1},{s.pitchDegrees:F1},{s.verticalSpeedFeetPerMinute:F0},{evaluator.HeldSeconds:F1},{evaluator.Feedback}"));
                        if(s.altitudeAglMeters< -1 || Math.Abs(s.bankDegrees)>80){result.failure="Unusable checkpoint/trajectory";break;}
                        if(!first && lesson.start.calibratedAirspeedKnots>0 && tick>240 &&
                            (Math.Abs(s.pitchDegrees)>10 || s.calibratedAirspeedKnots<50 || Math.Abs(s.verticalSpeedFeetPerMinute)>900))
                        {result.failure="Airborne checkpoint is not manageable with neutral primary controls";break;}
                    }
                }
                if(first && index<lesson.stages.Length && result.failure==null) result.failure="First flight did not complete all stages";
            }
            Debug.Log("[TrainingNative] "+JsonUtility.ToJson(result));return result;
        }
    }
}
