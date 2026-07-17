#!/usr/bin/env python3
"""Generate deterministic, fail-closed static HLOD and render-budget evidence.

This tool reads serialized Unity assets and reconstruction manifests. It does not
launch Unity and its output is planning evidence, never Quest timing evidence.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import struct
import sys
from collections import Counter
from pathlib import Path
from typing import Any


REPORT_JSON = "hlod_render_budget_report.json"
REPORT_MARKDOWN = "hlod_render_budget_report.md"


def _load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _relative(path: Path, root: Path) -> str:
    try:
        return path.relative_to(root).as_posix()
    except ValueError:
        return str(path)


def _input(path: Path, root: Path) -> dict[str, Any]:
    return {
        "path": _relative(path, root),
        "exists": path.is_file(),
        "sha256": _sha256(path) if path.is_file() else None,
    }


def _recipe_batches(recipe: dict[str, Any]) -> list[tuple[str, str]]:
    result: list[tuple[str, str]] = []
    if "building_runtime_batches" in recipe:
        for key, category in (
            ("building_runtime_batches", "building"),
            ("vegetation_runtime_batches", "vegetation"),
            ("parked_aircraft_runtime_batches", "parked_aircraft"),
        ):
            for batch in recipe.get(key, []):
                if isinstance(batch, dict) and batch.get("id"):
                    result.append((str(batch["id"]), category))
        return sorted(result)
    for key, category in (
        ("building_batches", "building"),
        ("vegetation_batches", "vegetation"),
        ("parked_aircraft_batches", "parked_aircraft"),
    ):
        for batch in recipe.get(key, []):
            if isinstance(batch, dict) and batch.get("id"):
                result.append((str(batch["id"]), category))
    return sorted(result)


def _mesh_evidence(path: Path) -> dict[str, Any]:
    evidence: dict[str, Any] = {
        "exists": path.is_file(),
        "index_count": 0,
        "triangles": 0,
        "vertex_count": 0,
        "triangle_topology_only": False,
        "valid": False,
    }
    if not path.is_file():
        return evidence
    text = path.read_text(encoding="utf-8")
    index_counts = [int(value) for value in re.findall(r"^\s*indexCount:\s*(\d+)\s*$", text, re.MULTILINE)]
    topologies = [int(value) for value in re.findall(r"^\s*topology:\s*(\d+)\s*$", text, re.MULTILINE)]
    vertices = [int(value) for value in re.findall(r"^\s*m_VertexCount:\s*(\d+)\s*$", text, re.MULTILINE)]
    evidence.update(
        {
            "sha256": _sha256(path),
            "index_count": sum(index_counts),
            "triangles": sum(index_counts) // 3,
            "vertex_count": sum(vertices),
            "triangle_topology_only": bool(topologies) and all(value == 0 for value in topologies),
            "valid": bool(index_counts)
            and sum(index_counts) > 0
            and all(value % 3 == 0 for value in index_counts)
            and bool(topologies)
            and all(value == 0 for value in topologies)
            and bool(vertices)
            and sum(vertices) > 0,
        }
    )
    return evidence


def _png_dimensions(path: Path) -> tuple[int, int] | None:
    if not path.is_file():
        return None
    with path.open("rb") as stream:
        header = stream.read(24)
    if len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    return struct.unpack(">II", header[16:24])


def _astc_mip_bytes(width: int, height: int, block: int) -> int:
    total = 0
    while True:
        total += math.ceil(width / block) * math.ceil(height / block) * 16
        if width == 1 and height == 1:
            return total
        width = max(1, width // 2)
        height = max(1, height // 2)


def _shader_evidence(path: Path, required_tokens: list[str]) -> dict[str, Any]:
    result: dict[str, Any] = {"exists": path.is_file(), "compatible": False}
    if not path.is_file():
        return result
    text = path.read_text(encoding="utf-8")
    missing = [token for token in required_tokens if token not in text]
    opaque = '"RenderType"="Opaque"' in text.replace(" ", "") or '"RenderType" = "Opaque"' in text
    result.update(
        {
            "sha256": _sha256(path),
            "missing_contract_tokens": missing,
            "pass_count": len(re.findall(r"^\s*Pass\s*$", text, re.MULTILINE)),
            "opaque_contract": opaque,
            "compatible": not missing and opaque,
        }
    )
    return result


def _material_evidence(path: Path) -> dict[str, Any]:
    result: dict[str, Any] = {"exists": path.is_file(), "compatible": False}
    if not path.is_file():
        return result
    text = path.read_text(encoding="utf-8")
    render_queue_match = re.search(r"^\s*m_CustomRenderQueue:\s*(-?\d+)\s*$", text, re.MULTILINE)
    render_queue = int(render_queue_match.group(1)) if render_queue_match else None
    instancing = bool(re.search(r"^\s*m_EnableInstancingVariants:\s*1\s*$", text, re.MULTILINE))
    opaque_queue = render_queue is not None and (render_queue == -1 or render_queue <= 2500)
    result.update(
        {
            "sha256": _sha256(path),
            "instancing_enabled": instancing,
            "custom_render_queue": render_queue,
            "opaque_queue": opaque_queue,
            "compatible": instancing and opaque_queue,
        }
    )
    return result


def _texture_evidence(path: Path, android_format: int, astc_block: int) -> dict[str, Any]:
    meta = Path(str(path) + ".meta")
    result: dict[str, Any] = {"exists": path.is_file(), "meta_exists": meta.is_file(), "valid": False}
    dimensions = _png_dimensions(path)
    if not (path.is_file() and meta.is_file() and dimensions):
        return result
    text = meta.read_text(encoding="utf-8")
    android_blocks = re.findall(
        r"- serializedVersion:.*?(?=\n\s*- serializedVersion:|\Z)", text, re.DOTALL
    )
    android = next((block for block in android_blocks if "buildTarget: Android" in block), "")
    format_match = re.search(r"textureFormat:\s*(-?\d+)", android)
    overridden = bool(re.search(r"overridden:\s*1", android))
    mipmaps = bool(re.search(r"enableMipMap:\s*1", text))
    actual_format = int(format_match.group(1)) if format_match else None
    width, height = dimensions
    result.update(
        {
            "sha256": _sha256(path),
            "meta_sha256": _sha256(meta),
            "width": width,
            "height": height,
            "mipmaps": mipmaps,
            "android_override": overridden,
            "android_texture_format": actual_format,
            "expected_android_texture_format": android_format,
            "estimated_runtime_bytes": _astc_mip_bytes(width, height, astc_block) if mipmaps else math.ceil(width / astc_block) * math.ceil(height / astc_block) * 16,
            "valid": overridden and mipmaps and actual_format == android_format,
        }
    )
    return result


def _prefab_evidence(path: Path, required_levels: int) -> dict[str, Any]:
    result: dict[str, Any] = {"exists": path.is_file(), "valid": False}
    if not path.is_file():
        return result
    text = path.read_text(encoding="utf-8")
    blocks = re.split(r"(?=^--- !u!205\s)", text, flags=re.MULTILINE)[1:]
    level_counts = [len(re.findall(r"^\s*- screenRelativeHeight:", block, re.MULTILINE)) for block in blocks]
    crossfade_disabled = all(
        re.search(r"^\s*m_FadeMode:\s*0\s*$", block, re.MULTILINE)
        and re.search(r"^\s*m_AnimateCrossFading:\s*0\s*$", block, re.MULTILINE)
        for block in blocks
    ) if blocks else False
    result.update(
        {
            "sha256": _sha256(path),
            "lod_group_count": len(blocks),
            "lod_level_counts": level_counts,
            "minimum_levels": min(level_counts) if level_counts else 0,
            "crossfade_disabled": crossfade_disabled,
            "valid": bool(blocks) and min(level_counts) >= required_levels and crossfade_disabled,
        }
    )
    return result


def _check(check_id: str, passed: bool, evidence: str) -> dict[str, Any]:
    return {"id": check_id, "status": "PASS" if passed else "FAIL", "evidence": evidence}


def build_report(repo_root: Path, config: dict[str, Any]) -> dict[str, Any]:
    repo_root = repo_root.resolve()
    paths = {name: repo_root / value for name, value in config["paths"].items()}
    required_inputs = ["recipe", "placement_manifest", "building_coverage", "vegetation_manifest"]
    inputs = {name: _input(path, repo_root) for name, path in sorted(paths.items()) if name != "mesh_root"}
    missing = [name for name in required_inputs if not paths[name].is_file()]
    if missing:
        return {
            "schema_version": 1,
            "classification": "FAIL_MISSING_REQUIRED_INPUTS",
            "evidence_kind": "STATIC_OFFLINE_PLANNING_ONLY",
            "hardware_performance_claim_allowed": False,
            "headset_performance_established": False,
            "inputs": inputs,
            "checks": [_check("required_inputs", False, "Missing: " + ", ".join(missing))],
            "blocking_reasons": ["Required input missing: " + name for name in missing],
        }

    recipe = _load_json(paths["recipe"])
    placement = _load_json(paths["placement_manifest"])
    coverage = _load_json(paths["building_coverage"])
    vegetation = _load_json(paths["vegetation_manifest"])
    batches = _recipe_batches(recipe)
    placements = placement.get("placements", [])
    category_counts = Counter(str(item.get("category", "unknown")) for item in placements)
    hlod_classes = Counter(str(item.get("lod_hlod", {}).get("class", "missing")) for item in placements)
    cells = sorted({str(item.get("lod_hlod", {}).get("cell_id")) for item in placements if item.get("lod_hlod", {}).get("cell_id")})

    levels = list(config["required_lod_levels"])
    mesh_rows: list[dict[str, Any]] = []
    tier_totals = {level: {"mesh_count": 0, "triangles": 0, "vertices": 0, "valid_mesh_count": 0} for level in levels}
    for batch_id, category in batches:
        row: dict[str, Any] = {"batch_id": batch_id, "category": category, "levels": {}}
        for level in levels:
            path = paths["mesh_root"] / f"{batch_id}_{level}.asset"
            evidence = _mesh_evidence(path)
            evidence["path"] = _relative(path, repo_root)
            row["levels"][level] = evidence
            if evidence["exists"]:
                tier_totals[level]["mesh_count"] += 1
                tier_totals[level]["triangles"] += evidence["triangles"]
                tier_totals[level]["vertices"] += evidence["vertex_count"]
            if evidence["valid"]:
                tier_totals[level]["valid_mesh_count"] += 1
        mesh_rows.append(row)

    prefab = _prefab_evidence(paths["prefab"], len(levels))
    shaders = {path.name: _shader_evidence(path, config["required_shader_tokens"]) for path in (repo_root / item for item in config["shader_paths"])}
    materials = {path.name: _material_evidence(path) for path in (repo_root / item for item in config["material_paths"])}
    texture = _texture_evidence(paths["context_atlas"], config["android_astc_texture_format"], config["astc_block_size"])

    placement_cell = placement.get("cell_size_meters")
    recipe_cell = recipe.get("logical_cell_size_meters")
    if recipe_cell is None:
        recipe_cell = recipe.get("integration", {}).get("building_spatial_batch_cell_meters")
    low, high = config["cell_size_meters_range"]
    lod_complete = all(tier_totals[level]["valid_mesh_count"] == len(batches) for level in levels)
    shader_ok = bool(shaders) and all(value["compatible"] for value in shaders.values())
    material_ok = bool(materials) and all(value["compatible"] for value in materials.values())
    integrated_budget = config.get("integrated_product_budget")
    if integrated_budget is None and recipe.get("static_budget", {}).get("status") == "PASS":
        source_estimates = recipe["static_budget"].get("estimates", {})
        integrated_budget = {
            "aggregate_serialized_renderers": source_estimates.get("integrated_aggregate_serialized_renderers"),
            "aggregate_serialized_triangles": source_estimates.get("integrated_aggregate_serialized_triangles"),
            "visible_draw_slots": source_estimates.get("integrated_visible_draw_slots"),
            "visible_triangles": source_estimates.get("integrated_visible_triangles"),
            "visible_materials": source_estimates.get("integrated_visible_materials"),
            "texture_memory_bytes": source_estimates.get("integrated_texture_memory_bytes"),
            "transparent_material_slots": source_estimates.get("integrated_transparent_material_slots"),
        }
    integrated_known = isinstance(integrated_budget, dict) and all(
        name in integrated_budget for name in ("visible_draw_slots", "visible_triangles", "visible_materials", "texture_memory_bytes", "transparent_material_slots")
    )
    limits = config["limits"]
    budget_status: dict[str, Any] = {"evidence_available": integrated_known, "limits": limits}
    if integrated_known:
        comparisons = {
            key: {"value": int(integrated_budget[key]), "limit": int(limit), "within_limit": int(integrated_budget[key]) <= int(limit)}
            for key, limit in limits.items()
        }
        budget_status.update({"source": "configured_integrated_static_evidence", "comparisons": comparisons, "within_all_limits": all(item["within_limit"] for item in comparisons.values())})
    else:
        budget_status.update({"source": None, "comparisons": {}, "within_all_limits": False, "reason": "No integrated whole-product static budget evidence was configured."})

    context_subset = {
        "scope": "serialized_context_subset_only_not_whole_product",
        "conservative_visible_draw_slots": len(batches),
        "maximum_single_tier_triangles": max((value["triangles"] for value in tier_totals.values()), default=0),
        "material_assets": len(materials),
        "shader_assets": len(shaders),
        "estimated_texture_memory_bytes": texture.get("estimated_runtime_bytes"),
        "transparent_material_slots": 0 if material_ok and all(item["opaque_queue"] for item in materials.values()) else None,
    }

    checks = [
        _check("placement_cell_size", isinstance(placement_cell, (int, float)) and low <= placement_cell <= high, f"placement={placement_cell} m; required={low}..{high} m"),
        _check("recipe_logical_cell_size", isinstance(recipe_cell, (int, float)) and low <= recipe_cell <= high, f"recipe logical cell={recipe_cell} m; required={low}..{high} m"),
        _check("three_level_mesh_coverage", lod_complete, "; ".join(f"{level}={tier_totals[level]['valid_mesh_count']}/{len(batches)}" for level in levels)),
        _check("serialized_prefab_lod_contract", prefab["valid"] and prefab.get("lod_group_count") == len(batches), f"groups={prefab.get('lod_group_count', 0)}/{len(batches)}; minimum_levels={prefab.get('minimum_levels', 0)}; crossfade_disabled={prefab.get('crossfade_disabled', False)}"),
        _check("shader_instancing_stereo_contract", shader_ok, f"compatible={sum(1 for item in shaders.values() if item['compatible'])}/{len(shaders)}"),
        _check("material_instancing_opaque_contract", material_ok, f"compatible={sum(1 for item in materials.values() if item['compatible'])}/{len(materials)}"),
        _check("android_context_texture_contract", texture["valid"], f"format={texture.get('android_texture_format')}; mips={texture.get('mipmaps')}; estimated_bytes={texture.get('estimated_runtime_bytes')}"),
        _check("building_source_acceptance", coverage.get("v2_acceptance_status") == "PASS", f"status={coverage.get('v2_acceptance_status')}"),
        _check("vegetation_source_ready", vegetation.get("distribution_source", {}).get("status") in
               ("READY", "PASS", "PASS_NATURAL_COLOR_FALLBACK"),
               f"status={vegetation.get('distribution_source', {}).get('status')}"),
        _check("vegetation_species_library_ready", vegetation.get("species_library", {}).get("status") in
               ("READY", "PASS", "PROJECT_OWNED_PROCEDURAL_LOD0_LOD1_LOD2_AUTHORED"),
               f"status={vegetation.get('species_library', {}).get('status')}"),
        _check("integrated_whole_product_budget", budget_status["within_all_limits"], budget_status.get("reason", "configured comparisons evaluated")),
    ]
    blockers = [item["id"] + ": " + item["evidence"] for item in checks if item["status"] == "FAIL"]
    classification = "PASS_STATIC_HLOD_AND_BUDGET_EVIDENCE" if not blockers else "FAIL_MISSING_V2_HLOD_OR_BUDGET_EVIDENCE"

    return {
        "schema_version": 1,
        "classification": classification,
        "evidence_kind": "STATIC_OFFLINE_PLANNING_ONLY",
        "hardware_performance_claim_allowed": False,
        "headset_performance_established": False,
        "deterministic_output": True,
        "inputs": inputs,
        "placement_inventory": {
            "placement_count": len(placements),
            "unique_cell_count": len(cells),
            "cell_size_meters": placement_cell,
            "category_counts": dict(sorted(category_counts.items())),
            "hlod_class_counts": dict(sorted(hlod_classes.items())),
        },
        "recipe_inventory": {
            "batch_count": len(batches),
            "batch_counts": dict(sorted(Counter(category for _, category in batches).items())),
            "logical_cell_size_meters": recipe_cell,
            "runtime_building_cell_sizes_meters": sorted({batch.get("runtime_cell_size_meters") for batch in recipe.get("building_runtime_batches", []) if batch.get("runtime_cell_size_meters") is not None}),
            "lod_crossfade": recipe.get("integration", {}).get("lod_crossfade"),
        },
        "mesh_tier_totals": tier_totals,
        "mesh_batches": mesh_rows,
        "prefab": prefab,
        "shaders": shaders,
        "materials": materials,
        "context_atlas": texture,
        "context_subset_estimate": context_subset,
        "integrated_product_budget": budget_status,
        "checks": checks,
        "blocking_reasons": blockers,
        "limitations": [
            "Counts are derived from manifests and serialized Unity YAML; Unity was not launched.",
            "Context-subset draw slots are conservative renderer/material slots, not measured draw calls.",
            "Instancing and stereo results establish source contracts, not runtime batching or stereo correctness.",
            "Texture memory is a format/mipmap estimate and excludes driver/runtime residency overhead.",
            "Only physical Quest evidence can establish frame time, thermal behavior, comfort, or product performance.",
        ],
    }


def render_markdown(report: dict[str, Any]) -> str:
    lines = [
        "# KBDU HLOD V2 Static Rendering Audit",
        "",
        f"**Result:** `{report['classification']}`",
        "",
        "This is deterministic offline planning evidence. It does not establish Quest hardware performance.",
        "",
    ]
    inventory = report.get("placement_inventory")
    if inventory:
        lines.extend([
            "## Inventory",
            "",
            f"- Placements: {inventory['placement_count']:,} across {inventory['unique_cell_count']:,} cells at {inventory['cell_size_meters']} m",
            f"- Runtime recipe batches: {report['recipe_inventory']['batch_count']}; logical cells: {report['recipe_inventory']['logical_cell_size_meters']} m",
            "",
            "| Tier | Valid meshes | Serialized meshes | Triangles | Vertices |",
            "|---|---:|---:|---:|---:|",
        ])
        expected = report["recipe_inventory"]["batch_count"]
        for level, data in report["mesh_tier_totals"].items():
            lines.append(f"| {level} | {data['valid_mesh_count']}/{expected} | {data['mesh_count']} | {data['triangles']:,} | {data['vertices']:,} |")
        subset = report["context_subset_estimate"]
        lines.extend([
            "",
            "## Context-subset planning estimate",
            "",
            f"- Conservative visible renderer/material slots: {subset['conservative_visible_draw_slots']}",
            f"- Maximum single-tier triangles: {subset['maximum_single_tier_triangles']:,}",
            f"- Material assets: {subset['material_assets']}; shader assets: {subset['shader_assets']}",
            f"- Estimated texture memory: {subset['estimated_texture_memory_bytes']} bytes",
            f"- Transparent material slots: {subset['transparent_material_slots']}",
            "",
            "These values cover only the serialized context subset and cannot be substituted for a whole-product budget.",
            "",
        ])
    lines.extend(["## Gates", "", "| Gate | Result | Evidence |", "|---|---|---|"])
    for item in report.get("checks", []):
        evidence = str(item["evidence"]).replace("|", "\\|")
        lines.append(f"| `{item['id']}` | **{item['status']}** | {evidence} |")
    blockers = report.get("blocking_reasons", [])
    lines.extend(["", "## Blocking reasons", ""])
    lines.extend([f"- {item}" for item in blockers] or ["- None."])
    lines.extend(["", "## Limitations", ""])
    lines.extend(f"- {item}" for item in report.get("limitations", []))
    return "\n".join(lines) + "\n"


def write_report(report: dict[str, Any], output_dir: Path) -> tuple[Path, Path]:
    output_dir.mkdir(parents=True, exist_ok=True)
    json_path = output_dir / REPORT_JSON
    markdown_path = output_dir / REPORT_MARKDOWN
    json_path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    markdown_path.write_text(render_markdown(report), encoding="utf-8")
    return json_path, markdown_path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--config", type=Path, default=Path(__file__).with_name("config.json"))
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args(argv)
    config = _load_json(args.config)
    report = build_report(args.repo_root, config)
    json_path, markdown_path = write_report(report, args.output_dir)
    print(json_path)
    print(markdown_path)
    print(report["classification"])
    return 0 if report["classification"].startswith("PASS_") else 2


if __name__ == "__main__":
    sys.exit(main())
