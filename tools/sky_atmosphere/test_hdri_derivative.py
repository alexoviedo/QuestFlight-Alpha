#!/usr/bin/env python3

from __future__ import annotations

from pathlib import Path
import tempfile
import unittest

import numpy as np

from build_hdri_derivative import downsample_box, read_rgbe, sha256, write_rgbe


class HdriDerivativeTests(unittest.TestCase):
    def test_linear_box_filter_is_exact(self) -> None:
        source = np.arange(8 * 16 * 3, dtype=np.float32).reshape(8, 16, 3)
        expected = source.reshape(4, 2, 8, 2, 3).mean(axis=(1, 3))
        np.testing.assert_array_equal(downsample_box(source, 2), expected)

    def test_rgbe_rle_roundtrip_is_deterministic_and_finite(self) -> None:
        rng = np.random.default_rng(0x4B424455)
        source = rng.uniform(0.0001, 64.0, size=(8, 256, 3)).astype(np.float32)
        source[:, 32:180, :] = 0.25  # Long runs exercise packet splitting.
        source[:, 200:204, :] = 32000.0
        with tempfile.TemporaryDirectory() as directory:
            first = Path(directory) / "first.hdr"
            second = Path(directory) / "second.hdr"
            write_rgbe(first, source)
            write_rgbe(second, source)
            decoded, header = read_rgbe(first)
            self.assertEqual(sha256(first), sha256(second))
            self.assertEqual((header["height"], header["width"]), source.shape[:2])
            self.assertTrue(np.all(np.isfinite(decoded)))
            self.assertTrue(np.all(decoded >= 0.0))
            # RGBE shares one exponent across a pixel, so small channels are bounded by
            # the largest channel's quantization step rather than their own magnitude.
            per_pixel_bound = np.max(source, axis=2, keepdims=True) / 128.0 + 1.0e-6
            self.assertTrue(np.all(np.abs(decoded - source) <= per_pixel_bound))


if __name__ == "__main__":
    unittest.main()
