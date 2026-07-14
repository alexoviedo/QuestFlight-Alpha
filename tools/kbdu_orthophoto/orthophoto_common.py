"""Deterministic helpers shared by the bounded KBDU orthophoto pipeline."""

from __future__ import annotations

import hashlib
import json
import math
import os
import tempfile
from pathlib import Path
from typing import Any, Iterable


SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[1]
DEFAULT_CONFIG_PATH = SCRIPT_DIR / "config.json"
DEFAULT_TEXTURE_DIR = REPO_ROOT / "QuestFlight" / "Assets" / "Production" / "Environment" / "Textures"
DEFAULT_MANIFEST_PATH = SCRIPT_DIR / "kbdu_orthophoto_manifest.json"

WGS84_A = 6378137.0
WGS84_F = 1.0 / 298.257223563
WGS84_E2 = WGS84_F * (2.0 - WGS84_F)


def load_config(path: Path | str = DEFAULT_CONFIG_PATH) -> dict[str, Any]:
    with Path(path).open("r", encoding="utf-8") as handle:
        config = json.load(handle)
    if config.get("schema_version") != 1:
        raise ValueError(f"Unsupported orthophoto config schema: {config.get('schema_version')!r}")
    return config


def canonical_json_bytes(value: Any) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n").encode("utf-8")


def atomic_write_bytes(path: Path | str, payload: bytes) -> None:
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(prefix=target.name + ".", suffix=".tmp", dir=target.parent)
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(payload)
        os.replace(temporary, target)
    except Exception:
        try:
            os.unlink(temporary)
        except FileNotFoundError:
            pass
        raise


def atomic_write_json(path: Path | str, value: Any) -> None:
    atomic_write_bytes(path, canonical_json_bytes(value))


def sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def sha256_file(path: Path | str) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def file_record(path: Path, *, relative_to: Path | None = None) -> dict[str, Any]:
    display_path = path.relative_to(relative_to).as_posix() if relative_to else path.as_posix()
    return {"path": display_path, "bytes": path.stat().st_size, "sha256": sha256_file(path)}


def require_external_directory(path: Path | str) -> Path:
    candidate = Path(path).expanduser().resolve()
    repository = REPO_ROOT.resolve()
    try:
        candidate.relative_to(repository)
    except ValueError:
        pass
    else:
        raise ValueError(f"Raw/work directory must stay outside the repository: {candidate}")
    candidate.mkdir(parents=True, exist_ok=True)
    return candidate


def geodetic_to_ecef(latitude_degrees: float, longitude_degrees: float, height_meters: float) -> tuple[float, float, float]:
    latitude = math.radians(latitude_degrees)
    longitude = math.radians(longitude_degrees)
    sin_latitude = math.sin(latitude)
    cos_latitude = math.cos(latitude)
    radius = WGS84_A / math.sqrt(1.0 - WGS84_E2 * sin_latitude * sin_latitude)
    return (
        (radius + height_meters) * cos_latitude * math.cos(longitude),
        (radius + height_meters) * cos_latitude * math.sin(longitude),
        (radius * (1.0 - WGS84_E2) + height_meters) * sin_latitude,
    )


def ecef_to_geodetic(x: float, y: float, z: float) -> tuple[float, float, float]:
    longitude = math.atan2(y, x)
    horizontal = math.hypot(x, y)
    latitude = math.atan2(z, horizontal * (1.0 - WGS84_E2))
    height = 0.0
    for _ in range(12):
        sin_latitude = math.sin(latitude)
        radius = WGS84_A / math.sqrt(1.0 - WGS84_E2 * sin_latitude * sin_latitude)
        height = horizontal / max(math.cos(latitude), 1e-14) - radius
        next_latitude = math.atan2(z, horizontal * (1.0 - WGS84_E2 * radius / (radius + height)))
        if abs(next_latitude - latitude) < 1e-14:
            latitude = next_latitude
            break
        latitude = next_latitude
    return math.degrees(latitude), math.degrees(longitude), height


