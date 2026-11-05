using System;

using System.Collections.Generic;

using QuestFlight.Product.Training;

using QuestFlightLab.Flight.Soaring;

using UnityEngine;

namespace QuestFlight.Product

{

    public sealed partial class ProductAppNavigation

    {

        [SerializeField] private MonoBehaviour[] cockpitInteractables=Array.Empty<MonoBehaviour>();
        private bool[] cockpitInteractionStates;
        private bool menuOwnsInteractions;
        public int MenuInteractionScopeCount=>cockpitInteractables.Length;
        public void ConfigureMenuInteractionScope(MonoBehaviour[] value)=>cockpitInteractables=value??Array.Empty<MonoBehaviour>();
        private void SetMenuInteractionScope(bool menu)
        {
            if(menu==menuOwnsInteractions)return;
            if(menu)
            {
                cockpitInteractionStates=new bool[cockpitInteractables.Length];
                for(int n=0;n<cockpitInteractables.Length;n++)if(cockpitInteractables[n]!=null){cockpitInteractionStates[n]=cockpitInteractables[n].enabled;cockpitInteractables[n].enabled=false;}
            }
            else if(cockpitInteractionStates!=null)
                for(int n=0;n<cockpitInteractables.Length;n++)if(cockpitInteractables[n]!=null)cockpitInteractables[n].enabled=cockpitInteractionStates[n];
            menuOwnsInteractions=menu;
        }
        private void OnDestroy()=>SetMenuInteractionScope(false);
        private struct Route { public Page page; public int action, lessonPage; }

        private readonly Stack<Route> routes = new Stack<Route>();

        private int lessonPage, pendingMenuAction;

        private bool choosingLessonAircraft,pointerRequiresRelease;

        private string pendingLessonId;

        private string actionFailure;

        public string LastActionResult { get; private set; }

        public string AircraftName => modes.IsGlider ? "SGS 2-33A reference" : "C172";

        public int FocusedSlot => focus;

        public string VisibleLabel(int slot) => slot>=0&&slot<8?labels[slot]:null;

        public int VisibleAction(int slot) => slot>=0&&slot<8?actions[slot]:0;

        public void RefreshMenu() => Refresh();

        private void NavigateTo(Page page)

        {

            if(page==CurrentPage)return;

            if(page==Page.None) { routes.Clear();CurrentPage=page;return; }

            routes.Push(new Route {page=CurrentPage,action=focus>=0&&focus<8?actions[focus]:0,lessonPage=lessonPage});

            CurrentPage=page;focus=0;pauseNotice=null;

        }

        private bool TryPopRoute()

        {

            if(routes.Count==0)return false;

            var previous=routes.Pop();CurrentPage=previous.page;lessonPage=previous.lessonPage;pauseNotice=null;

            Refresh();focus=0;

            for(int n=0;n<8;n++)if(actions[n]==previous.action&&previous.action!=0){focus=n;break;}

            Paint();return true;

        }

        private void OperationResult(bool success,string message,string failure) {if(success)pauseNotice=message;else Reject(failure);}
        private void Reject(string reason)

        { actionFailure=reason;pauseNotice=reason;LastActionResult="UNAVAILABLE: "+reason;Refresh(); }

        public bool CanResume(out string reason)

        {

            var flight=modes.AuthoredFlightCoordinator;

            if(!FocusAvailable)reason="Put on the headset and return focus to QuestFlight.";

            else if(calibration.IsOpen)reason="Save or cancel seat setup before resuming.";

            else if(modes.State==ProductFlightModeCoordinator.FlightModeState.Blocked||!flight.IsInitialized||!flight.enabled)reason="Aircraft unavailable. Return Home and retry preparation.";

            else if(modes.AuthoredDemoPilot.IsSoaringDemo&&modes.AuthoredDemoPilot.NeedsAssistance)reason="Tour held. Choose Tour options / restart, or End flight.";
            else if(flight.RecoveryHold||recoveryAssistance!=null)reason=modes.IsGlider?"Flight is held. Restart ground preparation, restart the lesson or return Home.":"Flight is held. Restart at the runway or return Home.";

            else if(Application.platform==RuntimePlatform.Android&&!input.AuthoredGamepad.Current.connected&&

                (!OVRInput.IsControllerConnected(OVRInput.Controller.LTouch)||!OVRInput.IsControllerConnected(OVRInput.Controller.RTouch)||

                 !OVRInput.GetControllerOrientationTracked(OVRInput.Controller.LTouch)||!OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch)))reason="Wake both controllers and restore tracking, then choose Resume.";

