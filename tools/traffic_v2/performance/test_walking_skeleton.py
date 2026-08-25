from __future__ import annotations

import importlib.util
import json
import math
import os
from pathlib import Path

import pytest


REPO_ROOT = Path(__file__).resolve().parents[3]


def load_module(name: str, relative: str):
    path = REPO_ROOT / relative
    specification = importlib.util.spec_from_file_location(name, path)
    assert specification is not None and specification.loader is not None
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


lanes = load_module(
    "traffic_v2_gate1_lanes",
    "tools/traffic_v2/geospatial/compile_walking_skeleton_lanes.py",
)
performance = load_module(
    "traffic_v2_gate1_performance",
    "tools/traffic_v2/performance/compile_walking_skeleton.py",
)


def test_osm_direction_access_and_lane_rules_fail_closed() -> None:
    assert lanes.driving_direction({"oneway": "yes"})[0] == "forward"
    assert lanes.driving_direction({"oneway": "-1"})[0] == "reverse"
    assert lanes.driving_direction({"highway": "motorway"})[0] == "forward"
    assert lanes.driving_direction({"oneway:conditional": "yes @ (snow)"})[0] is None
    assert lanes.effective_access({"motor_vehicle": "no"})[0] is False
    assert lanes.effective_access({"access": "no", "motorcar": "yes"})[0] is True
    assert lanes.effective_access({"motor_vehicle:lanes": "yes|no"})[0] is False
    assert lanes.lane_counts({"lanes": "4"}, "both") == (2, 2)
    assert lanes.lane_counts({"lanes": "3"}, "both") is None
    assert lanes.lane_counts(
        {"lanes": "4", "lanes:forward": "5", "lanes:backward": "1"}, "both"
    ) is None


def test_offset_lanes_and_motion_profile_are_well_formed() -> None:
    centerline = [(0.0, 0.0), (0.0, 100.0)]
    right_lane = lanes.offset_polyline(centerline, -1.75)
    left_lane = lanes.offset_polyline(list(reversed(centerline)), -1.75)
    assert right_lane == [[1.75, 0.0], [1.75, 100.0]]
    assert left_lane == [[-1.75, 100.0], [-1.75, 0.0]]
    parameters = performance.motion_parameters(500.0, 15.0, 1.8, 2.1)
    distances = []
    for sample in range(math.ceil(parameters["total_time"]) + 1):
        distance, speed = performance.motion_at(
            min(float(sample), parameters["total_time"]), parameters, 1.8
        )
        distances.append(distance)
        assert speed >= 0.0
    final_distance, final_speed = performance.motion_at(
        parameters["total_time"], parameters, 1.8
    )
    assert distances == sorted(distances)
    assert final_distance == pytest.approx(500.0)
    assert final_speed == pytest.approx(0.0)


def test_real_frozen_gate1_rebuild_is_deterministic(tmp_path: Path) -> None:
    external = os.environ.get("TRAFFIC_V2_GATE1_EXTERNAL_RUN")
    freeze_sha = os.environ.get("TRAFFIC_V2_GATE1_FREEZE_SHA256")
    if not external or not freeze_sha:
        pytest.skip("Real frozen Gate-1 integration input is not configured")
    first = tmp_path / "first"
    second = tmp_path / "second"
    first_result = performance.compile_gate1(
        REPO_ROOT, Path(external), first, freeze_sha
    )
    second_result = performance.compile_gate1(
        REPO_ROOT, Path(external), second, freeze_sha
    )
    assert first_result["status"] == "PASS_GATE1_OFFLINE_ARTIFACT"
    assert second_result["status"] == "PASS_GATE1_OFFLINE_ARTIFACT"
    expected = {
        "traffic_v2_gate1_hashes.json",
        "traffic_v2_gate1_lane_graph.json",
        "traffic_v2_gate1_overlay.png",
        "traffic_v2_gate1_report.json",
        "traffic_v2_gate1_trajectory_manifest.json",
    }
    assert {path.name for path in first.iterdir()} == expected
    assert {path.name for path in second.iterdir()} == expected
    for name in expected:
        assert (first / name).read_bytes() == (second / name).read_bytes()
    manifest = json.loads((first / "traffic_v2_gate1_trajectory_manifest.json").read_text())
    report = json.loads((first / "traffic_v2_gate1_report.json").read_text())
    assert manifest["schedule_looping"] is False
    assert manifest["schedule_duration_seconds"] == 900.0
    assert len(manifest["trajectories"]) == 12
    assert report["checks"]["opposing_highway_heading_difference_degrees"] >= 120.0
    assert report["checks"]["sampled_vehicle_separation"]["status"] == (
        "PASS_WALKING_SKELETON_SAMPLE"
    )
