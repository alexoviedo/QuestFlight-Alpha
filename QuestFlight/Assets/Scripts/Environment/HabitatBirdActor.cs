using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>Authored visual wildlife, never a flight authority. All motion is world-space.</summary>
    public sealed class HabitatBirdActor : MonoBehaviour
    {
        public string birdId;
        public bool goose, hero;
        public Vector3 habitatCenter, secondaryCenter;
        public float orbitRadius=100, cruiseAgl=140, span=1.65f, airspeed=13, sink=.65f, phase;
        public int thermalIndex=-1;
        public HabitatBirdActor leader;
        public Vector2 formationOffset;
        public Animator animator;
        public SkinnedMeshRenderer[] lods;
        public int[] triangles;
        public float flapPlaybackSpeed=3;
        public Vector3 Velocity { get; internal set; }
        public Vector3 AirVelocity { get; internal set; }
        public Vector3 Wind { get; internal set; }
        public Vector3 Target { get; internal set; }
        public Vector3 AvoidanceVelocity { get; internal set; }
        public float MinimumClimb { get; internal set; }
        public float Lift { get; internal set; }
        public bool Powered { get; internal set; }
        public bool Travelling { get; internal set; }
        public bool Secondary { get; internal set; }
        public float NextDecision { get; internal set; }
        public float NextTravel { get; internal set; }
        public int Lod { get; private set; }=-1;
        public bool Shown => Lod>=0;
        private float bank,animationClock;
        private bool flapping,animationStarted;
        private bool visibilityInitialized;
        private static readonly int Flap=Animator.StringToHash("FlapLoop"),Soar=Animator.StringToHash("SoarIdle");

        public void ResetBird(Vector3 center,float ground)
        {
            Vector3 radial=new Vector3(Mathf.Cos(phase),0,Mathf.Sin(phase));
            transform.position=new Vector3(center.x,Mathf.Max(center.y,ground+cruiseAgl),center.z)+radial*orbitRadius;
            AirVelocity=new Vector3(-radial.z,0,radial.x)*airspeed;
            transform.rotation=Quaternion.LookRotation(AirVelocity);Velocity=AirVelocity;
            Target=center;bank=0;animationClock=phase;Travelling=false;Secondary=false;
            NextDecision=0;NextTravel=90+phase*11;animationStarted=false;
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            SetLod(-1);
        }

        public void Integrate(Vector3 desiredGroundVelocity,float dt)
        {
            Vector3 desiredAir=desiredGroundVelocity-Wind;
            // Species airspeed is independent of wind; vertical guidance is bounded separately.
            Vector3 horizontal=new Vector3(desiredAir.x,0,desiredAir.z).normalized*airspeed;
            horizontal.y=Mathf.Clamp(desiredAir.y,-3,Powered?2.5f:1);
            Vector3 previous=AirVelocity;
            float turnRate=9.81f*Mathf.Tan(28*Mathf.Deg2Rad)/airspeed;
            AirVelocity=Vector3.RotateTowards(previous,horizontal,turnRate*dt,3*dt);
            Velocity=AirVelocity+Wind;
            transform.position+=Velocity*dt;
            float yaw=Vector3.SignedAngle(new Vector3(previous.x,0,previous.z),new Vector3(AirVelocity.x,0,AirVelocity.z),Vector3.up);
            float targetBank=-Mathf.Atan(airspeed*yaw*Mathf.Deg2Rad/Mathf.Max(dt,.001f)/9.81f)*Mathf.Rad2Deg;
            bank=Mathf.MoveTowards(bank,Mathf.Clamp(targetBank,-30,30),35*dt);
            transform.rotation=Quaternion.LookRotation(AirVelocity,Vector3.up)*Quaternion.AngleAxis(bank,Vector3.forward);
            animationClock+=dt;
            bool flap=goose || Powered || Mathf.Repeat(animationClock+phase,14)<1.2f;
            if(animator.enabled && (!animationStarted || flap!=flapping))
            {
                if(!animationStarted)animator.Play(flap?Flap:Soar,0,Mathf.Repeat(phase,.95f));
                else animator.CrossFadeInFixedTime(flap?Flap:Soar,.3f,0,0);
                animationStarted=true;
            }
            flapping=flap;
            animator.speed=flap?flapPlaybackSpeed*(1+.035f*Mathf.Sin(phase*7)):1;
        }

        public void SetLod(int requested)
        {
            requested=Mathf.Clamp(requested,-1,lods.Length-1);
            if(!hero && requested<0)animator.enabled=false;
            if(requested==Lod && visibilityInitialized)return;
            visibilityInitialized=true;
            // Logical phase advances while ambient skinning sleeps. Warm the current pose before showing it.
            // The accepted hero deliberately retains AlwaysAnimate throughout.
            if(!hero && requested>=0 && !animator.enabled)
            {
                animator.enabled=true;
                animator.Play(flapping?Flap:Soar,0,Mathf.Repeat(animationClock*(flapping?flapPlaybackSpeed:1)/2,1));
                animator.Update(0);animationStarted=true;
            }
            for(int i=0;i<lods.Length;i++)lods[i].enabled=i==requested;
            Lod=requested;
        }
    }
}
