using UnityEditor;
namespace QuestFlightLab.Editor
{
    public static class MenuWorkflowAuthoring
    {
        public static void Run()
        {
            SoaringTrainingAuthoring.Run();
            QuestFlight.Editor.ProductLessonTeachingAuthoring.Apply();
            AssetDatabase.SaveAssets();
        }
    }
}
