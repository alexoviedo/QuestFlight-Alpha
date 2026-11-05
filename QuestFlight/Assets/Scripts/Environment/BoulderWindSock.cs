using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    public sealed class BoulderWindSock : MonoBehaviour
    {
        public BoulderWindFieldProfile windField;
        public FlightDynamicsCoordinator player;
        public Transform fabric;
        private float countdown;
        private Quaternion target = Quaternion.Euler(80,0,0);
        private QuestFlight.Rendering.ProductRigidRendererHistory motionHistory;
        private Renderer[] motionRenderers;
        private void OnEnable()
        {
            motionRenderers = fabric != null ? fabric.GetComponentsInChildren<Renderer>(true) : System.Array.Empty<Renderer>();
            motionHistory = new QuestFlight.Rendering.ProductRigidRendererHistory();
            motionHistory.Upload(motionRenderers);
        }
        private void LateUpdate() { motionHistory?.Upload(motionRenderers); }
        private void OnDisable() { motionHistory?.Dispose(); motionHistory = null; }
        private void Update()
        {
            if (windField == null || player == null || fabric == null || !player.IsInitialized) return;
            countdown -= Time.deltaTime;
            if (countdown <= 0)
            {
                countdown = .1f;
                Vector3 wind = player.SampleWind(transform.position,player.CurrentState.simulationTimeSeconds);
                wind.y=0;
                if (!BoulderWindFieldProfile.Finite(wind)) return;
                float yaw=wind.sqrMagnitude>.01f?Mathf.Atan2(wind.x,wind.z)*Mathf.Rad2Deg:target.eulerAngles.y;
                target=Quaternion.Euler(Mathf.Lerp(80,8,Mathf.Clamp01(wind.magnitude/7)),yaw,0);
            }
            fabric.localRotation=Quaternion.RotateTowards(fabric.localRotation,target,Time.deltaTime*35);
        }
    }
}
