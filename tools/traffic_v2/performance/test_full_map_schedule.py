from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path

import pytest


REPO_ROOT = Path(__file__).resolve().parents[3]
MODULE_PATH = REPO_ROOT / "tools/traffic_v2/performance/compile_full_map_schedule.py"
SPEC = importlib.util.spec_from_file_location("traffic_v2_full_map", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
full_map = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(full_map)


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, sort_keys=True), encoding="utf-8")


def test_build_corridors_is_full_universe_deterministic_and_demand_derived() -> None:
    records = [
        {
            "canonical_segment_id": "city:1:0",
            "correspondence_semantics": {
                "carriageway": {"value": "SINGLE_CENTERLINE"},
                "direction": {"value": "BIDIRECTIONAL"},
            },
            "eligibility_status_or_exclusion_reason": "ELIGIBLE",
            "geometry": {
                "coordinates": [[0.0, 0.0], [0.0, 500.0]],
                "type": "LineString",
            },
            "lane_count": 2,
            "road_class": "primary",
            "source_id": "city_street_centerlines",
            "source_record_id": "1",
            "source_record_part": 0,
        },
        {
            "canonical_segment_id": "osm:2:0",
            "correspondence_semantics": {
                "carriageway": {"value": "DIRECTIONAL_CARRIAGEWAY"},
                "direction": {"value": "FORWARD"},
            },
            "eligibility_status_or_exclusion_reason": "ELIGIBLE",
            "geometry": {
                "coordinates": [[100.0, 0.0], [100.0, 1000.0]],
                "type": "LineString",
            },
            "lane_count": 2,
            "road_class": "motorway",
            "source_id": "osm_bounded_roads",
            "source_record_id": "2",
            "source_record_part": 0,
        },
        {
            "canonical_segment_id": "private:3:0",
            "eligibility_status_or_exclusion_reason": "EXCLUDED_PRIVATE_ACCESS",
            "geometry": {
                "coordinates": [[200.0, 0.0], [200.0, 100.0]],
                "type": "LineString",
            },
            "lane_count": None,
            "road_class": None,
        },
    ]
    observations = [
        {
            "aadt": 18000.0,
            "geometry": full_map.LineString([[-1.0, 0.0], [-1.0, 500.0]]),
            "id": "count:1",
            "identity_tokens": {"route1"},
            "source": "cdot_traffic_counts",
            "year": "2024",
        }
    ]
    tree = full_map.STRtree([value["geometry"] for value in observations])
    import tempfile

    with tempfile.TemporaryDirectory() as directory:
        canonical = Path(directory) / "canonical.jsonl"
        canonical.write_text(
            "".join(json.dumps(record) + "\n" for record in records),
            encoding="utf-8",
        )
        first = full_map.build_corridors(canonical, observations, tree)
        second = full_map.build_corridors(canonical, observations, tree)
    corridors, tiles, metrics = first
    assert first == second
    assert len(corridors) == 2
    assert metrics["canonical_input_record_count"] == 3
    assert metrics["explicit_or_unresolved_record_count"] == 1
    assert metrics["direct_count_corridor_count"] == 1
    assert corridors[0]["aadt"] == 18000.0
    assert corridors[0]["aadt_authority"]["observation_id"] == "count:1"
    assert corridors[0]["direction_mode"] == "BIDIRECTIONAL"
    assert corridors[1]["direction_mode"] == "FORWARD"
    assert tiles
    assert {index for tile in tiles for index in tile["corridor_indices"]} == {0, 1}


def test_compile_rejects_unbound_canonical_universe(tmp_path: Path) -> None:
    external = tmp_path / "external"
    output = tmp_path / "output"
    freeze = external / "freeze_manifest.json"
    write_json(freeze, {})
    freeze_sha = hashlib.sha256(freeze.read_bytes()).hexdigest()
    canonical = external / "intermediates/canonical_road_universe.geojsonl"
    canonical.parent.mkdir(parents=True, exist_ok=True)
    canonical.write_text("{}\n", encoding="utf-8")
    coverage = REPO_ROOT / "docs/data/traffic_v2/traffic_source_coverage.json"
    if coverage.exists():
        pytest.skip("Test fixture must not replace live normative coverage")
    with pytest.raises(full_map.FullMapCompileError, match="Missing full-map prerequisite"):
        full_map.compile_full_map(REPO_ROOT, external, output, freeze_sha)