def enu_to_geodetic(
    east_meters: float,
    north_meters: float,
    up_meters: float,
    origin: dict[str, float],
) -> tuple[float, float, float]:
    latitude = math.radians(origin["latitude_degrees"])
    longitude = math.radians(origin["longitude_degrees"])
    ox, oy, oz = geodetic_to_ecef(
        origin["latitude_degrees"], origin["longitude_degrees"], origin["elevation_msl_meters"]
    )
    dx = (
        -math.sin(longitude) * east_meters
        - math.sin(latitude) * math.cos(longitude) * north_meters
        + math.cos(latitude) * math.cos(longitude) * up_meters
    )
    dy = (
        math.cos(longitude) * east_meters
        - math.sin(latitude) * math.sin(longitude) * north_meters
        + math.cos(latitude) * math.sin(longitude) * up_meters
    )
    dz = math.cos(latitude) * north_meters + math.sin(latitude) * up_meters
    return ecef_to_geodetic(ox + dx, oy + dy, oz + dz)


def zone_bbox(config: dict[str, Any], half_extent_meters: float) -> tuple[float, float, float, float]:
    corners = [
        enu_to_geodetic(east, north, 0.0, config["origin"])
        for east in (-half_extent_meters, half_extent_meters)
        for north in (-half_extent_meters, half_extent_meters)
    ]
    latitudes = [corner[0] for corner in corners]
    longitudes = [corner[1] for corner in corners]
    return min(longitudes), min(latitudes), max(longitudes), max(latitudes)


def iter_zone_tiles(config: dict[str, Any], zone: dict[str, Any]) -> Iterable[dict[str, Any]]:
    west, south, east, north = zone_bbox(config, float(zone["half_extent_meters"]))
    grid = int(zone["tile_grid"])
    atlas_size = int(zone["atlas_size_pixels"])
    if atlas_size % grid:
        raise ValueError(f"Zone {zone['id']} atlas size must be divisible by tile grid")
    tile_size = atlas_size // grid
    longitude_step = (east - west) / grid
    latitude_step = (north - south) / grid
    # Row zero is north so raw tiles paste directly into top-origin image coordinates.
    for row in range(grid):
        tile_north = north - row * latitude_step
        tile_south = tile_north - latitude_step
        for column in range(grid):
            tile_west = west + column * longitude_step
            tile_east = tile_west + longitude_step
            yield {
                "zone_id": zone["id"],
                "row": row,
                "column": column,
                "size_pixels": tile_size,
                "bbox_wgs84": [tile_west, tile_south, tile_east, tile_north],
                "file_name": f"{zone['id']}_r{row}_c{column}_{tile_size}.png",
            }


def astc_mip_chain_bytes(width: int, height: int, block_width: int = 6, block_height: int = 6) -> int:
    total = 0
    while True:
        total += ((width + block_width - 1) // block_width) * ((height + block_height - 1) // block_height) * 16
        if width == 1 and height == 1:
            return total
        width = max(1, width // 2)
        height = max(1, height // 2)


def image_channel_stats(image: Any) -> list[dict[str, float]]:
    # A deterministic 512 px thumbnail bounds memory and makes statistics independent of host RAM.
    from PIL import Image

    sample = image.convert("RGB")
    sample.thumbnail((512, 512), Image.Resampling.BOX)
    count = sample.width * sample.height
    sums = [0.0, 0.0, 0.0]
    sums_squared = [0.0, 0.0, 0.0]
    for pixel in sample.getdata():
        for channel, value in enumerate(pixel):
            sums[channel] += value
            sums_squared[channel] += value * value
    result: list[dict[str, float]] = []
    for channel in range(3):
        mean = sums[channel] / count
        variance = max(0.0, sums_squared[channel] / count - mean * mean)
        result.append({"mean": round(mean, 6), "standard_deviation": round(math.sqrt(variance), 6)})
    return result