            else if((training.AssistancePaused||training.DemonstrationPaused)&&!training.CanResumeAssistance)reason="Assisted glide reached its limit. Choose Take control or restart the exercise.";
            else if(modes.State==ProductFlightModeCoordinator.FlightModeState.Training&&training.State!=TrainingSessionState.Paused&& !training.IsFlying)reason="Choose a lesson action to continue this session.";

            else {reason=null;return true;}

            return false;

        }

        public bool CanExecute(int action,out string reason)

        {

            reason=null;

            if(action==0){reason="No action available.";return false;}

            if(action==20&&modes.AuthoredDemoPilot.IsSoaringDemo&&!modes.AuthoredDemoPilot.SoaringGuidance.CanTakeControl(modes.AuthoredFlightCoordinator.CurrentState)){reason="Take control is available during stable sightseeing glide. Pause, restart or end remain available.";return false;}
            if(action==15&&training.AssistancePaused)return CanTakeManualControl(out reason);
            if(action==1||action==12||action==15||action==20||action==21||action==22)return CanResume(out reason);

            if((action==301||action==302||action==310||action==311||action==313||action==314||action==321||action==303||action==304)&&(!modes.AuthoredFlightCoordinator.IsInitialized||modes.AircraftPresentation==null||!modes.AircraftPresentation.Ready))
            {reason="The native aircraft is preparing or unavailable. Wait, or return Home and retry.";return false;}
            if(action==196){reason="Experimental rendering changes are unavailable in this product profile.";return false;}

            if(action==301||action==302||action==310||action==311)

            {if(modes.State!=ProductFlightModeCoordinator.FlightModeState.Selection){reason="End this flight before changing aircraft.";return false;}}



            if((action==306||action==307||action==308)&&modes.AuthoredFlightCoordinator.Operation==null){reason="Start ground aerotow preparation first.";return false;}

            if(action==306)return modes.AuthoredFlightCoordinator.CanReconnectSoaring(out reason);
            if(action==336)return modes.AuthoredFlightCoordinator.CanSkipSoaringPreflight(out reason);
            if(action==307)return modes.AuthoredFlightCoordinator.CanClearSoaringLaunch(out reason);
            if(action==308&&!modes.AuthoredFlightCoordinator.CanAbortSoaringGroundLaunch){reason="Abort requires an authorized ground launch below 20 kt. Physical release remains available.";return false;}
            if(!CanExecuteWorkflow(action,out reason))return false;

            if(action==137&&(experience==null||!experience.HasRecommendedLesson)){reason="This record has no unambiguous aircraft and lesson. Choose a lesson from Home.";return false;}

            if(action>=100&&action<107 && action>=100+training.Lessons.Length){reason="Lesson is not in this aircraft catalog.";return false;}

            if(!IsKnownAction(action)){reason="This menu action is unavailable. Return Home and report the build.";return false;}

            return true;

        }

        public static bool IsKnownAction(int action) => action>=100&&action<107 ||

            Enum.IsDefined(typeof(MenuActionId),action);

        public void InvokeAction(int slot) => DispatchAction(slot,false);

        public void InvokePointerAction(int slot) => DispatchAction(slot,true);

        private void DispatchAction(int slot,bool pointer)

        {

            if(slot<0||slot>=8||string.IsNullOrEmpty(labels[slot]))return;

            if(!FocusAvailable||ConsumedThisFrame||(!pointer&&releaseRequired)||(pointer&&pointerRequiresRelease))return;
            if(pointer)pointerRequiresRelease=true;

            int action=actions[slot];var before=CurrentPage;actionFailure=null;

            if(!CanExecute(action,out var reason)){Reject(reason);LogAction(before,action,pointer);return;}

            if(NeedsConfirmation(action))

            {Consume();pendingMenuAction=action;NavigateTo(Page.ConfirmAction);Refresh();LogAction(before,action,pointer);return;}

            try { ExecuteAction(slot); }

            catch(Exception exception){Debug.LogException(exception,this);Reject("The action could not finish. Return Home and retry, or mark an issue if it repeats.");}

            if(modes.State==ProductFlightModeCoordinator.FlightModeState.Blocked)Reject(modes.LastError);

            LastActionResult=actionFailure!=null?"UNAVAILABLE: "+actionFailure:handoverCompletion!=null||training.State==TrainingSessionState.Preparing||modes.AuthoredModeMenuView.TransitionPending?"PENDING":"COMPLETED";

            LogAction(before,action,pointer);

        }

