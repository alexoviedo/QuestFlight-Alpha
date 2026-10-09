"""Offline remaster inventory and immutable baseline checkpoints; no runtime code.

The inventory deliberately includes unloaded scenery and StreamingAssets. Static
GUID references are evidence, not proof of the runtime dependency closure.
"""
from __future__ import annotations

import argparse
import collections
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import time
import sys
import concurrent.futures
import os

from PIL import Image
import numpy as np

VERSION = 1
GUID = re.compile(r"guid:\s*([0-9a-f]{32})")
TEXT = {".mat", ".prefab", ".unity", ".asset", ".json", ".cs", ".shader"}
IMAGES = {".png", ".jpg", ".jpeg", ".tga", ".exr"}


def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    staging = path.with_suffix(path.suffix + ".tmp")
    staging.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    staging.replace(path)


def inside(root, relative):
    path = (root / relative).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError(f"Path escapes root: {relative}")
    return path


def checkpoint(repo, output, *, base_ref=None):
    """Preserve staged/unstaged regular files relative to HEAD; ignored files excluded.

    Binary patches retain index modes, deletes and renames. Separate index bytes
    preserve files whose staged and working versions differ. Unmerged and changed
    symlink/submodule states are rejected rather than described as regular files.
    A finalized destination is immutable; identical retries are read-only.
    """
    repo, output = repo.resolve(), output.resolve()
    if output.is_relative_to(repo):
        raise ValueError("Checkpoint output must be outside the source repository")
    def git(*args):
        return subprocess.check_output(["git", *args], cwd=repo)
    if git("ls-files", "--unmerged", "-z"):
        raise ValueError("Resolve unmerged index entries before checkpointing")
    head = subprocess.run(["git", "rev-parse", "--verify", "HEAD"], cwd=repo,
                          capture_output=True, check=False)
    base = head.stdout.decode().strip() if head.returncode == 0 else None
    base_tree = git("rev-parse", f"{base_ref or base}^{{tree}}").decode().strip() if base_ref or base else None
    comparison = [base_tree] if base_tree else []
    staged = git("diff", "--cached", "--binary", "--full-index", *comparison)
    unstaged = git("diff", "--binary", "--full-index")
    names = git("diff", "--cached", "--no-renames", "--name-only", "-z", *comparison).split(b"\0")
    names += git("ls-files", "--modified", "--others", "--exclude-standard", "-z").split(b"\0")
    index = {}
    for entry in git("ls-files", "--stage", "-z").split(b"\0"):
        if not entry:
            continue
        info, name = entry.split(b"\t", 1)
        mode, oid, stage = info.decode().split()
        index[name] = (mode, oid)
    records, payloads = [], {"staged.patch": staged, "unstaged.patch": unstaged}
    for raw in sorted(set(names) - {b""}):
        name = raw.decode("utf-8")
        source = inside(repo, name)
        if (repo / name).is_symlink():
            raise ValueError(f"Changed symlinks are unsupported: {name}")
        record = {"path": name, "deleted": not source.exists(), "index": None}
        if raw in index:
            mode, oid = index[raw]
            if mode not in ("100644", "100755"):
                raise ValueError(f"Changed index mode is unsupported: {name} ({mode})")
            content = git("cat-file", "blob", oid)
            payloads["index/" + name] = content
            record["index"] = {"mode": mode, "sha256": hashlib.sha256(content).hexdigest(), "bytes": len(content)}
        if source.exists():
            if not source.is_file():
                raise ValueError(f"Changed non-file is unsupported: {name}")
            content = source.read_bytes()
            payloads["files/" + name] = content
            record.update(sha256=hashlib.sha256(content).hexdigest(), bytes=len(content))
        records.append(record)
    manifest = {"version": 2, "base_head": base, "base_tree": base_tree, "files": records,
                "scope": "Staged and unstaged regular source files; ignored files excluded; unmerged/symlink/submodule changes unsupported",
                "payloads": {name: hashlib.sha256(data).hexdigest() for name, data in sorted(payloads.items())}}
    serialized = (json.dumps(manifest, indent=2) + "\n").encode("utf-8")
    if output.exists():
        if not (output / "checkpoint.json").is_file() or (output / "checkpoint.json").read_bytes() != serialized:
            raise RuntimeError("Checkpoint is immutable; choose a new output directory")
        for name, data in payloads.items():
            target = inside(output, name)
            if not target.is_file() or target.read_bytes() != data:
                raise RuntimeError(f"Checkpoint payload is corrupt: {name}")
        return {"checkpoint_files": len(records)}
    # Publish only a fully verified snapshot. Rename to a new sibling destination
    # does not overwrite an existing finalized manifest or individual payload.
    import tempfile
    output.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=output.name + ".pending-", dir=output.parent))
    try:
        for name, data in payloads.items():
            target = inside(staging, name)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            if target.read_bytes() != data:
                raise RuntimeError(f"Backup verification failed: {name}")
        (staging / "checkpoint.json").write_bytes(serialized)
        staging.rename(output)
    finally:
        if staging.exists():
            shutil.rmtree(staging)
    return {"checkpoint_files": len(records)}


