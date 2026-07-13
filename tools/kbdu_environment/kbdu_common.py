"""Shared deterministic geometry and file helpers for the KBDU data pipeline."""

from __future__ import annotations

import base64
import hashlib
import json
import math
import os
import struct
import tempfile
from pathlib import Path
from typing import Any, Iterable, Iterator, Sequence


SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_CONFIG_PATH = SCRIPT_DIR / "config.json"

WGS84_A = 6378137.0
WGS84_F = 1.0 / 298.257223563
WGS84_E2 = WGS84_F * (2.0 - WGS84_F)


def load_config(path: Path | str = DEFAULT_CONFIG_PATH) -> dict[str, Any]:
    with Path(path).open("r", encoding="utf-8") as handle:
        config = json.load(handle)
    if config.get("schema_version") != 1:
        raise ValueError(f"Unsupported KBDU config schema: {config.get('schema_version')!r}")
    return config


def repo_root() -> Path:
    return SCRIPT_DIR.parents[1]


def default_raw_parent() -> Path:
    root = repo_root()
    return root.with_name(root.name + "-setup-artifacts") / "kbdu_environment_pipeline" / "raw"


def canonical_json_bytes(value: Any, *, pretty: bool = True) -> bytes:
    if pretty:
        text = json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n"
    else:
        text = json.dumps(value, separators=(",", ":"), sort_keys=True, ensure_ascii=False)
    return text.encode("utf-8")


def atomic_write_bytes(path: Path | str, payload: bytes) -> None:
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    fd, temp_name = tempfile.mkstemp(prefix=target.name + ".", suffix=".tmp", dir=target.parent)
    try:
        with os.fdopen(fd, "wb") as handle:
            handle.write(payload)
        os.replace(temp_name, target)
    except Exception:
        try:
            os.unlink(temp_name)
        except FileNotFoundError:
            pass
        raise


def atomic_write_json(path: Path | str, value: Any, *, pretty: bool = True) -> None:
    atomic_write_bytes(path, canonical_json_bytes(value, pretty=pretty))


def sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def sha256_file(path: Path | str) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def file_record(path: Path, *, relative_to: Path | None = None) -> dict[str, Any]:
    display = path.relative_to(relative_to).as_posix() if relative_to else path.as_posix()
    return {"path": display, "bytes": path.stat().st_size, "sha256": sha256_file(path)}


def geodetic_to_ecef(latitude_degrees: float, longitude_degrees: float, height_meters: float) -> tuple[float, float, float]:
    lat = math.radians(latitude_degrees)
    lon = math.radians(longitude_degrees)
    sin_lat = math.sin(lat)
    cos_lat = math.cos(lat)
    radius = WGS84_A / math.sqrt(1.0 - WGS84_E2 * sin_lat * sin_lat)
    x = (radius + height_meters) * cos_lat * math.cos(lon)
    y = (radius + height_meters) * cos_lat * math.sin(lon)
    z = (radius * (1.0 - WGS84_E2) + height_meters) * sin_lat
    return x, y, z


def ecef_to_geodetic(x: float, y: float, z: float) -> tuple[float, float, float]:
    longitude = math.atan2(y, x)
    horizontal = math.hypot(x, y)
    latitude = math.atan2(z, horizontal * (1.0 - WGS84_E2))
    height = 0.0
    for _ in range(12):
        sin_lat = math.sin(latitude)
        radius = WGS84_A / math.sqrt(1.0 - WGS84_E2 * sin_lat * sin_lat)
        cos_lat = math.cos(latitude)
        if abs(cos_lat) < 1e-14:
            height = abs(z) - radius * (1.0 - WGS84_E2)
        else:
            height = horizontal / cos_lat - radius
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
    origin_latitude_degrees: float,
    origin_longitude_degrees: float,
    origin_height_meters: float,
) -> tuple[float, float, float]:
    lat = math.radians(origin_latitude_degrees)
    lon = math.radians(origin_longitude_degrees)
    ox, oy, oz = geodetic_to_ecef(origin_latitude_degrees, origin_longitude_degrees, origin_height_meters)
    dx = -math.sin(lon) * east_meters - math.sin(lat) * math.cos(lon) * north_meters + math.cos(lat) * math.cos(lon) * up_meters
    dy = math.cos(lon) * east_meters - math.sin(lat) * math.sin(lon) * north_meters + math.cos(lat) * math.sin(lon) * up_meters
    dz = math.cos(lat) * north_meters + math.sin(lat) * up_meters
    return ecef_to_geodetic(ox + dx, oy + dy, oz + dz)


