#!/usr/bin/env python3
"""Validate standalone environment asset lineage and emit the compact integration ledger."""

from __future__ import annotations

import argparse
import json
import re
from datetime import datetime, timezone
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
AIRPORT_MANIFEST = Path(__file__).resolve().parent / "kbdu_airport_art_manifest.json"
RUNWAY_MANIFEST = Path(__file__).resolve().parent / "kbdu_runway_art_manifest.json"
ORTHOPHOTO_MANIFEST = REPO_ROOT / "tools" / "kbdu_orthophoto" / "kbdu_orthophoto_manifest.json"
FAR_DEM_MANIFEST = REPO_ROOT / "tools" / "kbdu_orthophoto" / "kbdu_far_dem_100m.json"
DEFAULT_OUTPUT = Path(__file__).resolve().parent / "kbdu_environment_integration_manifest.json"
GENERATED_FOLDER = REPO_ROOT / "QuestFlight" / "Assets" / "Production" / "Environment" / "Generated" / "StandaloneRecovery"
FAR_MESH = REPO_ROOT / "QuestFlight" / "Assets" / "Production" / "Environment" / "Generated" / "Terrain_Far24km_Immutable_USGS_Mesh.asset"


def sha256(path: Path) -> str:
    import hashlib

    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def file_record(path: Path) -> dict:
    return {
        "path": path.relative_to(REPO_ROOT).as_posix(),
        "bytes": path.stat().st_size,
        "sha256": sha256(path),
    }


