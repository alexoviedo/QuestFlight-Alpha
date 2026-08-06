using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics
{
    /// <summary>
    /// Compact hardware evidence for the exact Meta concurrent route. Only
    /// startup, selection boundaries, and rejected updates are persisted;
    /// accepted per-frame Move events never perform file I/O.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OvrConcurrentInputTrace : MonoBehaviour
    {
        [SerializeField] private OVRManager manager;

        private static OvrConcurrentInputTrace _instance;
        private string _path;
        private bool _simultaneousEnableResult;
        private const long MaximumTraceBytes = 256 * 1024;

        public bool SimultaneousEnableResult => _simultaneousEnableResult;

        public void ConfigureAuthoredReferences(OVRManager authoredManager)
        {
            manager = authoredManager;
        }

        private void Awake()
        {
            _instance = this;
            _path = Path.Combine(
                Application.persistentDataPath,
                "questflight_ovr_concurrent_trace.jsonl");
            if (manager == null)
            {
                Debug.LogError(
                    "[QuestFlight][OVRInput] Serialized OVRManager is missing.",
                    this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            _simultaneousEnableResult =
                OVRPlugin.SetSimultaneousHandsAndControllersEnabled(true);
            Debug.Log(
                "[QuestFlight][OVRInput] simultaneous hands/controllers=" +
                _simultaneousEnableResult,
                this);
        }

        private void Start()
        {
            WriteLine(
                "{\"type\":\"startup\",\"enabled\":" +
                (_simultaneousEnableResult ? "true" : "false") +
                ",\"managerConfigured\":" +
                (manager != null &&
                 manager.SimultaneousHandsAndControllersEnabled &&
                 manager.launchSimultaneousHandsControllersOnStartup
                    ? "true" : "false") + "}");
        }

        private void OnDisable()
        {
            OVRPlugin.SetSimultaneousHandsAndControllersEnabled(false);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public static void RecordInteraction(
            in CockpitTouchInteractionV2Command command,
            bool accepted,
            string adapterName)
        {
            if (_instance == null) return;
            if (accepted &&
                command.operation ==
                    CockpitTouchInteractionV2Operation.UpdateGrab)
                return;
            var builder = new StringBuilder(256);
            builder.Append("{\"type\":\"interaction\",\"frame\":")
                .Append(command.frameNumber)
                .Append(",\"source\":\"").Append(command.sourceKind)
                .Append("\",\"side\":\"").Append(command.hand)
                .Append("\",\"target\":\"").Append(command.target)
                .Append("\",\"operation\":\"").Append(command.operation)
                .Append("\",\"accepted\":")
                .Append(accepted ? "true" : "false")
                .Append(",\"adapter\":\"")
                .Append(Escape(adapterName)).Append("\"}");
            _instance.WriteLine(builder.ToString());
        }

        private void WriteLine(string line)
        {
            try
            {
                if (File.Exists(_path) &&
                    new FileInfo(_path).Length > MaximumTraceBytes)
                    File.Delete(_path);
                File.AppendAllText(_path, line + System.Environment.NewLine);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[QuestFlight][OVRInput] trace write failed: " +
                    exception.Message,
                    this);
            }
        }

        private static string Escape(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
