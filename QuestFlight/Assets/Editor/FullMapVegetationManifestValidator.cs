using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Central editor-only contract for the accepted V3.2 full-map vegetation data.
    /// Authoring and tests share this parser so JSON formatting cannot become policy.
    /// </summary>
    public static class FullMapVegetationManifestValidator
    {
        public const string AcceptedSchema =
            "questflight.full-map-vegetation-hlod.v3.2";
        public const string AcceptedGeometry =
            "volumetric near trees/shrubs plus small eight-view individual impostors";
        public const string AcceptedIndividualOrder =
            "stable real-placement spatial hash with a bounded shrub reserve";
        public const string AcceptedFarHandoff =
            "orthophoto-authoritative canopy color plus aggregate crown normals";
        public const string AcceptedGrovePolicy =
            "no aggregate grove geometry; grouped real placements form groves";

        [Serializable]
        private sealed class Manifest
        {
            public string schema;
            public Contract contract;
            public Processing processing;
            public Statistics statistics;
        }

        [Serializable]
        private sealed class Contract
        {
            public string coordinate_frame;
            public float[] bounds_meters;
            public float runtime_page_meters;
            public int columns;
            public int rows;
            public string far_prefix;
            public string near_suffix;
            public string geometry;
            public string far_handoff;
        }

        [Serializable]
        private sealed class Processing
        {
            public string source_schema;
            public int far_prefix_records;
            public int far_prefix_shrub_reserve;
            public string individual_order;
            public string grove_policy;
        }

        [Serializable]
        private sealed class Statistics
        {
            public int runtime_records;
            public int grove_hlods;
            public long binary_bytes;
            public string binary_sha256;
            public int retained_individuals;
        }

        public static void ValidateOrThrow(TextAsset manifestAsset, TextAsset databaseAsset)
        {
            if (!TryValidate(manifestAsset, databaseAsset, out string report))
                throw new InvalidOperationException(
                    "Full-map vegetation V3.2 contract is invalid: " + report);
        }

        public static bool TryValidate(
            TextAsset manifestAsset,
            TextAsset databaseAsset,
            out string report)
        {
            List<string> failures = new List<string>();
            if (manifestAsset == null) failures.Add("manifest asset is missing");
            if (databaseAsset == null) failures.Add("database asset is missing");
            if (failures.Count > 0)
            {
                report = string.Join("; ", failures);
                return false;
            }

            Manifest manifest = null;
            try
            {
                manifest = JsonUtility.FromJson<Manifest>(manifestAsset.text);
            }
            catch (Exception exception)
            {
                failures.Add("manifest JSON could not be decoded: " + exception.Message);
            }

            if (manifest == null)
            {
                failures.Add("manifest JSON decoded to null");
            }
            else
            {
                Require(manifest.schema == AcceptedSchema,
                    $"schema must be {AcceptedSchema}", failures);
                ValidateContract(manifest.contract, failures);
                ValidateProcessing(manifest.processing, failures);
                ValidateStatistics(manifest.statistics, databaseAsset.bytes, failures);
            }

            report = failures.Count == 0 ? "PASS" : string.Join("; ", failures);
            return failures.Count == 0;
        }

        private static void ValidateContract(Contract contract, List<string> failures)
        {
            if (contract == null)
            {
                failures.Add("contract is missing");
                return;
            }

            Require(contract.coordinate_frame ==
                    "KBDU local AEQD; Unity +X east, +Y up, +Z north",
                "coordinate frame changed", failures);
            Require(contract.bounds_meters != null && contract.bounds_meters.Length == 4 &&
                    contract.bounds_meters[0] == -12000f &&
                    contract.bounds_meters[1] == -12000f &&
                    contract.bounds_meters[2] == 12000f &&
                    contract.bounds_meters[3] == 12000f,
                "24 km bounds changed", failures);
            Require(contract.runtime_page_meters == 1000f &&
                    contract.columns == 24 && contract.rows == 24,
                "runtime page grid changed", failures);
            Require(contract.far_prefix ==
                    "up to 128 real trees plus 16 shrubs in stable whole-page spatial order",
                "far-prefix selection contract changed", failures);
            Require(contract.near_suffix ==
                    "all remaining real individual trees and shrubs in stable spatial order",
                "near-suffix selection contract changed", failures);
            Require(contract.geometry == AcceptedGeometry,
                "individual-impostor geometry policy changed", failures);
            Require(contract.far_handoff == AcceptedFarHandoff,
                "orthophoto handoff policy changed", failures);
        }

        private static void ValidateProcessing(
            Processing processing,
            List<string> failures)
        {
            if (processing == null)
            {
                failures.Add("processing contract is missing");
                return;
            }

            Require(processing.source_schema ==
                    "questflight.full-map-vegetation-clipmap.v2",
                "source schema changed", failures);
            Require(processing.far_prefix_records == 144 &&
                    processing.far_prefix_shrub_reserve == 16,
                "far-prefix record contract changed", failures);
            Require(processing.individual_order == AcceptedIndividualOrder,
                "stable individual order changed", failures);
            Require(processing.grove_policy == AcceptedGrovePolicy,
                "no-aggregate-grove policy changed", failures);
        }

        private static void ValidateStatistics(
            Statistics statistics,
            byte[] databaseBytes,
            List<string> failures)
        {
            if (statistics == null)
            {
                failures.Add("statistics are missing");
                return;
            }

            Require(statistics.runtime_records > 0 &&
                    statistics.retained_individuals == statistics.runtime_records,
                "runtime/retained individual counts are invalid", failures);
            Require(statistics.grove_hlods == 0,
                "aggregate grove records are prohibited", failures);
            Require(databaseBytes != null && statistics.binary_bytes == databaseBytes.LongLength,
                "database byte length does not match the manifest", failures);
            Require(databaseBytes != null &&
                    string.Equals(statistics.binary_sha256, Sha256(databaseBytes),
                        StringComparison.OrdinalIgnoreCase),
                "database SHA-256 does not match the manifest", failures);
            Require(databaseBytes != null && databaseBytes.Length >= 12 &&
                    databaseBytes[0] == (byte)'Q' && databaseBytes[1] == (byte)'F' &&
                    databaseBytes[2] == (byte)'F' && databaseBytes[3] == (byte)'M' &&
                    databaseBytes[4] == (byte)'V' && databaseBytes[5] == (byte)'G' &&
                    databaseBytes[6] == (byte)'3' && databaseBytes[7] == 0 &&
                    BitConverter.ToInt32(databaseBytes, 8) == 3,
                "database header/version is not QFFMVG3 version 3", failures);
        }

        private static string Sha256(byte[] bytes)
        {
            if (bytes == null) return string.Empty;
            using SHA256 sha = SHA256.Create();
            byte[] digest = sha.ComputeHash(bytes);
            return BitConverter.ToString(digest).Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static void Require(
            bool condition,
            string failure,
            ICollection<string> failures)
        {
            if (!condition) failures.Add(failure);
        }
    }
}
