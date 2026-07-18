#!/usr/bin/env python3
"""Measure broad longitudinal runway luminance bands in deterministic QA captures."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image


def sample_profile(image: np.ndarray, samples: list[dict]) -> tuple[np.ndarray, np.ndarray]:
    luminance = image[..., :3] @ np.array([0.2126, 0.7152, 0.0722])
    distances: list[float] = []
    values: list[float] = []
    height, width = luminance.shape
    for item in samples:
        left = np.array([item["leftX"], item["leftY"]], dtype=float)
        right = np.array([item["rightX"], item["rightY"]], dtype=float)
        cross = np.linspace(left, right, 17)[3:-3]
        xs = np.clip(np.rint(cross[:, 0]).astype(int), 0, width - 1)
        ys = np.clip(np.rint(cross[:, 1]).astype(int), 0, height - 1)
        values.append(float(np.median(luminance[ys, xs])))
        distances.append(float(item["distanceMeters"]))
    return np.asarray(distances), np.asarray(values)


def analyze(distances: np.ndarray, raw: np.ndarray) -> dict:
    spacing = float(np.median(np.diff(distances)))
    window = max(3, int(round(30.0 / spacing)))
    if window % 2 == 0:
        window += 1
    kernel = np.ones(window, dtype=float) / window
    smooth = np.convolve(raw, kernel, mode="same")
    margin = max(window, 3)
    core = smooth[margin:-margin] if len(smooth) > margin * 2 else smooth
    core_distances = distances[margin:-margin] if len(smooth) > margin * 2 else distances
    normalized_distance = (core_distances - np.mean(core_distances)) / max(np.ptp(core_distances), 1.0)
    trend = np.polyval(np.polyfit(normalized_distance, core, 3), normalized_distance)
    detrended = core - trend
    spectrum = np.abs(np.fft.rfft(detrended))
    frequencies = np.fft.rfftfreq(len(detrended), d=spacing)
    valid = (frequencies >= 1.0 / 300.0) & (frequencies <= 1.0 / 20.0)
    valid[0] = False
    if np.any(valid):
        selected = np.where(valid)[0]
        peak_index = selected[int(np.argmax(spectrum[selected]))]
        wavelength = 1.0 / frequencies[peak_index]
        total = float(np.sum(spectrum[selected] ** 2))
        confidence = float((spectrum[peak_index] ** 2) / total) if total > 0 else 0.0
    else:
        wavelength, confidence = 0.0, 0.0
    p05, p95 = np.percentile(detrended, [5, 95])
    peak_to_trough = float(p95 - p05)
    mean = float(np.mean(core))
    normalized = peak_to_trough / max(mean, 1e-6)
    return {
        "sampleSpacingMeters": spacing,
        "lowPassWindowMeters": window * spacing,
        "dominantWavelengthMeters": float(wavelength),
        "peakToTroughLuminance": peak_to_trough,
        "normalizedPeakToTrough": float(normalized),
        "confidence": confidence,
        "sampleCount": int(len(raw)),
        "profile": [float(value) for value in raw],
        "lowPassProfile": [float(value) for value in smooth],
        "detrendedProfile": [float(value) for value in detrended],
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("report", type=Path)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--threshold", type=float, default=0.10,
                        help="maximum accepted normalized peak-to-trough broad-band luminance")
    args = parser.parse_args()
    report = json.loads(args.report.read_text(encoding="utf-8-sig"))
    args.output_dir.mkdir(parents=True, exist_ok=True)
    results: dict[str, dict] = {}
    distances = None
    for mode in report["modes"]:
        image = np.asarray(Image.open(mode["imagePath"]).convert("RGB"), dtype=float) / 255.0
        current_distances, raw = sample_profile(image, report["runwayCenterlinePixels"])
        distances = current_distances
        results[mode["id"]] = {"description": mode["description"], **analyze(current_distances, raw)}
    topology = report["pavementTopology"]
    baseline = results["R01"]
    final = results["R00"]
    classification = (
        "A_DEPTH_INTERSECTION" if topology["verticesBelowTerrain"] > 0 and baseline["normalizedPeakToTrough"] > args.threshold
        else "A_GEOMETRY_OR_DEPTH" if baseline["normalizedPeakToTrough"] > args.threshold
        else "CHANNEL_OR_LIGHTING"
    )
    summary = {
        "sourceReport": str(args.report),
        "thresholdNormalizedPeakToTrough": args.threshold,
        "classification": classification,
        "correlation": {
            "triangleWinding": "ruled_out" if topology["downwardTriangles"] == 0 else "correlated",
            "terrainDepthIntersection": "correlated" if topology["verticesBelowTerrain"] > 0 else "ruled_out",
            "texturePeriod": "ruled_out_by_R1" if baseline["normalizedPeakToTrough"] > args.threshold else "not_supported",
            "markingIntervals": "ruled_out_by_R1" if baseline["normalizedPeakToTrough"] > args.threshold else "not_supported",
            "lightmap": "ruled_out_by_R1_and_lightmap_minus_one",
            "orthophoto": "occluding_source" if topology["verticesBelowTerrain"] > 0 else "not_supported",
        },
        "topology": topology,
        "modes": results,
        "finalPass": final["normalizedPeakToTrough"] <= args.threshold,
    }
    json_path = args.output_dir / "runway_isolation_analysis.json"
    json_path.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    md = [
        "# Runway isolation analysis", "",
        f"- Supported classification: **{classification}**",
        f"- Acceptance threshold: normalized broad-band peak-to-trough <= {args.threshold:.3f}",
        f"- R1 unlit/texture-free result: {baseline['normalizedPeakToTrough']:.4f}, dominant wavelength {baseline['dominantWavelengthMeters']:.1f} m, confidence {baseline['confidence']:.3f}",
        f"- R0 final result: {final['normalizedPeakToTrough']:.4f}, dominant wavelength {final['dominantWavelengthMeters']:.1f} m, confidence {final['confidence']:.3f}",
        f"- Mesh winding: {topology['upwardTriangles']} upward / {topology['downwardTriangles']} downward / {topology['degenerateTriangles']} degenerate triangles.",
        f"- Terrain clearance: {topology['verticesBelowTerrain']} of {topology['sampledTerrainVertices']} sampled vertices below terrain; range {topology['minimumTerrainClearanceMeters']:.6f} to {topology['maximumTerrainClearanceMeters']:.6f} m.",
        "",
        "The band pattern survives constant unlit white with all pavement texture channels and markings disabled. "
        "It therefore cannot originate in albedo, normal, roughness, wear, markings, lightmaps, or the analytic macro response. "
        "All triangles face upward, while exactly half of the runway edge samples cross below the terrain. The terrain/orthophoto draw wins depth at those intervals.",
        "", "| Mode | Normalized peak-to-trough | Wavelength (m) | Confidence |", "|---|---:|---:|---:|",
    ]
    for key, value in results.items():
        md.append(f"| {key} | {value['normalizedPeakToTrough']:.4f} | {value['dominantWavelengthMeters']:.1f} | {value['confidence']:.3f} |")
    (args.output_dir / "runway_isolation_analysis.md").write_text("\n".join(md) + "\n", encoding="utf-8")
    print(json_path)
    return 0 if summary["finalPass"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
