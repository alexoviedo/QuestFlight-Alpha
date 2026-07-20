#!/usr/bin/env python3
"""Exact horizontal-frame conversions for Photoreal Aerial Surface V1.

The pinned lidar rasters are axis-aligned in UTM zone 13N.  Their historical
``pixel_center_affine_local_meters`` values are UTM easting/northing deltas,
not product-local coordinates.  Product geometry and imagery use the WGS84
azimuthal-equidistant (AEQD) frame centred on the serialized KBDU origin.

All production conversions in this module use PROJ directly.  The fitted
affine included in the manifest contract is diagnostic evidence only.
"""

from __future__ import annotations

from functools import lru_cache
import math
from typing import Any

import numpy as np
import pyproj


UTM13N_EPSG = 32613
EXACT_ROUND_TRIP_MAXIMUM_METERS = 1.0e-6
DIAGNOSTIC_AFFINE_MAXIMUM_RESIDUAL_METERS = 0.005
DIAGNOSTIC_SAMPLE_GRID_SIZE = 17


def product_aeqd_proj4(mapping: dict[str, Any]) -> str:
    """Return the canonical product AEQD definition at deterministic precision."""

    origin = mapping["origin"]
    latitude = float(origin["wgs84_latitude_degrees"])
    longitude = float(origin["wgs84_longitude_degrees"])
    return (
        f"+proj=aeqd +lat_0={latitude:.12f} +lon_0={longitude:.12f} "
        "+datum=WGS84 +units=m +no_defs"
    )


def _frame_parameters(mapping: dict[str, Any]) -> tuple[int, float, float, float, float]:
    grid = mapping["grid"]
    epsg = int(grid["product_crs_epsg"])
    if epsg != UTM13N_EPSG:
        raise ValueError(f"Expected lidar raster CRS EPSG:{UTM13N_EPSG}, got EPSG:{epsg}")
    origin = mapping["origin"]
    return (
        epsg,
        float(origin["utm13n_easting_meters"]),
        float(origin["utm13n_northing_meters"]),
        float(origin["wgs84_latitude_degrees"]),
        float(origin["wgs84_longitude_degrees"]),
    )


@lru_cache(maxsize=8)
def _transformers(
    epsg: int, latitude: float, longitude: float
) -> tuple[pyproj.Transformer, pyproj.Transformer]:
    product = pyproj.CRS.from_proj4(
        f"+proj=aeqd +lat_0={latitude:.12f} +lon_0={longitude:.12f} "
        "+datum=WGS84 +units=m +no_defs"
    )
    utm = pyproj.CRS.from_epsg(epsg)
    return (
        pyproj.Transformer.from_crs(utm, product, always_xy=True),
        pyproj.Transformer.from_crs(product, utm, always_xy=True),
    )


def utm13n_delta_to_product_aeqd(
    x_utm_delta: Any, z_utm_delta: Any, mapping: dict[str, Any]
) -> tuple[Any, Any]:
    """Convert origin-relative UTM13N deltas to product-local AEQD metres."""

    epsg, origin_easting, origin_northing, latitude, longitude = _frame_parameters(mapping)
    forward, _inverse = _transformers(epsg, latitude, longitude)
    x = np.asarray(x_utm_delta, dtype=np.float64)
    z = np.asarray(z_utm_delta, dtype=np.float64)
    if x.shape != z.shape:
        x, z = np.broadcast_arrays(x, z)
    if x.ndim == 0:
        return forward.transform(origin_easting + float(x), origin_northing + float(z))
    if x.size == 1:
        transformed_x, transformed_z = forward.transform(
            origin_easting + float(x.reshape(-1)[0]),
            origin_northing + float(z.reshape(-1)[0]),
        )
        return (
            np.asarray([transformed_x], dtype=np.float64).reshape(x.shape),
            np.asarray([transformed_z], dtype=np.float64).reshape(x.shape),
        )
    shape = x.shape
    transformed_x, transformed_z = forward.transform(
        (origin_easting + x).reshape(-1), (origin_northing + z).reshape(-1)
    )
    return np.asarray(transformed_x).reshape(shape), np.asarray(transformed_z).reshape(shape)