def test_directional_carriageway_without_direction_fails_closed(tmp_path: Path) -> None:
    canonical = tmp_path / "canonical.jsonl"
    canonical.write_text(
        json.dumps(
            {
                "canonical_segment_id": "bad:1",
                "correspondence_semantics": {
                    "carriageway": {"value": "DIRECTIONAL_CARRIAGEWAY"},
                    "direction": {"value": "UNKNOWN"},
                },
                "eligibility_status_or_exclusion_reason": "ELIGIBLE",
                "geometry": {
                    "coordinates": [[0.0, 0.0], [0.0, 100.0]],
                    "type": "LineString",
                },
                "lane_count": 1,
                "road_class": "primary",
                "source_id": "fixture",
                "source_record_id": "1",
                "source_record_part": 0,
            }
        )
        + "\n",
        encoding="utf-8",
    )
    observations = [
        {
            "aadt": 10000.0,
            "geometry": full_map.LineString([[1.0, 0.0], [1.0, 100.0]]),
            "id": "count:1",
            "identity_tokens": {"route1"},
            "source": "cdot_traffic_counts",
            "year": "2024",
        }
    ]
    tree = full_map.STRtree([value["geometry"] for value in observations])
    with pytest.raises(full_map.FullMapCompileError, match="lacks direction"):
        full_map.build_corridors(canonical, observations, tree)


def test_canonical_checkpoint_adapter_defaults_only_unresolved_lane_counts(
    tmp_path: Path,
) -> None:
    canonical = tmp_path / "canonical.jsonl"
    records = [
        {
            "canonical_segment_id": "official:known",
            "correspondence_semantics": {"direction": {"value": "FORWARD"}},
            "eligibility_status_or_exclusion_reason": "ELIGIBLE",
            "geometry": {"coordinates": [[0.0, 0.0], [0.0, 100.0]], "type": "LineString"},
            "lane_count": 3,
            "road_class": "primary",
        },
        {
            "canonical_segment_id": "official:directional-default",
            "correspondence_semantics": {"direction": {"value": "REVERSE"}},
            "eligibility_status_or_exclusion_reason": "ELIGIBLE",
            "geometry": {"coordinates": [[1.0, 0.0], [1.0, 100.0]], "type": "LineString"},
            "lane_count": None,
            "road_class": "primary",
        },
        {
            "canonical_segment_id": "official:centerline-default",
            "correspondence_semantics": {
                "carriageway": {"value": "SINGLE_CENTERLINE"},
                "direction": {"value": "UNKNOWN"},
            },
            "eligibility_status_or_exclusion_reason": "ELIGIBLE",
            "geometry": {"coordinates": [[2.0, 0.0], [2.0, 100.0]], "type": "LineString"},
            "lane_count": None,
            "road_class": "secondary",
        },
        {
            "canonical_segment_id": "private:excluded",
            "eligibility_status_or_exclusion_reason": "EXCLUDED_PRIVATE_ACCESS",
            "geometry": {"coordinates": [[3.0, 0.0], [3.0, 100.0]], "type": "LineString"},
            "lane_count": None,
            "road_class": None,
        },
    ]
    canonical.write_text(
        "".join(json.dumps(record) + "\n" for record in records),
        encoding="utf-8",
    )

    adapted = list(full_map.canonical_checkpoint_candidate_records(canonical))
    assert [value["lane_count"] for value in adapted[:3]] == [3, 1, 2]
    assert [
        value["headset_development_lane_count_basis"] for value in adapted[:3]
    ] == [
        "CANONICAL_EVIDENCE",
        "DISCLOSED_HEADSET_DEVELOPMENT_DEFAULT",
        "DISCLOSED_HEADSET_DEVELOPMENT_DEFAULT",
    ]
    assert adapted[3] == records[3]


