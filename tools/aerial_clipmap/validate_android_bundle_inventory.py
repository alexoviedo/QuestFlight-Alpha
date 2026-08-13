"""Validate the ignored Android AssetBundle payload for the full-map clipmap.

Unity's binary bundles are local build inputs, not Git assets. This independent
inventory gate proves that the compiled directory covers every manifest page,
contains no obsolete 2024 sample assets, and has exactly the reviewed bundle
partition before an APK is built.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
from typing import Any


def sha256_file(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def manifest_assets(path: pathlib.Path) -> list[str]:
    assets: list[str] = []
    in_assets = False
    for line in path.read_text(encoding="utf-8").splitlines():
        if line == "Assets:":
            in_assets = True
            continue
        if in_assets and line.startswith("Dependencies:"):
            break
        if in_assets and line.startswith("- "):
            assets.append(line[2:])
    return assets


def validate(
    page_manifest_path: pathlib.Path,
    bundle_root: pathlib.Path,
) -> dict[str, Any]:
    page_manifest = json.loads(page_manifest_path.read_text(encoding="utf-8"))
    pages = list(page_manifest.get("pages", []))
    expected_bundles = {str(page["bundle_name"]) for page in pages}
    failures: list[str] = []

    if page_manifest.get("mode") != "full":
        failures.append("page manifest is not the complete full-map manifest")
    if len(pages) != 1873:
        failures.append(f"page count {len(pages)} != 1873")
    if page_manifest.get("source", {}).get("item", {}).get("id") != \
            "22b7c93a74394696824258f807f345ba":
        failures.append("page manifest is not the reviewed City 2020 source")

    actual_bundles = {
        path.name for path in bundle_root.iterdir()
        if path.is_file() and path.name.startswith("qf_aerial_")
        and path.suffix not in {".manifest", ".meta"}
    } if bundle_root.is_dir() else set()
    missing_bundles = sorted(expected_bundles - actual_bundles)
    unexpected_bundles = sorted(actual_bundles - expected_bundles)
    if missing_bundles:
        failures.append(f"missing {len(missing_bundles)} bundles")
    if unexpected_bundles:
        failures.append(f"unexpected bundles: {unexpected_bundles[:10]}")

    expected_assets = {
        f"Assets/Generated/FullMapAerialClipmap/{page['tier']}/"
        f"{page['asset_name']}.jpg"
        for page in pages
    }
    actual_assets: list[str] = []
    bundle_records: list[dict[str, Any]] = []
    for bundle_name in sorted(expected_bundles & actual_bundles):
        bundle_path = bundle_root / bundle_name
        text_manifest = bundle_root / f"{bundle_name}.manifest"
        if not text_manifest.is_file():
            failures.append(f"missing text manifest for {bundle_name}")
            continue
        assets = manifest_assets(text_manifest)
        actual_assets.extend(assets)
        if any("KBDU_2024" in asset for asset in assets):
            failures.append(f"obsolete 2024 asset in {bundle_name}")
        bundle_records.append({
            "name": bundle_name,
            "bytes": bundle_path.stat().st_size,
            "sha256": sha256_file(bundle_path),
            "asset_count": len(assets),
        })

    actual_asset_set = set(actual_assets)
    missing_assets = sorted(expected_assets - actual_asset_set)
    unexpected_assets = sorted(actual_asset_set - expected_assets)
    duplicate_count = len(actual_assets) - len(actual_asset_set)
    if missing_assets:
        failures.append(f"missing {len(missing_assets)} compiled page assets")
    if unexpected_assets:
        failures.append(f"unexpected compiled assets: {unexpected_assets[:10]}")
    if duplicate_count:
        failures.append(f"{duplicate_count} page assets are duplicated across bundles")

    return {
        "passed": not failures,
        "failures": failures,
        "page_manifest": str(page_manifest_path),
        "bundle_root": str(bundle_root),
        "source_item_id": page_manifest.get("source", {}).get("item", {}).get("id"),
        "expected_page_count": len(pages),
        "compiled_page_count": len(actual_assets),
        "unique_compiled_page_count": len(actual_asset_set),
        "expected_bundle_count": len(expected_bundles),
        "compiled_bundle_count": len(actual_bundles),
        "compiled_bundle_bytes": sum(record["bytes"] for record in bundle_records),
        "bundles": bundle_records,
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--page-manifest", type=pathlib.Path, required=True)
    parser.add_argument("--bundle-root", type=pathlib.Path, required=True)
    parser.add_argument("--report", type=pathlib.Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    report = validate(args.page_manifest.resolve(), args.bundle_root.resolve())
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(
        f"{'PASS' if report['passed'] else 'FAIL'}: "
        f"{report['compiled_page_count']}/{report['expected_page_count']} pages; "
        f"{report['compiled_bundle_count']}/{report['expected_bundle_count']} bundles; "
        f"{report['compiled_bundle_bytes']} bytes"
    )
    if not report["passed"]:
        for failure in report["failures"]:
            print(f"- {failure}")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
