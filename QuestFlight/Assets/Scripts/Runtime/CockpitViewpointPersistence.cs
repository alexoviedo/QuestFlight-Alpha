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

        public static string SaveCurrent(CockpitViewpointCalibrationState state, string persistentDataRoot = null,
            Action<string> saveBoundary = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.schemaVersion = SchemaVersion;
            if (string.IsNullOrWhiteSpace(state.aircraftId)) state.aircraftId = DefaultAircraftId;
            if (string.IsNullOrWhiteSpace(state.generatedUtc)) state.generatedUtc = DateTime.UtcNow.ToString("O");
            if (!Finite(state.calibrationOffset) || !Finite(state.calibrationYawDeg))
                throw new InvalidDataException("Seat-relative calibration must be finite.");

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
            saveBoundary?.Invoke("before_timestamp_write");
            File.WriteAllText(timestampedPath, json);
            saveBoundary?.Invoke("before_temporary_write");
            File.WriteAllText(temporaryPath, json);
            saveBoundary?.Invoke("before_replace");
            // Match the existing ProductLocalSave replacement contract: retain the
            // prior committed record as .bak instead of deleting the canonical file.
            if (File.Exists(currentPath))
            {
                // A corrupt canonical record must not overwrite a valid recovery copy.
                string backup = TryLoadRecord(currentPath, state.aircraftId, out _, out _) ? currentPath + ".bak" : null;
                File.Replace(temporaryPath, currentPath, backup);
            }
            else File.Move(temporaryPath, currentPath);
            saveBoundary?.Invoke("after_replace");
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
            string current = CurrentPath(persistentDataRoot, aircraftId);
            string root = string.IsNullOrWhiteSpace(persistentDataRoot)
                ? Application.persistentDataPath : persistentDataRoot;
            string legacy = Path.Combine(root, "QuestFlightLab", DirectoryName, CurrentFileName);
            string firstError = string.Empty;
            foreach (string candidate in new[] { current, current + ".bak", current + ".tmp", legacy })
            {
                if (!File.Exists(candidate)) continue;
                if (TryLoadRecord(candidate, aircraftId, out state, out string candidateError))
                {
                    path = candidate;
                    error = candidate == current ? string.Empty : "Recovered validated seat calibration from " + Path.GetFileName(candidate);
                    return true;
                }
                if (string.IsNullOrEmpty(firstError)) firstError = candidateError;
            }
            path = current;
            error = firstError;
            return false;
        }

        private static bool TryLoadRecord(string path, string aircraftId, out CockpitViewpointCalibrationState state, out string error)
        {
            state = null;
            error = string.Empty;
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
                if (!string.Equals(SanitizeAircraftId(state.aircraftId), SanitizeAircraftId(aircraftId), StringComparison.Ordinal) ||
                    !Finite(state.calibrationOffset) || !Finite(state.calibrationYawDeg))
                {
                    state = null;
                    error = "Calibration has a mismatched aircraft or non-finite seat transform.";
                    return false;
                }
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
                foreach (string recoveryPath in new[] { path + ".bak", path + ".tmp" })
                    if (File.Exists(recoveryPath)) { File.Delete(recoveryPath); deleted = true; }

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

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
