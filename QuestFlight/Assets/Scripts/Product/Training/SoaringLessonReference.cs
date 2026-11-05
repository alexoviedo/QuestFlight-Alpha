using UnityEngine;
namespace QuestFlight.Product.Training
{
    // Simulator task regions shared by scoring, authored targets and the existing map.
    public static class SoaringLessonReference
    {
        public const float DownwindSouth=350,DownwindNorth=800,BaseWest=-1800,BaseEast=-700;
        public const float RidgeBoundary=-6250,RidgeExit=-5700;
        public static Vector2 Target(TrainingTask task,TrainingLessonDefinition lesson)
        {
            if(task>=TrainingTask.SoaringLift&&task<=TrainingTask.SoaringLiftExit && lesson.soaringWeather!=null&&lesson.soaringWeather.sources.Length>0)return lesson.soaringWeather.sources[0].position;
            return task switch
            {
                TrainingTask.SoaringRidgeTrack or TrainingTask.SoaringOutwardTurn=>new Vector2(-6150,1400),
                TrainingTask.SoaringRidgeExit=>new Vector2(RidgeExit,1400),
                TrainingTask.SoaringDownwind=>new Vector2(-500,(DownwindSouth+DownwindNorth)/2),
                TrainingTask.SoaringBase=>new Vector2((BaseWest+BaseEast)/2,350),
                TrainingTask.SoaringFinal=>new Vector2(-1000,-30),
                TrainingTask.SoaringLanding or TrainingTask.SoaringStop=>new Vector2(-400,-30),
                _=>Vector2.zero
            };
        }
        public static string Description(TrainingTask task)=>task switch
        {
            TrainingTask.SoaringLift or TrainingTask.SoaringEnergyGain or TrainingTask.SoaringLiftExit=>"Thermal reference: cyan source marker northeast of the field. The core tilts/drifts with height and wind; locate useful lift with energy indications, not the ground marker alone.",
            TrainingTask.SoaringRidgeTrack or TrainingTask.SoaringOutwardTurn or TrainingTask.SoaringRidgeExit=>"Ridge reference: west of airport. Remain east of the cyan west boundary (6.25 km west); turn through open east. Exit beyond 5.7 km west with 80 m terrain clearance.",
            TrainingTask.SoaringDownwind or TrainingTask.SoaringBase or TrainingTask.SoaringFinal or TrainingTask.SoaringLanding or TrainingTask.SoaringStop=>"Runway 08 reference: downwind box 350–800 m north, west of origin, above 180 m FIELD height. Base 700–1800 m west. Final eastbound along paved centerline 30 m south. Cyan is the active segment, not a certified procedure.",
            _=>"Ground/tow reference: the selected runway and tow route determine the operation. Release remains pilot-controlled; wait for the live release-area cue."
        };
        public static Vector2[] Outline(TrainingTask task,Vector2 target)=>task switch
        {
            TrainingTask.SoaringDownwind=>new[]{new Vector2(BaseEast,DownwindSouth),new Vector2(0,DownwindSouth),new Vector2(0,DownwindNorth),new Vector2(BaseEast,DownwindNorth),new Vector2(BaseEast,DownwindSouth)},
            TrainingTask.SoaringBase=>new[]{new Vector2(BaseWest,0),new Vector2(BaseEast,0),new Vector2(BaseEast,700),new Vector2(BaseWest,700),new Vector2(BaseWest,0)},
            TrainingTask.SoaringFinal or TrainingTask.SoaringLanding or TrainingTask.SoaringStop=>new[]{new Vector2(-1800,-30),new Vector2(640,-30)},
            TrainingTask.SoaringRidgeTrack or TrainingTask.SoaringOutwardTurn or TrainingTask.SoaringRidgeExit=>new[]{new Vector2(RidgeBoundary,500),new Vector2(RidgeBoundary,2500),new Vector2(RidgeExit,2500),new Vector2(RidgeExit,500)},
            _=>new[]{target+Vector2.left*100,target+Vector2.right*100,target,target+Vector2.up*100,target+Vector2.down*100}
        };
    }
}
