"""Build deterministic, aircraft-centered aerial clipmap source pages.

Raw ArcGIS responses and generated page images stay outside Git. The script
commits only compact manifests through an explicit --manifest-output path.
It never downloads imagery at product runtime.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import io
import json
import math
import multiprocessing
import os
import pathlib
import random
import shutil
import sys
import time
import urllib.parse
import urllib.request
from dataclasses import dataclass
from typing import Any, Iterable, Mapping

import numpy as np
from PIL import Image
from pyproj import CRS, Transformer
import rasterio
from rasterio.transform import from_bounds
from rasterio.warp import Resampling, reproject


ROOT = pathlib.Path(__file__).resolve().parents[2]
DEFAULT_CONFIG = pathlib.Path(__file__).with_name("config.json")


def canonical_json(value: Any) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_json(path: pathlib.Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: pathlib.Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def local_crs(config: Mapping[str, Any]) -> CRS:
    origin = config["origin"]
    return CRS.from_proj4(
        "+proj=aeqd "
        f"+lat_0={origin['latitude_degrees']} "
        f"+lon_0={origin['longitude_degrees']} "
        "+datum=WGS84 +units=m +no_defs"
    )


def headers(config: Mapping[str, Any]) -> dict[str, str]:
    processing = config["processing"]
    return {
        "User-Agent": str(processing["user_agent"]),
        "Referer": str(processing["referer"]),
    }


def request_bytes(config: Mapping[str, Any], url: str) -> bytes:
    attempts = int(config["processing"]["network_attempts"])
    timeout = int(config["processing"]["network_timeout_seconds"])
    max_delay = float(config["processing"]["maximum_retry_delay_seconds"])
    last: Exception | None = None
    for attempt in range(attempts):
        try:
            request = urllib.request.Request(url, headers=headers(config))
            with urllib.request.urlopen(request, timeout=timeout) as response:
                return response.read()
        except Exception as error:  # network retry boundary
            last = error
            if attempt + 1 == attempts:
                break
            delay = min(max_delay, (2**attempt) + random.random())
            time.sleep(delay)
    raise RuntimeError(f"Request failed after {attempts} attempts: {url}") from last


def request_json(config: Mapping[str, Any], url: str) -> dict[str, Any]:
    return json.loads(request_bytes(config, url).decode("utf-8"))


def audit_source(config: Mapping[str, Any], output_root: pathlib.Path) -> dict[str, Any]:
    source = config["source"]
    item_bytes = request_bytes(config, str(source["item_metadata_url"]))
    service_bytes = request_bytes(config, str(source["service_metadata_url"]))
    policy_bytes = request_bytes(config, str(source["license_policy_url"]))
    item = json.loads(item_bytes)
    service = json.loads(service_bytes)
    failures: list[str] = []
    if item.get("id") != source["arcgis_item_id"]:
        failures.append("ArcGIS item identifier changed")
    if item.get("access") != source["required_access"]:
        failures.append("ArcGIS item is not public")
    if item.get("url") != source["service_url"]:
        failures.append("ArcGIS item service URL changed")
    tile_info = service.get("tileInfo", {})
    spatial_reference = tile_info.get("spatialReference", service.get("spatialReference", {}))
    service_wkid = spatial_reference.get("latestWkid") or spatial_reference.get("wkid")
    if service_wkid != source["required_service_wkid"]:
        failures.append(f"Service WKID changed: {service_wkid}")
    if int(tile_info.get("rows", 0)) != int(source["required_cache_tile_pixels"]) or \
       int(tile_info.get("cols", 0)) != int(source["required_cache_tile_pixels"]):
        failures.append("Service cache tile dimensions changed")
    if str(tile_info.get("format", "")).upper() != str(source["required_cache_format"]).upper():
        failures.append("Service cache format changed")
    if "Map" not in str(service.get("capabilities", "")):
        failures.append("Service no longer exposes reviewed map imagery")
    policy_text = policy_bytes.decode("utf-8", errors="ignore").lower()
    if "cc0 1.0" not in policy_text and "cc zero" not in policy_text:
        failures.append("City policy no longer exposes reviewed CC0 language")

    output_root.mkdir(parents=True, exist_ok=True)
    (output_root / "arcgis_item.json").write_bytes(item_bytes)
    (output_root / "map_service.json").write_bytes(service_bytes)
    (output_root / "city_open_data_policy.html").write_bytes(policy_bytes)
    result = {
        "passed": not failures,
        "failures": failures,
        "item": {
            "id": item.get("id"),
            "title": item.get("title"),
            "access": item.get("access"),
            "modified_epoch_ms": item.get("modified"),
            "response_sha256": sha256_bytes(item_bytes),
        },
        "service": {
            "name": service.get("name"),
            "source_resolution_meters": source["native_pixel_size_meters"],
            "cache_tile_pixels": tile_info.get("rows"),
            "cache_format": tile_info.get("format"),
            "export_tiles_allowed": service.get("exportTilesAllowed"),
            "capabilities": service.get("capabilities"),
            "extent": service.get("extent"),
            "response_sha256": sha256_bytes(service_bytes),
        },
        "license": {
            "name": source["license_name"],
            "spdx": source["license_spdx"],
            "policy_url": source["license_policy_url"],
            "policy_response_sha256": sha256_bytes(policy_bytes),
        },
    }
    write_json(output_root / "source_audit.json", result)
    if failures:
        raise RuntimeError("; ".join(failures))
    return result


@dataclass(frozen=True)
class TileSpec:
    tier: str
    column: int
    row: int
    local_bounds: tuple[float, float, float, float]
    output_pixels: int
    gutter_pixels: int
    meters_per_texel: float
    bundle_name: str

    @property
    def asset_name(self) -> str:
        return f"KBDU_2020_{self.tier}_{self.column:03d}_{self.row:03d}"

    @property
    def source_contract_sha256(self) -> str:
        return sha256_bytes(canonical_json({
            "asset_name": self.asset_name,
            "local_bounds": self.local_bounds,
            "output_pixels": self.output_pixels,
            "gutter_pixels": self.gutter_pixels,
            "meters_per_texel": self.meters_per_texel,
        }))


def tier_specs(config: Mapping[str, Any], tier_name: str) -> list[TileSpec]:
    tier = config["tiers"][tier_name]
    bounds = [float(value) for value in config["local_bounds_meters"]]
    output_pixels = int(tier["output_pixels"])
    gutter = int(tier.get("gutter_pixels", 0))
    meters_per_texel = float(tier["meters_per_texel"])
    inner_pixels = output_pixels - gutter * 2
    if inner_pixels <= 0:
        raise ValueError(f"{tier_name} gutter consumes the output")
    tile_size = inner_pixels * meters_per_texel
    center_x = (bounds[0] + bounds[2]) * 0.5
    center_z = (bounds[1] + bounds[3]) * 0.5
    columns = int(math.ceil((bounds[2] - bounds[0]) / tile_size))
    rows = int(math.ceil((bounds[3] - bounds[1]) / tile_size))
    grid_min_x = center_x - columns * tile_size * 0.5
    grid_min_z = center_z - rows * tile_size * 0.5
    region = int(tier["bundle_region_cells"])
    specs: list[TileSpec] = []
    for row in range(rows):
        for column in range(columns):
            inner_min_x = grid_min_x + column * tile_size
            inner_min_z = grid_min_z + row * tile_size
            gutter_meters = gutter * meters_per_texel
            local_bounds = (
                inner_min_x - gutter_meters,
                inner_min_z - gutter_meters,
                inner_min_x + tile_size + gutter_meters,
                inner_min_z + tile_size + gutter_meters,
            )
            region_x = column // region
            region_z = row // region
            specs.append(
                TileSpec(
                    tier=tier_name,
                    column=column,
                    row=row,
                    local_bounds=local_bounds,
                    output_pixels=output_pixels,
                    gutter_pixels=gutter,
                    meters_per_texel=meters_per_texel,
                    bundle_name=f"qf_aerial_{tier_name}_r{region_x:02d}_{region_z:02d}",
                )
            )
    return specs


def global_spec(config: Mapping[str, Any]) -> TileSpec:
    tier = config["tiers"]["global"]
    bounds = tuple(float(value) for value in config["local_bounds_meters"])
    return TileSpec(
        tier="global",
        column=0,
        row=0,
        local_bounds=bounds,
        output_pixels=int(tier["output_pixels"]),
        gutter_pixels=0,
        meters_per_texel=float(tier["meters_per_texel"]),
        bundle_name="qf_aerial_global",
    )


def transformed_bounds(
    transformer: Transformer, local_bounds: tuple[float, float, float, float]
) -> tuple[float, float, float, float]:
    minimum_x, minimum_z, maximum_x, maximum_z = local_bounds
    points: list[tuple[float, float]] = []
    segments = 12
    for index in range(segments + 1):
        t = index / segments
        x = minimum_x + (maximum_x - minimum_x) * t
        z = minimum_z + (maximum_z - minimum_z) * t
        points.extend(
            (
                transformer.transform(x, minimum_z),
                transformer.transform(x, maximum_z),
                transformer.transform(minimum_x, z),
                transformer.transform(maximum_x, z),
            )
        )
    return (
        min(point[0] for point in points),
        min(point[1] for point in points),
        max(point[0] for point in points),
        max(point[1] for point in points),
    )


def export_source(
    config: Mapping[str, Any],
    tile: TileSpec,
    raw_root: pathlib.Path,
    local_to_service: Transformer,
) -> tuple[pathlib.Path, dict[str, Any]]:
    raw_root.mkdir(parents=True, exist_ok=True)
    raw_path = raw_root / f"{tile.asset_name}.jpg"
    metadata_path = raw_root / f"{tile.asset_name}.json"
    if raw_path.exists() and metadata_path.exists():
        cached = load_json(metadata_path)
        if cached.get("tile_source_contract_sha256") == tile.source_contract_sha256 and \
           image_bytes_are_complete(raw_path.read_bytes()):
            return raw_path, cached
        raw_path.unlink(missing_ok=True)
        metadata_path.unlink(missing_ok=True)

    bbox = transformed_bounds(local_to_service, tile.local_bounds)
    request_pixels = max(tile.output_pixels, int(config["processing"]["request_pixels"]))
    parameters = {
        "bbox": ",".join(f"{value:.6f}" for value in bbox),
        "bboxSR": "3857",
        "imageSR": "3857",
        "size": f"{request_pixels},{request_pixels}",
        "dpi": "96",
        "format": "jpg",
        "transparent": "false",
        "f": "json",
    }
    export_url = str(config["source"]["export_url"]) + "?" + urllib.parse.urlencode(parameters)
    metadata = request_json(config, export_url)
    if "href" not in metadata:
        raise RuntimeError(f"ArcGIS export did not return an image for {tile.asset_name}: {metadata}")
    image_bytes = request_bytes(config, str(metadata["href"]))
    if not image_bytes_are_complete(image_bytes):
        error = OSError("image response did not fully decode")
        raise RuntimeError(f"Invalid image response for {tile.asset_name}") from error
    raw_path.write_bytes(image_bytes)
    metadata["request_url"] = export_url
    metadata["image_sha256"] = sha256_bytes(image_bytes)
    metadata["tile_source_contract_sha256"] = tile.source_contract_sha256
    write_json(metadata_path, metadata)
    return raw_path, metadata


def image_bytes_are_complete(image_bytes: bytes) -> bool:
    try:
        with Image.open(io.BytesIO(image_bytes)) as image:
            image.load()
            image.convert("RGB").getpixel((image.width - 1, image.height - 1))
        return True
    except Exception:
        return False


def reproject_tile(
    config: Mapping[str, Any],
    tile: TileSpec,
    raw_path: pathlib.Path,
    export_metadata: Mapping[str, Any],
    output_path: pathlib.Path,
) -> dict[str, Any]:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with Image.open(raw_path) as image:
        source = np.asarray(image.convert("RGB"), dtype=np.uint8).transpose(2, 0, 1)
    extent = export_metadata["extent"]
    source_transform = from_bounds(
        float(extent["xmin"]),
        float(extent["ymin"]),
        float(extent["xmax"]),
        float(extent["ymax"]),
        source.shape[2],
        source.shape[1],
    )
    destination = np.zeros((3, tile.output_pixels, tile.output_pixels), dtype=np.uint8)
    destination_transform = from_bounds(
        *tile.local_bounds,
        tile.output_pixels,
        tile.output_pixels,
    )
    resampling = getattr(Resampling, str(config["processing"]["resampling"]))
    destination_valid = np.zeros((tile.output_pixels, tile.output_pixels), dtype=np.uint8)
    source_valid = np.full((source.shape[1], source.shape[2]), 255, dtype=np.uint8)
    reproject(
        source=source_valid,
        destination=destination_valid,
        src_transform=source_transform,
        src_crs="EPSG:3857",
        dst_transform=destination_transform,
        dst_crs=local_crs(config),
        resampling=Resampling.nearest,
        num_threads=1,
    )
    for channel in range(3):
        reproject(
            source=source[channel],
            destination=destination[channel],
            src_transform=source_transform,
            src_crs="EPSG:3857",
            dst_transform=destination_transform,
            dst_crs=local_crs(config),
            resampling=resampling,
            num_threads=1,
        )
    rgb = destination.transpose(1, 2, 0)
    Image.fromarray(rgb).save(
        output_path,
        format="JPEG",
        quality=int(config["processing"]["jpeg_quality"]),
        subsampling=0,
        optimize=False,
        progressive=False,
    )
    valid = destination_valid > 0
    return {
        "asset_name": tile.asset_name,
        "tier": tile.tier,
        "column": tile.column,
        "row": tile.row,
        "local_bounds_meters": list(tile.local_bounds),
        "output_pixels": [tile.output_pixels, tile.output_pixels],
        "gutter_pixels": tile.gutter_pixels,
        "meters_per_texel": tile.meters_per_texel,
        "bundle_name": tile.bundle_name,
        "path": output_path.as_posix(),
        "bytes": output_path.stat().st_size,
        "sha256": sha256_file(output_path),
        "valid_pixel_fraction": float(valid.mean()),
        "channel_mean": [float(rgb[:, :, channel].mean()) for channel in range(3)],
        "channel_standard_deviation": [float(rgb[:, :, channel].std()) for channel in range(3)],
        "source_request_sha256": sha256_bytes(canonical_json(export_metadata)),
    }


def build_one(
    config: Mapping[str, Any],
    tile: TileSpec,
    raw_root: pathlib.Path,
    output_root: pathlib.Path,
    local_to_service: Transformer,
) -> dict[str, Any]:
    output_path = output_root / tile.tier / f"{tile.asset_name}.jpg"
    record_path = output_path.with_suffix(".record.json")
    if output_path.exists() and record_path.exists():
        record = load_json(record_path)
        if record_matches_tile(record, tile, output_path):
            return record
    raw_path, metadata = export_source(config, tile, raw_root / tile.tier, local_to_service)
    record = reproject_tile(config, tile, raw_path, metadata, output_path)
    write_json(record_path, record)
    return record


def record_matches_tile(
    record: Mapping[str, Any], tile: TileSpec, output_path: pathlib.Path
) -> bool:
    return (
        record.get("sha256") == sha256_file(output_path)
        and record.get("asset_name") == tile.asset_name
        and record.get("local_bounds_meters") == list(tile.local_bounds)
        and record.get("output_pixels") == [tile.output_pixels, tile.output_pixels]
        and record.get("gutter_pixels") == tile.gutter_pixels
        and record.get("meters_per_texel") == tile.meters_per_texel
        and record.get("bundle_name") == tile.bundle_name
    )


def build_tier(
    config: Mapping[str, Any],
    tier_name: str,
    raw_root: pathlib.Path,
    output_root: pathlib.Path,
    selected: set[tuple[int, int]] | None = None,
) -> list[dict[str, Any]]:
    specs = tier_specs(config, tier_name)
    if selected is not None:
        specs = [spec for spec in specs if (spec.column, spec.row) in selected]
    transformer = Transformer.from_crs(local_crs(config), 3857, always_xy=True)
    workers = int(config["processing"]["network_workers"])
    records: list[dict[str, Any]] = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as executor:
        futures = {
            executor.submit(build_one, config, spec, raw_root, output_root, transformer): spec
            for spec in specs
        }
        for completed, future in enumerate(concurrent.futures.as_completed(futures), 1):
            spec = futures[future]
            record = future.result()
            records.append(record)
            print(
                f"[{tier_name}] {completed}/{len(specs)} "
                f"{spec.asset_name} valid={record['valid_pixel_fraction']:.4f}",
                flush=True,
            )
    return sorted(records, key=lambda item: (item["row"], item["column"]))


def _process_downloaded_tile(arguments: tuple[dict[str, Any], TileSpec, str, dict[str, Any], str]) -> dict[str, Any]:
    config, tile, raw_path, metadata, output_path = arguments
    record = reproject_tile(
        config,
        tile,
        pathlib.Path(raw_path),
        metadata,
        pathlib.Path(output_path),
    )
    write_json(pathlib.Path(output_path).with_suffix(".record.json"), record)
    return record


def build_tier_pipelined(
    config: Mapping[str, Any],
    tier_name: str,
    raw_root: pathlib.Path,
    output_root: pathlib.Path,
    selected: set[tuple[int, int]] | None = None,
) -> list[dict[str, Any]]:
    specs = tier_specs(config, tier_name)
    if selected is not None:
        specs = [spec for spec in specs if (spec.column, spec.row) in selected]
    transformer = Transformer.from_crs(local_crs(config), 3857, always_xy=True)
    records: list[dict[str, Any]] = []
    pending: list[TileSpec] = []
    for spec in specs:
        output_path = output_root / spec.tier / f"{spec.asset_name}.jpg"
        record_path = output_path.with_suffix(".record.json")
        if output_path.exists() and record_path.exists():
            record = load_json(record_path)
            if record_matches_tile(record, spec, output_path):
                records.append(record)
                continue
        pending.append(spec)

    workers = int(config["processing"]["network_workers"])
    process_workers = max(1, min(workers, max(1, (os.cpu_count() or 2) // 2)))
    process_pool = concurrent.futures.ProcessPoolExecutor(
        max_workers=process_workers,
        mp_context=multiprocessing.get_context("spawn"),
    )
    process_futures: dict[concurrent.futures.Future[dict[str, Any]], TileSpec] = {}
    try:
        with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as network_pool:
            downloads = {
                network_pool.submit(export_source, config, spec, raw_root / spec.tier, transformer): spec
                for spec in pending
            }
            for downloaded in concurrent.futures.as_completed(downloads):
                spec = downloads[downloaded]
                raw_path, metadata = downloaded.result()
                output_path = output_root / spec.tier / f"{spec.asset_name}.jpg"
                future = process_pool.submit(
                    _process_downloaded_tile,
                    (dict(config), spec, str(raw_path), metadata, str(output_path)),
                )
                process_futures[future] = spec

        completed = len(records)
        for future in concurrent.futures.as_completed(process_futures):
            spec = process_futures[future]
            record = future.result()
            records.append(record)
            completed += 1
            print(
                f"[{tier_name}] {completed}/{len(specs)} "
                f"{spec.asset_name} valid={record['valid_pixel_fraction']:.4f}",
                flush=True,
            )
    finally:
        process_pool.shutdown(wait=True, cancel_futures=True)
    return sorted(records, key=lambda item: (item["row"], item["column"]))


def sample_cells(config: Mapping[str, Any], tier_name: str) -> set[tuple[int, int]]:
    specs = tier_specs(config, tier_name)
    center = min(
        specs,
        key=lambda tile: abs((tile.local_bounds[0] + tile.local_bounds[2]) * 0.5)
        + abs((tile.local_bounds[1] + tile.local_bounds[3]) * 0.5),
    )
    return {
        (center.column + dx, center.row + dz)
        for dz in range(-1, 2)
        for dx in range(-1, 2)
        if any(tile.column == center.column + dx and tile.row == center.row + dz for tile in specs)
    }


def prune_stale_outputs(
    config: Mapping[str, Any], output_root: pathlib.Path, tier_names: Iterable[str]
) -> None:
    for tier_name in tier_names:
        tier_root = output_root / tier_name
        if not tier_root.exists():
            continue
        expected = (
            {global_spec(config).asset_name}
            if tier_name == "global"
            else {spec.asset_name for spec in tier_specs(config, tier_name)}
        )
        for path in tier_root.iterdir():
            name = path.name.split(".record.json")[0].split(".jpg")[0]
            if path.is_file() and (name.startswith("KBDU_2024_") or name.startswith("KBDU_2020_")) \
               and name not in expected:
                path.unlink()


def build_manifest(
    config: Mapping[str, Any],
    audit: Mapping[str, Any],
    records: Iterable[Mapping[str, Any]],
    mode: str,
) -> dict[str, Any]:
    records = list(records)
    tiers: dict[str, Any] = {}
    for name in ("high", "medium"):
        specs = tier_specs(config, name)
        tier_config = config["tiers"][name]
        columns = max(spec.column for spec in specs) + 1
        rows = max(spec.row for spec in specs) + 1
        inner_pixels = int(tier_config["output_pixels"]) - 2 * int(tier_config["gutter_pixels"])
        tiers[name] = {
            **tier_config,
            "columns": columns,
            "rows": rows,
            "tile_inner_size_meters": inner_pixels * float(tier_config["meters_per_texel"]),
            "grid_min_x": min(spec.local_bounds[0] + spec.gutter_pixels * spec.meters_per_texel for spec in specs),
            "grid_min_z": min(spec.local_bounds[1] + spec.gutter_pixels * spec.meters_per_texel for spec in specs),
        }
    tiers["global"] = dict(config["tiers"]["global"])
    return {
        "schema_version": 1,
        "mode": mode,
        "label": config["label"],
        "coordinate_frame": config["coordinate_frame"],
        "origin": config["origin"],
        "local_bounds_meters": config["local_bounds_meters"],
        "source": audit,
        "processing": config["processing"],
        "tiers": tiers,
        "runtime": config["runtime"],
        "pages": records,
        "page_count": len(records),
        "page_bytes": sum(int(record["bytes"]) for record in records),
        "config_sha256": sha256_bytes(canonical_json(config)),
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=pathlib.Path, default=DEFAULT_CONFIG)
    parser.add_argument("--mode", choices=("audit", "sample", "full", "global"), required=True)
    parser.add_argument("--raw-root", type=pathlib.Path, required=True)
    parser.add_argument("--output-root", type=pathlib.Path, required=True)
    parser.add_argument("--manifest-output", type=pathlib.Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    config = load_json(args.config)
    audit = audit_source(config, args.raw_root / "source_audit")
    records: list[dict[str, Any]] = []
    if args.mode not in ("audit", "global"):
        prune_stale_outputs(config, args.output_root, ("high", "medium", "global"))
        for tier_name in ("high", "medium"):
            selected = sample_cells(config, tier_name) if args.mode == "sample" else None
            records.extend(
                build_tier_pipelined(config, tier_name, args.raw_root, args.output_root, selected)
            )
    if args.mode in ("full", "global"):
        prune_stale_outputs(config, args.output_root, ("global",))
        transformer = Transformer.from_crs(local_crs(config), 3857, always_xy=True)
        records.append(build_one(
            config, global_spec(config), args.raw_root, args.output_root, transformer
        ))
    if args.mode == "global" and args.manifest_output.exists():
        previous = load_json(args.manifest_output)
        records = [
            record for record in previous.get("pages", []) if record.get("tier") != "global"
        ] + records
    manifest = build_manifest(config, audit, records, args.mode)
    write_json(args.manifest_output, manifest)
    print(
        f"PASS {args.mode}: {manifest['page_count']} pages, "
        f"{manifest['page_bytes']} bytes -> {args.manifest_output}",
        flush=True,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
