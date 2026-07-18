#!/usr/bin/env python3
from __future__ import annotations

import hashlib
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
SOURCE_FILES = [
    ROOT / "QuestFlight/Assets/ArtSource/Blender/KBDU/ExecutiveTerminal.blend",
    ROOT / "QuestFlight/Assets/ArtSource/Blender/KBDU/Type2THangarRow.blend",
    ROOT / "QuestFlight/Assets/ArtSource/Blender/KBDU/FuelServiceCore.blend",
    ROOT / "QuestFlight/Assets/ArtSource/Blender/KBDU/AirportCoreProps.blend",
]
RUNTIME_ROOT = ROOT / "QuestFlight/Assets/Production/Environment/AirportCoreV4/Buildings"
GLB_TEMPLATE = ROOT / "QuestFlight/Assets/Production/Environment/AirportKit/KBDU_AirportKit.glb.meta"
MODEL_TEMPLATE = ROOT / "QuestFlight/Assets/Production/Environment/ReconstructionV2/vegetation_meshes/rocky_mountain_juniper_LOD0.obj.meta"


def guid(path: Path) -> str:
    relative = str(path.relative_to(ROOT)).replace("\\", "/").lower()
    return hashlib.md5(("airport-core-v4:" + relative).encode("utf-8")).hexdigest()


def default_meta(path: Path, folder: bool) -> str:
    marker = "folderAsset: yes\n" if folder else ""
    return f"fileFormatVersion: 2\nguid: {guid(path)}\n{marker}DefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"


def templated_meta(path: Path, template: Path) -> str:
    lines = template.read_text(encoding="utf-8-sig").splitlines()
    return "\n".join((f"guid: {guid(path)}" if line.startswith("guid: ") else line) for line in lines) + "\n"


def write(path: Path) -> None:
    meta = Path(str(path) + ".meta")
    if meta.exists():
        return
    if path.is_dir():
        text = default_meta(path, True)
    elif path.suffix.lower() == ".glb":
        text = templated_meta(path, GLB_TEMPLATE)
    elif path.suffix.lower() == ".blend":
        text = templated_meta(path, MODEL_TEMPLATE)
    else:
        text = default_meta(path, False)
    meta.write_text(text, encoding="utf-8", newline="\n")


def main() -> None:
    count = 0
    roots = [RUNTIME_ROOT]
    for source in SOURCE_FILES:
        roots.append(source.parent)
    seen_parents: set[Path] = set()
    for tree in roots:
        parents = []
        current = tree
        while current != ROOT / "QuestFlight/Assets" and current.is_relative_to(ROOT / "QuestFlight/Assets"):
            parents.append(current)
            current = current.parent
        for path in reversed(parents):
            if path in seen_parents:
                continue
            seen_parents.add(path)
            before = Path(str(path) + ".meta").exists()
            write(path)
            count += int(not before)
    for path in SOURCE_FILES:
        before = Path(str(path) + ".meta").exists()
        write(path)
        count += int(not before)
    for path in sorted(RUNTIME_ROOT.rglob("*")):
            if path.name.endswith(".meta") or path.suffix.lower() == ".blend1":
                continue
            before = Path(str(path) + ".meta").exists()
            write(path)
            count += int(not before)
    print(f"Wrote {count} paired Unity meta files")


if __name__ == "__main__":
    main()