def geodetic_to_enu(
    latitude_degrees: float,
    longitude_degrees: float,
    height_meters: float,
    origin_latitude_degrees: float,
    origin_longitude_degrees: float,
    origin_height_meters: float,
) -> tuple[float, float, float]:
    lat = math.radians(origin_latitude_degrees)
    lon = math.radians(origin_longitude_degrees)
    x, y, z = geodetic_to_ecef(latitude_degrees, longitude_degrees, height_meters)
    ox, oy, oz = geodetic_to_ecef(origin_latitude_degrees, origin_longitude_degrees, origin_height_meters)
    dx, dy, dz = x - ox, y - oy, z - oz
    east = -math.sin(lon) * dx + math.cos(lon) * dy
    north = -math.sin(lat) * math.cos(lon) * dx - math.sin(lat) * math.sin(lon) * dy + math.cos(lat) * dz
    up = math.cos(lat) * math.cos(lon) * dx + math.cos(lat) * math.sin(lon) * dy + math.sin(lat) * dz
    return east, north, up


def bbox_for_half_extent(config: dict[str, Any], half_extent_meters: float) -> tuple[float, float, float, float]:
    origin = config["airport"]["origin"]
    corners = [
        enu_to_geodetic(east, north, 0.0, origin["latitude_degrees"], origin["longitude_degrees"], origin["elevation_msl_meters"])
        for east in (-half_extent_meters, half_extent_meters)
        for north in (-half_extent_meters, half_extent_meters)
    ]
    latitudes = [item[0] for item in corners]
    longitudes = [item[1] for item in corners]
    return min(latitudes), min(longitudes), max(latitudes), max(longitudes)


def iter_layer_grid(layer: dict[str, Any]) -> Iterator[tuple[int, int, float, float]]:
    if layer["kind"] == "patch":
        min_x = float(layer["min_x_meters"])
        max_x = float(layer["max_x_meters"])
        min_z = float(layer["min_z_meters"])
        max_z = float(layer["max_z_meters"])
    else:
        radius = float(layer["outer_radius_meters"])
        min_x = min_z = -radius
        max_x = max_z = radius
    spacing = float(layer["spacing_meters"])
    width = int(round((max_x - min_x) / spacing)) + 1
    height = int(round((max_z - min_z) / spacing)) + 1
    if not math.isclose(min_x + (width - 1) * spacing, max_x, abs_tol=1e-6):
        raise ValueError(f"Layer {layer['id']} x extent is not divisible by spacing")
    if not math.isclose(min_z + (height - 1) * spacing, max_z, abs_tol=1e-6):
        raise ValueError(f"Layer {layer['id']} z extent is not divisible by spacing")
    for row in range(height):
        z = min_z + row * spacing
        for column in range(width):
            x = min_x + column * spacing
            yield row, column, x, z


def layer_dimensions(layer: dict[str, Any]) -> tuple[int, int, float, float, float, float]:
    if layer["kind"] == "patch":
        min_x, max_x = float(layer["min_x_meters"]), float(layer["max_x_meters"])
        min_z, max_z = float(layer["min_z_meters"]), float(layer["max_z_meters"])
    else:
        radius = float(layer["outer_radius_meters"])
        min_x = min_z = -radius
        max_x = max_z = radius
    spacing = float(layer["spacing_meters"])
    width = int(round((max_x - min_x) / spacing)) + 1
    height = int(round((max_z - min_z) / spacing)) + 1
    return width, height, min_x, max_x, min_z, max_z