def product_aeqd_to_utm13n_delta(
    x_product: Any, z_product: Any, mapping: dict[str, Any]
) -> tuple[Any, Any]:
    """Convert product-local AEQD metres to origin-relative UTM13N deltas."""

    epsg, origin_easting, origin_northing, latitude, longitude = _frame_parameters(mapping)
    _forward, inverse = _transformers(epsg, latitude, longitude)
    x = np.asarray(x_product, dtype=np.float64)
    z = np.asarray(z_product, dtype=np.float64)
    if x.shape != z.shape:
        x, z = np.broadcast_arrays(x, z)
    if x.ndim == 0:
        easting, northing = inverse.transform(float(x), float(z))
        return easting - origin_easting, northing - origin_northing
    if x.size == 1:
        easting, northing = inverse.transform(
            float(x.reshape(-1)[0]), float(z.reshape(-1)[0])
        )
        return (
            np.asarray([easting - origin_easting], dtype=np.float64).reshape(x.shape),
            np.asarray([northing - origin_northing], dtype=np.float64).reshape(x.shape),
        )
    shape = x.shape
    easting, northing = inverse.transform(x.reshape(-1), z.reshape(-1))
    return (
        np.asarray(easting).reshape(shape) - origin_easting,
        np.asarray(northing).reshape(shape) - origin_northing,
    )


def product_points_to_utm13n_delta(
    points_xz: np.ndarray, mapping: dict[str, Any]
) -> np.ndarray:
    """Convert an ``[..., 2]`` product X/Z array to lidar-grid UTM deltas."""

    points = np.asarray(points_xz, dtype=np.float64)
    if points.ndim < 1 or points.shape[-1] != 2:
        raise ValueError("Expected an [..., 2] product X/Z array")
    x, z = product_aeqd_to_utm13n_delta(points[..., 0], points[..., 1], mapping)
    return np.stack((x, z), axis=-1)


def utm13n_delta_points_to_product(
    points_xz: np.ndarray, mapping: dict[str, Any]
) -> np.ndarray:
    """Convert an ``[..., 2]`` lidar-grid UTM-delta array to product X/Z."""

    points = np.asarray(points_xz, dtype=np.float64)
    if points.ndim < 1 or points.shape[-1] != 2:
        raise ValueError("Expected an [..., 2] UTM-delta X/Z array")
    x, z = utm13n_delta_to_product_aeqd(points[..., 0], points[..., 1], mapping)
    return np.stack((x, z), axis=-1)


def lidar_pixel_centers_product_aeqd(
    mapping: dict[str, Any], target_shape: tuple[int, int] | None = None
) -> tuple[np.ndarray, np.ndarray]:
    """Return exact product X/Z coordinates for every UTM-grid pixel centre."""

    grid = mapping["grid"]
    shape = target_shape or (
        int(grid["height_pixels"]),
        int(grid["width_pixels"]),
    )
    if shape[0] <= 0 or shape[1] <= 0:
        raise ValueError("Lidar grid dimensions must be positive")
    affine = mapping["pixel_center_affine_local_meters"]
    rows = np.arange(shape[0], dtype=np.float64)[:, None]
    columns = np.arange(shape[1], dtype=np.float64)[None, :]
    x_utm = (
        float(affine["x_at_column0_row0"])
        + columns * float(affine["x_per_column"])
        + rows * float(affine.get("x_per_row", 0.0))
    )
    z_utm = (
        float(affine["z_at_column0_row0"])
        + columns * float(affine.get("z_per_column", 0.0))
        + rows * float(affine["z_per_row"])
    )
    x_utm, z_utm = np.broadcast_arrays(x_utm, z_utm)
    x_product, z_product = utm13n_delta_to_product_aeqd(x_utm, z_utm, mapping)
    return np.asarray(x_product), np.asarray(z_product)