def test_compile_canonical_checkpoint_candidate_is_hash_bound_and_disclosed(
    tmp_path: Path,
) -> None:
    external = tmp_path / "external"
    output = tmp_path / "output"
    freeze = external / "freeze_manifest.json"
    write_json(
        freeze,
        {
            "sources": [
                {
                    "id": "city_street_centerlines",
                    "license_partition": "US-LOCAL-PUBLIC-DATA",
                }
            ]
        },
    )
    freeze_sha = hashlib.sha256(freeze.read_bytes()).hexdigest()
    write_json(
        external / "raw/county_traffic_stations/oid_1.json",
        {
            "features": [
                {
                    "attributes": {
                        "CR": "1",
                        "OBJECTID": 1,
                        "STREET_NAME": "Fixture Road",
                        "TRAFFIC_COUNT": 1000,
                        "TRAFFIC_YEAR_COUNTED": 2024,
                    },
                    "geometry": {"x": -105.22608958, "y": 40.03936527},
                }
            ]
        },
    )
    canonical = tmp_path / "checkpoint.jsonl"
    canonical.write_text(
        json.dumps(
            {
                "canonical_segment_id": "city:1:0",
                "correspondence_semantics": {
                    "carriageway": {"value": "SINGLE_CENTERLINE"},
                    "direction": {"value": "BIDIRECTIONAL"},
                },
                "eligibility_status_or_exclusion_reason": "ELIGIBLE",
                "geometry": {
                    "coordinates": [[0.0, 0.0], [0.0, 100.0]],
                    "type": "LineString",
                },
                "lane_count": None,
                "license_partition": "US-LOCAL-PUBLIC-DATA",
                "road_class": "secondary",
                "source_id": "city_street_centerlines",
                "source_record_id": "1",
                "source_record_part": 0,
            }
        )
        + "\n",
        encoding="utf-8",
    )
    canonical_sha = hashlib.sha256(canonical.read_bytes()).hexdigest()

    registry_path = REPO_ROOT / "tools/traffic_v2/geospatial/source_registry.json"
    registry_value = json.loads(registry_path.read_text(encoding="utf-8"))
    freeze_value = json.loads(freeze.read_text(encoding="utf-8"))
    count_path = external / "raw/county_traffic_stations/oid_1.json"
    freeze_value.update({
        "registry": {"path": "tools/traffic_v2/geospatial/source_registry.json", "bytes": registry_path.stat().st_size, "sha256": full_map.sha256_file(registry_path)},
        "coordinate_contract": registry_value["coordinate_contract"],
    })
    freeze_value["sources"].extend([
        {"id": "cdot_traffic_counts", "license_partition": "CC-BY-SA-4.0", "pages": []},
        {"id": "county_traffic_stations", "license_partition": "CC-BY-4.0", "pages": [{"receipt": {"path": count_path.relative_to(external).as_posix(), "bytes": count_path.stat().st_size, "sha256": full_map.sha256_file(count_path)}}]},
    ])
    write_json(freeze, freeze_value)
    freeze_sha = full_map.sha256_file(freeze)

    report = full_map.compile_full_map(
        REPO_ROOT,
        external,
        output,
        freeze_sha,
        canonical_checkpoint_candidate=canonical,
        expected_canonical_checkpoint_sha256=canonical_sha,
    )

    manifest = json.loads((output / "traffic_v2_full_map_manifest.json").read_text())
    provenance = json.loads(
        (output / "traffic_v2_full_map_source_and_license.json").read_text()
    )
    assert report["candidate_source_mode"] == (
        "CANONICAL_SELECTION_CHECKPOINT_WITH_DISCLOSED_LANE_DEFAULTS"
    )
    assert manifest["canonical_universe"]["sha256"] == canonical_sha
    assert manifest["source_gate_status"] == "PENDING_PHASE3_SOURCE_GATE"
    assert manifest["metrics"]["corridor_count"] == 1
    assert (
        manifest["metrics"]["headset_development_lane_default_corridor_count"]
        == 1
    )
    assert provenance["source_ids"] == ["cdot_traffic_counts", "city_street_centerlines", "county_traffic_stations"]
    assert report["checks"]["all_576_source_cells_evaluated"] is False
    assert manifest["frozen_count_inputs"]["count_pages"][0]["sha256"] == full_map.sha256_file(count_path)
    assert "did not complete" in manifest["limitations"][0]

    with pytest.raises(full_map.FullMapCompileError, match="SHA-256 mismatch"):
        full_map.compile_full_map(
            REPO_ROOT,
            external,
            tmp_path / "bad-output",
            freeze_sha,
            canonical_checkpoint_candidate=canonical,
            expected_canonical_checkpoint_sha256="0" * 64,
        )


def test_candidate_input_modes_are_mutually_exclusive(tmp_path: Path) -> None:
    candidate = tmp_path / "candidate.jsonl"
    candidate.write_text("{}\n", encoding="utf-8")
    with pytest.raises(full_map.FullMapCompileError, match="mutually exclusive"):
        full_map.compile_full_map(
            REPO_ROOT,
            tmp_path,
            tmp_path / "output",
            "0" * 64,
            normalized_osm_candidate=candidate,
            expected_normalized_osm_sha256=hashlib.sha256(candidate.read_bytes()).hexdigest(),
            canonical_checkpoint_candidate=candidate,
            expected_canonical_checkpoint_sha256=hashlib.sha256(
                candidate.read_bytes()
            ).hexdigest(),
        )
