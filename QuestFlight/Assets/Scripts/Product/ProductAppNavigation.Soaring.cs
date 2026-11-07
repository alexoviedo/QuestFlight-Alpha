using QuestFlightLab.Flight.Soaring;
using UnityEngine;

namespace QuestFlight.Product

{

    public sealed partial class ProductAppNavigation

    {

        private void SelectSoaringRunway(int index)
        {
            pendingSoaringRunway=index;pauseNotice=null;focus=0;Consume();Refresh();
        }

        private void StartSoaringOperation(int index,bool skipPreflight=false)

        {

            if(modes.State!=ProductFlightModeCoordinator.FlightModeState.Selection){Reject("Return Home before preparing a new launch.");return;}

            Page returnPage=CurrentPage==Page.None?Page.Home:CurrentPage;
            CurrentPage=Page.None;Refresh();
            if(!modes.PrepareBoulderOperation(index))
            {
                string reason=string.IsNullOrEmpty(modes.LastError)?modes.AuthoredFlightCoordinator.LastError:modes.LastError;
                CurrentPage=modes.State==ProductFlightModeCoordinator.FlightModeState.Blocked?Page.Home:returnPage;
                pauseNotice="Launch preparation failed. "+(string.IsNullOrEmpty(reason)?"The aircraft or tow configuration was unavailable.":reason);
                focus=0;Refresh();return;
            }

            var flight=modes.AuthoredFlightCoordinator;
            bool launchCleared=false;
            string launchBlockReason=null;
            if(skipPreflight)
            {
                if(!flight.TrySkipSoaringPreflight(out var skipReason))
                {
                    CurrentPage=returnPage;
                    Reject("Could not skip preflight. "+skipReason);return;
                }
                // Skipping the checklist still obeys native rope, traffic, brake,
                // control-centering and runway-clearance gates. Start the tow now
                // when they pass; otherwise enter the cockpit with a staged pair.
                bool launchReady=flight.CanClearSoaringLaunch(out launchBlockReason);
                launchCleared=launchReady&&flight.TryClearSoaringLaunch();
                if(launchReady&&!launchCleared)launchBlockReason="Launch conditions changed before clearance was applied.";
                if(!launchCleared&&flight.Operation!=null&&flight.Operation.Phase==SoaringOperationPhase.Ready)
                {
                    pendingSoaringStartClearance=flight.Operation;
                    nextSoaringStartClearanceCheck=Time.unscaledTime;
                }
            }
            pendingSoaringRunway=-1;
            soaringStartNotice=skipPreflight?(launchCleared?"Preflight skipped with no checklist credit. Aerotow launch clearance is active; remain in the cockpit and follow the tug.":"Preflight skipped with no checklist credit. The tow pair is staged; launch clearance will start automatically when safe."+(string.IsNullOrEmpty(launchBlockReason)?"":" Current gate: "+launchBlockReason)):null;
            pausedTraining=ownsPause=false;Consume();modes.SelectFreeFlight();
            if(modes.State!=ProductFlightModeCoordinator.FlightModeState.FreeFlight)
            {soaringStartNotice=null;OpenMenu();if(modes.State!=ProductFlightModeCoordinator.FlightModeState.Blocked)CurrentPage=Page.Soaring;Reject("Free flight could not start. "+(string.IsNullOrEmpty(modes.LastError)?flight.LastError:modes.LastError));return;}
            Refresh();

        }

        private void PresentSoaringOperation()

