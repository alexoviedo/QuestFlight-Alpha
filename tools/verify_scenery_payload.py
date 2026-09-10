"""Verify the authored aerial/building bundle payload before and after packaging."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile


def digest(stream):
    return hashlib.file_digest(stream, "sha256").hexdigest()


def inventory(streaming_root):
    records = []
    for folder, manifest, array, field in (
        ("QuestFlightAerialClipmap", "clipmap_manifest.json", "pages", "bundle_name"),
        ("QuestFlightBuildingClipmap", "bundle_index.json", "bundles", "name"),
    ):
        root = streaming_root / folder
        document = json.loads((root / manifest).read_text(encoding="utf-8-sig"))
        names = {record[field] for record in document[array]}
        if not names:
            raise ValueError(f"Empty scenery manifest: {folder}/{manifest}")
        for name in names:
            if not isinstance(name, str) or not name or Path(name).name != name or "\\" in name or name in (".", ".."):
                raise ValueError(f"Invalid bundle name: {name!r}")
        names.add(manifest)
        if folder == "QuestFlightBuildingClipmap":
            names.add("build_stamp.txt")
        for name in sorted(names):
            path = root / name
            if not path.is_file() or path.stat().st_size == 0:
                raise ValueError(f"Missing or empty scenery payload: {path}")
            with path.open("rb") as stream:
                records.append({"path": f"assets/{folder}/{name}",
                                "bytes": path.stat().st_size, "sha256": digest(stream)})
    # Optional for historical fixtures; production preflight requires this folder.
    branch_root=streaming_root/'QuestFlightBranchGroves'
    if branch_root.exists():
        branch_manifest=streaming_root.parent/'Production/Environment/MapBranchGrovesV1/SOURCE_MANIFEST.json'
        document=json.loads(branch_manifest.read_text(encoding='utf-8-sig'))
        if not document['complete'] or len(document['pages'])!=1296:
            raise ValueError('Incomplete branch canopy payload')
        for page in document['pages']:
            name=page['file']
            if Path(name).name!=name or not name.startswith('branches_') or not name.endswith('.bytes'):
                raise ValueError('Invalid branch page path')
            path=branch_root/name
            with path.open('rb') as stream: actual=digest(stream)
            if path.stat().st_size!=page['bytes'] or actual!=page['sha256']:
                raise ValueError('Branch payload changed: '+name)
            records.append({'path':f'assets/QuestFlightBranchGroves/{name}','bytes':page['bytes'],'sha256':actual})
    return {"schema": "questflight.scenery-payload.v1", "files": records}


def verify(apk, expected):
    with zipfile.ZipFile(apk) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError("APK has duplicate archive entries")
        for record in expected["files"]:
            name = record["path"]
            if name not in names or archive.getinfo(name).file_size != record["bytes"]:
                raise ValueError(f"APK scenery missing or wrong size: {name}")
            with archive.open(name) as stream:
                if digest(stream) != record["sha256"]:
                    raise ValueError(f"APK scenery checksum mismatch: {name}")
    return {"passed": True, "verifiedFiles": len(expected["files"])}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--streaming-root", type=Path)
    parser.add_argument("--inventory", required=True, type=Path)
    parser.add_argument("--apk", type=Path)
    args = parser.parse_args()
    if args.apk:
        print(json.dumps(verify(args.apk, json.loads(args.inventory.read_text()))))
    else:
        if args.streaming_root is None:
            parser.error("--streaming-root is required when writing an inventory")
        data = inventory(args.streaming_root)
        args.inventory.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
        print(f"Inventoried {len(data['files'])} authored scenery files")


if __name__ == "__main__":
    main()
