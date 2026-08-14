"""Build compact Quest cloud-volume textures from external CC0 VDB density.

Run with Blender's bundled Python because Blender supplies the OpenVDB binding::

    blender --background --factory-startup \
      --python tools/clouds/build_sparse_volumetric_field.py -- \
      --source-a <cloud_01.vdb> --source-b <cloud_06.vdb> \
      --density-output <KBDU_CloudDensitySlices.png> \
      --weather-output <KBDU_CloudCoverage.png> \
      --diagnostic-output <cloud_density_reference.png> \
      --manifest-output <KBDU_CloudVolumeBuild.json>

Raw VDB data remains outside Git.  The runtime derivative is a 64-slice atlas
whose channels contain two normalized density archetypes, a precomputed
top-light transmittance approximation, and edge erosion. A separate authored
weather map records deterministic formation placement for evidence and future
weather variants; the shipping mesh carries those eight bounds directly.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import sys

import bpy
import numpy as np
import openvdb


TARGET_X = 64
TARGET_Y = 32
TARGET_Z = 64
SLICE_COLUMNS = 8
WEATHER_SIZE = 256
SEED = 43117


def parse_args() -> argparse.Namespace:
    arguments = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-a", required=True)
    parser.add_argument("--source-b", required=True)
    parser.add_argument("--density-output", required=True)
    parser.add_argument("--weather-output", required=True)
    parser.add_argument("--diagnostic-output", required=True)
    parser.add_argument("--manifest-output", required=True)
    return parser.parse_args(arguments)


def sha256(path: str) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_density(path: str) -> np.ndarray:
    grids, _ = openvdb.readAll(path)
    if not grids:
        raise RuntimeError(f"VDB contains no grids: {path}")
    grid = next((candidate for candidate in grids if candidate.name == "density"), grids[0])
    minimum, maximum = grid.evalActiveVoxelBoundingBox()
    dimensions = tuple(maximum[index] - minimum[index] + 1 for index in range(3))
    values = np.zeros(dimensions, dtype=np.float32)
    grid.copyToArray(values, ijk=minimum)
    values = np.maximum(values, 0.0)
    positive = values[values > 1e-6]
    if positive.size == 0:
        raise RuntimeError(f"VDB density is empty: {path}")
    scale = float(np.quantile(positive, 0.995))
    return np.clip(values / max(scale, 1e-6), 0.0, 1.0)


def resize_axis(values: np.ndarray, size: int, axis: int) -> np.ndarray:
    if values.shape[axis] == size:
        return values
    coordinates = np.linspace(0.0, values.shape[axis] - 1.0, size, dtype=np.float32)
    lower = np.floor(coordinates).astype(np.int32)
    upper = np.minimum(lower + 1, values.shape[axis] - 1)
    fraction = coordinates - lower
    shape = [1] * values.ndim
    shape[axis] = size
    fraction = fraction.reshape(shape)
    return np.take(values, lower, axis=axis) * (1.0 - fraction) + np.take(values, upper, axis=axis) * fraction


def resample(values: np.ndarray) -> np.ndarray:
    resized = resize_axis(values, TARGET_X, 0)
    resized = resize_axis(resized, TARGET_Y, 1)
    resized = resize_axis(resized, TARGET_Z, 2)
    return np.asarray(resized, dtype=np.float32)


def blur3(values: np.ndarray, iterations: int = 2) -> np.ndarray:
    result = values.copy()
    for _ in range(iterations):
        for axis in range(3):
            result = (
                np.roll(result, -1, axis=axis)
                + 2.0 * result
                + np.roll(result, 1, axis=axis)
            ) * 0.25
    return result


def normalize_archetype(values: np.ndarray) -> np.ndarray:
    # Keep the useful vapor islands that the rejected polygon conversion had
    # discarded, while making the atlas periodic and stable at its borders.
    # OpenVDB exposes Blender grids as X,Z(horizontal),Y(vertical) in the
    # source array. Runtime cloud coordinates are X,Y(vertical),Z, so make
    # that basis conversion explicit before resampling instead of allowing a
    # cloud's horizontal depth to become its apparent height.
    values = np.transpose(values, (0, 2, 1))
    values = resample(values)
    # Downsampling a sparse VDB directly leaves isolated voxel islands that
    # read as vertical combing at flight distances. Reconstruct a coherent
    # vapor body offline while retaining enough of the original high-frequency
    # boundary to avoid a synthetic blob.
    values = values * 0.35 + blur3(values, 2) * 0.65
    values = np.maximum(values - 0.015, 0.0)
    values /= max(float(np.quantile(values[values > 0.0], 0.99)), 1e-6)
    values = np.clip(values, 0.0, 1.0)
    window_x = np.maximum(
        np.sin(np.linspace(0.0, math.pi, TARGET_X, dtype=np.float32)), 0.0
    ) ** 0.35
    window_y = np.maximum(
        np.sin(np.linspace(0.0, math.pi, TARGET_Y, dtype=np.float32)), 0.0
    ) ** 0.18
    window_z = np.maximum(
        np.sin(np.linspace(0.0, math.pi, TARGET_Z, dtype=np.float32)), 0.0
    ) ** 0.35
    values *= window_x[:, None, None] * window_y[None, :, None] * window_z[None, None, :]
    return np.clip(values, 0.0, 1.0)


def top_light_transmittance(density: np.ndarray) -> np.ndarray:
    # Static midday sky contract: the exact product sun remains authoritative
    # at runtime; this channel only approximates optical depth from cloud top.
    optical_depth = np.cumsum(density[:, ::-1, :], axis=1)[:, ::-1, :]
    optical_depth /= max(float(TARGET_Y), 1.0)
    return np.exp(-4.2 * optical_depth).astype(np.float32)


def pack_density(archetype_a: np.ndarray, archetype_b: np.ndarray) -> np.ndarray:
    combined = np.maximum(archetype_a, archetype_b)
    smooth = blur3(combined, 2)
    erosion = np.clip((combined - blur3(combined, 5)) * 3.5 + 0.5, 0.0, 1.0)
    light = top_light_transmittance(0.55 * archetype_a + 0.45 * archetype_b)
    channels = np.stack((archetype_a, archetype_b, light, erosion), axis=-1)
    rows = int(math.ceil(TARGET_Z / SLICE_COLUMNS))
    atlas = np.zeros((rows * TARGET_Y, SLICE_COLUMNS * TARGET_X, 4), dtype=np.float32)
    for z_index in range(TARGET_Z):
        column = z_index % SLICE_COLUMNS
        row = z_index // SLICE_COLUMNS
        # Numpy source order is X,Y,Z; PNG order is row(Y), column(X).
        atlas[
            row * TARGET_Y : (row + 1) * TARGET_Y,
            column * TARGET_X : (column + 1) * TARGET_X,
            :,
        ] = np.transpose(channels[:, :, z_index, :], (1, 0, 2))
    # PNG rows are top-origin while Unity samples UV.y from the bottom. Store
    # the complete atlas inverted so slice 0 / voxel Y=0 is addressed by the
    # shader at UV.y=0 instead of silently mirroring both volume axes.
    return np.flipud(atlas).copy()


def low_frequency_noise(size: int, seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    coarse = rng.random((9, 9), dtype=np.float32)
    x = np.linspace(0.0, 8.0, size, dtype=np.float32)
    lower = np.floor(x).astype(np.int32)
    upper = np.minimum(lower + 1, 8)
    fraction = x - lower
    rows = coarse[lower] * (1.0 - fraction[:, None]) + coarse[upper] * fraction[:, None]
    result = rows[:, lower] * (1.0 - fraction[None, :]) + rows[:, upper] * fraction[None, :]
    for _ in range(3):
        result = (
            result
            + np.roll(result, 1, 0)
            + np.roll(result, -1, 0)
            + np.roll(result, 1, 1)
            + np.roll(result, -1, 1)
        ) / 5.0
    return result


def build_weather() -> tuple[np.ndarray, list[dict[str, float]]]:
    # Normalized positions correspond to a 16 km square around KBDU.  Several
    # overlapping ellipses form connected cloud groups rather than isolated
    # props, and clear corridors remain around the runway and reservoir route.
    formations = [
        {"x": 0.20, "z": 0.22, "rx": 0.13, "rz": 0.10, "strength": 0.88, "type": 0.15},
        {"x": 0.34, "z": 0.20, "rx": 0.11, "rz": 0.09, "strength": 0.70, "type": 0.32},
        {"x": 0.58, "z": 0.18, "rx": 0.14, "rz": 0.09, "strength": 0.82, "type": 0.55},
        {"x": 0.78, "z": 0.29, "rx": 0.12, "rz": 0.12, "strength": 0.74, "type": 0.72},
        {"x": 0.18, "z": 0.60, "rx": 0.15, "rz": 0.11, "strength": 0.82, "type": 0.25},
        {"x": 0.38, "z": 0.72, "rx": 0.13, "rz": 0.10, "strength": 0.72, "type": 0.48},
        {"x": 0.64, "z": 0.68, "rx": 0.16, "rz": 0.12, "strength": 0.86, "type": 0.66},
        {"x": 0.84, "z": 0.78, "rx": 0.12, "rz": 0.09, "strength": 0.68, "type": 0.82},
    ]
    coordinate = (np.arange(WEATHER_SIZE, dtype=np.float32) + 0.5) / WEATHER_SIZE
    grid_x, grid_z = np.meshgrid(coordinate, coordinate)
    coverage = np.zeros((WEATHER_SIZE, WEATHER_SIZE), dtype=np.float32)
    cloud_type = np.zeros_like(coverage)
    weight_sum = np.zeros_like(coverage)
    for formation in formations:
        dx = (grid_x - formation["x"]) / formation["rx"]
        dz = (grid_z - formation["z"]) / formation["rz"]
        radial = np.sqrt(dx * dx + dz * dz)
        weight = np.clip(1.0 - radial, 0.0, 1.0)
        weight = weight * weight * (3.0 - 2.0 * weight)
        signal = weight * formation["strength"]
        coverage = np.maximum(coverage, signal)
        cloud_type += signal * formation["type"]
        weight_sum += signal
    cloud_type = np.divide(cloud_type, np.maximum(weight_sum, 1e-5))
    modulation = 0.82 + 0.36 * (low_frequency_noise(WEATHER_SIZE, SEED) - 0.5)
    coverage = np.clip(coverage * modulation, 0.0, 1.0)
    density_bias = np.clip(0.40 + coverage * 0.55, 0.0, 1.0)
    occupancy = np.clip((coverage - 0.035) / 0.12, 0.0, 1.0)
    return np.stack((coverage, cloud_type, density_bias, occupancy), axis=-1), formations


def save_png(path: str, values: np.ndarray, color_space: str = "Non-Color") -> None:
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    height, width, channels = values.shape
    if channels != 4:
        raise ValueError("PNG output must be RGBA")
    image = bpy.data.images.new(os.path.basename(path), width=width, height=height, alpha=True, float_buffer=False)
    image.colorspace_settings.name = color_space
    # Blender image coordinates start at the lower left.
    image.pixels.foreach_set(np.flipud(np.clip(values, 0.0, 1.0)).astype(np.float32).reshape(-1))
    image.filepath_raw = os.path.abspath(path)
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


def build_diagnostic(density: np.ndarray, weather: np.ndarray) -> np.ndarray:
    height = 1024
    width = 1024
    diagnostic = np.zeros((height, width, 4), dtype=np.float32)
    diagnostic[:, :, 3] = 1.0
    top = np.max(density, axis=1)
    top = resize_axis(resize_axis(top, 512, 0), 512, 1).T
    front = np.max(density, axis=2)
    front = resize_axis(resize_axis(front, 512, 0), 512, 1).T
    side = np.max(density, axis=0)
    side = resize_axis(resize_axis(side, 512, 0), 512, 1)
    diagnostic[:512, :512, :3] = top[:, :, None]
    diagnostic[512:, :512, :3] = front[:, :, None]
    diagnostic[:512, 512:, :3] = side[:, :, None]
    weather_large = resize_axis(resize_axis(weather[:, :, 0], 512, 0), 512, 1)
    diagnostic[512:, 512:, 0] = weather_large * 0.82
    diagnostic[512:, 512:, 1] = weather_large * 0.90
    diagnostic[512:, 512:, 2] = weather_large
    return diagnostic


def main() -> None:
    options = parse_args()
    source_a = normalize_archetype(load_density(options.source_a))
    source_b = normalize_archetype(load_density(options.source_b))
    density_atlas = pack_density(source_a, source_b)
    weather, formations = build_weather()
    save_png(options.density_output, density_atlas)
    save_png(options.weather_output, weather)
    save_png(options.diagnostic_output, build_diagnostic(np.maximum(source_a, source_b), weather), "sRGB")
    manifest = {
        "schemaVersion": 1,
        "compiler": "tools/clouds/build_sparse_volumetric_field.py",
        "dimensions": {"x": TARGET_X, "y": TARGET_Y, "z": TARGET_Z, "sliceColumns": SLICE_COLUMNS},
        "weatherSize": WEATHER_SIZE,
        "sourceA": {"path": os.path.basename(options.source_a), "sha256": sha256(options.source_a)},
        "sourceB": {"path": os.path.basename(options.source_b), "sha256": sha256(options.source_b)},
        "densityOutput": {"path": os.path.basename(options.density_output)},
        "weatherOutput": {"path": os.path.basename(options.weather_output)},
        "formations": formations,
        "rawVdbCommitted": False,
    }
    for key, path in (("densityOutput", options.density_output), ("weatherOutput", options.weather_output)):
        manifest[key]["sha256"] = sha256(path)
    os.makedirs(os.path.dirname(os.path.abspath(options.manifest_output)), exist_ok=True)
    with open(options.manifest_output, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(manifest, handle, indent=2, sort_keys=True)
        handle.write("\n")
    print(json.dumps(manifest, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