        private void LogAction(Page page,int action,bool pointer)

        {if(actionTrace.Count==24)actionTrace.Dequeue();actionTrace.Enqueue($"{page} > {(MenuActionId)action}: {LastActionResult}; now {CurrentPage}");Debug.Log($"[QuestFlight][MenuAction] page={page} action={(MenuActionId)action} input={(pointer?"pointer":"buttons")} result={LastActionResult} destination={CurrentPage}",this);}

        private bool NeedsConfirmation(int action) => pendingMenuAction==0&&modes.State!=ProductFlightModeCoordinator.FlightModeState.Selection&&

            ((action==10&&(training.State==TrainingSessionState.Paused||training.State==TrainingSessionState.Debrief||training.IsFlying))||action==49||action==63||action==64||action==65||action==66||action==136||action==137||action==190||action==325||action==326||action==334||action==328||action==314||action==316||(action==13&&training.State==TrainingSessionState.Paused));

        private void SelectMenuAircraft(bool glider)

        {

            if(!modes.SelectAircraft(glider)){Reject(string.IsNullOrEmpty(modes.LastError)?"Aircraft is not ready. Return Home and retry.":modes.LastError);return;}

            if(choosingLessonAircraft){choosingLessonAircraft=false;OpenCurrentLessons();return;}

            NavigateTo(glider?Page.GliderSetup:Page.C172Setup);

        }

        private void OpenCurrentLessons()

        {

            NavigateTo(Page.None);modes.SelectTraining();

            if(modes.State!=ProductFlightModeCoordinator.FlightModeState.Training){Reject("The lesson catalog could not open. Return Home and retry.");CurrentPage=Page.Home;}

        }

        private bool InvokeHubAction(int action)

        {

            switch(action)

            {

                case (int)MenuActionId.FlyC172:choosingLessonAircraft=false;SelectMenuAircraft(false);break;

                case (int)MenuActionId.FlySgs:choosingLessonAircraft=false;SelectMenuAircraft(true);break;

                case (int)MenuActionId.ChooseLessons:choosingLessonAircraft=true;NavigateTo(Page.Aircraft);break;

                case (int)MenuActionId.StartFree:StartActivity(-3);break;

                case (int)MenuActionId.SoaringDemo:NavigateTo(Page.SoaringDemo);break;
                case (int)MenuActionId.StartDemo:StartActivity(-2);break;

                case (int)MenuActionId.Help:NavigateTo(Page.Help);break;

                case (int)MenuActionId.StartShowcase:StartActivity(-4);break;

                case (int)MenuActionId.ConfirmAction:

                    int pending=pendingMenuAction;

                    if(pending==0){Reject("No pending action. Go back and choose an activity.");break;}

                    if(!CanExecute(pending,out var reason)){pendingMenuAction=0;TryPopRoute();Reject(reason);break;}
                    pendingMenuAction=0;TryPopRoute();

                    // The confirmation is already consumed; call the same handler once.

                    actions[0]=pending;ExecuteAction(0);break;

                case (int)MenuActionId.Home:experience?.FinishFlight("Flight ended by pilot");ReturnMain();break;

                case (int)MenuActionId.NextLessons:lessonPage++;focus=0;break;

                case (int)MenuActionId.PreviousLessons:lessonPage=Math.Max(0,lessonPage-1);focus=0;break;

                case (int)MenuActionId.AircraftLessons:OpenCurrentLessons();break;
                case (int)MenuActionId.RetryAircraft:if(!modes.RetryNativePreparation())Reject("Native preparation is still unavailable. "+modes.AuthoredFlightCoordinator.LastError);else {CurrentPage=Page.Home;pauseNotice="Aircraft ready. Choose a flight.";}break;

                default:return false;

            }

            return true;

        }

