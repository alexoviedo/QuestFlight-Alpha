"""Validate the reviewed cockpit representation and source-identity contract."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_MANIFEST = (
    ROOT / "tools" / "cockpit_dynamics" / "cockpit_representation_manifest_v2.json"
)

EXPECTED_MOTION_CHANNELS = {
    "pilot_yoke": ["aileron", "elevator"], "copilot_yoke": ["aileron", "elevator"],
    "throttle": ["throttle"], "mixture": ["mixture"],
    "alt_bat_master": ["batteryMaster", "alternatorMaster"], "primer": ["primer"],
    "ignition_start_key": ["ignitionPosition", "starterEngaged"],
    "avionics_master": ["avionicsMaster"], "wing_flap_selector": ["flapCommand"],
    "fuel_selector": ["fuelSelector"],
    "exterior_flight_controls": ["aileron", "elevator", "rudder", "flapActual", "groundSteering"],
}
EXPECTED_IMPLEMENTED_IDS = set(EXPECTED_MOTION_CHANNELS)
EXPECTED_PRESENTATION_ITEMS = {
    "implementedInstrumentPresentation": {
        "airspeedIndicator", "verticalSpeedIndicator", "directionalGyroCard", "magneticCompassCard",
        "altimeterTenThousands", "altimeterThousands", "altimeterHundreds", "altimeterBarometricSetting",
        "flapPositionIndicator", "brakePressureIndicator", "tachometer", "turnCoordinator",
        "slipSkidBall", "attitudeGyroSphere",
    },
    "implementedAvionicsPresentation": {"gpsKbduDepartureStatus", "com1", "com2", "transponder1200AltitudeMode"},
    "implementedTouchInteraction": {"pilotYoke", "throttle", "mixture", "altBatMaster", "primer",
        "ignitionStart", "avionicsMaster", "wingFlapSelector", "fuelSelector"},
}



def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def validate_manifest(
    manifest_path: Path = DEFAULT_MANIFEST,
    inspection_path: Path | None = None,
) -> dict[str, Any]:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    errors: list[str] = []

    if manifest.get("schemaVersion") != 3:
        errors.append("schemaVersion must be 3")
    if manifest.get("runtimeHierarchyConstructionAllowed") is not False:
        errors.append("runtime hierarchy construction must remain prohibited")

    source = ROOT / str(manifest.get("sourceAsset", ""))
    if not source.is_file():
        errors.append(f"source asset is missing: {source}")
    else:
        if source.stat().st_size != manifest.get("sourceBytes"):
            errors.append("source byte count does not match the reviewed asset")
        if _sha256(source) != manifest.get("sourceSha256"):
            errors.append("source SHA256 does not match the reviewed asset")

    architecture = manifest.get("authorityArchitecture")
    if not isinstance(architecture, dict):
        errors.append("authorityArchitecture must be an object")
        architecture = {}
    for key, expected in (("owner", "ProductionCockpitSystemsController"),
                          ("flightAuthority", "FlightDynamicsCoordinator"),
                          ("systemsReducer", "CockpitSystemsAuthorityV2Model"),
                          ("zeroInventedRuntimeHierarchy", True)):
        if architecture.get(key) != expected:
            errors.append(f"authorityArchitecture.{key} does not match the reviewed authority")
    authoritative_channels = architecture.get("authoritativeOrDeterministicChannels")
    if (not isinstance(authoritative_channels, list) or not authoritative_channels
            or not all(isinstance(item, str) for item in authoritative_channels)
            or not {"flightControlCommands", "engineRpm", "pressureAltitude", "magneticHeading"}.issubset(authoritative_channels)):
        errors.append("authorityArchitecture requires the authoritative flight channels")

    implemented = manifest.get("implementedMotion")
    if not isinstance(implemented, list) or not implemented:
        errors.append("implementedMotion must be a non-empty array")
        implemented = []
    identifiers = [entry.get("id") for entry in implemented if isinstance(entry, dict)]
    if len(identifiers) != len(implemented) or any(not isinstance(identifier, str) or not identifier for identifier in identifiers):
        errors.append("every implemented motion target requires a string id")
        identifiers = [identifier for identifier in identifiers if isinstance(identifier, str) and identifier]
    if len(identifiers) != len(set(identifiers)):
        errors.append("implemented target ids must be unique")
    if set(identifiers) != EXPECTED_IMPLEMENTED_IDS:
        errors.append("implemented target ids must exactly match the serialized reviewed baseline")
    for entry in implemented:
        if not isinstance(entry, dict):
            errors.append("each implemented motion target must be an object")
            continue
        identifier = entry.get("id")
        if not entry.get("geometry"):
            errors.append(f"implemented target {identifier} has no represented geometry")
        if not isinstance(identifier, str) or entry.get("channels") != EXPECTED_MOTION_CHANNELS.get(identifier):
            errors.append(f"implemented target {identifier} has invalid authoritative channels")
        owner = entry.get("owner", architecture.get("owner"))
        if not isinstance(owner, str) or owner not in {
                "ProductionCockpitSystemsController", "ProductionAircraftControlAnimator", "ProductionCockpitDynamicsController"}:
            errors.append(f"implemented target {identifier} has no reviewed owner")
        for key in ("renderCostDelta", "maximumRendererDelta", "maximumDrawDelta", "maximumMaterialDelta", "maximumTextureDelta"):
            value = entry.get(key)
            if value is not None and (type(value) is not int or value < 0):
                errors.append(f"{identifier}.{key} must be a non-negative integer")

    for section, expected in EXPECTED_PRESENTATION_ITEMS.items():
        presentation = manifest.get(section)
        if not isinstance(presentation, dict):
            errors.append(f"{section} must be an object")
            continue
        key = "targets" if section == "implementedTouchInteraction" else "items"
        items = presentation.get(key)
        if not isinstance(items, list) or not all(isinstance(item, str) for item in items):
            errors.append(f"{section}.{key} must be a string array")
        elif len(items) != len(set(items)) or set(items) != expected:
            errors.append(f"{section}.{key} must match the unique reviewed presentation")
        if section != "implementedTouchInteraction":
            for budget in ("rendererCount", "materialCount"):
                if presentation.get(budget) != 1:
                    errors.append(f"{section}.{budget} must remain one")
        elif presentation.get("bindingCount") != 9 or presentation.get("hands") != "left_or_right":
            errors.append("implementedTouchInteraction must retain nine either-hand bindings")

    represented = manifest.get("representedButBlocked")
    if not isinstance(represented, list):
        errors.append("representedButBlocked must be an array")
        represented = []
    represented_ids = []
    for entry in represented:
        if not isinstance(entry, dict) or not isinstance(entry.get("id"), str) or not entry.get("id") or not entry.get("reason"):
            errors.append("each represented blocked target requires an id and reason")
        else:
            represented_ids.append(entry["id"])
    excluded = manifest.get("excludedBecauseNotRepresented")
    if not isinstance(excluded, list) or not all(isinstance(item, str) and item for item in excluded):
        errors.append("excludedBecauseNotRepresented must be a string array")
        excluded = []
    all_ids = identifiers + represented_ids + excluded
    if len(all_ids) != len(set(all_ids)):
        errors.append("target ids must be disjoint across motion, blocked and excluded classifications")

    if inspection_path is not None:
        inspection = json.loads(inspection_path.read_text(encoding="utf-8"))
        inspection_source = Path(str(inspection.get("source", ""))).resolve()
        if inspection_source != source.resolve():
            errors.append("inspection source does not match the reviewed source asset")
        if inspection.get("animationCount") != manifest.get("embeddedAnimationClips"):
            errors.append("inspection animation count does not match the manifest")
        if inspection.get("objectCount", 0) <= 0:
            errors.append("inspection contains no source objects")

    return {
        "passed": not errors,
        "manifest": str(manifest_path.resolve()),
        "source": str(source.resolve()),
        "implementedTargetCount": len(implemented),
        "implementedTargetIds": sorted(identifiers),
        "representedCandidateCount": len(represented),
        "excludedTargetCount": len(excluded),
        "errors": errors,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--inspection", type=Path)
    parser.add_argument("--output", type=Path)
    arguments = parser.parse_args()
    report = validate_manifest(arguments.manifest.resolve(), arguments.inspection)
    payload = json.dumps(report, indent=2) + "\n"
    if arguments.output is not None:
        arguments.output.parent.mkdir(parents=True, exist_ok=True)
        arguments.output.write_text(payload, encoding="utf-8")
    print(payload, end="")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