def inventory(repo, output):
    assets = repo / "QuestFlight/Assets"
    all_paths = sorted(p for p in assets.rglob("*") if p.is_file())
    guids, references, opaque = {}, collections.defaultdict(set), []
    for path in all_paths:
        if path.suffix == ".meta":
            match = GUID.search(path.read_text(encoding="utf-8", errors="replace"))
            if match:
                guids[str(path.with_suffix("").relative_to(repo))] = match[1]
        elif path.suffix in TEXT:
            # Large generated mesh payloads must not be decoded as text.
            with path.open("rb") as stream:
                prefix = stream.read(64)
            if path.stat().st_size > 32 * 1024 * 1024 or b"\0" in prefix:
                opaque.append(str(path.relative_to(repo)))
                continue
            for guid in set(GUID.findall(path.read_text(encoding="utf-8", errors="replace"))):
                references[guid].add(str(path.relative_to(repo)))
    scenery = assets / "Production/Environment"
    generated = assets / "Generated"
    guid_paths = {guid: path for path, guid in guids.items()}
    dependencies = collections.defaultdict(set)
    for guid, consumers in references.items():
        if guid in guid_paths:
            for consumer in consumers:
                dependencies[consumer].add(guid_paths[guid])
    pending = [str(Path("QuestFlight/Assets/Scenes/ProductionVerticalSlice.unity"))]
    reachable = set()
    while pending:
        current = pending.pop()
        if current in reachable:
            continue
        reachable.add(current)
        pending.extend(dependencies.get(current, ()))
    records = []
    for path in all_paths:
        if not (path.is_relative_to(scenery) or path.is_relative_to(generated)) or path.suffix not in IMAGES | {".mat", ".fbx", ".glb", ".obj", ".bytes", ".asset"}:
            continue
        relative = str(path.relative_to(repo))
        family = (path.relative_to(scenery).parts[0] if path.is_relative_to(scenery)
                  else "Generated/" + path.relative_to(generated).parts[0])
        guid = guids.get(relative)
        record = {"path": relative, "family": family, "guid": guid,
                  "sha256": digest(path), "bytes": path.stat().st_size,
                  "static_consumers": sorted(references.get(guid, [])),
                  "static_reachable_from_product_scene": relative in reachable,
                  "status": "pending-contract-review"}
        if path.suffix in IMAGES:
            try:
                with Image.open(path) as image:
                    record.update(dimensions=list(image.size), mode=image.mode)
            except (OSError, ValueError) as exc:
                record["image_inspection_error"] = str(exc)
        meta = Path(str(path) + ".meta")
        if meta.exists():
            record["meta_sha256"] = digest(meta)
            # Preserve the complete importer contract, including per-platform settings.
            record["importer_contract"] = meta.read_text(encoding="utf-8")
        records.append(record)
    bundles = []
    streaming = assets / "StreamingAssets"
    for path in all_paths:
        if path.is_relative_to(streaming) and path.suffix != ".meta":
            bundles.append({"path": str(path.relative_to(repo)),
                            "bytes": path.stat().st_size, "sha256": digest(path)})
    duplicate_groups = collections.defaultdict(list)
    for record in records:
        duplicate_groups[record["sha256"]].append(record["path"])
    aerial_manifest = streaming / "QuestFlightAerialClipmap/clipmap_manifest.json"
    declared_pages = []
    if aerial_manifest.exists():
        for page in json.loads(aerial_manifest.read_text())["pages"]:
            source_path = inside(repo, page["path"])
            declared_pages.append({"path": page["path"], "asset_name": page["asset_name"],
                                   "expected_sha256": page["sha256"], "present": source_path.is_file(),
                                   "bundle_name": page["bundle_name"], "tier": page["tier"]})
    result = {"version": VERSION, "coverage": "all on-disk scenery families, including inactive assets",
              "runtime_closure_verified": False,
              "limitations": ["Static GUID consumers omit runtime string bindings and bundle internals.",
                              "Pending entries are not eligibility decisions or integrated enhancements."],
              "opaque_or_large_reference_files": opaque, "assets": records,
              "static_reachable_assets": sorted(reachable), "declared_aerial_pages": declared_pages,
              "streaming_payload": bundles,
              "identical_content_groups": [v for v in duplicate_groups.values() if len(v) > 1]}
    write_json(output, result)
    return {"assets": len(records), "families": dict(collections.Counter(r["family"] for r in records)),
            "streaming_files": len(bundles), "manifest": str(output)}


