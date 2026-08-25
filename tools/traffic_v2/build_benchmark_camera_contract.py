#!/usr/bin/env python3
"""Freeze the accepted Traffic V2 benchmark-camera source contract.

This lead-owned tool binds the exact Phase-2 baseline camera records without
claiming that a static capture is a flight-path or visibility test.  Phase 3
uses the complete, classified map universe as a conservative superset: any
in-bounds road visible from a frozen view must be a member of that universe.
Later visibility, density, stereo, LOD, and headset gates still require their
own projected and time-domain evidence.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
SPEC_PATH = REPO_ROOT / "tools/traffic_v2/schemas/traffic_system_v2_spec.json"
OUTPUT_PATH = REPO_ROOT / "tools/traffic_v2/schemas/traffic_v2_benchmark_camera_contract_v1.json"
AIRCRAFT_PREFAB_PATH = (
    REPO_ROOT / "QuestFlight/Assets/Production/Prefabs/ProductionC172AircraftRig.prefab"
)
EXPECTED_BASELINE_SHA256 = "185908c0becf5491fa2dedefdc26ed799068c57faacd7882a1cd46024a5b1016"
EXPECTED_SOURCE_SHA = "fda11a25eb526824d82a47330f2be80b1410bb64"
EXPECTED_VIEW_COUNT = 15
BOUNDS_XZ_METERS = [-12000.0, -12000.0, 12000.0, 12000.0]


def sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def git_blob_sha1(payload: bytes) -> str:
    header = f"blob {len(payload)}\0".encode("ascii")
    return hashlib.sha1(header + payload).hexdigest()


def canonical_json(value: Any) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n").encode("utf-8")


def require_external(path: Path) -> Path:
    resolved = path.resolve()
    try:
        resolved.relative_to(REPO_ROOT.resolve())
    except ValueError:
        return resolved
    raise ValueError(f"Baseline evidence must be external to the repository: {resolved}")


def vector(value: dict[str, Any]) -> dict[str, float]:
    return {axis: float(value[axis]) for axis in ("x", "y", "z")}


def camera_clip_contract(prefab_text: str) -> tuple[float, float]:
    marker = "  m_Name: Main Camera\n"
    if prefab_text.count(marker) != 1:
        raise ValueError("Expected exactly one serialized Main Camera in the production aircraft prefab")
    start = prefab_text.index(marker)
    camera_start = prefab_text.find("Camera:\n", start)
    if camera_start < 0:
        raise ValueError("Serialized Main Camera has no following Camera component")
    block_end = prefab_text.find("--- !u!", camera_start + len("Camera:\n"))
    block = prefab_text[camera_start : block_end if block_end >= 0 else None]
    near_match = re.search(r"^  near clip plane: ([0-9.]+)$", block, re.MULTILINE)
    far_match = re.search(r"^  far clip plane: ([0-9.]+)$", block, re.MULTILINE)
    if not near_match or not far_match:
        raise ValueError("Could not read serialized Main Camera clip planes")
    return float(near_match.group(1)), float(far_match.group(1))


def build_contract(baseline_root: Path) -> dict[str, Any]:
    baseline_root = require_external(baseline_root)
    baseline_path = baseline_root / "traffic_v2_baseline.json"
    baseline_bytes = baseline_path.read_bytes()
    baseline_sha = sha256_bytes(baseline_bytes)
    if baseline_sha != EXPECTED_BASELINE_SHA256:
        raise ValueError(f"Unexpected Phase-2 baseline SHA-256: {baseline_sha}")
    baseline = json.loads(baseline_bytes)
    if baseline.get("acceptedSourceSha") != EXPECTED_SOURCE_SHA:
        raise ValueError("Baseline accepted source does not match fda11a2")
    if baseline.get("status") != "CAPTURED_WITH_EXPLICIT_LIMITATIONS":
        raise ValueError("Baseline is not the accepted captured-with-limitations artifact")

    spec_bytes = SPEC_PATH.read_bytes()
    spec = json.loads(spec_bytes)
    expected_view_ids = list(spec["benchmark_views"])
    views = list(baseline.get("views") or [])
    if len(views) != EXPECTED_VIEW_COUNT:
        raise ValueError(f"Expected {EXPECTED_VIEW_COUNT} baseline views; found {len(views)}")
    if [view.get("id") for view in views] != expected_view_ids:
        raise ValueError("Baseline views do not match the frozen specification order")
    if baseline.get("frozenSpecSha256") != sha256_bytes(spec_bytes):
        raise ValueError("Baseline does not bind the current frozen specification bytes")

    prefab_bytes = AIRCRAFT_PREFAB_PATH.read_bytes()
    prefab_text = prefab_bytes.decode("utf-8")
    near_clip, far_clip = camera_clip_contract(prefab_text)
    scene_relative = "QuestFlight/" + str(baseline["scenePath"]).replace("\\", "/")
    scene_path = REPO_ROOT / scene_relative
    scene_bytes = scene_path.read_bytes()
    if sha256_bytes(scene_bytes) != baseline["sceneSha256Before"]:
        raise ValueError("Current accepted scene bytes do not match the captured baseline")

    frozen_views: list[dict[str, Any]] = []
    minimum_x, minimum_z, maximum_x, maximum_z = BOUNDS_XZ_METERS
    for view in views:
        resolved = vector(view["resolvedPosition"])
        inside = minimum_x <= resolved["x"] <= maximum_x and minimum_z <= resolved["z"] <= maximum_z
        if not inside:
            raise ValueError(f"Benchmark view origin is outside the frozen map bounds: {view['id']}")
        screenshot_path = baseline_root / str(view["pngPath"])
        screenshot_bytes = screenshot_path.read_bytes()
        screenshot_sha = sha256_bytes(screenshot_bytes)
        if screenshot_sha != view["pngSha256"] or len(screenshot_bytes) != int(view["pngBytes"]):
            raise ValueError(f"Screenshot integrity failure for {view['id']}")
        frozen_views.append(
            {
                "field_of_view_degrees": float(view["fieldOfView"]),
                "frozen_euler_degrees": vector(view["frozenEuler"]),
                "frozen_position_or_offset_meters": vector(view["frozenPositionOrOffset"]),
                "id": view["id"],
                "origin_inside_frozen_bounds": True,
                "pose_authority": view["poseAuthority"],
                "resolved_euler_degrees": vector(view["resolvedEuler"]),
                "resolved_position_meters": resolved,
                "road_candidate_set": "FULL_MAP_UNIVERSE_SUPERSET",
                "screenshot": {
                    "bytes": len(screenshot_bytes),
                    "logical_path": f"baseline/{view['pngPath']}",
                    "sha256": screenshot_sha,
                },
            }
        )

    return {
        "accepted_source_sha": EXPECTED_SOURCE_SHA,
        "baseline": {
            "bytes": len(baseline_bytes),
            "generated_utc": baseline["generatedUtc"],
            "logical_path": "baseline/traffic_v2_baseline.json",
            "schema": baseline["schema"],
            "sha256": baseline_sha,
            "status": baseline["status"],
        },
        "camera_serialization": {
            "aspect_ratio": float(baseline["renderWidth"]) / float(baseline["renderHeight"]),
            "far_clip_plane_meters": far_clip,
            "git_blob_sha1": git_blob_sha1(prefab_bytes),
            "near_clip_plane_meters": near_clip,
            "path": AIRCRAFT_PREFAB_PATH.relative_to(REPO_ROOT).as_posix(),
            "render_height_pixels": int(baseline["renderHeight"]),
            "render_width_pixels": int(baseline["renderWidth"]),
            "sha256": sha256_bytes(prefab_bytes),
        },
        "coverage_contract": {
            "bounds_xz_meters": BOUNDS_XZ_METERS,
            "candidate_mode": "FULL_MAP_UNIVERSE_SUPERSET",
            "claim_scope": "TV2-COV-001 benchmark-visible road classification only",
            "excluded_claims": [
                "per-frame projected or unoccluded visibility",
                "viewport density",
                "stereo visibility or per-eye agreement",
                "LOD stability",
                "dynamic path-sweep coverage",
                "Quest headset acceptance",
            ],
            "no_frustum_or_occlusion_exclusions": True,
            "proof": "For every frozen view, any visible road inside the product bounds is a member of the complete canonical full-map road universe; every view binds the same universe hash.",
            "required_universe_member_fields": [
                "canonical_segment_id",
                "road_class",
                "eligibility_status_or_exclusion_reason",
                "source_precedence_decision",
                "represented_source_disposition",
                "license_partition",
                "lane_count",
                "lane_count_authority",
                "confidence",
            ],
        },
        "frozen_specification": {
            "bytes": len(spec_bytes),
            "path": SPEC_PATH.relative_to(REPO_ROOT).as_posix(),
            "sha256": sha256_bytes(spec_bytes),
        },
        "schema": "questflight.traffic-v2-benchmark-camera-contract.v1",
        "scene": {
            "bytes": len(scene_bytes),
            "git_blob_sha1": git_blob_sha1(scene_bytes),
            "path": scene_relative,
            "sha256": sha256_bytes(scene_bytes),
        },
        "status": "FROZEN",
        "view_count": len(frozen_views),
        "views": frozen_views,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=OUTPUT_PATH)
    args = parser.parse_args()
    contract = build_contract(args.baseline_root)
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(canonical_json(contract))
    print(
        json.dumps(
            {
                "bytes": output.stat().st_size,
                "output": output.as_posix(),
                "sha256": sha256_bytes(output.read_bytes()),
                "views": contract["view_count"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
