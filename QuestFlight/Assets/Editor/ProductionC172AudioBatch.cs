using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static class ProductionC172AudioBatch
    {
        public static void Run()
        {
            try
            {
                ProductionC172AudioAuthoring.AuthorAndIntegrate();
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