        {

            var flight=modes.AuthoredFlightCoordinator;var op=flight.Operation;

            if(modes.State==ProductFlightModeCoordinator.FlightModeState.Selection)
            {
                if(pendingSoaringRunway<0)
                {
                    heading.text="Ground aerotow · choose runway";
                    detail.text="SGS 2-33A / PA-18 development pairing. Choose the runway once, then choose a checked start or skip directly to launch. Tow release is tested in flight. Reference runway, weather and towline values are provisional.";
                    Choice(0,"Runway 08",303);Choice(1,"Runway 26",304);Choice(7,"Back",5);return;
                }
                heading.text=$"Runway {(pendingSoaringRunway==0?"08":"26")} · choose start";
                detail.text="Choose whether to complete the stopped cockpit checks or skip them. Skip records no checklist credit and requests a real launch clearance; traffic, rope, brakes and control gates still apply.";
                if(!string.IsNullOrEmpty(pauseNotice))detail.text+="\n"+pauseNotice;
                Choice(0,"Start with preflight checks",338);Choice(1,"Skip preflight and start launch",339);Choice(6,"Choose different runway",340);Choice(7,"Back to flight options",5);return;
            }
            heading.text=SoaringPageTitle;
            if(op==null)
            {
                detail.text=flight.CurrentState.weightOnWheels?"No tow operation is attached to this stopped glider. Choose a new ground launch preparation or an explicit airborne start.":"Explicit airborne checkpoint · no tow operation. Follow the lesson or land unpowered. Ground launch preparation starts a new session.";
                Choice(0,"Restart ground aerotow preparation",325);Choice(1,"Start airborne practice",326);Choice(6,"Return to cockpit",1);Choice(7,"Back",5);return;
            }
            bool stopped=flight.CurrentState.weightOnWheels&&flight.CurrentState.groundSpeedKnots<1;
            string rope=flight.TowSession==null?"unavailable":flight.TowSession.RopeState.ToString();
            detail.text=op.Instruction+"\nRope now: "+rope+(op.PreflightSkipped?" · Preflight SKIPPED · no checklist credit":" · Ground checks recorded: "+CountBits(op.CheckedControls)+" / 7");
            if(op.Phase==SoaringOperationPhase.ControlsCheck)
            {
                string[] checks={"stick left","stick right","stick back","stick forward","rudder left","rudder right","full combined brake"};
                string missing="";for(int n=0;n<checks.Length;n++)if((op.CheckedControls&(1<<n))==0)missing+=(missing.Length>0?", ":"")+checks[n];
                detail.text+="\nRemaining: "+missing+". Checks record only while stopped below 1 kt. Return to cockpit to operate controls.";
                detail.text+="\nTow release: test only after launch when separation is safe. Trim: reference preset; no movement check. Real pre-takeoff trim setting is covered in the lesson briefing.";
                Choice(0,"Skip preflight and start launch",336);
            }
            if(op.Phase==SoaringOperationPhase.CrewRehook)Choice(0,"Reconnect rope and prepare launch",306);
            if(op.Phase==SoaringOperationPhase.Ready)
            {
                flight.CanClearSoaringLaunch(out var why);
                detail.text+="\n"+(why??"Ready: valid pair, rope connected, traffic clear, controls centered, brakes closed.");
                Choice(0,"Clear and begin launch",307);Choice(2,"Restart preparation",325);
            }
            if(flight.CanAbortSoaringGroundLaunch)Choice(1,"Abort ground launch below 20 kt",308);
            if(op.Phase==SoaringOperationPhase.Landed||op.Phase==SoaringOperationPhase.Aborted)
            {
                if(stopped||flight.RecoveryHold)
                {Choice(0,"View flight summary",49);Choice(1,"Prepare another ground launch",325);Choice(2,"Home / end session",3);}
                else detail.text+="\nStop aircraft first with the combined lever, or maintain a safe unpowered landing plan if airborne.";
            }
            if(op.ReturnEstimate.supported)detail.text+=$"\nPlanning return margin: {op.ReturnEstimate.marginMeters:0} m at {op.ReturnEstimate.calibratedSpeedKnots:0} kt (reference estimate).";
            if(!string.IsNullOrEmpty(pauseNotice))detail.text+="\n"+pauseNotice;
            Choice(6,op.Phase==SoaringOperationPhase.Ready?"Return to cockpit / wait for traffic":"Return to cockpit",1);Choice(7,"Back",5);
        }

        private static int CountBits(int value){int count=0;for(;value!=0;value>>=1)count+=value&1;return count;}

    }

}

