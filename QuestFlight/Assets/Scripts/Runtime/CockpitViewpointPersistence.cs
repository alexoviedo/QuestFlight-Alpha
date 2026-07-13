using System;
using System.IO;
using UnityEngine;

namespace QuestFlightLab.Runtime
{
    [Serializable]
    public class CockpitViewpointCalibrationState
    {
        public int schemaVersion;
        public string aircraftId;
        public string generatedUtc;
        public string sceneryMode;
        public string demoMode;
        public Vector3 importedC172SeatReferenceLocal;
        public Vector3 importedC172DefaultPilotViewOffset;
        public Vector3 importedC172CockpitModelEye;
        public Vector3 importedC172PilotViewOffset;
        public float importedC172CockpitYawDeg;
        public Vector3 calibrationOffset;
        public float calibrationYawDeg;
        public Vector3 pilotEyeLocal;
        public Vector3 importedC172LocalPosition;
        public string instructions;
    }

    public static class CockpitViewpointPersistence
    {
        [Serializable]
        private sealed class CalibrationRecord
        {
            public int schemaVersion;
            public string aircraftId;
            public string generatedUtc;
            public Vector3 calibrationOffset;
            public float calibrationYawDeg;
        }

        public const int SchemaVersion = 5;
        public const string DefaultAircraftId = "imported_c172";
        public const string DirectoryName = "seat_calibration";
        public const string CurrentFileName = "seat_calibration_current.json";

        public static string DirectoryPath(string persistentDataRoot = null, string aircraftId = DefaultAircraftId)
        {
            string root = string.IsNullOrWhiteSpace(persistentDataRoot)
                ? Application.persistentDataPath
                : persistentDataRoot;
            return Path.Combine(root, "QuestFlightLab", DirectoryName, SanitizeAircraftId(aircraftId));
        }

        public static string CurrentPath(string persistentDataRoot = null, string aircraftId = DefaultAircraftId)
        {
            return Path.Combine(DirectoryPath(persistentDataRoot, aircraftId), CurrentFileName);
        }

        public static string SaveCurrent(CockpitViewpointCalibrationState state, string persistentDataRoot = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.schemaVersion = SchemaVersion;
            if (string.IsNullOrWhiteSpace(state.aircraftId)) state.aircraftId = DefaultAircraftId;
            if (string.IsNullOrWhiteSpace(state.generatedUtc)) state.generatedUtc = DateTime.UtcNow.ToString("O");

            // Schema 5 deliberately persists only the seat-relative calibration.
            // Tracked pose, model alignment, scenery, and diagnostic fields are
            // never part of the canonical per-aircraft save record.
            CalibrationRecord record = new CalibrationRecord
            {
                schemaVersion = SchemaVersion,
                aircraftId = state.aircraftId,
                generatedUtc = state.generatedUtc,
                calibrationOffset = state.calibrationOffset,
                calibrationYawDeg = state.calibrationYawDeg
            };

            string dir = DirectoryPath(persistentDataRoot, state.aircraftId);
            Directory.CreateDirectory(dir);
            string timestampedPath = Path.Combine(dir, $"seat_calibration_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            string currentPath = CurrentPath(persistentDataRoot, state.aircraftId);
            string temporaryPath = currentPath + ".tmp";
            string json = JsonUtility.ToJson(record, true);
            File.WriteAllText(timestampedPath, json);
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(currentPath)) File.Delete(currentPath);
            File.Move(temporaryPath, currentPath);
            return currentPath;
        }

        public static bool TryLoadCurrent(
            out CockpitViewpointCalibrationState state,
            out string path,
            out string error,
            string persistentDataRoot = null,
            string aircraftId = DefaultAircraftId)
        {
            state = null;
            error = string.Empty;
            path = CurrentPath(persistentDataRoot, aircraftId);
            if (!File.Exists(path))
            {
                string root = string.IsNullOrWhiteSpace(persistentDataRoot)
                    ? Application.persistentDataPath
                    : persistentDataRoot;
                string legacyPath = Path.Combine(root, "QuestFlightLab", DirectoryName, CurrentFileName);
                if (!File.Exists(legacyPath)) return false;
                path = legacyPath;
            }

            try
            {
                string json = File.ReadAllText(path);
                state = JsonUtility.FromJson<CockpitViewpointCalibrationState>(json);
                if (state == null)
                {
                    error = "empty or invalid calibration JSON";
                    return false;
                }

                if (state.schemaVersion <= 0 || state.schemaVersion > SchemaVersion)
                {
                    error = $"unsupported schema {state.schemaVersion}; expected 1-{SchemaVersion}";
                    state = null;
                    return false;
                }

                if (state.schemaVersion <= 3)
                {
                    // Legacy yaw rotated the cockpit model, which has the opposite
                    // relative-view meaning. Preserve translation, but reset yaw
                    // rather than reopening an existing calibration mirrored.
                    state.aircraftId = string.IsNullOrWhiteSpace(state.aircraftId) ? aircraftId : state.aircraftId;
                    state.calibrationOffset = state.importedC172PilotViewOffset;
                    state.calibrationYawDeg = 0f;
                }
                else if (state.schemaVersion >= 4)
                {
                    CalibrationRecord record = JsonUtility.FromJson<CalibrationRecord>(json);
                    if (record != null)
                    {
                        state.aircraftId = record.aircraftId;
                        state.generatedUtc = record.generatedUtc;
                        state.calibrationOffset = record.calibrationOffset;
                        state.calibrationYawDeg = record.calibrationYawDeg;
                    }
                }

                if (string.IsNullOrWhiteSpace(state.aircraftId)) state.aircraftId = aircraftId;
                state.schemaVersion = SchemaVersion;
                state.importedC172PilotViewOffset = state.calibrationOffset;
                state.importedC172CockpitYawDeg = state.calibrationYawDeg;

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                state = null;
                return false;
            }
        }

        public static bool DeleteCurrent(
            out string path,
            out string error,
            string persistentDataRoot = null,
            string aircraftId = DefaultAircraftId)
        {
            path = CurrentPath(persistentDataRoot, aircraftId);
            error = string.Empty;

            try
            {
                bool deleted = false;
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted = true;
                }

                string root = string.IsNullOrWhiteSpace(persistentDataRoot)
                    ? Application.persistentDataPath
                    : persistentDataRoot;
                string legacyPath = Path.Combine(root, "QuestFlightLab", DirectoryName, CurrentFileName);
                if (File.Exists(legacyPath))
                {
                    File.Delete(legacyPath);
                    deleted = true;
                }

                return deleted;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string SanitizeAircraftId(string aircraftId)
        {
            string value = string.IsNullOrWhiteSpace(aircraftId) ? DefaultAircraftId : aircraftId.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value.Replace(' ', '_').ToLowerInvariant();
        }
    }
}