def runtime_binary_asset_paths(manifests: dict) -> dict[str, list[Path]]:
    """Return runtime assets grouped by their source manifest category."""

    return {
        "orthophotos": [
            REPO_ROOT / asset["path"] for asset in manifests["orthophoto"]["derived_assets"]
        ],
        "airport_kit": [
            REPO_ROOT / output["path"]
            for output in manifests["airport"]["outputs"]
            if output["path"].startswith("QuestFlight/Assets/")
        ],
        "runway_textures": [
            REPO_ROOT / output["path"]
            for output in manifests["runway"]["outputs"]
            if output["path"].startswith("QuestFlight/Assets/")
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--report", type=Path, default=None)
    arguments = parser.parse_args()
    checks: list[dict] = []

    def check(name: str, condition: bool, detail: str) -> None:
        checks.append({"name": name, "passed": bool(condition), "detail": detail})

    manifests = {
        "airport": json.loads(AIRPORT_MANIFEST.read_text(encoding="utf-8")),
        "runway": json.loads(RUNWAY_MANIFEST.read_text(encoding="utf-8")),
        "orthophoto": json.loads(ORTHOPHOTO_MANIFEST.read_text(encoding="utf-8")),
        "far_dem": json.loads(FAR_DEM_MANIFEST.read_text(encoding="utf-8")),
    }
    for label in ("airport", "runway"):
        for output in manifests[label]["outputs"]:
            path = REPO_ROOT / output["path"]
            check(label + "_exists_" + path.name, path.is_file(), output["path"])
            if path.is_file():
                check(label + "_hash_" + path.name, sha256(path) == output["sha256"], output["sha256"])

    generated_assets = sorted(GENERATED_FOLDER.glob("*.asset"))
    # Twenty production-art meshes plus the authored airport/near terrain seam.
    check("combined_mesh_count", len(generated_assets) == 21, f"count={len(generated_assets)}")
    for lod in range(3):
        key = f"LOD{lod}"
        combined = sorted(GENERATED_FOLDER.glob(f"*_LOD{lod}_Combined.asset"))
        index_count = sum(
            sum(int(value) for value in re.findall(r"indexCount: (\d+)", path.read_text(encoding="utf-8")))
            for path in combined
        )
        observed_triangles = index_count // 3
        expected_triangles = manifests["airport"]["geometry"]["triangles_by_lod"][key]
        check(
            "airport_combined_triangles_" + key,
            index_count % 3 == 0 and observed_triangles == expected_triangles,
            f"Unity={observed_triangles}, Blender={expected_triangles}, meshes={len(combined)}",
        )
        cap = manifests["airport"]["validation"]["quonset_caps_and_shell"][key]
        check(
            "quonset_double_wound_caps_" + key,
            cap["passed"]
            and cap["front_outward_triangles"] == cap["front_inward_triangles"] > 0
            and cap["rear_outward_triangles"] == cap["rear_inward_triangles"] > 0
            and cap["outward_shell_faces"] == cap["shell_faces"] > 0,
            (
                f"front={cap['front_outward_triangles']}/{cap['front_inward_triangles']}, "
                f"rear={cap['rear_outward_triangles']}/{cap['rear_inward_triangles']}, "
                f"shell={cap['outward_shell_faces']}/{cap['shell_faces']}"
            ),
        )
    check("far_mesh_exists", FAR_MESH.is_file(), str(FAR_MESH))
    check("far_mesh_guid_preserved", "guid: f7e9defd4f2f8134ea4ed04d496c0495" in Path(str(FAR_MESH) + ".meta").read_text(encoding="utf-8"), "far mesh GUID")
    far_grid = manifests["far_dem"]["grid"]
    check("far_grid", far_grid["width"] == 241 and far_grid["height"] == 241 and far_grid["spacing_meters"] == 100.0, "241x241 @ 100 m")
    check("far_fidelity_disclosure", "not additional source elevation detail" in manifests["far_dem"]["source_fidelity_disclosure"], manifests["far_dem"]["source_fidelity_disclosure"])
    check("orthophoto_public_domain", "public domain" in manifests["orthophoto"]["source"]["license_name"].lower(), manifests["orthophoto"]["source"]["license_name"])

    runtime_asset_paths = runtime_binary_asset_paths(manifests)
    texture_paths = (
        runtime_asset_paths["orthophotos"]
        + [path for path in runtime_asset_paths["airport_kit"] if path.suffix.lower() != ".glb"]
        + runtime_asset_paths["runway_textures"]
    )
    for texture in texture_paths:
        meta = Path(str(texture) + ".meta")
        text = meta.read_text(encoding="utf-8") if meta.is_file() else ""
        check("astc_" + texture.name, "buildTarget: Android" in text and "textureFormat: 50" in text, "ASTC_6x6 Android override")
        check("mips_" + texture.name, "enableMipMap: 1" in text, "mipmaps enabled")

    integration = {
        "schema_version": 1,
        "generated_utc": datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
        "label": "Standalone Quest KBDU environment integration / not for navigation",
        "source_manifests": {
            "airport": file_record(AIRPORT_MANIFEST),
            "runway": file_record(RUNWAY_MANIFEST),
            "orthophoto": file_record(ORTHOPHOTO_MANIFEST),
            "far_dem": file_record(FAR_DEM_MANIFEST),
        },
        "runtime_binary_assets": {
            **{
                category: [file_record(path) for path in paths]
                for category, paths in runtime_asset_paths.items()
            },
            "unity_authored_meshes": [file_record(FAR_MESH)] + [file_record(path) for path in generated_assets],
        },
        "licenses": {
            "orthophotos": "USGS/USDA NAIP United States government work / public domain",
            "far_terrain": "USGS 3DEP United States government work / public domain; deterministic project-authored resampling and mesh",
            "airport_and_runway": "Project-owned original procedural work; no external mesh or texture input",
            "unity_authored_meshes": "Project-owned deterministic derivatives of the recorded sources",
        },
        "constraints": {
            "raw_downloads_committed": False,
            "unclear_license_assets": False,
            "google_derived_content": False,
            "android_texture_format": "ASTC_6x6",
            "far_runtime_lod": False,
            "airport_lod_crossfade": False,
        },
    }
    arguments.output.write_text(json.dumps(integration, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    failed = [item for item in checks if not item["passed"]]
    report = {"schema_version": 1, "passed": not failed, "failure_count": len(failed), "checks": checks}
    if arguments.report:
        arguments.report.parent.mkdir(parents=True, exist_ok=True)
        arguments.report.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    for item in checks:
        print(f"{'PASS' if item['passed'] else 'FAIL'} {item['name']}: {item['detail']}")
    return 0 if not failed else 1


if __name__ == "__main__":
    raise SystemExit(main())
