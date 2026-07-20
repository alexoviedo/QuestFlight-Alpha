#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import struct
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
RUNTIME = ROOT / "QuestFlight/Assets/Production/Environment/AirportCoreV4/Buildings"
REPORT_PATH = RUNTIME / "Data/AirportCoreBuildingGenerationReportV4.json"
MANIFEST_PATH = RUNTIME / "AirportCoreBuildingSourceManifestV4.json"
SPEC_PATH = Path(__file__).resolve().parent / "airport_core_building_spec.json"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def glb_json(path: Path) -> dict:
    data = path.read_bytes()
    magic, version, length = struct.unpack_from("<4sII", data, 0)
    assert magic == b"glTF" and version == 2 and length == len(data), path
    chunk_length, chunk_type = struct.unpack_from("<II", data, 12)
    assert chunk_type == 0x4E4F534A, path
    return json.loads(data[20 : 20 + chunk_length].decode("utf-8"))


def validate_lods(asset_id: str, record: dict, ratio_required: bool) -> list[dict]:
    rows = record["lods"]
    assert [row["lod"] for row in rows] == [0, 1, 2], asset_id
    triangles = [row["triangles"] for row in rows]
    assert triangles[0] > triangles[1] > triangles[2] > 0, (asset_id, triangles)
    if ratio_required:
        assert 0.35 <= triangles[1] / triangles[0] <= 0.50, (asset_id, triangles)
        assert 0.08 <= triangles[2] / triangles[0] <= 0.15, (asset_id, triangles)
    for row in rows:
        path = ROOT / row["glb"]
        assert path.exists() and path.stat().st_size == row["glb_bytes"] > 1024, path
        assert sha256(path) == row["glb_sha256"]
        assert row["mesh_objects"] == 1
        assert row["nonmanifold_edges"] == 0
        assert row["inverted_faces_detected"] == 0
        assert row["uv_layers"] >= 1 and row["normal_vectors"] > 0
        assert row["tangent_validation"] == "PASS", (asset_id, row["lod"], row["tangent_validation"])
        assert all(abs(value) <= 1e-5 for value in row["pivot_xyz_meters"])
        assert abs(row["bounds_min_xyz_meters"][2]) <= 1e-5, (asset_id, row["lod"], row["bounds_min_xyz_meters"])
        graph = glb_json(path)
        assert len(graph.get("meshes", [])) == 1, path
        assert len(graph.get("materials", [])) == 1, path
        assert not graph.get("images") and not graph.get("textures"), path
        primitives = graph["meshes"][0]["primitives"]
        assert len(primitives) == 1, path
        attributes = primitives[0]["attributes"]
        for required in ("POSITION", "NORMAL", "TANGENT", "TEXCOORD_0"):
            assert required in attributes, (path, attributes)
    return rows


def main() -> None:
    report = json.loads(REPORT_PATH.read_text(encoding="utf-8"))
    spec = json.loads(SPEC_PATH.read_text(encoding="utf-8"))
    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    assert report["blender_version"].startswith("5."), report["blender_version"]
    assert report["reference_photographs_embedded"] is False
    assert manifest["license"] == "Project-authored deterministic geometry and scripts"
    assert manifest["reference_photographs_embedded"] is False
    terminal = report["assets"]["ExecutiveTerminal"]
    hangar = report["assets"]["Type2THangarRow"]
    terminal_rows = validate_lods("ExecutiveTerminal", terminal, True)
    hangar_rows = validate_lods("Type2THangarRow", hangar, True)
    assert 20000 <= terminal_rows[0]["triangles"] <= 45000
    assert 25000 <= hangar_rows[0]["triangles"] <= 60000
    assert terminal["material_contract"] == spec["material_contract"]["terminal_material"]
    assert hangar["material_contract"] == spec["material_contract"]["hangar_kit_material"]
    validate_lods("FuelServiceCore", report["assets"]["FuelServiceCore"], False)
    props = report["assets"]["AirportCoreProps"]["members"]
    assert set(props) == set(spec["assets"]["AirportCoreProps"]["members"])
    for prop, record in props.items():
        rows = validate_lods(prop, record, False)
        assert rows[1]["triangles"] / rows[0]["triangles"] <= 0.55, (prop, [row["triangles"] for row in rows])
        assert rows[2]["triangles"] / rows[0]["triangles"] <= 0.30, (prop, [row["triangles"] for row in rows])
    source_records = [item for item in manifest["assets"] if item["role"] == "source_blend"]
    assert {item["asset_id"] for item in source_records} == {"ExecutiveTerminal", "Type2THangarRow", "FuelServiceCore", "AirportCoreProps"}
    for item in manifest["assets"]:
        path = ROOT / item["path"]
        assert path.exists() and path.stat().st_size == item["bytes"]
        assert sha256(path) == item["sha256"]
    forbidden = [path for path in RUNTIME.rglob("*") if path.suffix.lower() in {".jpg", ".jpeg", ".jfif", ".webp", ".zip"}]
    assert not forbidden, forbidden
    missing_meta = []
    source_files = [
        ROOT / "QuestFlight/Assets/ArtSource/Blender/KBDU/ExecutiveTerminal.blend",
        ROOT / "QuestFlight/Assets/ArtSource/Blender/KBDU/Type2THangarRow.blend",
        ROOT / "QuestFlight/Assets/ArtSource/Blender/KBDU/FuelServiceCore.blend",
        ROOT / "QuestFlight/Assets/ArtSource/Blender/KBDU/AirportCoreProps.blend",
    ]
    for path in source_files:
        if not Path(str(path) + ".meta").exists():
            missing_meta.append(str(path.relative_to(ROOT)))
    for base in (RUNTIME,):
        for path in base.rglob("*"):
            if path.name.endswith(".meta") or path.suffix.lower() == ".blend1":
                continue
            if not Path(str(path) + ".meta").exists():
                missing_meta.append(str(path.relative_to(ROOT)))
    assert not missing_meta, missing_meta
    backups = list((ROOT / "QuestFlight/Assets/ArtSource/Blender/KBDU").glob("*.blend1"))
    assert not backups, backups
    summary = {
        "status": "PASS",
        "blender_version": report["blender_version"],
        "terminal_triangles": [row["triangles"] for row in terminal_rows],
        "terminal_lod_ratios": [round(row["triangles"] / terminal_rows[0]["triangles"], 4) for row in terminal_rows],
        "hangar_triangles": [row["triangles"] for row in hangar_rows],
        "hangar_lod_ratios": [round(row["triangles"] / hangar_rows[0]["triangles"], 4) for row in hangar_rows],
        "isolated_glb_lods": "PASS",
        "uv_normal_tangent_contract": "PASS",
        "manifold_and_grounded_pivots": "PASS",
        "paired_meta_files": "PASS",
        "reference_pixels_in_runtime_tree": 0,
        "visual_acceptance": "NOT_CLAIMED"
    }
    output = RUNTIME / "Data/AirportCoreBuildingValidationV4.json"
    output.write_text(json.dumps(summary, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps(summary, indent=2, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(json.dumps({"status": "FAIL", "error": str(exc)}, indent=2), file=sys.stderr)
        raise
