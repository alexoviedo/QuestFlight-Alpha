using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AlexOviedo.QuestFlight.JSBSim;
using UnityEngine;

namespace QuestFlight.Editor.FlightDynamicsValidation
{
    [Serializable]
    public sealed class NativeFlightInputEvidence
    {
        public string status = "Unproven";
        public string sourceCommit, sourceInputsSha256, unityVersion, platform;
        public int apiVersion;
        public string bridgeSha256, jsbsimSha256, payloadSha256, aircraftXmlSha256, detail;
    }

    [Serializable]
    public sealed class FlightScenarioInputEvidence
    {
        public string scenario, definitionSha256, definitionJson;
    }

    public static class NativeFlightEvidenceIdentity
    {
        public static string NumericFieldsJson(object value) => "{" + string.Join(",", value.GetType().GetFields()
            .OrderBy(field=>field.Name,StringComparer.Ordinal).Select(field=>"\""+field.Name+"\":"+
                (field.GetValue(value) is double number ? number.ToString("R",CultureInfo.InvariantCulture) : Convert.ToString(field.GetValue(value),CultureInfo.InvariantCulture)))) + "}";

        public static string HashText(string text)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        public static string HashFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (Stream stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static NativeFlightInputEvidence Capture()
        {
            var result = new NativeFlightInputEvidence {unityVersion=Application.unityVersion, platform=Application.platform.ToString()};
            try
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
                var start = new ProcessStartInfo("git", "rev-parse HEAD") {WorkingDirectory=root,UseShellExecute=false,RedirectStandardOutput=true,CreateNoWindow=true};
                using (Process process = Process.Start(start))
                {
                    result.sourceCommit = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    if (process.ExitCode != 0 || result.sourceCommit.Length != 40) throw new InvalidOperationException("Source HEAD could not be resolved.");
                }
                string[] dirs = {"tools/native/jsbsim_bridge", "QuestFlight/Packages/com.alexoviedo.questflight.jsbsim/Runtime", "QuestFlight/Assets/Scripts/Flight", "QuestFlight/Assets/Editor/FlightDynamicsValidation"};
                IEnumerable<string> files = dirs.SelectMany(dir=>Directory.EnumerateFiles(Path.Combine(root,dir),"*",SearchOption.AllDirectories))
                    .Where(path=>new[]{".cs",".cpp",".h",".txt"}.Contains(Path.GetExtension(path)))
                    .OrderBy(path=>path,StringComparer.Ordinal);
                result.sourceInputsSha256 = HashText(string.Join("\n", files.Select(path=>path.Substring(root.Length+1).Replace('\\','/')+" "+HashFile(path))));
                // Hash modules actually loaded by this process, including Unity's copied plugin path.
                using (Process process = Process.GetCurrentProcess())
                {
                    foreach (ProcessModule module in process.Modules)
                    {
                        if (module.ModuleName.Equals("qfl_jsbsim_native.dll", StringComparison.OrdinalIgnoreCase)) result.bridgeSha256=HashFile(module.FileName);
                        if (module.ModuleName.Equals("JSBSim.dll", StringComparison.OrdinalIgnoreCase)) result.jsbsimSha256=HashFile(module.FileName);
                    }
                }
                if (string.IsNullOrEmpty(result.bridgeSha256) || string.IsNullOrEmpty(result.jsbsimSha256))
                    throw new InvalidOperationException("Actual loaded native module identity is unavailable on this evidence platform.");
                string dataRoot = JSBSimRuntimeData.EditorPackageDataRoot;
                if (!JSBSimRuntimeData.ValidateDataRoot(dataRoot, out string error)) throw new InvalidOperationException(error);
                result.payloadSha256=JSBSimRuntimeData.PayloadIdentity(dataRoot);
                result.aircraftXmlSha256=HashFile(Path.Combine(dataRoot,"aircraft/c172x/c172x.xml"));
                if (!JSBSimNativeSession.TryProbe(out string probeError)) throw new InvalidOperationException(probeError);
                result.apiVersion=JSBSimNativeSession.ExpectedApiVersion;
                result.status="Pass";
                result.detail="Actual loaded Windows native modules and validated exact session data root. Source HEAD plus current relevant source digest identifies this uncommitted candidate; this is not APK or hardware identity.";
            }
            catch(Exception exception) {result.status="Error";result.detail=exception.Message;}
            return result;
        }
    }
}
