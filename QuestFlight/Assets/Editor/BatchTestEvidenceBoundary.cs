using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace QuestFlight.Editor
{
    // SDK205's optional MCP observer appends CLI results to a persistent Editor
    // preference on every test, eventually exhausting memory while serializing
    // its accumulated history. CLI NUnit XML remains the authoritative evidence.
    // Do not alter the SDK, tests, interactive MCP sessions or shipping player.
    [InitializeOnLoad]
    internal static class BatchTestEvidenceBoundary
    {
        static BatchTestEvidenceBoundary()
        {
            if(Application.isBatchMode&&System.Environment.GetCommandLineArgs().Contains("-runTests"))
                EditorApplication.update+=DetachOptionalMcpObserver;
        }
        private static void DetachOptionalMcpObserver()
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Meta.MCPBridge.Editor.TestRunnerTools",false)).FirstOrDefault(t=>t!=null);
            if(type==null)return;
            const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
            var api=type.GetField("_testRunnerApi",flags)?.GetValue(null) as TestRunnerApi;
            var callback=type.GetField("_callbacks",flags)?.GetValue(null) as ICallbacks;
            if(api==null||callback==null)return;
            api.UnregisterCallbacks(callback);
            EditorApplication.update-=DetachOptionalMcpObserver;
            Debug.Log("[QuestFlight][BatchTests] Optional MCP history observer detached for CLI tests; NUnit result recording retained.");
        }
    }
}
