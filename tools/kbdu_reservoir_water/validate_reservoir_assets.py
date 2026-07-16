#!/usr/bin/env python3
"""Validate committed Boulder Reservoir texture, geometry, shader, and cost contracts."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

from generate_reservoir_assets import (
    DEFAULT_MANIFEST,
    DEFAULT_MESH,
    DEFAULT_REFERENCE,
    REPO_ROOT,
    parse_unity_mesh,
    sha256,
    validate_geometry,
)


SHADER_PATH = REPO_ROOT / (
    "QuestFlight/Assets/Resources/QuestFlightLab/Environment/Shaders/ProductionStableWater.shader"
)
MATERIAL_PATH = REPO_ROOT / (
    "QuestFlight/Assets/Production/Environment/Materials/Production_BoulderReservoir_Opaque.mat"
)


def validate(manifest_path: Path) -> dict:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    generator_path = REPO_ROOT / manifest["generator"]["path"]
    if sha256(generator_path) != manifest["generator"]["sha256"]:
        raise ValueError("Committed reservoir generator hash is stale")
    if sha256(DEFAULT_MESH) != manifest["source"]["authoritative_mesh_sha256"]:
        raise ValueError("Committed authoritative reservoir mesh hash is stale")
    if sha256(DEFAULT_REFERENCE) != manifest["source"]["reference_context_sha256"]:
        raise ValueError("Committed reservoir source-context hash is stale")
    mesh = parse_unity_mesh(DEFAULT_MESH)
    geometry = validate_geometry(mesh, DEFAULT_REFERENCE)
    if geometry != manifest["geometry_validation"]:
        raise ValueError("Committed geometry validation manifest is stale")

    for asset in manifest["derived_assets"]:
        path = REPO_ROOT / asset["path"]
        if not path.is_file() or path.stat().st_size != asset["bytes"] or sha256(path) != asset["sha256"]:
            raise ValueError(f"Derived reservoir asset hash/size mismatch: {path}")
        meta = Path(str(path) + ".meta")
        if not meta.is_file():
            raise ValueError(f"Derived reservoir asset is missing its Unity .meta file: {path}")
        meta_text = meta.read_text(encoding="utf-8")
        required_import = ("enableMipMap: 1", "sRGBTexture: 0", "buildTarget: Android", "textureFormat: 50")
        for token in required_import:
            if token not in meta_text:
                raise ValueError(f"{path.name} importer is missing {token!r}")

    shader = SHADER_PATH.read_text(encoding="utf-8")
    required_shader_tokens = (
        '"RenderType"="Opaque"',
        "ZWrite On",
        "_BroadNormal",
        "_RippleNormal",
        "_DepthShoreRoughness",
        "_EnvironmentCube",
        "SchlickFresnel",
        "texCUBElod(_EnvironmentCube",
        "_WorldSpaceLightPos0",
        "UNITY_VERTEX_OUTPUT_STEREO",
    )
    for token in required_shader_tokens:
        if token not in shader:
            raise ValueError(f"Quest water shader is missing {token!r}")
    prohibited_shader_tokens = (
        "GrabPass",
        "Blend SrcAlpha",
        "unity_SpecCube0",
        "_CameraOpaqueTexture",
        "_CameraDepthTexture",
        "tessell",
    )
    for token in prohibited_shader_tokens:
        if token in shader:
            raise ValueError(f"Quest water shader contains prohibited path {token!r}")
    if len(re.findall(r"^\s*Pass\s*$", shader, flags=re.MULTILINE)) != 1:
        raise ValueError("Quest water shader must contain exactly one pass")

    material = MATERIAL_PATH.read_text(encoding="utf-8")
    for token in ("_BroadNormal:", "_RippleNormal:", "_DepthShoreRoughness:", "_EnvironmentCube:", "_ZWrite: 1"):
        if token not in material:
            raise ValueError(f"Reservoir material is missing {token!r}")

    return {
        "status": "PASS",
        "manifest": str(manifest_path),
        "geometry": geometry,
        "derived_asset_count": len(manifest["derived_assets"]),
        "estimated_android_astc_6x6_bytes_with_full_mips": manifest[
            "estimated_android_astc_6x6_bytes_with_full_mips"
        ],
        "shader_pass_count": 1,
        "water_surface_draw_slots": 1,
        "water_surface_materials": 1,
        "transparent_slots": 0,
        "selected_sky_cubemap": "required integration injection; validator does not substitute a fallback",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    report = validate(args.manifest.resolve())
    rendered = json.dumps(report, indent=2, sort_keys=True) + "\n"
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(rendered, encoding="utf-8")
    print(rendered, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
