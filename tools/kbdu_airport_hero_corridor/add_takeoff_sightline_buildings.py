#!/usr/bin/env python3
"""Extend the airport hero manifest with the bounded pilot-left takeoff sightline.

The source is the repository's pinned OSM snapshot.  This does not fetch data and
does not infer survey-grade architecture; it preserves footprint identity and
uses OSM semantic tags plus deterministic, conservative height rules.
"""
from __future__ import annotations

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MANIFEST = Path(__file__).with_name("airport_hero_corridor_manifest.json")
CONTEXT = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/ReconstructionV2/"
    / "KBDU_ReferenceContextV2.json"
)
BASE_IDS = {
    77499385, 77499386, 77499387, 77499388, 77499394,
    78054436, 78054447, 78054451, 78054452, 78054454, 78054456,
    78054458, 78054459, 78054461, 78054465, 78054470, 78054473,
    78060787,
}


def family(tags: dict, width: float, depth: float) -> str:
    kind = str(tags.get("building", "yes")).lower()
    if kind in {"house", "residential", "detached", "apartments"}:
        return "residential"
    if kind in {"commercial", "retail", "office"} or tags.get("name"):
        return "commercial"
    if max(width, depth) >= 32:
        return "light_industrial"
    return "residential"


def main() -> None:
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    manifest["buildings"] = [
        row for row in manifest["buildings"] if int(row["osm_id"]) in BASE_IDS
    ]
    context = json.loads(CONTEXT.read_text(encoding="utf-8"))
    quantum = float(context["coordinate_quantization_meters"])
    additions = []
    for feature in context["openstreetmap"]["features"]:
        if feature.get("category") != "building" or feature.get("geometry_type") != "polygon":
            continue
        values = feature.get("points_q", [])
        points = [
            [round(values[index] * quantum, 3), round(values[index + 1] * quantum, 3)]
            for index in range(0, len(values), 2)
        ]
        if len(points) < 4:
            continue
        cx = sum(point[0] for point in points) / len(points)
        cz = sum(point[1] for point in points) / len(points)
        # Pilot-left from the authored runway-08 departure is product +Z.
        # The bounds stop before ordinary city scenery becomes the dominant view.
        if not (-900.0 <= cx <= 900.0 and 50.0 <= cz <= 700.0):
            continue
        osm_id = int(feature["source"]["osm_id"])
        if osm_id in BASE_IDS:
            continue
        min_x = min(point[0] for point in points)
        max_x = max(point[0] for point in points)
        min_z = min(point[1] for point in points)
        max_z = max(point[1] for point in points)
        tags = feature.get("tags", {})
        building_family = family(tags, max_x - min_x, max_z - min_z)
        source_height = float(feature.get("render_height_meters", 0.0) or 0.0)
        if building_family == "residential":
            height = min(9.5, max(4.8, source_height or 6.2))
        elif building_family == "commercial":
            height = min(13.0, max(5.5, source_height or 7.2))
        else:
            height = min(11.0, max(5.5, source_height or 6.8))
        additions.append(
            {
                "asset_id": f"TakeoffLeft_{osm_id}",
                "address": tags.get("name") or f"Near-airport OSM {osm_id}",
                "osm_id": osm_id,
                "family": building_family,
                "height_m": round(height, 2),
                "door_side": "-y",
                "footprint_xz_m": points,
                "scope_group": "pilot_left_runway08_takeoff",
                "source_tags": {
                    key: tags[key]
                    for key in ("building", "name", "building:levels")
                    if key in tags
                },
            }
        )
    additions.sort(key=lambda row: (row["footprint_xz_m"][0][1], row["osm_id"]))
    manifest["buildings"].extend(additions)
    manifest["scope"] += (
        " The bounded pilot-left runway-08 takeoff sightline is also represented "
        "with semantic footprint-faithful near buildings."
    )
    manifest["takeoff_sightline_contract"] = {
        "source": str(CONTEXT.relative_to(ROOT)).replace("\\", "/"),
        "bounds_xz_m": [-900.0, 50.0, 900.0, 700.0],
        "building_count": len(additions),
        "classification": "OSM tags plus deterministic footprint-size fallback",
        "limitations": "Reference-faithful footprint and semantic class; not surveyed facade geometry.",
    }
    MANIFEST.write_text(
        json.dumps(manifest, indent=2, sort_keys=False) + "\n", encoding="utf-8"
    )
    print(json.dumps({"added": len(additions), "total": len(manifest["buildings"])}, indent=2))


if __name__ == "__main__":
    main()