        private bool PresentHubPage()

        {

            switch(CurrentPage)

            {

                case Page.Home:

                    heading.text="QuestFlight · Home";detail.text=(training.IsGuestSession?"Guest session":"Owner session")+" · "+(experience!=null?experience.Build:Application.version);

                    if(modes.State==ProductFlightModeCoordinator.FlightModeState.Blocked){detail.text=modes.LastError;Choice(0,"Retry native aircraft preparation",322);Choice(1,"Help / report an issue",315);Choice(2,"Settings",25);return true;}
                    Choice(0,"Fly C172",310);Choice(1,"Fly SGS sailplane",311);Choice(2,"Lessons",312);Choice(3,"Settings",25);Choice(4,"Help / setup",315);Choice(5,"Flight history",47);if(firstUsePending)Choice(6,"First visit: read setup guide",30);

                    footer.text="Point and squeeze trigger, or stick up/down and A to select.\nChoose an aircraft to begin.";return true;

                case Page.C172Setup:

                    heading.text="C172 · Choose a flight";detail.text="Powered trainer. Free flight begins at the authored airport start.";

                    Choice(0,"Free flight · airport start",313);Choice(1,"Scenic demonstration",314);Choice(2,"Short activities",46);Choice(3,"C172 lessons",321);Choice(4,"Seat setup",2);Choice(7,"Back",5);return true;

                case Page.GliderSetup:

                    heading.text="SGS sailplane · Choose a flight";detail.text="SGS 2-33A reference configuration. Ground aerotow includes controls checks and launch clearance.";

                    Choice(0,"Ground aerotow",305);Choice(1,"Airborne gliding practice",313);Choice(2,"Soaring lessons",321);Choice(3,"Scenic soaring demo",337);Choice(4,"Seat setup",2);Choice(7,"Back",5);return true;

                case Page.SoaringDemo:
                    heading.text="Boulder soaring tour";detail.text="SGS 2-33A reference. Instructor flies thermal soaring, wildlife, sightseeing and landing. Full starts on the ground; short starts airborne. Route and aircraft fidelity are provisional.";
                    Choice(0,modes.AuthoredDemoPilot.IsSoaringDemo?"Restart full tour · about 20 min":"Full flight · about 20 min",314);Choice(1,modes.AuthoredDemoPilot.IsSoaringDemo?"Restart short tour · about 12 min":"Short tour · about 12 min",316);Choice(2,"Narration / captions",130);Choice(3,"Audio settings",25);Choice(7,"Back",5);return true;
                case Page.Help:

                    heading.text="Help / setup · "+AircraftName;detail.text="Choose a topic. Flight stays paused while you review it.";

                    Choice(0,"Quick setup guide",30);Choice(1,"Controller bindings",120);Choice(2,"Cockpit instruments",125);Choice(3,"Readiness checklist",135);Choice(4,"Local briefing / map",43);Choice(5,"Mark an issue",48);Choice(6,"Seat position",2);Choice(7,"Back",5);return true;

                case Page.ConfirmAction:

                    heading.text=pendingMenuAction==49?"End this flight?":pendingMenuAction==13?"Leave this attempt?":pendingMenuAction==10||pendingMenuAction==325?"Restart this session?":"Replace this session?";detail.text="Selected: "+ActionTitle(pendingMenuAction)+"\nThe current attempt ends. Completed progress is kept.";

                    Choice(0,"Confirm · "+ActionTitle(pendingMenuAction),317);Choice(7,"Cancel · keep this session",5);return true;

                default:return false;

            }

        }

        private string ActionTitle(int action) => action switch

        {10=>"restart exercise",49=>"end flight and view summary",63=>modes.IsGlider?"SGS circuit and unpowered landing":"C172 landing practice",64=>modes.IsGlider?"SGS ridge soaring":"C172 circuit practice",65=>"scenic demo",66=>"fresh free flight",136=>"short showcase",137=>"recommended lesson",190=>"C172 runway restart",325=>"ground launch preparation",326=>"airborne gliding practice",328=>"stopped controls lesson",334=>"repeat selected activity",13=>"leave attempt and return to lessons",_=>"start selected activity"};

    }

}

