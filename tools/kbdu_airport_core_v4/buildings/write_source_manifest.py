#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
TOOLS = Path(__file__).resolve().parent
RUNTIME = ROOT / "QuestFlight/Assets/Production/Environment/AirportCoreV4/Buildings"
REPORT = RUNTIME / "Data/AirportCoreBuildingGenerationReportV4.json"
OUTPUT = RUNTIME / "AirportCoreBuildingSourceManifestV4.json"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    generation = json.loads(REPORT.read_text(encoding="utf-8"))
    assets = []
    for asset_id, asset in generation["assets"].items():
        if asset_id == "AirportCoreProps":
            source = ROOT / asset["source_blend"]
            assets.append({"role": "source_blend", "asset_id": asset_id, "path": asset["source_blend"], "bytes": source.stat().st_size, "sha256": sha256(source)})
            for member, record in asset["members"].items():
                for lod in record["lods"]:
                    path = ROOT / lod["glb"]
                    assets.append({"role": "runtime_lod", "asset_id": member, "lod": lod["lod"], "path": lod["glb"], "bytes": path.stat().st_size, "sha256": sha256(path)})
            continue
        source = ROOT / asset["source_blend"]
        assets.append({"role": "source_blend", "asset_id": asset_id, "path": asset["source_blend"], "bytes": source.stat().st_size, "sha256": sha256(source)})
        for lod in asset["lods"]:
            path = ROOT / lod["glb"]
            assets.append({"role": "runtime_lod", "asset_id": asset_id, "lod": lod["lod"], "path": lod["glb"], "bytes": path.stat().st_size, "sha256": sha256(path)})
    for tool_name in (
        "generate_airport_core_buildings.py",
        "airport_core_building_spec.json",
        "airport_core_replacement_footprints.json",
        "validate_airport_core_buildings.py",
        "write_source_manifest.py",
        "write_unity_meta.py",
        "render_building_previews.py",
        "make_reference_contact_sheet.py",
        "make_model_preview_contact_sheet.py",
    ):
        path = TOOLS / tool_name
        assets.append({"role": "authoring_input", "path": str(path.relative_to(ROOT)).replace("\\", "/"), "bytes": path.stat().st_size, "sha256": sha256(path)})
    manifest = {
        "schema_version": 1,
        "status": "PROJECT_AUTHORED_CANDIDATE_REQUIRES_INTEGRATED_VISUAL_QA",
        "license": "Project-authored deterministic geometry and scripts",
        "author": "QuestFlight project",
        "created_utc_date": "2026-07-17",
        "reference_photographs_embedded": False,
        "external_reference_board_committed": False,
        "external_reference_board_location": "Product artifact root / workstream_a / reference_board",
        "factual_sources": [
            {"url": "https://bouldercolorado.gov/airport-hangar-addresses", "use": "official address arrangement and terminal identity", "rights_handling": "facts only; City image remains external"},
            {"url": "https://bouldercolorado.gov/services/airport-hangar-and-tie-down-leasing", "use": "official Type 2 years, addresses, dimensions, door type/orientation, and floor", "rights_handling": "facts only"},
            {"url": "https://bouldercolorado.gov/projects/airport-master-plan", "use": "official general-aviation and terminal-area context", "rights_handling": "facts only"},
            {"url": "https://airport-data.com/airport/photo/007472.html", "use": "terminal massing and facade cues", "rights_handling": "copyrighted reference-only image kept external"},
            {"url": "https://airport-data.com/airport/photo/007473.html", "use": "terminal facade and apron cues", "rights_handling": "copyrighted reference-only image kept external"},
            {"url": "https://commons.wikimedia.org/wiki/File:Boulder_Municipal_Airport.JPG", "use": "airport/apron scale and context", "rights_handling": "CC BY 4.0 copy kept external; no pixels embedded"},
            {"url": "https://stories.opengov.com/cityofboulderco/published/SoI4Kud_R", "use": "airport-wide context", "rights_handling": "reference-only images kept external"}
        ],
        "material_dependency": {
            "owner": "Workstream B",
            "terminal_material": "AirportCoreTerminalAtlas",
            "hangar_kit_material": "AirportCoreHangarKitAtlas",
            "textures_embedded_in_glb": False
        },
        "assets": assets,
        "limitations": [
            "Models are reference-faithful authoring candidates, not survey-grade replicas.",
            "No model is visually accepted until Unity atlas binding, clean plate, lighting, full-resolution QA, stereo review, and Quest budgets pass.",
            "FuelServiceCore is intentionally unplaced because no survey-grade public footprint was identified."
        ]
    }
    OUTPUT.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps({"status": "PASS", "output": str(OUTPUT.relative_to(ROOT)), "asset_records": len(assets)}, indent=2))


if __name__ == "__main__":
    main()
