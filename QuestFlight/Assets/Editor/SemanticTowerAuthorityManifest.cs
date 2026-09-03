using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>Editor-only reader for the frozen semantic-tower authority decision.</summary>
    public static class SemanticTowerAuthorityManifest
    {
        public const string AssetPath =
            "Assets/Production/Environment/SemanticTowersV1/KBDU_SemanticTowerAuthorityV1.json";

        [Serializable]
        public sealed class Document
        {
            public string schema;
            public string coordinate_frame;
            public float origin_elevation_msl_meters;
            public float product_half_extent_meters;
            public float inventory_safety_margin_meters;
            public string vertical_surface_authority;
            public string source_epoch_policy;
            public string fixed_presentation;
            public SourceRecord[] sources;
            public InventorySummary inventory_summary;
            public ExclusionRecord[] exclusions;
            public StructureRecord[] structures;
        }

        [Serializable]
        public sealed class InventorySummary
        {
            public int faa_obstacles_in_product_bounds;
            public int faa_obstacles_in_inventory_extent;
            public int faa_vertical_candidates_at_least_15m;
            public int faa_vertical_candidates_in_inventory_extent;
            public int osm_vertical_elements_in_query;
            public int osm_vertical_elements_overlapping_building_shells;
            public int preserved_extreme_named_building_components;
            public int accepted_structures;
            public int excluded_building_shells;
            public int authored_obstruction_light_systems;
            public int authored_obstruction_fixtures;
            public int shipping_active_obstruction_emitters;
        }

        [Serializable]
        public sealed class SourceRecord
        {
            public string authority;
            public string source_url;
            public string acquired_utc;
            public string source_date;
            public string sha256;
        }

        [Serializable]
        public sealed class ExclusionRecord
        {
            public int building_id;
            public string building_source_id;
            public int component;
            public string authority_id;
            public float authority_distance_meters;
            public string horizontal_accuracy_code;
            public float horizontal_accuracy_radius_meters;
            public string replacement_stable_id;
        }

        [Serializable]
        public sealed class StructureRecord
        {
            public string stable_id;
            public string semantic_type;
            public string selected_archetype;
            public string faa_obstacle_id;
            public string fcc_asr_number;
            public string osm_source_id;
            public double latitude_degrees;
            public double longitude_degrees;
            public float product_x_meters;
            public float product_z_meters;
            public float height_agl_meters;
            public float terrain_elevation_product_meters;
            public float terrain_elevation_msl_meters;
            public float authority_ground_elevation_msl_meters;
            public float authority_top_elevation_msl_meters;
            public float baked_top_elevation_msl_meters;
            public float amsl_difference_meters;
            public float heading_degrees_true;
            public string canonical_position_authority;
            public string height_authority;
            public string type_authority;
            public string chronology_authority;
            public string chronology_status;
            public string imagery_confirmation;
            public string lighting_authority;
            public string lighting_code;
            public string lighting_shipping_state;
            public string marking_code;
            public string confidence;
            public string coordinate_confidence;
            public string height_confidence;
            public string derivative_asset;
            public string lod0_mesh;
            public string lod1_mesh;
            public float lod0_screen_height;
            public float lod1_screen_height;
            public int[] preserved_building_ids;
            public int[] excluded_building_ids;
        }

        public static Document Load()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetPath);
            if (asset == null) throw new FileNotFoundException("Semantic tower authority manifest is missing.", AssetPath);
            Document document = JsonUtility.FromJson<Document>(asset.text);
            Validate(document);
            return document;
        }

        public static HashSet<int> LoadExcludedBuildingIds() =>
            new HashSet<int>(Load().exclusions.Select(record => record.building_id));

        private static void Validate(Document document)
        {
            if (document == null ||
                !string.Equals(document.schema, "questflight.semantic-tower-authority-v1", StringComparison.Ordinal) ||
                document.inventory_summary == null || document.exclusions == null || document.structures == null)
                throw new InvalidOperationException("Semantic tower authority manifest contract is invalid.");
            if (document.structures.Length != document.inventory_summary.accepted_structures ||
                document.exclusions.Length != document.inventory_summary.excluded_building_shells)
                throw new InvalidOperationException("Semantic tower authority summary is stale.");
            if (document.exclusions.Select(record => record.building_id).Distinct().Count() !=
                document.exclusions.Length)
                throw new InvalidOperationException("Semantic tower building exclusions are not unique.");
            if (document.structures.Select(record => record.stable_id).Distinct(StringComparer.Ordinal).Count() !=
                document.structures.Length)
                throw new InvalidOperationException("Semantic tower stable ids are not unique.");
            if (document.structures.Any(record =>
                    string.IsNullOrWhiteSpace(record.stable_id) || record.height_agl_meters <= 0f ||
                    string.IsNullOrWhiteSpace(record.derivative_asset) ||
                    string.IsNullOrWhiteSpace(record.selected_archetype) ||
                    string.IsNullOrWhiteSpace(record.chronology_status) ||
                    string.IsNullOrWhiteSpace(record.imagery_confirmation) ||
                    string.IsNullOrWhiteSpace(record.lighting_code)))
                throw new InvalidOperationException("Semantic tower structure authority is incomplete.");
            if (document.sources == null || document.sources.Length != 4 ||
                document.sources.Take(3).Any(record => string.IsNullOrWhiteSpace(record.sha256)))
                throw new InvalidOperationException("Semantic tower provenance sources are incomplete.");
            if (document.inventory_summary.authored_obstruction_light_systems != 1 ||
                document.inventory_summary.authored_obstruction_fixtures != 3 ||
                document.inventory_summary.shipping_active_obstruction_emitters != 0)
                throw new InvalidOperationException("Semantic tower obstruction-light authority is stale.");

            HashSet<int> structureExclusions = new HashSet<int>(document.structures
                .SelectMany(record => record.excluded_building_ids ?? Array.Empty<int>()));
            if (!structureExclusions.SetEquals(document.exclusions.Select(record => record.building_id)))
                throw new InvalidOperationException("Structure and compiler exclusion decisions disagree.");
            if (document.exclusions.Any(record => record.authority_distance_meters < 0f ||
                record.authority_distance_meters > record.horizontal_accuracy_radius_meters))
                throw new InvalidOperationException("A semantic tower exclusion falls outside its authority radius.");
        }
    }
}