def process_facades(repo, output, backend):
    """Stage all 16 RGB slices; never process packed maps or integrate unseen art."""
    source = repo / "QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/Materials/FullMapBuildingFacadeArray_Albedo.png"
    source_manifest = source.with_name("FullMapBuildingFacadeArray_SOURCE_MANIFEST.json")
    metadata = json.loads(source_manifest.read_text(encoding="utf-8"))
    executable = backend / "realesrgan-ncnn-vulkan.exe"
    model = backend / "models/realesrgan-x4plus.bin"
    param = model.with_suffix(".param")
    recipe = {"version": VERSION, "source": digest(source), "source_manifest": digest(source_manifest),
              "executable": digest(executable), "weights": digest(model), "parameters": digest(param),
              "model": "realesrgan-x4plus", "backend": "ncnn-vulkan", "tile": 128,
              "halo": 32, "blend": 0.2, "scale": 4, "target_slice": 512,
              "encoding": "sRGB RGB; alpha and PBR channels untouched", "seed": "backend exposes no seed"}
    key = hashlib.sha256(json.dumps(recipe, sort_keys=True).encode()).hexdigest()
    work = output / key
    work.mkdir(parents=True, exist_ok=True)
    with Image.open(source) as image:
        atlas = image.convert("RGB")
    if atlas.size != (2048, 2048) or metadata["slice_count"] != 16:
        raise ValueError("Unexpected facade array contract")
    result = atlas.copy()
    records = []
    for index in range(16):
        x, y = (index % 4) * 512, (index // 4) * 512
        original = atlas.crop((x, y, x + 512, y + 512))
        record_path = work / f"slice_{index:02d}.json"
        final = work / f"slice_{index:02d}_candidate.png"
        if record_path.exists() and final.exists():
            record = json.loads(record_path.read_text())
            if record["sha256"] == digest(final):
                records.append(record)
                with Image.open(final) as image:
                    result.paste(image, (x, y))
                print(f"slice {index:02d}: validated cache", flush=True)
                continue
        original.save(work / f"slice_{index:02d}_original.png")
        input_path, raw = work / f"slice_{index:02d}_padded.png", work / f"slice_{index:02d}_inference.png"
        Image.fromarray(np.pad(np.array(original), ((32, 32), (32, 32), (0, 0)), mode="wrap")).save(input_path)
        started = time.monotonic()
        with (work / f"slice_{index:02d}_backend.log").open("w") as log:
            subprocess.run([str(executable), "-i", str(input_path), "-o", str(raw),
                            "-m", str(backend / "models"), "-n", "realesrgan-x4plus",
                            "-s", "4", "-t", "128", "-j", "1:1:1"],
                           stdout=log, stderr=log, check=True, timeout=300)
        with Image.open(raw) as image:
            if image.size != (2304, 2304):
                raise ValueError("Backend returned unexpected dimensions")
            restored = image.crop((128, 128, 2176, 2176)).resize((512, 512), Image.Resampling.LANCZOS).convert("RGB")
        candidate = Image.blend(original, restored, 0.2)
        before, after = np.asarray(original, dtype=float), np.asarray(candidate, dtype=float)
        delta = after - before
        record = {"slice": index, "name": metadata["slices"][index]["name"],
                  "source_asset": metadata["slices"][index]["source_asset"],
                  "recipe_key": key, "seconds": time.monotonic() - started,
                  "mean_absolute_rgb_delta": float(np.abs(delta).mean()),
                  "mean_rgb_shift": delta.mean(axis=(0, 1)).tolist(),
                  "max_rgb_delta": float(np.abs(delta).max()),
                  "status": "staged-pending-visual-review", "integrated": False}
        candidate.save(final)
        record["sha256"] = digest(final)
        write_json(record_path, record)
        result.paste(candidate, (x, y))
        records.append(record)
        print(f"slice {index:02d}: staged, mean delta {record['mean_absolute_rgb_delta']:.3f}/255", flush=True)
    result.save(work / source.name)
    write_json(work / "batch.json", {"recipe": recipe, "slices": records, "integrated": False,
                                   "output_sha256": digest(work / source.name)})
    return {"staged_slices": len(records), "integrated": False, "path": str(work)}


def recover_terrain(repo, output):
    """Reconstruct missing editable pages, accepting only exact baseline hashes."""
    sys.path.insert(0, str(repo / "tools/aerial_clipmap"))
    import build_full_map_clipmap as builder
    manifest_path = repo / "QuestFlight/Assets/StreamingAssets/QuestFlightAerialClipmap/clipmap_manifest.json"
    expected = {r["asset_name"]: r for r in json.loads(manifest_path.read_text())["pages"]}
    config = builder.load_json(repo / "tools/aerial_clipmap/config.json")
    specs = builder.tier_specs(config, "high") + builder.tier_specs(config, "medium") + [builder.global_spec(config)]
    if {s.asset_name for s in specs} != set(expected):
        raise ValueError("Current generator does not cover the exact shipping page set")
    config["processing"]["network_attempts"] = 2
    config["processing"]["network_timeout_seconds"] = 45
    output.mkdir(parents=True, exist_ok=True)
    results = []
    def recover(spec):
        try:
            if shutil.disk_usage(output).free < 8 * 1024**3:
                return {"asset_name": spec.asset_name, "status": "blocked-storage-reserve"}
            transform = builder.Transformer.from_crs(builder.local_crs(config), 3857, always_xy=True)
            record = builder.build_one(config, spec, output / "raw", output / "pages", transform)
            matched = record["sha256"] == expected[spec.asset_name]["sha256"]
            return {"asset_name": spec.asset_name, "status": "recovered-exact" if matched else "source-mismatch",
                    "expected_sha256": expected[spec.asset_name]["sha256"], "record": record}
        except Exception as exc:
            return {"asset_name": spec.asset_name, "status": "blocked-error", "error": str(exc)}
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        for result in pool.map(recover, specs):
            results.append(result)
            if len(results) % 20 == 0 or len(results) == len(specs):
                counts = dict(collections.Counter(r["status"] for r in results))
                write_json(output / "recovery.json", {"shipping_manifest_sha256": digest(manifest_path),
                           "expected_pages": len(specs), "counts": counts, "results": results,
                           "integrated": False})
                print(f"terrain {len(results)}/{len(specs)}: {counts}", flush=True)
    return {"counts": dict(collections.Counter(r["status"] for r in results)), "integrated": False}


def traffic_files():
    base = Path("QuestFlight/Assets/Production/Environment/TrafficV1")
    return [base / "Models" / ("KBDU_Traffic" + name + ".asset")
            for name in ("CargoVan", "Crossover", "Hatchback", "PassengerProxy", "Pickup", "Sedan", "Semi", "SemiProxy")] + [base / "Materials/KBDU_TrafficVehicle.mat"]


def process_traffic(repo, output):
    import remaster_self_ao as baker
    records, entries = [], []
    work = output / "traffic_ao"
    work.mkdir(parents=True, exist_ok=True)
    for relative in traffic_files()[:-1]:
        backup = output / "integration_backup" / relative.relative_to("QuestFlight")
        source = backup if backup.exists() else inside(repo, relative)
        staged = work / relative.name
        record_path = work / (relative.stem + ".bake.json")
        recipe = {"source_sha256": digest(source), "baker_sha256": digest(Path(baker.__file__)),
                  "numpy": np.__version__, "radius": .4, "samples": 48,
                  "visibility_min": .85, "seed": "deterministic cosine hemisphere; no RNG"}
        key = hashlib.sha256(json.dumps(recipe, sort_keys=True).encode()).hexdigest()
        record = json.loads(record_path.read_text()) if record_path.exists() else {}
        if record.get("recipe_key") != key or not staged.exists() or record.get("sha256") != digest(staged):
            record = baker.stage(source, staged)
            record.update(recipe_key=key, recipe=recipe, product_path=str(relative))
            write_json(record_path, record)
        _, _, _, _, vertices, normals, colors, indices = baker.read_mesh(source)
        alpha = baker.read_mesh(staged, require_opaque=False)[-2][:, 3].astype(int).tolist()
        identity = hashlib.sha256(vertices.astype("<f4").tobytes() + normals.astype("<f4").tobytes() +
                                  indices.astype("<i4").tobytes() + colors[:, :3].tobytes()).hexdigest()
        entries.append({"name": relative.stem, "geometry_sha256": identity, "alpha": alpha})
        write_json(work / (relative.stem + ".json"), {"alpha": alpha})
        records.append(record)
        print(f"traffic {relative.stem}: staged/cache verified", flush=True)
    write_json(work / "batch.json", records)
    write_json(work / "authoring_records.json", {
        "source_license": "CC0 traffic derivatives; TrafficV1/Data/KBDU_TrafficManifestV1.json source.vehicle_assets",
        "bake": "CPU bounded self-AO; 48 rays, radius .4m, visibility .85 to 1; only body/trim",
        "entries": entries})
    return {"staged_archetypes": len(records), "integrated": False}


def integrate_traffic(repo, output, unity):
    records = output / "traffic_ao/authoring_records.json"
    if not records.exists():
        raise ValueError("Run process-traffic and inspect its material review before integration")
    control = inside(repo, "QuestFlight/Assets/Editor/SceneryRemaster/TrafficSelfOcclusion.json")
    control.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(records, control)
    environment = dict(os.environ, QFL_REMASTER_ROOT=str(output),
                       QFL_PRODUCTION_VISUAL_QA_DIR=str(output / "final_qa"))
    startup = None
    if os.name == "nt":
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = 0
    subprocess.run([str(unity), "-batchmode", "-quit", "-projectPath", str(repo / "QuestFlight"),
                    "-executeMethod", "QuestFlightLab.Editor.Tests.OfflineRemasterIntegrateTraffic.RunWithReview",
                    "-logFile", str(output / "traffic_integration_final.log")],
                   env=environment, startupinfo=startup, check=True, timeout=1200)
    return verify_traffic(repo, output)


def verify_traffic(repo, output):
    from remaster_self_ao import read_mesh
    control = repo / "QuestFlight/Assets/Editor/SceneryRemaster/TrafficSelfOcclusion.json"
    approved = {r["name"]: r["alpha"] for r in json.loads(control.read_text())["entries"]}
    receipt = []
    for relative in traffic_files():
        current = inside(repo, relative)
        backup = inside(output / "integration_backup", relative.relative_to("QuestFlight"))
        if relative.suffix == ".asset":
            old = read_mesh(backup)
            new = read_mesh(current, require_opaque=False)
            _, old_blob, stride, offset, *_ = old
            new_blob = bytearray(new[1])
            if new[2:4] != old[2:4] or len(new_blob) != len(old_blob):
                raise ValueError(f"Vertex layout changed: {relative}")
            new_blob[offset + 3::stride] = old_blob[offset + 3::stride]
            if bytes(new_blob) != old_blob or not np.array_equal(old[-1], new[-1]):
                raise ValueError(f"Protected geometry/attributes changed: {relative}")
            if not np.array_equal(new[-2][:, 3], approved.get(relative.stem, [])):
                raise ValueError(f"Integrated alpha differs from reviewed bake: {relative}")
        elif digest(current) != digest(backup):
            raise ValueError("Traffic material must remain unchanged")
        if digest(Path(str(current) + ".meta")) != digest(Path(str(backup) + ".meta")):
            raise ValueError(f"Metadata changed: {relative}")
        receipt.append({"path": str(relative), "integrated_sha256": digest(current),
                        "original_sha256": digest(backup), "backup": str(backup)})
    write_json(output / "traffic_receipt.json", {"files": receipt, "control_sha256": digest(control)})
    return {"verified_files": len(receipt), "protected_geometry_equal": True, "metadata_equal": True}


def restore_traffic(repo, output):
    """Restore only this task's nine reviewed product outputs, after a full preflight."""
    receipt = json.loads((output / "traffic_receipt.json").read_text())
    if {Path(r["path"]) for r in receipt["files"]} != set(traffic_files()) or len(receipt["files"]) != 9:
        raise ValueError("Unexpected restore target set")
    control = repo / "QuestFlight/Assets/Editor/SceneryRemaster/TrafficSelfOcclusion.json"
    if digest(control) != receipt["control_sha256"]:
        raise ValueError("Authoring records changed after integration; refusing overwrite")
    targets = []
    for record in receipt["files"]:
        target = inside(repo, record["path"])
        backup = inside(output / "integration_backup", Path(record["path"]).relative_to("QuestFlight"))
        if digest(target) != record["integrated_sha256"] or digest(backup) != record["original_sha256"]:
            raise ValueError(f"Changed output/backup; refusing overwrite: {target}")
        targets.append((target, backup, record["original_sha256"]))
    for target, backup, expected in targets:
        shutil.copy2(backup, target)
        if digest(target) != expected:
            raise IOError(f"Restore verification failed: {target}")
    write_json(control, {"entries": [], "status": "task outputs restored; baked attributes disabled"})
    write_json(output / "traffic_restored.json", {"restored_files": len(targets)})
    return {"restored_files": len(targets)}


def candidate_restore_files():
    return traffic_files() + [Path(p) for p in (
        "QuestFlight/Assets/Production/Environment/TrafficV1/Materials/KBDU_TrafficVehicle.shader",
        "QuestFlight/Assets/Editor/KbduTrafficAuthoring.cs",
        "QuestFlight/Assets/Production/Environment/FullMapBuildingsV1/Materials/FullMapBuildingClipmap.mat",
        "QuestFlight/Assets/Editor/FullMapBuildingClipmapAssetAuthoring.cs")]


def restore_candidate(repo, output):
    """Restore the fixed remaster product scope, preserving staged Editor bake records."""
    receipt = json.loads((output / "candidate_restore.json").read_text(encoding="utf-8"))
    records = receipt["files"]
    expected = set(candidate_restore_files())
    if len(records) != len(expected) or {Path(r["path"]) for r in records} != expected:
        raise ValueError("Unexpected candidate restore target set")
    prepared = []
    for record in records:
        relative = Path(record["path"])
        target = inside(repo, relative)
        backup = inside(output / "candidate_restore_originals", relative)
        if digest(target) != record["candidate_sha256"] or digest(backup) != record["original_sha256"]:
            raise ValueError(f"Changed output/backup; refusing all writes: {relative}")
        prepared.append((target, backup, record["original_sha256"]))
    for target, backup, sha in prepared:
        shutil.copy2(backup, target)
        if digest(target) != sha:
            raise IOError(f"Candidate restore failed: {target}")
    result = {"restored_files": len(prepared), "runtime_experiment_restored": True,
              "editor_bake_records": "retained for reproducible rejected experiment; no automatic authoring hook"}
    write_json(output / "candidate_restored.json", result)
    return result


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("command", choices=["inventory", "checkpoint", "process-facades", "recover-terrain", "process-traffic", "integrate-traffic", "verify-traffic", "restore-traffic", "restore-candidate"])
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--backend", type=Path)
    parser.add_argument("--unity", type=Path, default=Path("C:/Program Files/Unity/Hub/Editor/6000.3.8f1/Editor/Unity.exe"))
    args = parser.parse_args()
    repo, output = args.repo.resolve(), args.output.resolve()
    if output.is_relative_to(repo):
        parser.error("Evidence/checkpoints must be external to the repository")
    if args.command == "restore-candidate":
        result = restore_candidate(repo, output)
    elif args.command == "process-traffic":
        result = process_traffic(repo, output)
    elif args.command == "integrate-traffic":
        result = integrate_traffic(repo, output, args.unity)
    elif args.command == "verify-traffic":
        result = verify_traffic(repo, output)
    elif args.command == "restore-traffic":
        result = restore_traffic(repo, output)
    elif args.command == "recover-terrain":
        result = recover_terrain(repo, output)
    elif args.command == "process-facades":
        if args.backend is None:
            parser.error("--backend is required for process-facades")
        result = process_facades(repo, output, args.backend.resolve())
    else:
        result = checkpoint(repo, output) if args.command == "checkpoint" else inventory(repo, output)
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
