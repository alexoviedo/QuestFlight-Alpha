#!/usr/bin/env python3
"""Build deterministic, compact C172 cockpit audio derivatives.

Raw recordings stay outside Git.  The committed outputs are optimized source
WAV files that Unity imports for Android.  Run with the CC0 Freesound preview
or original recording and an output directory.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import subprocess
import tempfile
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.signal import butter, sosfiltfilt

RATE = 48000
SEED = 17208


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def decode(ffmpeg: Path, source: Path) -> tuple[np.ndarray, int]:
    with tempfile.TemporaryDirectory() as td:
        wav = Path(td) / "decoded.wav"
        subprocess.run(
            [str(ffmpeg), "-hide_banner", "-loglevel", "error", "-y", "-i",
             str(source), "-ar", str(RATE), "-ac", "2", "-c:a", "pcm_f32le", str(wav)],
            check=True,
        )
        return sf.read(wav, dtype="float32", always_2d=True)


def peak_normalize(x: np.ndarray, peak_db: float = -5.0) -> np.ndarray:
    target = 10.0 ** (peak_db / 20.0)
    peak = float(np.max(np.abs(x)))
    return x if peak < 1e-8 else (x * (target / peak)).astype(np.float32)


def overlap_loop(y: np.ndarray, sr: int) -> np.ndarray:
    # Overlap the tail and head, then remove the duplicate overlap. Merely
    # replacing the tail with the first 0.75 s jumps back by 0.75 s at wrap.
    n = min(int(0.75 * sr), len(y) // 4)
    fade = np.linspace(0.0, 1.0, n, dtype=np.float32)[:, None]
    overlap = y[-n:] * (1.0 - fade) + y[:n] * fade
    return np.concatenate((overlap, y[n:-n]))


def loop_segment(x: np.ndarray, sr: int, start: float, duration: float) -> np.ndarray:
    first = int(round(start * sr))
    count = int(round(duration * sr))
    y = np.array(x[first:first + count], dtype=np.float32, copy=True)
    if len(y) != count:
        raise ValueError(f"Source is too short for {start:.1f}s + {duration:.1f}s")
    return peak_normalize(overlap_loop(y, sr))


def band_noise(rng: np.random.Generator, seconds: float, low: float, high: float) -> np.ndarray:
    x = rng.standard_normal(int(RATE * seconds)).astype(np.float32)
    sos = butter(4, [low, high], btype="bandpass", fs=RATE, output="sos")
    y = sosfiltfilt(sos, x).astype(np.float32)
    # Slow deterministic amplitude motion avoids a frozen noise texture.
    t = np.arange(len(y), dtype=np.float32) / RATE
    y *= 0.72 + 0.16 * np.sin(2 * np.pi * 0.19 * t) + 0.08 * np.sin(2 * np.pi * 0.071 * t + 1.3)
    y = np.column_stack((y, np.roll(y, 127)))
    return peak_normalize(overlap_loop(y, RATE), -8.0)


def tone(seconds: float, frequencies: list[tuple[float, float]], peak_db: float) -> np.ndarray:
    t = np.arange(int(RATE * seconds), dtype=np.float32) / RATE
    y = np.zeros_like(t)
    for hz, amplitude in frequencies:
        y += amplitude * np.sin(2.0 * np.pi * hz * t)
    edge = min(int(0.02 * RATE), len(y) // 4)
    envelope = np.ones_like(y)
    envelope[:edge] = np.linspace(0.0, 1.0, edge)
    envelope[-edge:] = np.linspace(1.0, 0.0, edge)
    y *= envelope
    return peak_normalize(np.column_stack((y, y)), peak_db)


def impulse(seconds: float, kind: str) -> np.ndarray:
    rng = np.random.default_rng(SEED + len(kind))
    n = int(seconds * RATE)
    t = np.arange(n, dtype=np.float32) / RATE
    if kind == "switch":
        y = rng.standard_normal(n).astype(np.float32) * np.exp(-t * 85.0)
        y += 0.45 * np.sin(2 * np.pi * 1350 * t) * np.exp(-t * 55.0)
    else:
        y = rng.standard_normal(n).astype(np.float32) * np.exp(-t * 10.0)
        y += 0.6 * np.sin(2 * np.pi * 78 * t) * np.exp(-t * 8.0)
    return peak_normalize(np.column_stack((y, np.roll(y, 31))), -4.0)


def write(path: Path, audio: np.ndarray) -> None:
    sf.write(path, audio, RATE, subtype="PCM_16")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--ffmpeg", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--source-url", default="https://cdn.freesound.org/previews/322/322321_2489703-lq.mp3")
    parser.add_argument("--engine-only", action="store_true", help="Preserve the existing auxiliary clips and their provenance.")
    parser.add_argument("--reference-clips", type=Path, help="Match existing engine RMS levels when upgrading source quality.")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    recording, sr = decode(args.ffmpeg, args.input)
    derivatives = {
        "C172_Engine_Start.wav": peak_normalize(recording[int(447 * sr):int(465 * sr)], -4.5),
        "C172_Engine_Idle_Loop.wav": loop_segment(recording, sr, 490.0, 12.0),
        "C172_Engine_Cruise_Loop.wav": loop_segment(recording, sr, 880.0, 12.0),
        "C172_Propeller_High_Loop.wav": loop_segment(recording, sr, 820.0, 10.0),
    }
    rng = np.random.default_rng(SEED)
    auxiliary = {
        "C172_Wind_Low_Loop.wav": band_noise(rng, 8.0, 180.0, 2600.0),
        "C172_Wind_High_Loop.wav": band_noise(rng, 8.0, 320.0, 5200.0),
        "C172_Ground_Roll_Loop.wav": band_noise(rng, 6.0, 45.0, 950.0),
        "C172_Flap_Motor_Loop.wav": tone(4.0, [(145.0, 1.0), (290.0, 0.28), (435.0, 0.12)], -9.0),
        "C172_Stall_Horn_Loop.wav": tone(2.0, [(385.0, 1.0), (770.0, 0.22)], -5.0),
        "C172_Switch_Click.wav": impulse(0.10, "switch"),
        "C172_Touchdown.wav": impulse(0.55, "touchdown"),
    }
    if not args.engine_only:
        derivatives.update(auxiliary)

    outputs = []
    for name, audio in derivatives.items():
        if args.reference_clips:
            reference_path = args.reference_clips / name
            if reference_path.exists():
                reference, _ = sf.read(reference_path, dtype="float32", always_2d=True)
                target_rms = float(np.sqrt(np.mean(reference.astype(np.float64) ** 2)))
                actual_rms = float(np.sqrt(np.mean(audio.astype(np.float64) ** 2)))
                if actual_rms > 1e-8 and target_rms > 1e-8 and abs(20.0 * math.log10(target_rms / actual_rms)) > 0.01:
                    audio *= target_rms / actual_rms
                peak = float(np.max(np.abs(audio)))
                if peak > 0.89:
                    audio *= 0.89 / peak
        path = args.output / name
        write(path, audio)
        outputs.append({"path": name, "sha256": sha256(path), "sampleRate": RATE, "channels": int(audio.shape[1])})

    existing_path = args.output / "SOURCE_MANIFEST.json"
    existing = json.loads(existing_path.read_text(encoding="utf-8")) if existing_path.exists() else {}
    if args.engine_only:
        outputs.extend(entry for entry in existing.get("outputs", []) if entry["path"] not in derivatives)
    manifest = {
        "schemaVersion": 1,
        "generatedBy": "tools/audio/prepare_c172_audio.py",
        "source": {
            "title": "1964 Cessna 172E - Interior - Start, Idle, Takeoff, Flight",
            "author": "JLWSound",
            "page": "https://freesound.org/people/JLWSound/sounds/322321/",
            "download": args.source_url,
            "license": "CC0 1.0",
            "rawSha256": sha256(args.input),
            "rawCommitted": False,
        },
        "processing": {
            "sampleRate": RATE,
            "seed": SEED,
            "engineSegmentsSeconds": {"start": [447, 465], "idle": [490, 502], "propHigh": [820, 830], "cruise": [880, 892]},
            "notes": "Engine layers are edited from the real recording; auxiliary cues are deterministic project-authored synthesis.",
            "loopSeam": "Tail/head overlap removes duplicated overlap; wrap samples remain contiguous in the source recording.",
            "levelReference": "Existing engine-clip RMS, with a -1 dBFS peak ceiling." if args.reference_clips else "Peak normalization.",
        },
        "outputs": outputs,
    }
    previous_idle_review = existing.get("processing", {}).get("idleRevision")
    if previous_idle_review:
        manifest["processing"]["idleRevision"] = previous_idle_review
    (args.output / "SOURCE_MANIFEST.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
