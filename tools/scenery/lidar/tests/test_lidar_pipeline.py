from __future__ import annotations

import sys
from pathlib import Path

import numpy as np


LIDAR_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(LIDAR_DIR))

from build_near_terrain import (  # noqa: E402
    adaptive_axis,
    preserved_surface,
    reconstruct_mesh,
)
from common import compute_grid_contract, load_config, sample_north_up_grid  # noqa: E402


def test_grid_contract_is_exact_4096_meter_square() -> None:
    config = load_config(LIDAR_DIR / "config.json")
    contract = compute_grid_contract(config)
    assert contract.width == 1024
    assert contract.height == 1024
    assert contract.pixel_size == 4.0
    assert contract.max_easting - contract.min_easting == 4096.0
    assert contract.max_northing - contract.min_northing == 4096.0


def test_adaptive_axis_keeps_exact_borders_and_spacing() -> None:
    axis = adaptive_axis(2048.0, 400.0, 20.0, 14.0, [-30.671, -30.054])
    assert axis[0] == -2048.0
    assert axis[-1] == 2048.0
    assert np.all(np.diff(axis) > 0.0)
    assert float(np.max(np.diff(axis))) <= 20.0


def test_triangle_reconstruction_is_exact_for_plane() -> None:
    x_axis = np.asarray([-1.0, 1.0])
    z_axis = np.asarray([-1.0, 1.0])
    x_grid, z_grid = np.meshgrid(x_axis, z_axis)
    heights = 10.0 + 2.0 * x_grid - 3.0 * z_grid
    sample_x = np.asarray([[-0.75, 0.25], [0.5, -0.25]])
    sample_z = np.asarray([[-0.5, -0.25], [0.75, 0.5]])
    actual = reconstruct_mesh(x_axis, z_axis, heights, sample_x, sample_z)
    assert np.allclose(actual, 10.0 + 2.0 * sample_x - 3.0 * sample_z)


def test_center_refinement_reconstruction_hits_center() -> None:
    x_axis = np.asarray([-1.0, 1.0])
    z_axis = np.asarray([-1.0, 1.0])
    heights = np.zeros((2, 2), dtype=np.float64)
    lookup = np.asarray([[0]], dtype=np.int32)
    actual = reconstruct_mesh(
        x_axis,
        z_axis,
        heights,
        np.asarray([[0.0]]),
        np.asarray([[0.0]]),
        lookup,
        np.asarray([5.0]),
    )
    assert actual[0, 0] == 5.0


def test_runway_preservation_is_exact_in_core_and_zero_far_away() -> None:
    basis = {
        "p0": np.asarray([-100.0, 0.0]),
        "p1": np.asarray([100.0, 0.0]),
        "length": 200.0,
        "along_unit": np.asarray([1.0, 0.0]),
        "lateral_unit": np.asarray([0.0, 1.0]),
        "z0": 1600.0,
        "z1": 1598.0,
    }
    blend = {
        "core_lateral_margin_meters": 4.0,
        "outer_end_margin_meters": 55.0,
        "outer_lateral_margin_meters": 40.0,
    }
    source = np.asarray([1500.0, 1500.0])
    result, weight, plane = preserved_surface(
        source,
        np.asarray([0.0, 0.0]),
        np.asarray([0.0, 100.0]),
        basis,
        22.86,
        blend,
    )
    assert weight[0] == 1.0
    assert result[0] == plane[0]
    assert weight[1] == 0.0
    assert result[1] == source[1]


def test_north_up_sampler_matches_pixel_centers() -> None:
    config = load_config(LIDAR_DIR / "config.json")
    contract = compute_grid_contract(config)
    raster = np.arange(contract.width * contract.height, dtype=np.float64).reshape(
        contract.height, contract.width
    )
    value = sample_north_up_grid(raster, contract, np.asarray([-2046.0]), np.asarray([2046.0]))
    assert value[0] == raster[0, 0]
