#!/usr/bin/env python3
"""Build a deterministic Quest-safe runtime HLOD plan from 250 m logical cells."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any


CELL_ID = re.compile(
    r"^buildings_(?P<zone>0-2km|2-4km|4-6km)_cell_250m_"
    r"x(?P<xsign>[np])(?P<x>\d+)_z(?P<zsign>[np])(?P<z>\d+)$"
)
VEGETATION_CELL_ID = re.compile(
    r"^vegetation_v2_cell_250m_x(?P<xsign>[np])(?P<x>\d+)_z(?P<zsign>[np])(?P<z>\d+)$"
)


def _load(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _signed(sign: str, magnitude: str) -> int:
    value = int(magnitude)
    return value if sign == "p" else -value


def _axis(value: int) -> str:
    return ("p" if value >= 0 else "n") + f"{abs(value):03d}"


def _decode_points(feature: dict[str, Any], quantization: float) -> list[tuple[float, float]]:
    values = feature.get("points_q") or []
    points = [(values[index] * quantization, values[index + 1] * quantization) for index in range(0, len(values) - 1, 2)]
    if len(points) > 1 and points[0] == points[-1]:
        points.pop()
    return points


def _cluster_count(
    records: list[dict[str, Any]],
    features: list[dict[str, Any]],
    quantization: float,
    cluster_meters: float,
) -> int:
    cells: set[tuple[int, int]] = set()
    for record in records:
        points = _decode_points(features[record["feature_index"]], quantization)
        if len(points) < 3:
            continue
        centroid_x = sum(point[0] for point in points) / len(points)
        centroid_z = sum(point[1] for point in points) / len(points)
        cells.add((math.floor(centroid_x / cluster_meters), math.floor(centroid_z / cluster_meters)))
    return len(cells)


def _estimate_buildings(
    records: list[dict[str, Any]],
    features: list[dict[str, Any]],
    quantization: float,
    algorithm: str,
) -> tuple[int, int]:
    point_counts = [
        len(_decode_points(features[record["feature_index"]], quantization))
        for record in records
    ]
    point_counts = [count for count in point_counts if count >= 3]
    if algorithm == "detailed_footprints":
        return sum(3 * count - 2 for count in point_counts), sum(5 * count for count in point_counts)
    if algorithm == "building_boxes":
        return len(point_counts) * 10, len(point_counts) * 20
    if algorithm.startswith("cluster_boxes_") and algorithm.endswith("m"):
        cluster_size = float(algorithm[len("cluster_boxes_") : -1])
        count = _cluster_count(records, features, quantization, cluster_size)
        return count * 10, count * 20
    raise ValueError(f"Unsupported building HLOD algorithm: {algorithm}")


def _vegetation_estimate(batch: dict[str, Any], level: str) -> tuple[int, int]:
    placements = batch.get("placements", [])
    if level == "LOD2":
        return len(placements) * 8, len(placements) * 6
    triangles = 0
    vertices = 0
    for index, placement in enumerate(placements):
        near = placement.get("zone") == "near"
        if level == "LOD0":
            triangles += (92 if near else 44) + (8 if index % 12 == 0 else 0)
            vertices += (70 if near else 46) + (6 if index % 12 == 0 else 0)
        else:
            triangles += 46
            vertices += 47
    return triangles, vertices


def build_runtime_recipe(
    logical: dict[str, Any],
    context: dict[str, Any],
    config: dict[str, Any],
    logical_sha256: str,
    context_sha256: str,
) -> dict[str, Any]:
    features = context["openstreetmap"]["features"]
    quantization = float(context["coordinate_quantization_meters"])
    zones = config["runtime_hlod"]["zones"]
    grouped: dict[tuple[str, int, int], list[dict[str, Any]]] = defaultdict(list)
    logical_by_id: dict[str, dict[str, Any]] = {}

    for batch in logical["building_batches"]:
        match = CELL_ID.match(batch["id"])
        if not match:
            raise ValueError(f"Building logical-cell id does not match the 250 m contract: {batch['id']}")
        if batch["id"] in logical_by_id:
            raise ValueError(f"Duplicate logical building batch id: {batch['id']}")
        logical_by_id[batch["id"]] = batch
        zone = match["zone"]
        factor = int(zones[zone]["aggregation_factor"])
        x = _signed(match["xsign"], match["x"])
        z = _signed(match["zsign"], match["z"])
        grouped[(zone, x // factor, z // factor)].append(batch)

    runtime_batches: list[dict[str, Any]] = []
    for (zone, group_x, group_z), logical_batches in sorted(grouped.items()):
        zone_config = zones[zone]
        logical_batches.sort(key=lambda item: item["id"])
        records = [record for batch in logical_batches for record in batch["buildings"]]
        size = int(zone_config["aggregation_factor"]) * int(config["runtime_hlod"]["logical_cell_size_meters"])
        runtime_id = f"buildings_{zone}_hlod_{size}m_x{_axis(group_x)}_z{_axis(group_z)}"
        level_rows: list[dict[str, Any]] = []
        for level in ("LOD0", "LOD1", "LOD2"):
            algorithm = zone_config["algorithms"][level]
            triangles, vertices = _estimate_buildings(records, features, quantization, algorithm)
            level_rows.append(
                {
                    "algorithm": algorithm,
                    "estimated_triangles": triangles,
                    "estimated_vertices": vertices,
                    "level": level,
                    "screen_relative_height": zone_config["screen_relative_heights"][("LOD0", "LOD1", "LOD2").index(level)],
                }
            )
        runtime_batches.append(
            {
                "aggregation_factor": int(zone_config["aggregation_factor"]),
                "building_count": len(records),
                "id": runtime_id,
                "lod_policy": level_rows,
                "logical_batch_ids": [batch["id"] for batch in logical_batches],
                "runtime_cell_size_meters": float(size),
                "zone": zone,
            }
        )

    covered = [cell_id for batch in runtime_batches for cell_id in batch["logical_batch_ids"]]
    expected = sorted(logical_by_id)
    if sorted(covered) != expected or len(covered) != len(set(covered)):
        raise ValueError("Runtime HLOD grouping does not preserve every logical building cell exactly once")

    vegetation_source = sorted(logical.get("vegetation_batches", []), key=lambda item: item["id"])
    vegetation_factor = int(config["runtime_hlod"]["vegetation_aggregation_factor"])
    vegetation_groups: dict[tuple[int, int], list[dict[str, Any]]] = defaultdict(list)
    for batch in vegetation_source:
        match = VEGETATION_CELL_ID.match(batch["id"])
        if not match:
            raise ValueError(f"Vegetation logical-cell id does not match the 250 m contract: {batch['id']}")
        x = _signed(match["xsign"], match["x"])
        z = _signed(match["zsign"], match["z"])
        vegetation_groups[(x // vegetation_factor, z // vegetation_factor)].append(batch)

    vegetation_rows = []
    covered_vegetation_ids: list[str] = []
    all_stable_ids: list[str] = []
    all_species: set[str] = set()
    for (group_x, group_z), logical_batches in sorted(vegetation_groups.items()):
        logical_batches.sort(key=lambda item: item["id"])
        placements = [placement for batch in logical_batches for placement in batch.get("placements", [])]
        stable_ids = [str(placement.get("stable_id", "")) for placement in placements]
        if any(not value for value in stable_ids):
            raise ValueError("Vegetation placement is missing a stable_id")
        species_counts: dict[str, int] = defaultdict(int)
        for placement in placements:
            species = str(placement.get("species_id", ""))
            if not species:
                raise ValueError("Vegetation placement is missing a species_id")
            species_counts[species] += 1
            all_species.add(species)
        all_stable_ids.extend(stable_ids)
        covered_vegetation_ids.extend(batch["id"] for batch in logical_batches)
        size = vegetation_factor * int(config["runtime_hlod"]["logical_cell_size_meters"])
        runtime_id = f"vegetation_v2_hlod_{size}m_x{_axis(group_x)}_z{_axis(group_z)}"
        levels = []
        for level, height in zip(("LOD0", "LOD1", "LOD2"), config["runtime_hlod"]["vegetation_screen_relative_heights"]):
            triangles, vertices = _vegetation_estimate({"placements": placements}, level)
            levels.append({"algorithm": "authored_crowns" if level != "LOD2" else "octahedral_crowns", "estimated_triangles": triangles, "estimated_vertices": vertices, "level": level, "screen_relative_height": height})
        vegetation_rows.append({
            "id": runtime_id,
            "logical_batch_ids": [batch["id"] for batch in logical_batches],
            "lod_policy": levels,
            "placement_count": len(placements),
            "species_counts": dict(sorted(species_counts.items())),
        })

    expected_vegetation_ids = sorted(batch["id"] for batch in vegetation_source)
    if sorted(covered_vegetation_ids) != expected_vegetation_ids or len(covered_vegetation_ids) != len(set(covered_vegetation_ids)):
        raise ValueError("Runtime HLOD grouping does not preserve every logical vegetation cell exactly once")
    if len(all_stable_ids) != len(set(all_stable_ids)):
        raise ValueError("Runtime HLOD grouping contains duplicate vegetation placements")
    required_species = set(config["runtime_hlod"]["vegetation_required_species"])
    if all_species != required_species:
        raise ValueError(f"Vegetation species coverage mismatch: actual={sorted(all_species)} required={sorted(required_species)}")

    aircraft_rows = []
    aircraft_estimates = config["runtime_hlod"]["parked_aircraft_estimates"]
    for batch in sorted(logical.get("parked_aircraft_batches", []), key=lambda item: item["id"]):
        levels = []
        for level, height in zip(("LOD0", "LOD1", "LOD2"), config["runtime_hlod"]["parked_aircraft_screen_relative_heights"]):
            estimate = aircraft_estimates[level]
            levels.append({"algorithm": "source_mesh" if level != "LOD2" else "bounds_boxes", "estimated_triangles": int(estimate["triangles"]), "estimated_vertices": int(estimate["vertices"]), "level": level, "screen_relative_height": height})
        aircraft_rows.append({"id": batch["id"], "logical_batch_ids": [batch["id"]], "lod_policy": levels})

    all_batches = runtime_batches + vegetation_rows + aircraft_rows
    context_worst_triangles = sum(max(level["estimated_triangles"] for level in batch["lod_policy"]) for batch in all_batches)
    context_worst_vertices = sum(max(level["estimated_vertices"] for level in batch["lod_policy"]) for batch in all_batches)
    max_mesh_vertices = max(level["estimated_vertices"] for batch in all_batches for level in batch["lod_policy"])
    baseline = config["runtime_hlod"]["non_context_static_baseline"]
    reserve = config["runtime_hlod"]["planned_product_reserve"]
    limits = config["limits"]
    estimates = {
        "context_aggregate_serialized_renderers": len(all_batches) * 3,
        "context_aggregate_serialized_triangles": sum(
            level["estimated_triangles"] for batch in all_batches for level in batch["lod_policy"]
        ),
        "context_batch_count": len(all_batches),
        "context_material_count": 3,
        "context_transparent_material_slots": 0,
        "context_worst_case_triangles": context_worst_triangles,
        "context_worst_case_vertices": context_worst_vertices,
        "maximum_single_mesh_vertices": max_mesh_vertices,
        "integrated_texture_memory_bytes": int(baseline["texture_memory_bytes"]) + int(config["runtime_hlod"]["context_texture_memory_bytes"]),
        "integrated_transparent_material_slots": int(baseline["transparent_material_slots"]),
        "integrated_visible_draw_slots": int(baseline["visible_draw_slots"]) + len(all_batches) + int(reserve["visible_draw_slots"]),
        "integrated_visible_materials": int(baseline["visible_materials"]) + 3,
        "integrated_visible_triangles": int(baseline["visible_triangles"]) + context_worst_triangles + int(reserve["visible_triangles"]),
    }
    serialized_baseline = config["runtime_hlod"]["non_context_serialized_baseline"]
    estimates["integrated_aggregate_serialized_renderers"] = (
        int(serialized_baseline["aggregate_renderers"]) + estimates["context_aggregate_serialized_renderers"] + int(reserve["aggregate_serialized_renderers"])
    )
    estimates["integrated_aggregate_serialized_triangles"] = (
        int(serialized_baseline["aggregate_triangles"]) + estimates["context_aggregate_serialized_triangles"] + int(reserve["aggregate_serialized_triangles"])
    )
    comparison_keys = {
        "visible_draw_slots": "integrated_visible_draw_slots",
        "visible_triangles": "integrated_visible_triangles",
        "visible_materials": "integrated_visible_materials",
        "texture_memory_bytes": "integrated_texture_memory_bytes",
        "transparent_material_slots": "integrated_transparent_material_slots",
        "aggregate_serialized_renderers": "integrated_aggregate_serialized_renderers",
        "aggregate_serialized_triangles": "integrated_aggregate_serialized_triangles",
    }
    over = [name for name, estimate_name in comparison_keys.items() if estimates[estimate_name] > limits[name]]
    if max_mesh_vertices > int(config["runtime_hlod"]["maximum_single_mesh_vertices"]):
        over.append("maximum_single_mesh_vertices")
    if over:
        raise ValueError("Runtime HLOD plan exceeds static limits: " + ", ".join(over))

    return {
        "schema_version": 1,
        "label": "KBDU Reconstruction V2 Quest runtime HLOD recipe",
        "evidence_kind": "STATIC_OFFLINE_PLANNING_ONLY",
        "hardware_performance_established": False,
        "source_logical_recipe_path": config["runtime_hlod"]["logical_recipe_path"],
        "source_logical_recipe_sha256": logical_sha256,
        "source_context_path": config["runtime_hlod"]["context_path"],
        "source_context_sha256": context_sha256,
        "logical_cell_size_meters": float(config["runtime_hlod"]["logical_cell_size_meters"]),
        "logical_building_batch_count": len(expected),
        "logical_building_count": sum(len(batch["buildings"]) for batch in logical["building_batches"]),
        "logical_vegetation_batch_count": len(vegetation_source),
        "vegetation_placement_count": len(all_stable_ids),
        "vegetation_species_ids": sorted(all_species),
        "building_runtime_batches": runtime_batches,
        "vegetation_runtime_batches": vegetation_rows,
        "parked_aircraft_runtime_batches": aircraft_rows,
        "static_budget": {
            "limits": limits,
            "non_context_baseline": baseline,
            "non_context_serialized_baseline": serialized_baseline,
            "planned_product_reserve": reserve,
            "estimates": estimates,
            "status": "PASS",
        },
        "runtime_rules": {
            "crossfade": False,
            "material_instances_per_object": False,
            "runtime_mesh_combining": False,
            "runtime_per_instance_scripts": False,
            "shared_materials": True,
        },
        "limitations": [
            "Static mesh and renderer-slot estimates do not establish measured draw calls or Quest performance.",
            "The non-context baseline is inherited static evidence and must be refreshed after unrelated product changes.",
            "Physical Quest timing, thermals, stereo stability, and comfort remain separate acceptance gates.",
        ],
    }


def render_summary(recipe: dict[str, Any]) -> str:
    estimates = recipe["static_budget"]["estimates"]
    zones: dict[str, int] = defaultdict(int)
    for batch in recipe["building_runtime_batches"]:
        zones[batch["zone"]] += 1
    lines = [
        "# KBDU Reconstruction V2 runtime HLOD plan",
        "",
        "**Static planning result:** `PASS`",
        "",
        f"- Logical building coverage: {recipe['logical_building_batch_count']:,} cells / {recipe['logical_building_count']:,} buildings",
        f"- Runtime building groups: {len(recipe['building_runtime_batches'])} ({', '.join(f'{zone}: {count}' for zone, count in sorted(zones.items()))})",
        f"- Total context renderer slots: {estimates['context_batch_count']}",
        f"- Conservative integrated renderer slots: {estimates['integrated_visible_draw_slots']} / {recipe['static_budget']['limits']['visible_draw_slots']}",
        f"- Conservative integrated triangles: {estimates['integrated_visible_triangles']:,} / {recipe['static_budget']['limits']['visible_triangles']:,}",
        f"- Aggregate serialized renderers: {estimates['integrated_aggregate_serialized_renderers']} / {recipe['static_budget']['limits']['aggregate_serialized_renderers']}",
        f"- Aggregate serialized triangles: {estimates['integrated_aggregate_serialized_triangles']:,} / {recipe['static_budget']['limits']['aggregate_serialized_triangles']:,}",
        f"- Integrated visible materials: {estimates['integrated_visible_materials']} / {recipe['static_budget']['limits']['visible_materials']}",
        f"- Estimated texture memory: {estimates['integrated_texture_memory_bytes']:,} / {recipe['static_budget']['limits']['texture_memory_bytes']:,} bytes",
        f"- Transparent material slots: {estimates['integrated_transparent_material_slots']} / {recipe['static_budget']['limits']['transparent_material_slots']}",
        f"- Maximum single mesh vertices: {estimates['maximum_single_mesh_vertices']:,}",
        "",
        "These are deterministic offline estimates, not hardware-performance evidence.",
    ]
    return "\n".join(lines) + "\n"


def main(argv: list[str] | None = None) -> int:
    root_default = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=root_default)
    parser.add_argument("--config", type=Path, default=Path(__file__).with_name("config.json"))
    parser.add_argument("--output", type=Path)
    parser.add_argument("--summary-output", type=Path)
    args = parser.parse_args(argv)
    root = args.repo_root.resolve()
    config = _load(args.config)
    logical_path = root / config["runtime_hlod"]["logical_recipe_path"]
    context_path = root / config["runtime_hlod"]["context_path"]
    output = args.output or (root / config["runtime_hlod"]["runtime_recipe_path"])
    recipe = build_runtime_recipe(_load(logical_path), _load(context_path), config, _sha256(logical_path), _sha256(context_path))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(recipe, separators=(",", ":"), sort_keys=True) + "\n", encoding="utf-8")
    if args.summary_output:
        args.summary_output.parent.mkdir(parents=True, exist_ok=True)
        args.summary_output.write_text(render_summary(recipe), encoding="utf-8")
    print(output)
    print(render_summary(recipe), end="")
    return 0


if __name__ == "__main__":
    sys.exit(main())