def expected_triangle_count(layer: dict[str, Any], layers_by_id: dict[str, dict[str, Any]]) -> int:
    width, height, min_x, _, min_z, _ = layer_dimensions(layer)
    spacing = float(layer["spacing_meters"])
    triangles = 0
    for row in range(height - 1):
        center_z = min_z + (row + 0.5) * spacing
        for column in range(width - 1):
            center_x = min_x + (column + 0.5) * spacing
            include = True
            if layer["kind"] == "ring":
                include = max(abs(center_x), abs(center_z)) >= float(layer.get("inner_radius_meters", 0.0))
            cutout_id = layer.get("cutout_layer")
            if include and cutout_id:
                cutout = layers_by_id[cutout_id]
                include = not (
                    float(cutout["min_x_meters"]) <= center_x <= float(cutout["max_x_meters"])
                    and float(cutout["min_z_meters"]) <= center_z <= float(cutout["max_z_meters"])
                )
            if include:
                triangles += 2
    return triangles


def encode_int16_base64(values: Iterable[int]) -> str:
    values_list = list(values)
    for value in values_list:
        if value < -32768 or value > 32767:
            raise ValueError(f"Quantized terrain height {value} exceeds signed int16")
    payload = struct.pack("<" + "h" * len(values_list), *values_list)
    return base64.b64encode(payload).decode("ascii")


def decode_int16_base64(encoded: str, expected_count: int | None = None) -> list[int]:
    payload = base64.b64decode(encoded, validate=True)
    if len(payload) % 2:
        raise ValueError("Signed-int16 terrain payload has an odd byte count")
    count = len(payload) // 2
    if expected_count is not None and count != expected_count:
        raise ValueError(f"Terrain payload count {count} != expected {expected_count}")
    return list(struct.unpack("<" + "h" * count, payload))


def point_segment_distance_squared(point: tuple[float, float], start: tuple[float, float], end: tuple[float, float]) -> float:
    px, py = point
    ax, ay = start
    bx, by = end
    dx, dy = bx - ax, by - ay
    if dx == 0.0 and dy == 0.0:
        return (px - ax) ** 2 + (py - ay) ** 2
    t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    qx, qy = ax + t * dx, ay + t * dy
    return (px - qx) ** 2 + (py - qy) ** 2


def simplify_polyline(points: Sequence[tuple[float, float]], tolerance_meters: float, *, closed: bool = False) -> list[tuple[float, float]]:
    if len(points) <= (4 if closed else 2) or tolerance_meters <= 0.0:
        return list(points)
    work = list(points)
    if closed and work[0] == work[-1]:
        work = work[:-1]
    if closed:
        # Rotate to a stable extreme so the closing edge is not arbitrarily privileged.
        pivot = min(range(len(work)), key=lambda index: (work[index][0], work[index][1]))
        work = work[pivot:] + work[:pivot] + [work[pivot]]

    keep = {0, len(work) - 1}
    stack = [(0, len(work) - 1)]
    tolerance_squared = tolerance_meters * tolerance_meters
    while stack:
        first, last = stack.pop()
        best_index = -1
        best_distance = -1.0
        for index in range(first + 1, last):
            distance = point_segment_distance_squared(work[index], work[first], work[last])
            if distance > best_distance:
                best_distance = distance
                best_index = index
        if best_distance > tolerance_squared:
            keep.add(best_index)
            stack.append((first, best_index))
            stack.append((best_index, last))
    result = [work[index] for index in sorted(keep)]
    if closed:
        if len(result) < 4:
            result = work
        if result[0] != result[-1]:
            result.append(result[0])
    return result


def smoothstep(edge0: float, edge1: float, value: float) -> float:
    if edge0 == edge1:
        return 1.0 if value >= edge1 else 0.0
    t = max(0.0, min(1.0, (value - edge0) / (edge1 - edge0)))
    return t * t * (3.0 - 2.0 * t)


def normalized_sha_key(*parts: float) -> str:
    return ":".join(f"{part:.6f}" for part in parts)


def chunks(values: Sequence[Any], size: int) -> Iterator[Sequence[Any]]:
    for start in range(0, len(values), size):
        yield values[start : start + size]