def horizontal_frame_contract(mapping: dict[str, Any]) -> dict[str, Any]:
    """Build the deterministic exact-transform and bounded-affine audit contract."""

    epsg, origin_easting, origin_northing, _latitude, _longitude = _frame_parameters(mapping)
    bounds = [float(value) for value in mapping["grid"]["product_bounds_utm13n_meters"]]
    x_limits = (bounds[0] - origin_easting, bounds[2] - origin_easting)
    z_limits = (bounds[1] - origin_northing, bounds[3] - origin_northing)
    samples_x = np.linspace(*x_limits, DIAGNOSTIC_SAMPLE_GRID_SIZE, dtype=np.float64)
    samples_z = np.linspace(*z_limits, DIAGNOSTIC_SAMPLE_GRID_SIZE, dtype=np.float64)
    x_utm, z_utm = np.meshgrid(samples_x, samples_z)
    x_product, z_product = utm13n_delta_to_product_aeqd(x_utm, z_utm, mapping)
    x_round_trip, z_round_trip = product_aeqd_to_utm13n_delta(
        x_product, z_product, mapping
    )
    round_trip_residual = np.hypot(x_round_trip - x_utm, z_round_trip - z_utm)

    design = np.column_stack(
        (x_utm.reshape(-1), z_utm.reshape(-1), np.ones(x_utm.size, dtype=np.float64))
    )
    targets = np.column_stack((np.asarray(x_product).reshape(-1), np.asarray(z_product).reshape(-1)))
    coefficients, _residuals, _rank, _singular = np.linalg.lstsq(design, targets, rcond=None)
    fitted = design @ coefficients
    affine_residual = np.hypot(fitted[:, 0] - targets[:, 0], fitted[:, 1] - targets[:, 1])
    product_offset = np.hypot(
        targets[:, 0] - design[:, 0], targets[:, 1] - design[:, 1]
    )
    affine_maximum = float(np.max(affine_residual))
    round_trip_maximum = float(np.max(round_trip_residual))
    if affine_maximum > DIAGNOSTIC_AFFINE_MAXIMUM_RESIDUAL_METERS:
        raise ValueError("Product/UTM diagnostic affine residual exceeds its contract")
    if round_trip_maximum > EXACT_ROUND_TRIP_MAXIMUM_METERS:
        raise ValueError("Product/UTM exact round-trip residual exceeds its contract")

    # coefficients maps [x_utm, z_utm, 1] to [x_product, z_product].
    matrix = [
        [float(coefficients[0, 0]), float(coefficients[1, 0])],
        [float(coefficients[0, 1]), float(coefficients[1, 1])],
    ]
    forward_matrix = np.asarray(matrix, dtype=np.float64)
    forward_offset = np.asarray(
        [float(coefficients[2, 0]), float(coefficients[2, 1])],
        dtype=np.float64,
    )
    determinant = (
        forward_matrix[0, 0] * forward_matrix[1, 1]
        - forward_matrix[0, 1] * forward_matrix[1, 0]
    )
    inverse_matrix = np.asarray(
        [
            [forward_matrix[1, 1], -forward_matrix[0, 1]],
            [-forward_matrix[1, 0], forward_matrix[0, 0]],
        ],
        dtype=np.float64,
    ) / determinant
    inverse_offset = -(inverse_matrix @ forward_offset)
    runtime_utm = targets @ inverse_matrix.T + inverse_offset
    runtime_residual = np.hypot(
        runtime_utm[:, 0] - design[:, 0], runtime_utm[:, 1] - design[:, 1]
    )
    runtime_maximum = float(np.max(runtime_residual))
    if runtime_maximum > DIAGNOSTIC_AFFINE_MAXIMUM_RESIDUAL_METERS:
        raise ValueError("Product-to-UTM runtime affine residual exceeds its contract")

    # The closest proper rotation to the nearly conformal UTM-to-product matrix.
    rotation_cosine_unnormalized = forward_matrix[0, 0] + forward_matrix[1, 1]
    rotation_sine_unnormalized = forward_matrix[1, 0] - forward_matrix[0, 1]
    rotation_length = math.hypot(
        rotation_cosine_unnormalized, rotation_sine_unnormalized
    )
    rotation_cosine = rotation_cosine_unnormalized / rotation_length
    rotation_sine = rotation_sine_unnormalized / rotation_length
    rotation_degrees = math.degrees(math.atan2(rotation_sine, rotation_cosine))
    return {
        "diagnostic_affine_product_from_utm_delta": {
            "matrix_xz": matrix,
            "maximum_allowed_residual_meters": DIAGNOSTIC_AFFINE_MAXIMUM_RESIDUAL_METERS,
            "maximum_residual_meters": affine_maximum,
            "offset_xz_meters": [
                float(coefficients[2, 0]),
                float(coefficients[2, 1]),
            ],
            "sample_grid_size_per_axis": DIAGNOSTIC_SAMPLE_GRID_SIZE,
            "use_for_production_conversion": False,
        },
        "exact_conversion": {
            "always_xy": True,
            "implementation": "pyproj.Transformer",
            "maximum_allowed_round_trip_residual_meters": EXACT_ROUND_TRIP_MAXIMUM_METERS,
            "maximum_round_trip_residual_meters": round_trip_maximum,
            "product_aeqd_to_utm13n_delta_formula": (
                "(E,N)=transform(product_aeqd_x,product_aeqd_z); "
                "x_utm_delta=E-origin_easting; z_utm_delta=N-origin_northing"
            ),
            "utm13n_delta_to_product_aeqd_formula": (
                "product_aeqd_x,product_aeqd_z=transform("
                "origin_easting+x_utm_delta,origin_northing+z_utm_delta)"
            ),
        },
        "lidar_raster_frame": {
            "coordinates": "origin-relative UTM easting/northing deltas",
            "crs_epsg": epsg,
            "historical_affine_field": "coordinate_mapping.pixel_center_affine_local_meters",
            "origin_easting_meters": origin_easting,
            "origin_northing_meters": origin_northing,
        },
        "maximum_product_offset_from_utm_delta_meters": float(np.max(product_offset)),
        "product_geometry_frame": {
            "axes": {"x": "east", "z": "north"},
            "crs_proj4": product_aeqd_proj4(mapping),
            "name": "KBDU product-local WGS84 azimuthal equidistant",
        },
        "pyproj_version": pyproj.__version__,
        "runtime_affine_coefficients": {
            "position_maximum_allowed_residual_meters": DIAGNOSTIC_AFFINE_MAXIMUM_RESIDUAL_METERS,
            "position_maximum_residual_meters": runtime_maximum,
            "product_to_utm_x_from_product_x": float(inverse_matrix[0, 0]),
            "product_to_utm_x_from_product_z": float(inverse_matrix[0, 1]),
            "product_to_utm_x_offset_meters": float(inverse_offset[0]),
            "product_to_utm_z_from_product_x": float(inverse_matrix[1, 0]),
            "product_to_utm_z_from_product_z": float(inverse_matrix[1, 1]),
            "product_to_utm_z_offset_meters": float(inverse_offset[1]),
            "utm_normal_to_product_rotation_degrees_counterclockwise": rotation_degrees,
            "utm_normal_to_product_x_from_utm_x": rotation_cosine,
            "utm_normal_to_product_x_from_utm_z": -rotation_sine,
            "utm_normal_to_product_z_from_utm_x": rotation_sine,
            "utm_normal_to_product_z_from_utm_z": rotation_cosine,
        },
    }
