#!/usr/bin/env python3
"""Validate KBDU orthophoto lineage, dimensions, hashes, Unity imports, and memory bounds."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image

from orthophoto_common import DEFAULT_MANIFEST_PATH, REPO_ROOT, atomic_write_json, load_config, sha256_file


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST_PATH)
    parser.add_argument("--config", type=Path, default=None)
    parser.add_argument("--report", type=Path, default=None)
    args = parser.parse_args()
    config = load_config(args.config) if args.config else load_config()
    manifest_path = args.manifest.resolve()
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    checks: list[dict] = []

    def check(name: str, condition: bool, detail: str) -> None:
        checks.append({"name": name, "passed": bool(condition), "detail": detail})

    check("schema", manifest.get("schema_version") == 1, f"schema={manifest.get('schema_version')}")
    source = manifest.get("source", {})
    check("official_service", str(source.get("service_url", "")).startswith("https://imagery.nationalmap.gov/"), str(source.get("service_url")))
    check("public_domain", "public domain" in str(source.get("license_name", "")).lower(), str(source.get("license_name")))
    check("source_year", source.get("catalog_year") == 2019, f"year={source.get('catalog_year')}")
    check("raw_lineage", len(source.get("raw_tiles", [])) == 5, f"tiles={len(source.get('raw_tiles', []))}")
    check("prohibited_sources", "No Google" in manifest.get("prohibited_sources_confirmation", ""), manifest.get("prohibited_sources_confirmation", ""))

    total_runtime = 0
    by_zone = {zone["id"]: zone for zone in config["zones"]}
    for asset in manifest.get("derived_assets", []):
        path = REPO_ROOT / asset["path"]
        zone = by_zone[asset["zone_id"]]
        check(f"{asset['zone_id']}_exists", path.is_file(), str(path))
        if not path.is_file():
            continue
        check(f"{asset['zone_id']}_sha256", sha256_file(path) == asset["sha256"], asset["sha256"])
        with Image.open(path) as image:
            image.load()
            expected = int(zone["atlas_size_pixels"])
            check(f"{asset['zone_id']}_dimensions", image.size == (expected, expected), f"size={image.size}")
            check(f"{asset['zone_id']}_mode", image.mode == "RGB", f"mode={image.mode}")
        maximum_key = "maximum_committed_near_bytes" if asset["zone_id"] == "near_4km" else "maximum_committed_mid_bytes"
        check(
            f"{asset['zone_id']}_source_size",
            path.stat().st_size <= int(config["unity"][maximum_key]),
            f"bytes={path.stat().st_size}, maximum={config['unity'][maximum_key]}",
        )
        total_runtime += int(asset["android_import"]["estimated_mip_chain_bytes"])
        meta_path = Path(str(path) + ".meta")
        check(f"{asset['zone_id']}_meta", meta_path.is_file(), str(meta_path))
        if meta_path.is_file():
            text = meta_path.read_text(encoding="utf-8")
            check(f"{asset['zone_id']}_astc", "textureFormat: 50" in text, "Android TextureImporterFormat ASTC_6x6 (50)")
            check(f"{asset['zone_id']}_mipmaps", "enableMipMap: 1" in text and "streamingMipmaps: 1" in text, "mipmaps + streaming")
            check(f"{asset['zone_id']}_clamp", "wrapU: 1" in text and "wrapV: 1" in text, "clamp")

    check(
        "runtime_texture_budget",
        total_runtime <= int(config["unity"]["maximum_runtime_texture_bytes"]),
        f"estimated={total_runtime}, maximum={config['unity']['maximum_runtime_texture_bytes']}",
    )
    git_relative_manifest = manifest_path.relative_to(REPO_ROOT).as_posix()
    check("manifest_in_pipeline", git_relative_manifest.startswith("tools/kbdu_orthophoto/"), git_relative_manifest)

    failed = [item for item in checks if not item["passed"]]
    report = {
        "schema_version": 1,
        "passed": not failed,
        "checks": checks,
        "failure_count": len(failed),
    }
    if args.report:
        atomic_write_json(args.report, report)
    for item in checks:
        print(f"{'PASS' if item['passed'] else 'FAIL'} {item['name']}: {item['detail']}")
    return 0 if not failed else 1


if __name__ == "__main__":
    raise SystemExit(main())
