"""Read-only baseline accounting and safe discovery of the user-supplied donor.

Run before feature resources change. Outputs live outside Unity except the
immutable budget lock and concise status documents. Never executes archive code.
"""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import winreg
import zipfile

ROOT = Path(__file__).resolve().parents[2]
ARTIFACTS = ROOT.parent / "T2-QuestFlight-product-artifacts" / "soaring_autonomous_v2_20261013"
DOCS = ROOT / "docs/soaring"
UID = "d1da8f0a2c8241ab9d97c96fba530618"

def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

def main():
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                       r"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders") as key:
        downloads = Path(os.path.expandvars(winreg.QueryValueEx(key, "{374DE290-123F-4565-9164-39C4925E467B}")[0]))
    matches = []
    for archive in downloads.glob("*.zip"):
        with zipfile.ZipFile(archive) as z:
            metadata = [(i.filename, z.read(i).decode("utf-8", errors="replace"))
                        for i in z.infolist() if i.file_size < 200000
                        and (re.search(r"(licen[cs]e|readme|metadata|attribution)", i.filename, re.I)
                             or i.filename.endswith(".gltf"))]
            proof = [(name, value) for name, value in metadata
                     if UID in value or re.search(r"Schweizer[ _-]*SGS[ _-]*2[ _-]*33", value, re.I)]
            zone = Path(str(archive) + ":Zone.Identifier")
            try:
                if UID in zone.read_text(encoding="utf-8"):
                    # Signed CDN URLs can contain access tokens. Record identity,
                    # not their credentials, in evidence and console output.
                    proof.append(("Windows Zone.Identifier", "Download source URL contains exact Sketchfab UID " + UID))
            except (FileNotFoundError, OSError):
                pass
            if proof:
                matches.append((archive, proof))
    if len(matches) != 1:
        raise RuntimeError(f"Expected one metadata-matched donor; found {[str(m[0]) for m in matches]}")
    archive, proof = matches[0]
    source = ARTIFACTS / "sources" / ("sgs233_" + UID)
    raw = source / "raw" / archive.name
    raw.parent.mkdir(parents=True, exist_ok=True)
    if not raw.exists():
        shutil.copy2(archive, raw)
    assert digest(raw) == digest(archive)
    work = source / "extracted"
    with zipfile.ZipFile(raw) as z:
        for item in z.infolist():
            name = PurePosixPath(item.filename.replace("\\", "/"))
            if name.is_absolute() or ".." in name.parts or ":" in str(name) or (item.external_attr >> 16) & 0o170000 == 0o120000:
                raise RuntimeError(f"Unsafe archive entry: {name}")
            if name.suffix.lower() in {".exe", ".dll", ".bat", ".cmd", ".ps1", ".py", ".js", ".vbs", ".scr"}:
                raise RuntimeError(f"Executable archive entry: {name}")
        z.extractall(work)
    inventory = {"original": str(archive), "sha256": digest(archive), "uid": UID,
                 "metadata_match": proof, "source_copy": str(raw), "extracted": str(work),
                 "files": [{"path": str(p.relative_to(work)), "bytes": p.stat().st_size, "sha256": digest(p)}
                           for p in work.rglob("*") if p.is_file()],
                 "geometry_inspection": "PENDING_BLENDER", "rights": "READ_ACTUAL_NOTICE_BEFORE_DISTRIBUTION"}
    write(ARTIFACTS / "SOARING_MODEL_DISCOVERY.json", inventory)
    lock_path = DOCS / "SOARING_BUDGET_LOCK.json"
    if not lock_path.exists():
        package = ROOT / "QuestFlight/Packages/com.alexoviedo.questflight.jsbsim"
        inputs = [p for base in [ROOT / "QuestFlight/ProjectSettings", package, ROOT / "tools/native/jsbsim_bridge"]
                  for p in base.rglob("*") if p.is_file()]
        inputs += [ROOT / "QuestFlight/Assets/Editor/QuestPerformanceBudgetBatchRunner.cs"]
        records = [{"path": str(p.relative_to(ROOT)).replace("\\", "/"), "sha256": digest(p), "bytes": p.stat().st_size}
                   for p in sorted(inputs)]
        write(ARTIFACTS / "baseline_input_hashes.json", records)
        backup = ARTIFACTS / "baseline_native_package"
        shutil.copytree(package, backup, dirs_exist_ok=True)
        lock = {
            "schema": "questflight.soaring-budget-lock.v2", "frozen_date": "2026-10-13",
            "baseline_commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
            "initial_working_tree": "clean; source-only checkout excludes preserved local overlay",
            "baseline_overlay": "C:/QFBackup20261008/CLOSEOUT_20261012T185048Z/overlay_manifest.json",
            "baseline_apk": "C:/QFRC20261008/QuestFlight/Builds/Android/QuestFlight-production.apk",
            "baseline_apk_sha256": "2352c48108d984cda31a2e1b77227175041f20ebf78bfdffc63a03df0fa2676b",
            "baseline_complete_source_identity": "9a72ab062ed7fd48fcb7108b91bbe40c8232f293296e1731343f8df8a3e37f4f",
            "inputs": str(ARTIFACTS / "baseline_input_hashes.json"),
            "inputs_sha256": digest(ARTIFACTS / "baseline_input_hashes.json"),
            "hard_limits": {"draws": 77, "visible_triangles": 692871, "visible_materials": 38,
                            "texture_bytes": 307507088, "transparent_slots": 18, "msaa": 4},
            "incremental_planning_ceilings_not_headroom": {"cpu_p95_ms_per_rendered_frame": 0.5,
                "gpu_added_ms": 0.3, "weather_wake_bytes": 4194304, "steady_state_managed_allocation_bytes": 0},
            "simulation": {"base_hz": 120, "coupled_dropped_seconds_allowed": 0},
            "baseline_failures": ["Standard QA 107/130; 23 triangle failures; side view 80 draws/907053 triangles",
                "Objective views 892492/892498 triangles", "Native yaw 0.7886204147 vs 0.55; 2 pass/1 fail/9 unproven",
                "Historical hardware timing failure; installed candidate sustained cadence/thermal unmeasured"],
            "hardware_measurement_semantics": "Historical October 6 35.59 FPS, GPU mean 23.20 ms/p95 27.27 ms are 1-second aggregates on older APK; no current per-frame acceptance",
            "preserve": ["render resolution", "refresh", "MSAA", "foveation", "clouds", "scenery", "traffic", "all streamed modes"],
            "settings_evidence": "Exact ProjectSettings in hashed input inventory; effective device state not inferred from source",
            "effective_test_spacewarp": "OFF required; saved user preference preserved",
            "current_resources": "Existing retained QA; remeasure exact integrated scene and streamed modes; no startup-only claim",
            "physical_timing": "PENDING_PHYSICAL_QUEST"
        }
        write(lock_path, lock)
    print(json.dumps({"artifacts": str(ARTIFACTS), "donor": inventory, "budget_lock": str(lock_path)}, indent=2))

if __name__ == "__main__":
    main()
