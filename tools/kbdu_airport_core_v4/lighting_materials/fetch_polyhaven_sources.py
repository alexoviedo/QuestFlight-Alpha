#!/usr/bin/env python3
"""Fetch only configured Poly Haven CC0 maps into an external raw directory."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import pathlib
import urllib.request
from typing import Any


TOOL_ROOT = pathlib.Path(__file__).resolve().parent
CONFIG = TOOL_ROOT / "config.json"
MAP_KEYS = (("Diffuse", "diffuse"), ("nor_gl", "normalOpenGl"), ("arm", "arm"))


def load_json(path: pathlib.Path) -> dict[str, Any]:
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(data, dict):
        raise ValueError(f"Expected an object: {path}")
    return data


def digest(path: pathlib.Path, algorithm: str) -> str:
    value = hashlib.new(algorithm)
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def fetch_json(url: str) -> dict[str, Any]:
    request = urllib.request.Request(url, headers={"User-Agent": "T2-QuestFlight-AirportCoreV4/1"})
    with urllib.request.urlopen(request, timeout=45) as response:
        data = json.loads(response.read().decode("utf-8"))
    if not isinstance(data, dict):
        raise ValueError(f"Expected object from {url}")
    return data


def pick_file(files: dict[str, Any], key: str, resolution: str) -> dict[str, Any]:
    value = files.get(key, {}).get(resolution, {})
    candidate = value.get("jpg") or value.get("png")
    if not isinstance(candidate, dict) or not candidate.get("url") or not candidate.get("md5"):
        raise ValueError(f"Poly Haven file missing: {key}/{resolution}/jpg-or-png")
    return candidate


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--raw-root", type=pathlib.Path, required=True)
    parser.add_argument("--config", type=pathlib.Path, default=CONFIG)
    args = parser.parse_args()
    config = load_json(args.config.resolve())
    raw_root = args.raw_root.resolve()
    raw_root.mkdir(parents=True, exist_ok=True)
    receipts: list[dict[str, Any]] = []
    api_template = config["sourcePolicy"]["polyHavenApi"]

    for asset_id, source in sorted(config["materials"]["sources"].items()):
        resolution = str(source["resolution"])
        api_url = api_template.format(assetId=asset_id)
        files = fetch_json(api_url)
        api_snapshot = raw_root / f"{asset_id}.files.json"
        api_snapshot.write_text(json.dumps(files, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        for api_key, semantic in MAP_KEYS:
            selected = pick_file(files, api_key, resolution)
            suffix = pathlib.PurePosixPath(str(selected["url"])).suffix or ".jpg"
            destination = raw_root / f"{asset_id}_{semantic}_{resolution}{suffix}"
            if not destination.is_file() or digest(destination, "md5") != str(selected["md5"]).lower():
                request = urllib.request.Request(str(selected["url"]), headers={"User-Agent": "T2-QuestFlight-AirportCoreV4/1"})
                with urllib.request.urlopen(request, timeout=90) as response, destination.open("wb") as output:
                    while True:
                        block = response.read(1024 * 1024)
                        if not block:
                            break
                        output.write(block)
            verified_md5 = digest(destination, "md5")
            if verified_md5 != str(selected["md5"]).lower():
                raise ValueError(f"MD5 mismatch for {destination}: {verified_md5} != {selected['md5']}")
            receipts.append({
                "assetId": asset_id,
                "semantic": semantic,
                "resolution": resolution,
                "sourceUrl": selected["url"],
                "officialMd5": selected["md5"],
                "bytes": destination.stat().st_size,
                "sha256": digest(destination, "sha256"),
                "externalFile": destination.name,
            })

    receipt = {
        "schemaVersion": 1,
        "retrievedUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "license": "CC0-1.0",
        "licenseUrl": config["sourcePolicy"]["polyHavenLicenseUrl"],
        "rawPolicy": "External artifact only; raw maps and API snapshots must not be committed.",
        "files": receipts,
    }
    receipt_path = raw_root / "polyhaven_raw_receipt.json"
    receipt_path.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps({"receipt": str(receipt_path), "fileCount": len(receipts)}, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
