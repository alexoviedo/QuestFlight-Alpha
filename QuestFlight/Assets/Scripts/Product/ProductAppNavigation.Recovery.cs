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
            if(recoveryAssistance==null || CurrentPage!=Page.App)return false;
            heading.text="Flight needs assistance";
            detail.text=pauseNotice;
            Choice(0,"Restart at runway",190);
            Choice(1,"Main menu",3);
            return true;
        }
        private void RestartRecoveryFlight()
        {
            var recovery=recoveryAssistance!=null?recoveryAssistance:localRecovery;
            if(recovery==null || !recovery.RestartAtRunway() || !modes.CompleteExplicitRunwayRestart())
            { pauseNotice="A safe runway restart could not be verified. Return to the main menu.";Refresh();return; }
            recoveryAssistance=null;
            CurrentPage=Page.None;pausedTraining=ownsPause=false;pauseNotice=null;
            Time.timeScale=1;
            input.SetFlightLessonInputSuppressed(false);
            input.RequireTouchRelease();Consume();Refresh();
        }
    }
}
