"""Validate the ignored Android AssetBundle payload for the full-map clipmap.

Unity's binary bundles are local build inputs, not Git assets. This independent
inventory gate checks declared membership and requires byte-bound Unity load evidence for every manifest page,
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
    verification_path: pathlib.Path | None = None,
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
    expected_assets_by_bundle: dict[str,set[str]] = {}
    for page in pages:
        expected_assets_by_bundle.setdefault(page["bundle_name"],set()).add(
            f"Assets/Generated/FullMapAerialClipmap/{page['tier']}/{page['asset_name']}.jpg")
    actual_assets: list[str] = []
    bundle_records: list[dict[str, Any]] = []
    for bundle_name in sorted(expected_bundles & actual_bundles):
        bundle_path = bundle_root / bundle_name
        text_manifest = bundle_root / f"{bundle_name}.manifest"
        if not text_manifest.is_file():
            failures.append(f"missing text manifest for {bundle_name}")
            continue
        with bundle_path.open("rb") as stream: header=stream.read(32)
        if bundle_path.stat().st_size <= 32 or not header.startswith(b"UnityFS\x00"):
            failures.append(f"empty, truncated or unsupported binary bundle: {bundle_name}")
        assets = manifest_assets(text_manifest)
        if len(assets)!=len(set(assets)) or set(assets)!=expected_assets_by_bundle[bundle_name]:
            failures.append(f"wrong page partition in {bundle_name}")
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

    preliminary_passed = not failures
    # Sidecar claims and a UnityFS header cannot establish binary content.
    # The real Editor producer opens every bundle/asset and records exact bytes.
    if verification_path is None or not verification_path.is_file():
        failures.append("missing byte-bound Unity Android bundle verification report")
    else:
        try:
            evidence=json.loads(verification_path.read_text(encoding="utf-8"))
            if evidence.get("schema")!="questflight.bundle-verification.v1" or evidence.get("build_target")!="Android" or evidence.get("verified") is not True:
                failures.append("invalid Unity Android verification scope")
            if evidence.get("manifest_sha256")!=sha256_file(page_manifest_path):failures.append("stale page-source verification identity")
            verified=evidence.get("bundles",[])
            verified_names=[record.get("name") for record in verified]
            if len(verified_names)!=len(set(verified_names)) or set(verified_names)!=expected_bundles:
                failures.append("Unity verification bundle set is incomplete or duplicated")
            by_name={record.get("name"):record for record in verified}
            for current in bundle_records:
                proof=by_name.get(current["name"],{})
                if proof.get("bytes")!=current["bytes"] or proof.get("sha256")!=current["sha256"]:
                    failures.append("binary changed after Unity verification: "+current["name"])
                assets=proof.get("assets",[]);expected_pages=[page for page in pages if page["bundle_name"]==current["name"]]
                if len(assets)!=len(expected_pages) or {asset.get("name") for asset in assets}!={page['asset_name'].lower() for page in expected_pages}:
                    failures.append("Unity loaded asset partition mismatch: "+current["name"])
                for page in expected_pages:
                    asset=next((a for a in assets if a.get("name")==page['asset_name'].lower()),{})
                    dimensions=page.get("output_pixels")
                    if dimensions and (asset.get("width")!=dimensions[0] or asset.get("height")!=dimensions[1]):
                        failures.append("Unity loaded dimensions mismatch: "+page['asset_name'])
                    expected_mips=dimensions[0].bit_length() if dimensions and isinstance(dimensions[0],int) and dimensions[0]>0 else None
                    if asset.get("format")!="ASTC_10x10" or asset.get("mipmap_count",0)<=1 or expected_mips and asset.get("mipmap_count")!=expected_mips:
                        failures.append("Unity loaded texture format/mips mismatch: "+page['asset_name'])
            project_version=pathlib.Path(__file__).resolve().parents[2]/"QuestFlight/ProjectSettings/ProjectVersion.txt"
            expected_unity=next(line.split(": ",1)[1] for line in project_version.read_text().splitlines() if line.startswith("m_EditorVersion: "))
            if evidence.get("unity_version")!=expected_unity:failures.append("Unity verification editor version mismatch")
            compiler_inputs={record.get("path"):record.get("sha256") for record in evidence.get("compiler_inputs",[]) if isinstance(record,dict)}
            required_compilers=("QuestFlight/Assets/Editor/FullMapAerialClipmapAssetAuthoring.cs","QuestFlight/Assets/Editor/AuthoringBundleVerifier.cs")
            root=pathlib.Path(__file__).resolve().parents[2]
            for name in required_compilers:
                path=root/name.removeprefix("../")
                if not path.is_file() or compiler_inputs.get(name)!=sha256_file(path):failures.append("stale or missing compiler verification identity: "+name)
        except (OSError, ValueError, TypeError, AttributeError, KeyError) as error:
            failures.append("malformed Unity verification report: "+type(error).__name__)

    return {
        "passed": not failures,
        "preliminary_passed": preliminary_passed,
        "verification_scope": "all binaries and assets opened by pinned Unity Android verifier; not Quest performance",
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
    parser.add_argument("--unity-verification-report", type=pathlib.Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    report = validate(args.page_manifest.resolve(), args.bundle_root.resolve(), args.unity_verification_report)
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
