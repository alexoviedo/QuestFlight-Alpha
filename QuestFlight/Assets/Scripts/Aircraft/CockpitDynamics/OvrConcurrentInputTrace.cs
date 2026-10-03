using System;
using System.IO;
using System.Text;
using Oculus.Interaction;
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
        private string _sessionId;
        private bool _simultaneousEnableResult;
        private const long MaximumTraceBytes = 256 * 1024;
        private readonly StringBuilder pendingTrace = new StringBuilder(8192);
        private long acceptedTraceBytes;
        private float nextTraceFlush;
        private bool traceWriteFailed;

        public bool SimultaneousEnableResult => _simultaneousEnableResult;

        public void ConfigureAuthoredReferences(OVRManager authoredManager)
        {
            manager = authoredManager;
        }

        private void Awake()
        {
            _instance = this;
            _sessionId = Guid.NewGuid().ToString("N");
            string fileName =
                "questflight_ovr_concurrent_trace_" +
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" +
                _sessionId + ".jsonl";
            _path = Path.Combine(
                Application.persistentDataPath,
                fileName);
            File.WriteAllText(
                Path.Combine(
                    Application.persistentDataPath,
                    "questflight_ovr_concurrent_trace_latest.txt"),
                fileName);
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
                "{\"type\":\"startup\",\"session\":\"" +
                _sessionId + "\",\"buildGuid\":\"" +
                Escape(Application.buildGUID) +
                "\",\"version\":\"" +
                Escape(Application.version) +
                "\",\"unity\":\"" +
                Escape(Application.unityVersion) +
                "\",\"enabled\":" +
                (_simultaneousEnableResult ? "true" : "false") +
                ",\"managerConfigured\":" +
                (manager != null &&
                 manager.SimultaneousHandsAndControllersEnabled &&
                 manager.launchSimultaneousHandsControllersOnStartup
                    ? "true" : "false") + "}");
        }

        private void OnDisable()
        {
            FlushTrace();
            OVRPlugin.SetSimultaneousHandsAndControllersEnabled(false);
        }

        private void Update()
        {
            if (pendingTrace.Length > 0 && Time.unscaledTime >= nextTraceFlush)
            {
                FlushTrace();
                nextTraceFlush = Time.unscaledTime + 1f;
            }
        }

        private void OnApplicationPause(bool paused) { if (paused) FlushTrace(); }
        private void OnApplicationQuit() { FlushTrace(); }

        private void OnDestroy()
        {
            FlushTrace();
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

        public static void RecordPointerEvent(
            PointerEvent evt,
            CockpitTouchInteractionV2Target target,
            CockpitTouchInteractionV2Hand hand,
            CockpitInputSourceKind sourceKind,
            string pointableName,
            int grabPointCount)
        {
            if (_instance == null) return;
            var builder = new StringBuilder(320);
            builder.Append("{\"type\":\"pointer\",\"frame\":")
                .Append(Time.frameCount)
                .Append(",\"pointerId\":").Append(evt.Identifier)
                .Append(",\"eventId\":").Append(evt.EventId)
                .Append(",\"event\":\"").Append(evt.Type)
                .Append("\",\"source\":\"").Append(sourceKind)
                .Append("\",\"side\":\"").Append(hand)
                .Append("\",\"target\":\"").Append(target)
                .Append("\",\"pointable\":\"")
                .Append(Escape(pointableName))
                .Append("\",\"grabPoints\":").Append(grabPointCount)
                .Append("}");
            _instance.WriteLine(builder.ToString());
        }

        public static void RecordTransformer(
            CockpitTouchInteractionV2Target target,
            string operation,
            bool accepted,
            int grabPointCount,
            float primary,
            float secondary,
            string reason = "")
        {
            if (_instance == null) return;
            var builder = new StringBuilder(320);
            builder.Append("{\"type\":\"transformer\",\"frame\":")
                .Append(Time.frameCount)
                .Append(",\"target\":\"").Append(target)
                .Append("\",\"operation\":\"")
                .Append(Escape(operation))
                .Append("\",\"accepted\":")
                .Append(accepted ? "true" : "false")
                .Append(",\"grabPoints\":").Append(grabPointCount)
                .Append(",\"primary\":")
                .Append(primary.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                .Append(",\"secondary\":")
                .Append(secondary.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                .Append(",\"reason\":\"")
                .Append(Escape(reason)).Append("\"}");
            _instance.WriteLine(builder.ToString());
        }

        private void WriteLine(string line)
        {
            // The unique session file starts empty. Keep its bounded size in
            // memory; interaction callbacks must not open/stat the disk per event.
            if (traceWriteFailed || string.IsNullOrEmpty(_path)) return;
            int bytes = Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(System.Environment.NewLine);
            if (acceptedTraceBytes + bytes > MaximumTraceBytes) return;
            acceptedTraceBytes += bytes;
            pendingTrace.AppendLine(line);
            if (pendingTrace.Length >= 16384) FlushTrace();
        }

        private void FlushTrace()
        {
            if (pendingTrace.Length == 0 || traceWriteFailed || string.IsNullOrEmpty(_path)) return;
            try
            {
                File.AppendAllText(_path, pendingTrace.ToString());
                pendingTrace.Clear();
            }
            catch (Exception exception)
            {
                traceWriteFailed = true;
                pendingTrace.Clear();
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
