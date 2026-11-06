using QuestFlightLab.Flight.Soaring;

namespace QuestFlight.Product

{

    public sealed partial class ProductAppNavigation

    {

        private void StartSoaringOperation(int index,bool skipPreflight=false)

        {

            if(modes.State!=ProductFlightModeCoordinator.FlightModeState.Selection){Reject("Return Home before preparing a new launch.");return;}

            if(!modes.PrepareBoulderOperation(index)){Reject("Launch preparation failed. "+modes.AuthoredFlightCoordinator.LastError+" Return Home and retry.");return;}

            if(skipPreflight&&!modes.AuthoredFlightCoordinator.TrySkipSoaringPreflight())
            {Reject("Could not skip preflight while stopped. Choose Launch preparation to review the aircraft state.");return;}
            NavigateTo(Page.None);pausedTraining=ownsPause=false;Consume();modes.SelectFreeFlight();Refresh();

        }

        private void PresentSoaringOperation()

        {

            var flight=modes.AuthoredFlightCoordinator;var op=flight.Operation;

            heading.text=SoaringPageTitle;
            if(modes.State==ProductFlightModeCoordinator.FlightModeState.Selection)
            {
                detail.text="SGS 2-33A / PA-18 development pairing. Starts stopped with a connected rope; controls and deliberate release test precede explicit crew restaging. Reference routes and 60 m rope are not an operator-approved procedure.";
                Choice(0,"Runway 08 · prepare launch",303);Choice(1,"Runway 26 · prepare launch",304);
                Choice(2,"Runway 08 · start now (skip preflight)",338);Choice(3,"Runway 26 · start now (skip preflight)",339);Choice(7,"Back",5);return;
            }
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
                Choice(0,"Skip preflight and prepare launch",336);
            }
            if(op.Phase==SoaringOperationPhase.CrewRehook)Choice(0,"Reconnect and prepare launch",306);
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

