using System;
using System.IO;
using System.Linq;
using System.Reflection;
using QuestFlight.Product;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        // Scoped Android build validation for a model-only change. This is not
        // an APK, IL2CPP link, install, or live XR acceptance result.
        public static void BuildSgsAirbrakeAssetsForAndroid()
        {
            string output=System.Environment.GetEnvironmentVariable("QF_SGS_ANDROID_ASSET_QA");
            if(string.IsNullOrWhiteSpace(output)||!Path.IsPathRooted(output))throw new InvalidOperationException("External build output required.");
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)throw new InvalidOperationException("Android target required.");
            Directory.CreateDirectory(output);
            string scripts=Path.Combine(output,"scripts");Directory.CreateDirectory(scripts);
            var compiled=PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings{target=BuildTarget.Android,group=BuildTargetGroup.Android,options=ScriptCompilationOptions.None},scripts);
            if(compiled.assemblies==null||compiled.assemblies.Count==0)throw new InvalidOperationException("Android player script compilation returned no assemblies.");
            string folder=SoaringAircraftAuthoring.Folder+"/";
            var bundle=new AssetBundleBuild{assetBundleName="sgs_airbrake_validation",assetNames=new[]{folder+"SGS_Local_Static.asset",folder+"SGS_Local_Moving.asset",folder+"SGS_Local_Canopy.asset",folder+"SGS_Paint.mat",folder+"SGS_Canopy.mat"}};
            var manifest=BuildPipeline.BuildAssetBundles(output,new[]{bundle},BuildAssetBundleOptions.StrictMode|BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.Android);
            if(manifest==null||!File.Exists(Path.Combine(output,bundle.assetBundleName)))throw new InvalidOperationException("Android SGS asset build failed.");
            File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(new SgsAssetBuildReceipt{generatedUtc=DateTime.UtcNow.ToString("O"),playerAssemblyCount=compiled.assemblies.Count,bundleHash=manifest.GetAssetBundleHash(bundle.assetBundleName).ToString()},true));
        }
        [Serializable] private sealed class SgsAssetBuildReceipt
        {
            public string generatedUtc,bundleHash;
            public int playerAssemblyCount;
            public string result="PASS_ANDROID_PLAYER_SCRIPTS_AND_SGS_ASSET_BUNDLE";
            public string limitation="Not a complete APK/IL2CPP build, live simulator, or headset test.";
        }
        public static void AuthorAndRunSgsVisualRepairQa()
        {SoaringAircraftAuthoring.Run();RunSgsVisualRepairQa();}
        public static void RunSgsPreflightMenuQa()
        {
            string output=System.Environment.GetEnvironmentVariable("QF_SGS_REPAIR_QA");
            if(string.IsNullOrWhiteSpace(output))throw new InvalidOperationException("External output required.");
            Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,scenePath=ProductionScenePath,outputDirectory=output,renderWidth=1440,renderHeight=1080,humanHeadsetAcceptance="NOT_RUN",flightPhaseImageSemantics="Offline Editor stereo preflight menu; no live XR or hardware timing evidence."};
            float clock=Time.timeScale;bool hadScale=PlayerPrefs.HasKey("QuestFlight.TextScale");float oldScale=ProductComfortPreferences.TextScale;
            try
            {
                var c=LoadContext(1440,1080,report);Time.timeScale=0;
                var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                InvokePrivate(mode,"BeginSelection");var flight=mode.AuthoredFlightCoordinator;
                if(!flight.InitializeSelectedBackend()||!mode.SelectAircraft(true)||!mode.PrepareBoulderOperation(0))throw new InvalidOperationException(flight.LastError);
                mode.SelectFreeFlight();mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();Time.timeScale=0;
                var nav=mode.AuthoredNavigation;nav.EditorVerificationFocus=true;
                foreach(var r in c.rig.XrOrigin.GetComponentsInChildren<Renderer>(true))r.forceRenderingOff=true;
                var controls=new QuestFlightLab.Runtime.AircraftControlState();controls.SetGliderNeutral();controls.airbrake=controls.wheelBrake=1;
                for(int tick=0;tick<600;tick++)if(!flight.StepForTest(controls,1d/120))throw new InvalidOperationException(flight.LastError);
                nav.OpenMenu();
                var panel=(GameObject)typeof(ProductAppNavigation).GetField("panel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(nav);
                var labels=panel.GetComponentsInChildren<ProductUiLabel>(true);var sizes=labels.ToDictionary(x=>x,x=>x.Display.fontSize);
                for(int state=0;state<2;state++)
                {
                    if(state==1&&!flight.TrySkipSoaringPreflight())throw new InvalidOperationException("Native skip/restage failed.");
                    foreach(float scale in new[]{1f,1.2f})
                    {
                        PlayerPrefs.SetFloat("QuestFlight.TextScale",scale);foreach(var item in sizes)item.Key.Display.fontSize=item.Value*scale;
                        typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.Soaring);nav.RefreshMenu();
                        foreach(var label in labels)label.Display.ForceMeshUpdate(true,true);
                        string id=(state==0?"checks":"skipped_ready")+"_"+Mathf.RoundToInt(scale*100);
                        File.WriteAllLines(Path.Combine(output,id+"_text.txt"),labels.Where(x=>x.gameObject.activeInHierarchy).Select(x=>x.Display.text));
                        var facing=c.rig.PilotSeatAnchor.rotation;
                        for(int eye=0;eye<2;eye++)Capture(c,report,output,1440,1080,new ShotDefinition(id+(eye==0?"_left":"_right"),id,"preflight_skip",c.rig.PilotSeatAnchor.position+facing*Vector3.right*(eye==0?-.032f:.032f),facing,72,ViewProfile.Cockpit));
                    }
                }
                report.requiredShotCount=8;report.passed=report.shots.Count==8;report.classification="OFFLINE_MENU_STEREO_INSPECTION_REQUIRED";
            }
            catch(Exception e){report.errors.Add(e.ToString());Debug.LogException(e);}
            finally{Time.timeScale=clock;if(hadScale)PlayerPrefs.SetFloat("QuestFlight.TextScale",oldScale);else PlayerPrefs.DeleteKey("QuestFlight.TextScale");WriteReportFiles(report,output);}
            if(!report.passed)throw new InvalidOperationException("Preflight menu QA failed.");
        }
        public static void RunSgsVisualRepairQa()
        {
            string output=System.Environment.GetEnvironmentVariable("QF_SGS_REPAIR_QA");
            if(string.IsNullOrEmpty(output))throw new InvalidOperationException("External QA output required.");
            Directory.CreateDirectory(Path.Combine(output,"screenshots"));
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,scenePath=ProductionScenePath,outputDirectory=output,renderWidth=1440,renderHeight=1080,humanHeadsetAcceptance="NOT_RUN",flightPhaseImageSemantics="SGS reference presentation. Separate Editor cameras; not live XR or Quest performance. Grip travel uses native controls."};
            float clock=Time.timeScale;
            try
            {
                var c=LoadContext(1440,1080,report);Time.timeScale=0;
                var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                InvokePrivate(mode,"BeginSelection");var flight=mode.AuthoredFlightCoordinator;
                if(!flight.InitializeSelectedBackend()||!mode.SelectAircraft(true))throw new InvalidOperationException(flight.LastError);
                mode.SelectFreeFlight();mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(mode.AuthoredNavigation,ProductAppNavigation.Page.None);mode.AuthoredNavigation.RefreshMenu();
                foreach(var r in c.rig.XrOrigin.GetComponentsInChildren<Renderer>(true))r.forceRenderingOff=true;
                var presenter=mode.AircraftPresentation.instruments;InvokePrivate(presenter,"Awake");InvokePrivate(presenter,"LateUpdate");
                var frame=mode.AircraftPresentation.gliderRoot.transform;
                var eye=frame.TransformPoint(mode.AircraftPresentation.glider.eyeFromCgMeters);
                var cameraPosition=c.rig.TrackedCamera.transform.localPosition;var cameraRotation=c.rig.TrackedCamera.transform.localRotation;
                void Pair(string id,Vector3 position,Quaternion rotation,float fov)
                {
                    for(int e=0;e<2;e++)Capture(c,report,output,1440,1080,new ShotDefinition(id+(e==0?"_left":"_right"),id,"sgs_repair",position+rotation*Vector3.right*(e==0?-.032f:.032f),rotation,fov,ViewProfile.Cockpit));
                }
                Pair("pilot",eye,frame.rotation,82);
                Pair("panel",eye,frame.rotation*Quaternion.Euler(30,0,0),75);
                bool panelQa=System.Environment.GetEnvironmentVariable("QF_SGS_PANEL_QA")=="1";
                bool trimQa=System.Environment.GetEnvironmentVariable("QF_SGS_TRIM_QA")=="1";
                if(panelQa)
                {
                    Pair("panel_detail",frame.TransformPoint(new Vector3(0,.58f,.48f)),
                        Quaternion.LookRotation(frame.TransformPoint(new Vector3(0,.36f,1.1f))-frame.TransformPoint(new Vector3(0,.58f,.48f))),60);
                    Pair("nose_low_eye",eye+frame.TransformVector(new Vector3(0,-.04f,0)),frame.rotation,82);
                    Pair("nose_high_eye",eye+frame.TransformVector(new Vector3(0,.04f,0)),frame.rotation,82);
                }
                Pair("left_controls",eye+frame.TransformVector(new Vector3(-.05f,0,.08f)),frame.rotation*Quaternion.Euler(46,-32,0),75);
                Pair("right_lean",eye+frame.TransformVector(new Vector3(.12f,.04f,.08f)),frame.rotation*Quaternion.Euler(10,40,0),82);
                Pair("rear",eye,frame.rotation*Quaternion.Euler(0,180,0),82);
                foreach(var local in new[]{new Vector3(6,3,8),new Vector3(12,.4f,0)})
                {var position=frame.TransformPoint(local);Pair(local.z>0?"exterior_quarter":"exterior_side",position,Quaternion.LookRotation(frame.TransformPoint(new Vector3(0,.4f,0))-position),58);}
                if(trimQa)
                {
                    var trimControls=new QuestFlightLab.Runtime.AircraftControlState();trimControls.SetGliderNeutral();int pose=0;
                    foreach(float trim in new[]{-1f,-1f/3,1f/3,1f,flight.aircraftDefinition.referenceTrimNoseUp})
                    {
                        trimControls.trim=trim;
                        if(!flight.StepForTest(trimControls,1d/120))throw new InvalidOperationException(flight.LastError);
                        InvokePrivate(presenter,"LateUpdate");
                        Vector3 position=frame.TransformPoint(new Vector3(.02f,.44f,.43f));
                        Pair("trim_handle_"+pose++,position,Quaternion.LookRotation(frame.TransformPoint(new Vector3(-.29f,.045f,.86f))-position),58);
                    }
                }
                bool brakeQa=System.Environment.GetEnvironmentVariable("QF_SGS_AIRBRAKE_QA")=="1";
                bool leverQa=System.Environment.GetEnvironmentVariable("QF_SGS_BRAKE_LEVER_QA")=="1";
                if(leverQa)
                {
                    var leverControls=new QuestFlightLab.Runtime.AircraftControlState();leverControls.SetGliderNeutral();int phase=0;
                    foreach(float value in new[]{0f,.08f,.5f,.85f,1f,0f})
                    {
                        typeof(QuestFlightLab.Input.ProductControlInput).GetField("_airbrakeLever",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(flight.controls,value);
                        QuestFlightLab.Input.ProductControlInput.ResolveGliderLever(value,out leverControls.airbrake,out leverControls.wheelBrake);
                        for(int tick=0;tick<125;tick++)if(!flight.StepForTest(leverControls,1d/120))throw new InvalidOperationException(flight.LastError);
                        InvokePrivate(presenter,"LateUpdate");
                        Vector3 position=frame.TransformPoint(new Vector3(-.02f,.63f,.40f));
                        Pair($"brake_handle_{phase++}",position,Quaternion.LookRotation(frame.TransformPoint(new Vector3(-.23f,.23f,.84f))-position),68);
                    }
                }
                if(brakeQa)
                {
                    var brakeControls=new QuestFlightLab.Runtime.AircraftControlState();brakeControls.SetGliderNeutral();
                    int phase=0;
                    foreach(float extension in new[]{0f,.5f,1f,0f})
                    {
                        brakeControls.airbrake=extension;
                        for(int tick=0;tick<125;tick++)if(!flight.StepForTest(brakeControls,1d/120))throw new InvalidOperationException(flight.LastError);
                        InvokePrivate(presenter,"LateUpdate");
                        foreach(bool upper in new[]{true,false})
                        {
                            Vector3 position=frame.TransformPoint(new Vector3(2.48f,upper?2.2f:-.25f,1.25f));
                            Pair($"airbrake_{phase}_{(upper?"upper":"lower")}",position,Quaternion.LookRotation(frame.TransformPoint(new Vector3(2.48f,.96f,-.38f))-position),42);
                        }
                        phase++;
                    }
                }
                var controls=new QuestFlightLab.Runtime.AircraftControlState();controls.SetGliderNeutral();
                foreach(float value in new[]{-1f,1f})
                {
                    controls.trim=value;controls.elevator=value;controls.airbrake=value>0?1:0;
                    typeof(QuestFlightLab.Input.ProductControlInput).GetField("_airbrakeLever",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(flight.controls,value>0?1f:0f);
                    for(int tick=0;tick<120;tick++)if(!flight.StepForTest(controls,1d/120))throw new InvalidOperationException(flight.LastError);
                    InvokePrivate(presenter,"LateUpdate");Pair(value>0?"controls_aft":"controls_forward",frame.TransformPoint(mode.AircraftPresentation.glider.eyeFromCgMeters),frame.rotation*Quaternion.Euler(42,-15,0),85);
                }
                if(cameraPosition!=c.rig.TrackedCamera.transform.localPosition||cameraRotation!=c.rig.TrackedCamera.transform.localRotation)throw new InvalidOperationException("Tracked camera was changed.");
                report.requiredShotCount=18+(brakeQa?16:0)+(leverQa?12:0)+(panelQa?6:0)+(trimQa?10:0);report.passed=report.shots.Count==report.requiredShotCount&&report.errors.Count==0;report.classification="SGS_REFERENCE_CAPTURES_REQUIRE_INSPECTION_BUDGET_SEPARATE";
            }
            catch(Exception e){report.errors.Add(e.ToString());Debug.LogException(e);}
            finally{Time.timeScale=clock;WriteReportFiles(report,output);}
            if(!report.passed)throw new InvalidOperationException("SGS visual capture failed.");
        }
    }
}
