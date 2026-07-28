#!/usr/bin/env python3
"""Compile the KBDU airport boundary into a compact, reviewable fence authority.

The raw OSM extract remains external. The committed output is an ODbL-produced
work containing only the selected airport-boundary vertices and provenance.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
ENVIRONMENT_TOOLS = REPO_ROOT / "tools" / "kbdu_environment"
sys.path.insert(0, str(ENVIRONMENT_TOOLS))

from kbdu_common import geodetic_to_enu  # noqa: E402

WAY_ID = 77513664
ORIGIN_LATITUDE = 40.03936527
ORIGIN_LONGITUDE = -105.22608958
ORIGIN_ELEVATION = 1611.7824

DEFAULT_OUTPUT = (
    REPO_ROOT
    / "QuestFlight"
    / "Assets"
    / "Production"
    / "Environment"
    / "AirportFenceV1"
    / "Data"
    / "KBDU_AirportFenceSourceV1.json"
)

SECTION_CONTRACTS = (
    {
        "stable_id": "northeast_wildlife_fence",
        "start_index": 0,
        "end_index": 10,
        "representation": "four_foot_metal_wildlife_chain_link",
        "height_meters": 1.22,
        "color": "weathered dark galvanized metal",
        "gate_segment_indices": [],
    },
    {
        "stable_id": "south_black_chain_link",
        "start_index": 10,
        "end_index": 15,
        "representation": "black_chain_link_security_fence",
        "height_meters": 1.83,
        "color": "low-gloss black",
        "gate_segment_indices": [0],
    },
    {
        "stable_id": "west_hangar_fence",
        "start_index": 15,
        "end_index": 23,
        "representation": "weathered_wood_with_metal_replacement",
        "height_meters": 1.22,
        "color": "weathered brown and dark metal",
        "gate_segment_indices": [0],
    },
    {
        "stable_id": "northwest_wildlife_fence",
        "start_index": 23,
        "end_index": 26,
        "representation": "four_foot_metal_wildlife_chain_link",
        "height_meters": 1.22,
        "color": "weathered dark galvanized metal",
        "gate_segment_indices": [],
    },
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def parse_way(snapshot: Path) -> list[dict[str, float | int]]:
    root = ET.parse(snapshot).getroot()
    nodes = {
        int(node.attrib["id"]): (
            float(node.attrib["lat"]),
            float(node.attrib["lon"]),
        )
        for node in root.findall("node")
    }
    selected = None
    for way in root.findall("way"):
        if int(way.attrib["id"]) == WAY_ID:
            selected = way
            break
    if selected is None:
        raise ValueError(f"OSM way {WAY_ID} is absent from {snapshot}")

    tags = {
        tag.attrib["k"]: tag.attrib["v"]
        for tag in selected.findall("tag")
    }
    if tags.get("aeroway") != "aerodrome" or tags.get("icao") != "KBDU":
        raise ValueError(f"OSM way {WAY_ID} is not the KBDU aerodrome boundary")

    points: list[dict[str, float | int]] = []
    for index, reference in enumerate(selected.findall("nd")):
        node_id = int(reference.attrib["ref"])
        if node_id not in nodes:
            raise ValueError(f"OSM node {node_id} is missing from the snapshot")
        latitude, longitude = nodes[node_id]
        east, north, _ = geodetic_to_enu(
            latitude,
            longitude,
            ORIGIN_ELEVATION,
            ORIGIN_LATITUDE,
            ORIGIN_LONGITUDE,
            ORIGIN_ELEVATION,
        )
        points.append(
            {
                "index": index,
                "osm_node_id": node_id,
                "latitude_degrees": round(latitude, 7),
                "longitude_degrees": round(longitude, 7),
                "east_meters": round(east, 3),
                "north_meters": round(north, 3),
            }
        )
    if len(points) < 4 or points[0]["osm_node_id"] != points[-1]["osm_node_id"]:
        raise ValueError("KBDU boundary is not a closed polyline")
    return points


def build(snapshot: Path) -> dict:
    points = parse_way(snapshot)
    sections = []
    for contract in SECTION_CONTRACTS:
        start = contract["start_index"]
        end = contract["end_index"]
        if not isinstance(start, int) or not isinstance(end, int):
            raise TypeError("Fence section indices must be integers")
        section_points = points[start : end + 1]
        if len(section_points) < 2:
            raise ValueError(f"Fence section {contract['stable_id']} is empty")
        sections.append(
            {
                key: value
                for key, value in contract.items()
                if key not in {"start_index", "end_index"}
            }
            | {
                "source_vertex_start": start,
                "source_vertex_end": end,
                "points": section_points,
            }
        )

    return {
        "schema_version": "1",
        "stable_id": "kbdu-airport-perimeter-fence-v1",
        "coordinate_frame": "KBDU local tangent ENU; Unity +X=east, +Z=north",
        "origin": {
            "latitude_degrees": ORIGIN_LATITUDE,
            "longitude_degrees": ORIGIN_LONGITUDE,
            "elevation_msl_meters": ORIGIN_ELEVATION,
        },
        "source": {
            "name": "OpenStreetMap KBDU aerodrome boundary",
            "way_id": WAY_ID,
            "snapshot_sha256": sha256(snapshot),
            "snapshot_status": "external_only_not_committed",
            "source_url": "https://www.openstreetmap.org/way/77513664",
            "license": "Open Database License 1.0",
            "attribution": "© OpenStreetMap contributors",
            "derived_status": "ODbL Produced Work",
        },
        "reference_findings": {
            "city_2007_master_plan": (
                "Airport layout plan identifies an airport security fence and "
                "a gated vehicle barrier southeast of the terminal."
            ),
            "city_2007_inventory": (
                "Four-foot chain-link wildlife fencing existed around portions "
                "of the airport."
            ),
            "city_2024_capital_plan": (
                "Adequate wildlife fence exists on the northeast; older wood "
                "fence remains south of west hangars; newer black chain-link "
                "appears sporadically on the south. The AOA fence was not a "
                "continuous high-security enclosure in 2024."
            ),
            "simulation_decision": (
                "Use the current aerodrome boundary as the closed placement "
                "authority requested for the simulation while retaining the "
                "documented segment character. This is visually aligned, not "
                "survey-grade security-fence data."
            ),
        },
        "runtime_contract": {
            "authoring_only": True,
            "serialized_fixed_meshes": True,
            "section_count": len(sections),
            "lod_count_per_section": 1,
            "maximum_selected_draws": len(sections),
            "new_materials": 0,
            "new_textures": 0,
            "colliders": 0,
            "realtime_shadows": False,
            "lod_switching": False,
        },
        "sections": sections,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--osm-snapshot", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()

    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    document = build(args.osm_snapshot.resolve())
    output.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    print(
        json.dumps(
            {
                "output": str(output),
                "sections": len(document["sections"]),
                "source_vertices": sum(
                    len(section["points"]) for section in document["sections"]
                ),
                "snapshot_sha256": document["source"]["snapshot_sha256"],
            },
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
