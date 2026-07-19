"""Deterministic helpers for the bounded KBDU ground-imagery recovery pipeline."""

from __future__ import annotations

import hashlib
import json
import math
import os
import tempfile
from pathlib import Path
from typing import Any


SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[2]
DEFAULT_CONFIG = SCRIPT_DIR / "config.json"
DEFAULT_OUTPUT = (
    REPO_ROOT
    / "QuestFlight"
    / "Assets"
    / "Production"
    / "Environment"
    / "AerialSceneryRecoveryV2"
    / "Ground"
)

WGS84_A = 6378137.0
WGS84_F = 1.0 / 298.257223563
WGS84_E2 = WGS84_F * (2.0 - WGS84_F)


def canonical_json_bytes(value: Any) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n").encode("utf-8")


def atomic_write_bytes(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(payload)
        os.replace(temporary, path)
    except Exception:
        try:
            os.unlink(temporary)
        except FileNotFoundError:
            pass
        raise


def atomic_write_json(path: Path, value: Any) -> None:
    atomic_write_bytes(path, canonical_json_bytes(value))


def load_config(path: Path = DEFAULT_CONFIG) -> dict[str, Any]:
    config = json.loads(path.read_text(encoding="utf-8"))
    if config.get("schema_version") != 1:
        raise ValueError(f"Unsupported imagery config schema: {config.get('schema_version')!r}")
    return config


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def require_external(path: Path) -> Path:
    candidate = path.expanduser().resolve()
    try:
        candidate.relative_to(REPO_ROOT.resolve())
    except ValueError:
        pass
    else:
        raise ValueError(f"Raw/work data must remain outside Git: {candidate}")
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


def enu_to_geodetic(east_meters: float, north_meters: float, origin: dict[str, float]) -> tuple[float, float]:
    latitude = math.radians(origin["latitude_degrees"])
    longitude = math.radians(origin["longitude_degrees"])
    ox, oy, oz = geodetic_to_ecef(
        origin["latitude_degrees"], origin["longitude_degrees"], origin["elevation_msl_meters"]
    )
    dx = -math.sin(longitude) * east_meters - math.sin(latitude) * math.cos(longitude) * north_meters
    dy = math.cos(longitude) * east_meters - math.sin(latitude) * math.sin(longitude) * north_meters
    dz = math.cos(latitude) * north_meters
    result_latitude, result_longitude, _ = ecef_to_geodetic(ox + dx, oy + dy, oz + dz)
    return result_latitude, result_longitude


def local_bounds_to_wgs84(bounds: list[float], origin: dict[str, float]) -> list[float]:
    min_x, min_z, max_x, max_z = map(float, bounds)
    corners = [
        enu_to_geodetic(x, z, origin)
        for x in (min_x, max_x)
        for z in (min_z, max_z)
    ]
    latitudes = [corner[0] for corner in corners]
    longitudes = [corner[1] for corner in corners]
    return [min(longitudes), min(latitudes), max(longitudes), max(latitudes)]


def astc_mip_chain_bytes(width: int, height: int, block_width: int = 6, block_height: int = 6) -> int:
    total = 0
    while True:
        total += ((width + block_width - 1) // block_width) * ((height + block_height - 1) // block_height) * 16
        if width == 1 and height == 1:
            return total
        width = max(1, width // 2)
        height = max(1, height // 2)


def unity_guid(path: str) -> str:
    return hashlib.sha256(("QuestFlight:" + path.replace("\\", "/")).encode("utf-8")).hexdigest()[:32]
