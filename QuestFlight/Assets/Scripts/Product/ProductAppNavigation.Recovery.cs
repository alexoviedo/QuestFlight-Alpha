using UnityEngine;

namespace QuestFlight.Product

{

    public sealed partial class ProductAppNavigation

    {

        [SerializeField] private ProductMapBoundaryRecovery localRecovery;

        private ProductMapBoundaryRecovery recoveryAssistance;

        public void ConfigureRecovery(ProductMapBoundaryRecovery value) => localRecovery=value;

        public void ShowRecoveryAssistance(ProductMapBoundaryRecovery owner, string reason)

        {

            recoveryAssistance=owner;

            OpenMenu();

            pauseNotice=reason;

            Refresh();

        }

        private bool PresentRecoveryAssistance()

        {

            if((recoveryAssistance==null && !modes.AuthoredFlightCoordinator.RecoveryHold) || CurrentPage!=Page.App)return false;

            heading.text="Flight needs assistance";

            detail.text=pauseNotice;

            Choice(0,modes.IsGlider?"Restart ground aerotow preparation":"Restart C172 at runway",modes.IsGlider?325:190);
            if(modes.IsGlider)Choice(2,"Start airborne gliding practice",326);
            if(modes.State==ProductFlightModeCoordinator.FlightModeState.Training)Choice(3,"Restart this lesson",10);

            Choice(1,"Main menu",3);

            return true;

        }

        private void RestartRecoveryFlight()

        {

            if(modes.IsGlider){RestartGroundLaunch();return;}
            var recovery=recoveryAssistance!=null?recoveryAssistance:localRecovery;

            if(recovery==null || !recovery.RestartAtRunway() || !modes.CompleteExplicitRunwayRestart())

            { pauseNotice="A safe runway restart could not be verified. Return to the main menu.";Refresh();return; }

            recoveryAssistance=null;

            CurrentPage=Page.None;pausedTraining=ownsPause=false;pauseNotice=null;

            Time.timeScale=0;

            input.SetFlightLessonInputSuppressed(true);

            ownsPause=true;savedTimeScale=1;CurrentPage=Page.App;BeginHandover(CompleteResume);

        }

    }

}
