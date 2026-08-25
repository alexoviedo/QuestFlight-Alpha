#!/usr/bin/env python3
"""Acquire, freeze, compile, and validate Traffic System V2 geospatial authority.

Raw responses, normalized records, intermediates, and binary evidence are
always external.  Only the four small text artifacts named by Phase 3 are
written beneath ``docs/data/traffic_v2``; the fifth artifact is the external
coverage PNG whose hash is bound by the JSON report.
"""

from __future__ import annotations

import argparse
import ctypes
import datetime as dt
import gc
import hashlib
import html
import html.parser
import importlib
import importlib.metadata
import json
import locale
import math
import os
import platform
import re
import shutil
import site
import subprocess
import sys
import sysconfig
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import zlib
from collections import Counter, defaultdict
from contextlib import contextmanager
from pathlib import Path, PurePosixPath
from typing import Any, Iterable

import numpy as np
import pyproj
import shapely
from PIL import Image, ImageDraw, ImageFont
from PIL import features as pillow_features
from shapely.geometry import LineString, Point, Polygon, box, mapping, shape
from shapely.ops import transform as shapely_transform, unary_union
from shapely.strtree import STRtree


SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[2]
DEFAULT_REGISTRY = SCRIPT_DIR / "source_registry.json"
DEFAULT_DATA_DIR = REPO_ROOT / "docs/data/traffic_v2"
USER_AGENT = "QuestFlight-TrafficV2-Geospatial/1.0 (bounded public-data freeze)"
FREEZE_NAME = "freeze_manifest.json"
RUN_MANIFEST_NAME = "run_manifest.json"
EXPECTED_TEXT_ARTIFACTS = (
    "traffic_source_coverage.json",
    "traffic_source_conflicts.json",
    "traffic_missing_roads.geojson",
    "traffic_source_provenance.md",
)
EXPECTED_EXTERNAL_ARTIFACT = "traffic_source_coverage.png"
PACKAGE_INDEX_NAME = "package_index.json"
OPERATION_LOCK_NAME = ".traffic_v2_geospatial_operation.lock"
# Compatibility alias for callers of the first source-gate implementation.  It
# now names the one lock used by every external-package operation.
ACQUISITION_LOCK_NAME = OPERATION_LOCK_NAME
EXTERNAL_RECORD_ROOTS = frozenset(
    (
        "approval_candidate",
        "evidence",
        "intermediates",
        "metadata",
        "normalized",
        "product_source_inventory.json",
        "raw",
        "receipts",
        "requests",
        "toolchain",
    )
)
ARCHIVE_RECORD_ROOTS = frozenset(("attempts", "failures", "quarantine"))
ROAD_CLASSES = ("motorway", "trunk", "primary", "secondary", "tertiary", "residential")
MAJOR_CLASSES = frozenset(("motorway", "trunk", "primary", "secondary"))
BENCHMARK_CAMERA_CONTRACT_SCHEMA = "questflight.traffic-v2-benchmark-camera-contract.v1"
BENCHMARK_APPROVAL_SCHEMA = "questflight.traffic-v2-geospatial-universe-approval.v1"
TOOLCHAIN_MANIFEST_SCHEMA = "questflight.traffic-v2-geospatial-toolchain-manifest.v1"
TOOLCHAIN_PAYLOAD_SCHEMA = "questflight.traffic-v2-geospatial-installed-payload.v1"
D_RESULT_SCHEMA = "questflight.traffic-v2-phase3-source-gate-verification-result.v1"
LEAD_INSPECTION_SCHEMA = "questflight.traffic-v2-geospatial-universe-inspection.v1"
NAIP_AUTHORITY_SCHEMA = "questflight.traffic-v2-naip-orthophoto-authority.v1"
NAIP_AUTHORITY_SOURCE_ID = "usda_naip_current_colorado_orthophoto_metadata"
NAIP_LICENSE_SNAPSHOT_ID = "usda_fpac_imagery_public_domain"
NAIP_ARCHIVE_DOCUMENT_ID = "usda_naip_colorado_2021_quarter_quad_archive"
NAIP_LATEST_COVERAGE_DOCUMENT_ID = "usda_naip_most_recent_coverage_page"
NAIP_BOUNDED_IDS_DOCUMENT_ID = "usda_naip_kbdu_full_bounds_object_ids"
NAIP_BOUNDED_FOOTPRINTS_DOCUMENT_ID = "usda_naip_kbdu_full_bounds_footprints"
NAIP_LICENSE_URL = "https://www.fpacbc.usda.gov/geospatial-services/customer-services"
NAIP_COVERAGE_PAGE_URL = (
    "https://www.fpacbc.usda.gov/geospatial-services/customer-services/naip-coverage-map"
)
NAIP_ARCHIVE_URL = (
    "https://www.fpacbc.usda.gov/sites/default/files/2024-10/co_naip21qq-1.zip"
)
NAIP_ARCHIVE_BYTES = 994968
NAIP_ARCHIVE_SHA256 = "bdf40ca7de8e78d21e3dd27726c9719202587f3531d0abced64465d1b4ef9a45"
NAIP_STATEWIDE_FOOTPRINT_COUNT = 7423
# Compatibility names retained for older focused test selectors.  The bound
# authority is now the official USDA archive, not the unavailable GeoPlatform
# image-service endpoint.
NAIP_SERVICE_DOCUMENT_ID = NAIP_ARCHIVE_DOCUMENT_ID
NAIP_SERVICE_URL = NAIP_ARCHIVE_URL
NAIP_REQUIRED_BBOX_WGS84_XYXY = (
    -105.3669180624,
    39.931205495,
    -105.0852610976,
    40.1473524046,
)
NAIP_EXPECTED_OBJECT_IDS = (
    245982,
    245984,
    245985,
    245986,
    245987,
    245988,
    245990,
    245991,
    245992,
    245993,
    246017,
    246020,
    246021,
    246024,
    246025,
    248029,
    248031,
    248032,
    248033,
    248034,
    248035,
    248036,
    248037,
    248038,
    248039,
    259489,
)
NAIP_EXPECTED_OVERVIEW_ID = 259489
NAIP_EXPECTED_OVERVIEW_NAME = "CRF_CO_2021"
NAIP_EXPECTED_RAW_HREF = (
    "/sites/default/files/2024-10/co_naip21qq-1.zip"
)
NAIP_EXPECTED_RESOLVED_HREF = (
    "https://www.fpacbc.usda.gov/sites/default/files/2024-10/co_naip21qq-1.zip"
)
NAIP_EXPECTED_ANCHOR_LABEL = "Shapefile (zip, 442KB)"
NAIP_PRIMARY_ID_INVENTORY_SHA256 = (
    "128fc43d0b5067daca6f32318109e39ab933040960319b0f2e2f6cebcebfad5f"
)
NAIP_DISPOSITION_INVENTORY_SHA256 = (
    "075d0a0030c2a6d9418216ddfcb548537d15168cf305f7b3adc7b436409b39eb"
)
NAIP_GEOMETRY_INVENTORY_SHA256 = (
    "6352dc98ab7f95b28660b9937f6c03ea2cb5ee702c6c01d27293c654b91d4217"
)
NAIP_EXPECTED_PRIMARY_UNION_BOUNDS_WGS84_XYXY = (
    -105.37499975935506,
    39.87499975234054,
    -105.06249975806364,
    40.187499753355716,
)
NAIP_EXPECTED_SELECTED_RECORDS = (
    2483, 2484, 2485, 2486, 2487,
    2579, 2580, 2581, 2582, 2583,
    2675, 2676, 2677, 2678, 2679,
    2771, 2772, 2773, 2774, 2775,
    2867, 2868, 2869, 2870, 2871,
)
NAIP_SELECTED_RECORD_INVENTORY_SHA256 = (
    "0dd8e8afef072af28158769c55d7e3b8da6ac29ad0be73259a527792dd8901b9"
)
NAIP_ALLOWED_USE = "VISUAL_AMBIGUITY_RESOLUTION_ONLY"
NAIP_FORBIDDEN_USES = (
    "LANE_COUNT_AUTHORITY",
    "LANE_DIRECTION_AUTHORITY",
    "ROAD_CENTERLINE_OR_GEOMETRY_CREATION",
)
GATE_IMPORT_ORIGIN_MODULES = (
    "argparse",
    "collections",
    "contextlib",
    "ctypes",
    "datetime",
    "gc",
    "hashlib",
    "html",
    "html.parser",
    "importlib",
    "importlib.metadata",
    "json",
    "locale",
    "math",
    "numpy",
    "os",
    "PIL",
    "PIL.Image",
    "PIL.ImageDraw",
    "PIL.ImageFont",
    "PIL.features",
    "pathlib",
    "platform",
    "pyproj",
    "re",
    "shapely",
    "shapely.geometry",
    "shapely.ops",
    "shapely.strtree",
    "shutil",
    "site",
    "subprocess",
    "sys",
    "sysconfig",
    "tempfile",
    "time",
    "typing",
    "urllib.error",
    "urllib.parse",
    "urllib.request",
    "zlib",
)

TOOLCHAIN_FLAG_NAMES = (
    "bytes_warning",
    "debug",
    "dev_mode",
    "dont_write_bytecode",
    "gil",
    "hash_randomization",
    "ignore_environment",
    "inspect",
    "int_max_str_digits",
    "interactive",
    "isolated",
    "no_site",
    "no_user_site",
    "optimize",
    "quiet",
    "safe_path",
    "utf8_mode",
    "verbose",
    "warn_default_encoding",
)
TOOLCHAIN_SEMANTIC_ENVIRONMENT_KEYS = tuple(sorted((
    "PROJ_DATA",
    "PROJ_LIB",
    "PROJ_NETWORK",
    "PYTHONASYNCIODEBUG",
    "PYTHON_BASIC_REPL",
    "PYTHONBREAKPOINT",
    "PYTHONCASEOK",
    "PYTHONCOERCECLOCALE",
    "PYTHON_CPU_COUNT",
    "PYTHONDEBUG",
    "PYTHONDEVMODE",
    "PYTHONDONTWRITEBYTECODE",
    "PYTHONEXECUTABLE",
    "PYTHONFAULTHANDLER",
    "PYTHON_GIL",
    "PYTHONHASHSEED",
    "PYTHONHISTORY",
    "PYTHONHOME",
    "PYTHONINSPECT",
    "PYTHONINTMAXSTRDIGITS",
    "PYTHONIOENCODING",
    "PYTHONLEGACYWINDOWSFSENCODING",
    "PYTHONLEGACYWINDOWSSTDIO",
    "PYTHONMALLOC",
    "PYTHONMALLOCSTATS",
    "PYTHONNODEBUGRANGES",
    "PYTHONNOUSERSITE",
    "PYTHONOPTIMIZE",
    "PYTHONPATH",
    "PYTHONPERFSUPPORT",
    "PYTHONPLATLIBDIR",
    "PYTHONPROFILEIMPORTTIME",
    "PYTHONPYCACHEPREFIX",
    "PYTHONSAFEPATH",
    "PYTHONSTARTUP",
    "PYTHONTHREADDEBUG",
    "PYTHONTRACEMALLOC",
    "PYTHONUNBUFFERED",
    "PYTHONUSERBASE",
    "PYTHONUTF8",
    "PYTHONVERBOSE",
    "PYTHONWARNDEFAULTENCODING",
    "PYTHONWARNINGS",
)))


class GateError(RuntimeError):
    """A fail-closed acquisition or validation error."""


class ConcurrentAcquisitionError(GateError):
    """Another acquisition process owns the exact external package."""


class ToolchainPreflightError(GateError):
    """The live offline authoring environment differs from the frozen contract."""


_VERIFIED_NAIP_AUTHORITY_SEAL = object()


class VerifiedNaipAuthority:
    """Opaque in-memory proof produced only by exact frozen-receipt closure."""

    __slots__ = ("_contract_sha256", "_seal", "_summary_bytes")

    def __init__(
        self,
        summary: dict[str, Any],
        contract: dict[str, Any],
        seal: object,
    ) -> None:
        if seal is not _VERIFIED_NAIP_AUTHORITY_SEAL:
            raise TypeError("VerifiedNaipAuthority can only be produced by closure validation")
        self._seal = seal
        self._summary_bytes = canonical_json_bytes(summary)
        self._contract_sha256 = sha256_bytes(canonical_json_bytes(contract))

    @property
    def summary(self) -> dict[str, Any]:
        return json.loads(self._summary_bytes.decode("utf-8"))


def utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec="microseconds").replace("+00:00", "Z")


def parse_canonical_utc(value: Any, label: str) -> dt.datetime:
    if not isinstance(value, str) or not value.endswith("Z"):
        raise GateError(f"{label} is not canonical UTC")
    try:
        parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise GateError(f"{label} is invalid") from error
    if parsed.utcoffset() != dt.timedelta(0):
        raise GateError(f"{label} is not canonical UTC")
    return parsed


def require_not_future(value: Any, label: str) -> dt.datetime:
    """Parse canonical UTC and reject even a minimally future-dated authority."""

    parsed = parse_canonical_utc(value, label)
    if parsed > dt.datetime.now(dt.timezone.utc):
        raise GateError(f"{label} is future-dated")
    return parsed


def canonical_json_bytes(value: Any) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n").encode("utf-8")


def compact_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def atomic_write_bytes(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    temporary = Path(temporary_name)
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(payload)
        os.replace(temporary, path)
    except Exception:
        temporary.unlink(missing_ok=True)
        raise


def atomic_write_json(path: Path, value: Any) -> None:
    atomic_write_bytes(path, canonical_json_bytes(value))


def sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with open(_native_filesystem_path(path), "rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def file_record(path: Path, root: Path) -> dict[str, Any]:
    absolute_path = _absolute_without_resolving(path)
    absolute_root = _absolute_without_resolving(root)
    return {
        "bytes": os.stat(_native_filesystem_path(absolute_path)).st_size,
        "path": absolute_path.relative_to(absolute_root).as_posix(),
        "sha256": sha256_file(path),
    }


def repository_file_record(path: Path) -> dict[str, Any]:
    return file_record(path, REPO_ROOT)


def _absolute_without_resolving(path: Path) -> Path:
    return Path(os.path.abspath(os.fspath(path.expanduser())))


def _native_filesystem_path(path: Path) -> str:
    """Return an absolute path accepted past the legacy Windows MAX_PATH limit."""

    raw = os.fspath(path)
    if os.name != "nt":
        return os.path.abspath(raw)
    if raw.startswith("\\\\?\\"):
        return raw
    absolute = os.path.abspath(raw)
    if absolute.startswith("\\\\"):
        return "\\\\?\\UNC\\" + absolute[2:]
    return "\\\\?\\" + absolute


def _path_kind(path: Path) -> str:
    try:
        metadata = os.lstat(_native_filesystem_path(path))
    except (FileNotFoundError, NotADirectoryError):
        return "absent"
    attributes = int(getattr(metadata, "st_file_attributes", 0))
    file_type = int(metadata.st_mode) & 0o170000
    if attributes & 0x400 or file_type == 0o120000:
        return "reparse"
    if file_type == 0o100000:
        return "file"
    if file_type == 0o040000:
        return "dir"
    return "other"


def validate_path_topology(
    path: Path,
    label: str,
    *,
    allowed_kinds: set[str],
    containment_root: Path | None = None,
) -> Path:
    """Validate lexical containment, parent types, and all reparse points."""

    candidate = _absolute_without_resolving(path)
    if containment_root is not None:
        root = _absolute_without_resolving(containment_root)
        try:
            common = Path(os.path.commonpath((os.fspath(root), os.fspath(candidate))))
        except ValueError as error:
            raise GateError(f"{label} is on a different filesystem root") from error
        if os.path.normcase(os.fspath(common)) != os.path.normcase(os.fspath(root)):
            raise GateError(f"{label} escapes its reserved containment root")
    current = Path(candidate.anchor)
    parts = candidate.parts[1:] if candidate.anchor else candidate.parts
    for index, part in enumerate(parts):
        current /= part
        kind = _path_kind(current)
        if kind == "absent":
            break
        if kind == "reparse":
            raise GateError(f"{label} crosses a symlink/junction/reparse point: {current}")
        if index < len(parts) - 1 and kind != "dir":
            raise GateError(f"{label} has a non-directory parent: {current}")
    kind = _path_kind(candidate)
    if kind not in allowed_kinds:
        raise GateError(
            f"{label} has wrong filesystem kind {kind}; expected {sorted(allowed_kinds)}"
        )
    return candidate


def validate_tree_no_reparse(root: Path, label: str) -> None:
    """Reject every existing reparse/symlink or non-file/non-directory tree member."""

    if _path_kind(root) != "dir":
        return
    pending = [root]
    while pending:
        directory = pending.pop()
        try:
            entries = list(os.scandir(_native_filesystem_path(directory)))
        except OSError as error:
            raise GateError(f"{label} cannot be enumerated safely: {directory}") from error
        for entry in entries:
            path = directory / entry.name
            kind = _path_kind(path)
            if kind == "reparse":
                raise GateError(f"{label} contains a symlink/junction/reparse point: {path}")
            if kind == "dir":
                pending.append(path)
            elif kind != "file":
                raise GateError(f"{label} contains unsupported filesystem object: {path}")


def _walk_tree_files(root: Path, label: str) -> Iterator[Path]:
    """Yield normal lexical Paths while traversing with extended Windows syscalls."""

    if _path_kind(root) != "dir":
        return
    pending = [root]
    while pending:
        directory = pending.pop()
        try:
            entries = sorted(
                os.scandir(_native_filesystem_path(directory)),
                key=lambda entry: entry.name,
            )
        except OSError as error:
            raise GateError(f"{label} cannot be enumerated safely: {directory}") from error
        child_directories: list[Path] = []
        for entry in entries:
            path = directory / entry.name
            kind = _path_kind(path)
            if kind == "file":
                yield path
            elif kind == "dir":
                child_directories.append(path)
            else:
                raise GateError(f"{label} contains unsupported filesystem object: {path}")
        pending.extend(reversed(child_directories))


def validate_external_root_topology(path: Path, *, allow_absent: bool) -> Path:
    candidate = validate_path_topology(
        path,
        "External run root",
        allowed_kinds={"dir", *(("absent",) if allow_absent else ())},
    )
    repository = validate_path_topology(
        REPO_ROOT, "Repository root", allowed_kinds={"dir"}
    )
    try:
        common = Path(os.path.commonpath((os.fspath(repository), os.fspath(candidate))))
    except ValueError:
        common = Path(candidate.anchor)
    if os.path.normcase(os.fspath(common)) == os.path.normcase(os.fspath(repository)):
        raise GateError(f"External run directory is inside the repository: {candidate}")
    return candidate


def validate_command_path_topology(
    registry: dict[str, Any],
    external_run_path: Path,
    *,
    command: str,
    data_dir: Path | None = None,
    evidence_dir: Path | None = None,
) -> dict[str, Path]:
    """Validate every reserved command destination before lock/root/stage writes."""

    allow_absent = command in ("inventory", "acquire")
    external_run = validate_external_root_topology(
        external_run_path, allow_absent=allow_absent
    )
    external_paths: dict[str, tuple[set[str], str]] = {
        FREEZE_NAME: ({"absent", "file"}, "Source freeze"),
        RUN_MANIFEST_NAME: ({"absent", "file"}, "Run manifest"),
        PACKAGE_INDEX_NAME: ({"absent", "file"}, "Outer package index"),
        OPERATION_LOCK_NAME: ({"absent", "file"}, "Operation lock"),
        "product_source_inventory.json": ({"absent", "file"}, "Product inventory"),
        "acquisition_failure.json": ({"absent", "file"}, "Acquisition failure record"),
        "rebuild_check": ({"absent", "dir"}, "Rebuild-check root"),
    }
    for root_name in (
        *(registry["external_package"].get("active_roots") or []),
        *(registry["external_package"].get("archive_roots") or []),
        *(registry["external_package"].get("support_roots") or []),
    ):
        external_paths[str(root_name)] = ({"absent", "dir"}, f"External root {root_name}")
    manifest_relative = registry["toolchain"]["manifest_path"]
    external_paths[manifest_relative] = ({"absent", "file"}, "Toolchain manifest")
    for relative, (kinds, label) in external_paths.items():
        target = external_run.joinpath(*PurePosixPath(relative).parts)
        validate_path_topology(
            target,
            label,
            allowed_kinds=kinds,
            containment_root=external_run,
        )
    validate_tree_no_reparse(external_run, "External run package")
    candidate_root = external_run / "approval_candidate"
    if _path_kind(candidate_root) == "dir":
        for name in (*EXPECTED_TEXT_ARTIFACTS, EXPECTED_EXTERNAL_ARTIFACT, "candidate_manifest.json"):
            validate_path_topology(
                candidate_root / name,
                f"Approval candidate member {name}",
                allowed_kinds={"file"},
                containment_root=candidate_root,
            )
    if command in ("compile", "validate", "rebuild-check"):
        if _path_kind(external_run / FREEZE_NAME) != "file":
            raise GateError(f"{command} requires freeze_manifest.json to be a file")
        if data_dir is None or evidence_dir is None:
            raise GateError(f"{command} requires exact data/evidence roots")
        require_outputs = command in ("validate", "rebuild-check")
        data = validate_path_topology(
            data_dir,
            "Normative data root",
            allowed_kinds={"dir"} if require_outputs else {"absent", "dir"},
        )
        evidence = validate_path_topology(
            evidence_dir,
            "External evidence root",
            allowed_kinds={"dir"} if require_outputs else {"absent", "dir"},
        )
        validate_tree_no_reparse(data, "Normative data tree")
        validate_tree_no_reparse(evidence, "Coverage evidence tree")
        for name in EXPECTED_TEXT_ARTIFACTS:
            validate_path_topology(
                data / name,
                f"Normative artifact {name}",
                allowed_kinds={"file"} if require_outputs else {"absent", "file"},
                containment_root=data,
            )
        validate_path_topology(
            evidence / EXPECTED_EXTERNAL_ARTIFACT,
            "Coverage PNG",
            allowed_kinds={"file"} if require_outputs else {"absent", "file"},
            containment_root=evidence,
        )
        return {"data_dir": data, "evidence_dir": evidence, "external_run": external_run}
    return {"external_run": external_run}


def require_external(path: Path) -> Path:
    candidate = validate_external_root_topology(path, allow_absent=True)
    candidate.mkdir(parents=True, exist_ok=True)
    return candidate


def resolve_external_location(path: Path) -> Path:
    """Validate an external location without creating it."""

    return validate_external_root_topology(path, allow_absent=True)


def _unlink_owned_lock(path: Path, identity: tuple[int, int]) -> None:
    """Remove only the exact filesystem object created by this lock owner."""

    try:
        stat_result = path.stat(follow_symlinks=False)
    except OSError:
        return
    if (int(stat_result.st_dev), int(stat_result.st_ino)) == identity:
        try:
            path.unlink()
        except FileNotFoundError:
            pass


@contextmanager
def exclusive_operation_lock(
    external_run: Path, operation: str, *, allow_root_creation: bool = True
) -> Iterable[Path]:
    """Own the single external-package operation lock until exact cleanup.

    Read-only repository/toolchain preflight is intentionally performed by the
    public command wrappers before this context creates the package root.  Each
    wrapper repeats its external-state preflight after the lock is owned.
    """

    if not isinstance(operation, str) or not operation:
        raise GateError("Operation-lock owner must name its command")
    external_run = validate_external_root_topology(
        external_run, allow_absent=allow_root_creation
    )
    validate_path_topology(
        external_run / OPERATION_LOCK_NAME,
        "Operation lock",
        allowed_kinds={"absent", "file"},
        containment_root=external_run,
    )
    if allow_root_creation:
        external_run.mkdir(parents=True, exist_ok=True)
    elif not external_run.is_dir():
        raise GateError("Existing external package disappeared before operation lock")
    lock_path = external_run / OPERATION_LOCK_NAME
    payload = canonical_json_bytes(
        {
            "created_utc": utc_now(),
            "operation": operation,
            "pid": os.getpid(),
            "schema": "questflight.traffic-v2-geospatial-operation-lock.v1",
            "tool_sha256": sha256_file(Path(__file__)),
        }
    )
    try:
        descriptor = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
    except FileExistsError as error:
        raise ConcurrentAcquisitionError(
            f"External source package already has an operation lock: {lock_path}"
        ) from error
    identity = os.fstat(descriptor)
    owned_identity = (int(identity.st_dev), int(identity.st_ino))
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(payload)
        yield lock_path
    finally:
        _unlink_owned_lock(lock_path, owned_identity)


@contextmanager
def exclusive_acquisition_lock(external_run: Path) -> Iterable[Path]:
    """Backward-compatible name routed through the shared operation lock."""

    with exclusive_operation_lock(external_run, "acquire") as lock_path:
        yield lock_path


def compile_target_paths(data_dir: Path, evidence_dir: Path) -> tuple[Path, ...]:
    data = _absolute_without_resolving(data_dir)
    evidence = _absolute_without_resolving(evidence_dir)
    return (
        data,
        *(data / name for name in EXPECTED_TEXT_ARTIFACTS),
        evidence,
        evidence / EXPECTED_EXTERNAL_ARTIFACT,
    )


def canonical_target_identity(path: Path) -> str:
    """Return one stable filesystem identity for an output target path."""

    candidate = validate_path_topology(
        path,
        "Target-set member",
        allowed_kinds={"absent", "dir", "file"},
    )
    resolved = os.path.realpath(os.fspath(candidate))
    if os.name == "nt" and resolved.startswith("\\\\?\\"):
        resolved = (
            "\\\\" + resolved[8:]
            if resolved.upper().startswith("\\\\?\\UNC\\")
            else resolved[4:]
        )
    return os.path.normcase(os.path.normpath(resolved))


def target_set_lock_plan(targets: Iterable[Path]) -> list[tuple[str, str]]:
    """Resolve the machine-global target-lock identities without writing."""

    canonical_targets = sorted({canonical_target_identity(Path(path)) for path in targets})
    if not canonical_targets:
        raise GateError("Target-set lock requires at least one resolved target")
    plan = [
        (sha256_bytes(target.encode("utf-8")), target)
        for target in canonical_targets
    ]
    if os.name != "nt":
        lock_root = Path("/var/tmp/questflight_traffic_v2_geospatial_locks")
        validate_path_topology(
            lock_root,
            "Target-set lock root",
            allowed_kinds={"absent", "dir"},
        )
        for key, _ in plan:
            validate_path_topology(
                lock_root / f"{key}.lock",
                "Target-set lock",
                allowed_kinds={"absent", "file"},
                containment_root=lock_root,
            )
    return plan


@contextmanager
def _windows_target_mutexes(
    plan: list[tuple[str, str]], operation: str
) -> Iterable[tuple[str, ...]]:
    """Own non-persistent machine-global Windows mutexes for every target."""

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    create_mutex = kernel32.CreateMutexW
    create_mutex.argtypes = (ctypes.c_void_p, ctypes.c_int, ctypes.c_wchar_p)
    create_mutex.restype = ctypes.c_void_p
    wait_for_single = kernel32.WaitForSingleObject
    wait_for_single.argtypes = (ctypes.c_void_p, ctypes.c_ulong)
    wait_for_single.restype = ctypes.c_ulong
    release_mutex = kernel32.ReleaseMutex
    release_mutex.argtypes = (ctypes.c_void_p,)
    release_mutex.restype = ctypes.c_int
    close_handle = kernel32.CloseHandle
    close_handle.argtypes = (ctypes.c_void_p,)
    close_handle.restype = ctypes.c_int
    wait_object_0 = 0x00000000
    wait_abandoned = 0x00000080
    wait_timeout = 0x00000102
    wait_failed = 0xFFFFFFFF
    owned: list[tuple[int, str]] = []
    try:
        for key, _ in plan:
            mutex_name = f"Global\\QuestFlightTrafficV2GeospatialTarget_{key}"
            handle = create_mutex(None, 0, mutex_name)
            if not handle:
                raise GateError(
                    "Unable to create the machine-global Traffic V2 target mutex "
                    f"for {operation}: WinError {ctypes.get_last_error()}"
                )
            wait_status = int(wait_for_single(handle, 0))
            if wait_status == wait_timeout:
                close_handle(handle)
                raise ConcurrentAcquisitionError(
                    f"Traffic V2 geospatial target is already locked: {key}"
                )
            if wait_status not in (wait_object_0, wait_abandoned):
                error_code = ctypes.get_last_error() if wait_status == wait_failed else wait_status
                close_handle(handle)
                raise GateError(
                    "Unable to acquire the machine-global Traffic V2 target mutex "
                    f"for {operation}: {error_code}"
                )
            owned.append((int(handle), mutex_name))
        yield tuple(name for _, name in owned)
    finally:
        for handle, _ in reversed(owned):
            release_mutex(handle)
            close_handle(handle)


@contextmanager
def _posix_target_lock_files(
    plan: list[tuple[str, str]], operation: str
) -> Iterable[tuple[Path, ...]]:
    """Fixed-namespace fallback for non-Windows authoring hosts."""

    lock_root = Path("/var/tmp/questflight_traffic_v2_geospatial_locks")
    lock_root.mkdir(parents=True, exist_ok=True)
    acquired: list[tuple[Path, tuple[int, int]]] = []
    try:
        for key, _ in plan:
            lock_path = lock_root / f"{key}.lock"
            payload = canonical_json_bytes(
                {
                    "operation": operation,
                    "pid": os.getpid(),
                    "schema": "questflight.traffic-v2-geospatial-target-lock.v1",
                    "target_sha256": key,
                }
            )
            try:
                descriptor = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            except FileExistsError as error:
                raise ConcurrentAcquisitionError(
                    f"Traffic V2 geospatial target is already locked: {key}"
                ) from error
            stat_result = os.fstat(descriptor)
            acquired.append(
                (lock_path, (int(stat_result.st_dev), int(stat_result.st_ino)))
            )
            with os.fdopen(descriptor, "wb") as handle:
                handle.write(payload)
        yield tuple(path for path, _ in acquired)
    finally:
        for lock_path, identity in reversed(acquired):
            _unlink_owned_lock(lock_path, identity)
        try:
            lock_root.rmdir()
        except OSError:
            pass


@contextmanager
def exclusive_target_set_lock(
    targets: Iterable[Path], operation: str
) -> Iterable[Path]:
    """Serialize different external runs that share any final target set."""

    plan = target_set_lock_plan(targets)
    if os.name == "nt":
        with _windows_target_mutexes(plan, operation) as locks:
            yield locks
    else:
        with _posix_target_lock_files(plan, operation) as locks:
            yield locks


def naip_query_parameters(registry: dict[str, Any]) -> dict[str, str]:
    xmin, ymin, xmax, ymax = registry["arcgis_query"]["bbox_wgs84_xyxy"]
    return {
        "f": "json",
        "geometry": f"{xmin:.10f},{ymin:.10f},{xmax:.10f},{ymax:.10f}",
        "geometryType": "esriGeometryEnvelope",
        "inSR": "4326",
        "orderByFields": "objectid ASC",
        "outFields": "objectid,name,category,year,state",
        "outSR": "4326",
        "resultRecordCount": "1000",
        "returnGeometry": "true",
        "spatialRel": "esriSpatialRelIntersects",
        # The federal service rejects compound predicates through its public
        # edge.  Freeze the complete bounded Colorado response and perform the
        # category/year disposition locally instead of weakening the proof.
        "where": "state='CO'",
    }


def naip_ids_query_parameters(registry: dict[str, Any]) -> dict[str, str]:
    xmin, ymin, xmax, ymax = registry["arcgis_query"]["bbox_wgs84_xyxy"]
    return {
        "f": "json",
        "geometry": f"{xmin:.10f},{ymin:.10f},{xmax:.10f},{ymax:.10f}",
        "geometryType": "esriGeometryEnvelope",
        "inSR": "4326",
        "returnIdsOnly": "true",
        "spatialRel": "esriSpatialRelIntersects",
        "where": "state='CO'",
    }


def expected_naip_documents(registry: dict[str, Any]) -> list[dict[str, Any]]:
    """Return the one ordered metadata-only request inventory allowed by the gate."""

    return [
        {
            "id": NAIP_ARCHIVE_DOCUMENT_ID,
            "media_type": "application/zip",
            "minimum_bytes": NAIP_ARCHIVE_BYTES,
            "params": None,
            "path": "co_naip21qq-1.zip",
            "required_contains": [],
            "timeout_seconds": 180,
            "url": NAIP_ARCHIVE_URL,
        },
        {
            "id": NAIP_LATEST_COVERAGE_DOCUMENT_ID,
            "media_type": "text/html",
            "minimum_bytes": 40000,
            "params": None,
            "path": "naip_most_recent_coverage.html",
            "required_contains": [
                "most recent year available",
                "NAIP CONUS 2021-2022",
                "Colorado",
                "co_naip21qq-1.zip",
            ],
            "timeout_seconds": 180,
            "url": NAIP_COVERAGE_PAGE_URL,
        },
    ]


def encoded_request_url(url: str, params: dict[str, Any] | None) -> str:
    if params is None:
        return url
    encoded = urllib.parse.urlencode(
        [(str(key), str(value)) for key, value in sorted(params.items())]
    )
    return url + ("&" if "?" in url else "?") + encoded


def validate_naip_registry_contract(value: dict[str, Any]) -> None:
    """Pin one metadata-only, public-domain, complete-bounds NAIP authority."""

    if value.get("arcgis_query", {}).get("bbox_wgs84_xyxy") != list(
        NAIP_REQUIRED_BBOX_WGS84_XYXY
    ):
        raise GateError("Registry USDA NAIP query bounds differ from the exact +/-12 km envelope")
    snapshots = {
        snapshot.get("id"): snapshot for snapshot in value.get("license_snapshots") or []
    }
    license_snapshot = snapshots.get(NAIP_LICENSE_SNAPSHOT_ID)
    if license_snapshot != {
        "id": NAIP_LICENSE_SNAPSHOT_ID,
        "required_contains": [
            "Aerial photography and digital orthophotography acquired by the USDA",
            "public domain",
        ],
        "url": NAIP_LICENSE_URL,
    }:
        raise GateError("Registry USDA NAIP public-domain license snapshot is not exact")

    document_sources = {
        source.get("id"): source for source in value.get("document_sources") or []
    }
    source = document_sources.get(NAIP_AUTHORITY_SOURCE_ID)
    if not isinstance(source, dict):
        raise GateError("Registry is missing the required USDA NAIP authority source")
    expected_contract = {
        "accepted_state": "CO",
        "accepted_year": 2021,
        "allowed_use": NAIP_ALLOWED_USE,
        "archive_bytes": NAIP_ARCHIVE_BYTES,
        "archive_document_id": NAIP_ARCHIVE_DOCUMENT_ID,
        "archive_sha256": NAIP_ARCHIVE_SHA256,
        "coverage_bbox_wgs84_xyxy": list(NAIP_REQUIRED_BBOX_WGS84_XYXY),
        "coverage_union_bounds_wgs84_xyxy": list(
            NAIP_EXPECTED_PRIMARY_UNION_BOUNDS_WGS84_XYXY
        ),
        "expected_primary_footprint_count": 25,
        "expected_selected_record_numbers": list(NAIP_EXPECTED_SELECTED_RECORDS),
        "expected_statewide_footprint_count": NAIP_STATEWIDE_FOOTPRINT_COUNT,
        "expected_anchor_label": NAIP_EXPECTED_ANCHOR_LABEL,
        "expected_raw_href": NAIP_EXPECTED_RAW_HREF,
        "expected_resolved_href": NAIP_EXPECTED_RESOLVED_HREF,
        "forbidden_uses": list(NAIP_FORBIDDEN_USES),
        "latest_coverage_document_id": NAIP_LATEST_COVERAGE_DOCUMENT_ID,
        "license_snapshot_id": NAIP_LICENSE_SNAPSHOT_ID,
        "schema": NAIP_AUTHORITY_SCHEMA,
        "selected_record_inventory_sha256": NAIP_SELECTED_RECORD_INVENTORY_SHA256,
    }
    if source.get("orthophoto_authority_contract") != expected_contract:
        raise GateError("Registry USDA NAIP orthophoto authority contract is not exact")
    if source.get("license_partition") != "US-Government-Work-Public-Domain":
        raise GateError("Registry USDA NAIP authority is not in the public-domain partition")
    if source.get("license_snapshot_ids") != [NAIP_LICENSE_SNAPSHOT_ID]:
        raise GateError("Registry USDA NAIP authority has an inexact license binding")
    if source.get("metadata_only") is not True or source.get("required") is not True:
        raise GateError("Registry USDA NAIP authority must be required metadata only")
    if source.get("role") != "orthophoto_ambiguity_resolution_only":
        raise GateError("Registry USDA NAIP authority role is not ambiguity-resolution only")

    documents = source.get("documents")
    if documents != expected_naip_documents(value):
        raise GateError(
            "Registry USDA NAIP ordered document declarations are not exact"
        )
    for document in documents:
        request_url = encoded_request_url(document["url"], document.get("params"))
        lowered = request_url.lower()
        if any(token in lowered for token in ("exportimage", "download_url", ".jp2", ".tif")):
            raise GateError("Registry USDA NAIP authority attempts to acquire imagery bytes")

    forbidden_source_ids = {
        precedence["source_id"]
        for precedence in value["authority_model"]["canonical_centerline_precedence"]
    }
    forbidden_source_ids.update(
        evidence["source_id"] for evidence in value["authority_model"]["lane_evidence"]
    )
    forbidden_source_ids.update(
        reference["source_id"]
        for reference in value["authority_model"]["reference_sources"]
    )
    if NAIP_AUTHORITY_SOURCE_ID in forbidden_source_ids:
        raise GateError("Registry permits NAIP imagery to enter road/lane authority")
    if NAIP_AUTHORITY_SOURCE_ID in value["authority_model"]["class_evidence"]:
        raise GateError("Registry permits NAIP imagery to provide road-class authority")
    if NAIP_AUTHORITY_SOURCE_ID in value["authority_model"]["road_kind_evidence"]:
        raise GateError("Registry permits NAIP imagery to provide road-kind authority")


def load_registry(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if value.get("schema") != "questflight.traffic-v2-geospatial-source-registry.v2":
        raise GateError("Unsupported geospatial source registry schema")
    resolve_repository_relative_path(
        value["authority_model"]["benchmark_approval_path"],
        "Lead/D approval",
    )
    expected_witnesses = {"d_report", "d_result", "d_verifier", "lead_inspection"}
    witnesses = value["authority_model"].get("benchmark_approval_witnesses") or {}
    if set(witnesses) != expected_witnesses:
        raise GateError("Registry must declare exactly the four approval witness paths")
    for witness_id in sorted(expected_witnesses):
        record = witnesses[witness_id]
        if not isinstance(record, dict) or set(record) != {"path"}:
            raise GateError(f"Registry approval witness {witness_id} must contain only path")
        resolve_repository_relative_path(record["path"], f"Registry approval witness {witness_id}")
    approval_contract = value["authority_model"].get("benchmark_approval_contract") or {}
    if approval_contract.get("d_result_schema") != D_RESULT_SCHEMA:
        raise GateError("Registry D-result schema does not match the source-gate implementation")
    if approval_contract.get("lead_inspection_schema") != LEAD_INSPECTION_SCHEMA:
        raise GateError("Registry Lead-inspection schema does not match the source-gate implementation")
    required_checks = approval_contract.get("required_d_checks")
    if (
        not isinstance(required_checks, list)
        or not required_checks
        or required_checks != sorted(set(required_checks))
        or not all(isinstance(item, str) and item for item in required_checks)
    ):
        raise GateError("Registry D required-check inventory must be unique, nonempty, and sorted")
    toolchain = value.get("toolchain") or {}
    if toolchain.get("schema") != "questflight.traffic-v2-geospatial-toolchain-contract.v1":
        raise GateError("Unsupported geospatial toolchain contract schema")
    for key in ("source_registry_path", "source_tool_path"):
        resolve_repository_relative_path(toolchain.get(key), f"Toolchain {key}")
    lock = toolchain.get("requirements_lock") or {}
    if not isinstance(lock, dict) or set(lock) != {"bytes", "path", "sha256"}:
        raise GateError("Toolchain requirements-lock pin must contain exactly path, bytes, and sha256")
    resolve_repository_relative_path(lock.get("path"), "Toolchain requirements lock")
    if (
        isinstance(lock.get("bytes"), bool)
        or not isinstance(lock.get("bytes"), int)
        or lock["bytes"] < 1
        or not isinstance(lock.get("sha256"), str)
        or not re.fullmatch(r"[0-9a-f]{64}", lock["sha256"])
    ):
        raise GateError("Toolchain requirements-lock bytes/SHA pin is invalid")
    manifest_path = PurePosixPath(str(toolchain.get("manifest_path") or ""))
    if (
        manifest_path.is_absolute()
        or manifest_path.as_posix() != toolchain.get("manifest_path")
        or any(part in ("", ".", "..") for part in manifest_path.parts)
        or not manifest_path.parts
        or manifest_path.parts[0] not in set(value["external_package"]["support_roots"])
    ):
        raise GateError("Toolchain manifest path is not a canonical external support path")
    payload = toolchain.get("payload_identity") or {}
    expected_payload_keys = {
        "distribution_payloads",
        "import_origins",
        "installed_distributions",
        "runtime_payload",
        "schema",
        "site_packages",
    }
    if set(payload) != expected_payload_keys or payload.get("schema") != TOOLCHAIN_PAYLOAD_SCHEMA:
        raise GateError("Toolchain payload-identity contract is incomplete or unsupported")
    installed = payload.get("installed_distributions")
    distribution_payloads = payload.get("distribution_payloads")
    if (
        not isinstance(installed, dict)
        or not installed
        or not isinstance(distribution_payloads, dict)
        or set(installed) != set(distribution_payloads)
        or list(installed) != sorted(installed)
        or list(distribution_payloads) != sorted(distribution_payloads)
    ):
        raise GateError("Toolchain installed-distribution and payload inventories must match")
    canonical_distribution_name = re.compile(r"[a-z0-9]+(?:[.-][a-z0-9]+)*")
    for name, version in installed.items():
        if (
            not isinstance(name, str)
            or canonical_distribution_name.fullmatch(name) is None
            or not isinstance(version, str)
            or not version
        ):
            raise GateError("Toolchain installed-distribution inventory is not canonical")
    for label, summary in {
        "runtime": payload.get("runtime_payload"),
        **distribution_payloads,
    }.items():
        if not isinstance(summary, dict) or set(summary) != {
            "file_count",
            "inventory_sha256",
            "total_bytes",
        }:
            raise GateError(f"Toolchain {label} payload summary is incomplete")
        if (
            isinstance(summary["file_count"], bool)
            or not isinstance(summary["file_count"], int)
            or summary["file_count"] < 1
            or isinstance(summary["total_bytes"], bool)
            or not isinstance(summary["total_bytes"], int)
            or summary["total_bytes"] < 1
            or not isinstance(summary["inventory_sha256"], str)
            or re.fullmatch(r"[0-9a-f]{64}", summary["inventory_sha256"]) is None
        ):
            raise GateError(f"Toolchain {label} payload summary is invalid")
    import_origins = payload.get("import_origins")
    if (
        not isinstance(import_origins, dict)
        or set(import_origins) != set(GATE_IMPORT_ORIGIN_MODULES)
        or list(import_origins) != sorted(import_origins)
    ):
        raise GateError("Toolchain import-origin inventory is incomplete or unordered")
    for module_name, origin in import_origins.items():
        if not isinstance(module_name, str) or not module_name or not isinstance(origin, str):
            raise GateError("Toolchain import-origin inventory is invalid")
        if origin not in ("built-in", "frozen"):
            pure_origin = PurePosixPath(origin)
            if (
                pure_origin.is_absolute()
                or pure_origin.as_posix() != origin
                or any(part in ("", ".", "..") for part in pure_origin.parts)
                or not pure_origin.parts
                or pure_origin.parts[0] not in ("environment", "base_runtime")
            ):
                raise GateError("Toolchain import origin is not a canonical root-labelled path")
    site_packages = payload.get("site_packages") or {}
    if set(site_packages) != {
        "exceptions",
        "executable_pth_files",
        "payload",
        "roots",
        "startup_module_files",
    }:
        raise GateError("Toolchain site-packages closure is incomplete")
    roots = site_packages.get("roots")
    if (
        not isinstance(roots, list)
        or not roots
        or roots != sorted(set(roots))
        or not all(
            isinstance(root, str)
            and (root.startswith("environment/") or root.startswith("base_runtime/"))
            and PurePosixPath(root).as_posix() == root
            and all(part not in ("", ".", "..") for part in PurePosixPath(root).parts)
            for root in roots
        )
    ):
        raise GateError("Toolchain site-packages roots are not canonical")
    site_payload = site_packages.get("payload")
    if not isinstance(site_payload, dict) or set(site_payload) != {
        "file_count",
        "inventory_sha256",
        "total_bytes",
    }:
        raise GateError("Toolchain site-packages payload summary is incomplete")
    if (
        isinstance(site_payload["file_count"], bool)
        or not isinstance(site_payload["file_count"], int)
        or site_payload["file_count"] < 1
        or isinstance(site_payload["total_bytes"], bool)
        or not isinstance(site_payload["total_bytes"], int)
        or site_payload["total_bytes"] < 1
        or not isinstance(site_payload["inventory_sha256"], str)
        or re.fullmatch(r"[0-9a-f]{64}", site_payload["inventory_sha256"]) is None
    ):
        raise GateError("Toolchain site-packages payload summary is invalid")
    for list_name in ("exceptions", "executable_pth_files", "startup_module_files"):
        records = site_packages.get(list_name)
        if not isinstance(records, list):
            raise GateError(f"Toolchain site-packages {list_name} must be a list")
        paths: list[str] = []
        for record in records:
            if not isinstance(record, dict) or set(record) != {"bytes", "path", "sha256"}:
                raise GateError(f"Toolchain site-packages {list_name} record is incomplete")
            record_path = record.get("path")
            if (
                isinstance(record.get("bytes"), bool)
                or not isinstance(record.get("bytes"), int)
                or record["bytes"] < 0
                or not isinstance(record_path, str)
                or not any(
                    record_path.startswith(root + "/") for root in roots
                )
                or not isinstance(record.get("sha256"), str)
                or re.fullmatch(r"[0-9a-f]{64}", record["sha256"]) is None
            ):
                raise GateError(f"Toolchain site-packages {list_name} record is invalid")
            paths.append(record_path)
        if paths != sorted(set(paths)):
            raise GateError(f"Toolchain site-packages {list_name} records are unordered")
        if list_name == "executable_pth_files" and any(
            not path.lower().endswith(".pth") for path in paths
        ):
            raise GateError("Toolchain executable .pth inventory contains a non-.pth file")

    runtime_identity = toolchain.get("runtime_identity") or {}
    if set(runtime_identity) != {
        "abi",
        "cache_tag",
        "implementation",
        "launcher",
        "machine",
        "platform",
        "python_version",
        "sysconfig_platform",
    }:
        raise GateError("Toolchain runtime identity is incomplete")
    launcher = runtime_identity.get("launcher") or {}
    if set(launcher) != {
        "encodings",
        "flags",
        "host",
        "locale",
        "semantic_environment",
        "site_enable_user_site",
        "sys_path",
    }:
        raise GateError("Toolchain isolated-launch identity is incomplete")
    flags = launcher.get("flags")
    if (
        not isinstance(flags, dict)
        or set(flags) != set(TOOLCHAIN_FLAG_NAMES)
        or any(isinstance(value, (dict, list, str)) or value is None for value in flags.values())
        or flags.get("isolated") != 1
        or flags.get("ignore_environment") != 1
        or flags.get("dont_write_bytecode") != 1
        or flags.get("no_user_site") != 1
        or flags.get("safe_path") is not True
    ):
        raise GateError("Toolchain launcher flags do not require python -I -B")
    if launcher.get("site_enable_user_site") is not False:
        raise GateError("Toolchain launcher permits user-site loading")
    semantic_environment = launcher.get("semantic_environment")
    if (
        not isinstance(semantic_environment, dict)
        or tuple(semantic_environment) != TOOLCHAIN_SEMANTIC_ENVIRONMENT_KEYS
        or any(value is not None and not isinstance(value, str) for value in semantic_environment.values())
    ):
        raise GateError("Toolchain semantic environment inventory is incomplete or unordered")
    encodings = launcher.get("encodings")
    if (
        not isinstance(encodings, dict)
        or set(encodings) != {"default", "filesystem", "filesystem_errors"}
        or not all(isinstance(value, str) and value for value in encodings.values())
    ):
        raise GateError("Toolchain encoding identity is incomplete")
    locale_identity = launcher.get("locale")
    if (
        not isinstance(locale_identity, dict)
        or set(locale_identity) != {"ctype", "encoding", "preferred_encoding"}
        or not all(isinstance(value, str) and value for value in locale_identity.values())
    ):
        raise GateError("Toolchain locale identity is incomplete")
    host = launcher.get("host")
    if not isinstance(host, dict) or set(host) != {
        "byteorder",
        "machine",
        "os_name",
        "pointer_bits",
        "processor",
        "system",
        "system_release",
        "system_version",
        "windows_version",
    }:
        raise GateError("Toolchain host/native identity is incomplete")
    if host.get("byteorder") not in ("little", "big") or host.get("pointer_bits") not in (32, 64):
        raise GateError("Toolchain host/native identity is invalid")
    if not all(
        isinstance(host.get(key), str)
        for key in ("machine", "os_name", "processor", "system", "system_release", "system_version")
    ):
        raise GateError("Toolchain host/native string identity is invalid")
    windows_version = host.get("windows_version")
    if windows_version is not None and (
        not isinstance(windows_version, dict)
        or set(windows_version)
        != {
            "build",
            "major",
            "minor",
            "platform",
            "product_type",
            "service_pack",
            "service_pack_major",
            "service_pack_minor",
            "suite_mask",
        }
    ):
        raise GateError("Toolchain Windows native identity is invalid")
    sys_path_identity = launcher.get("sys_path")
    if not isinstance(sys_path_identity, list) or not sys_path_identity:
        raise GateError("Toolchain sys.path identity is empty")
    sys_path_labels: list[str] = []
    for entry in sys_path_identity:
        if (
            not isinstance(entry, dict)
            or set(entry) != {"kind", "path"}
            or entry.get("kind") not in ("absent", "directory", "file")
            or not isinstance(entry.get("path"), str)
            or not (
                entry["path"] in ("environment", "base_runtime")
                or entry["path"].startswith("environment/")
                or entry["path"].startswith("base_runtime/")
            )
        ):
            raise GateError("Toolchain sys.path identity is invalid")
        sys_path_labels.append(entry["path"])
    if len(sys_path_labels) != len(set(sys_path_labels)):
        raise GateError("Toolchain sys.path identity contains duplicates")

    native_libraries = toolchain.get("native_libraries") or {}
    if set(native_libraries) != {
        "GEOS",
        "Pillow_zlib",
        "PROJ",
        "PROJ_runtime",
        "zlib_compile",
        "zlib_runtime",
    }:
        raise GateError("Toolchain native-library identity is incomplete")
    proj_runtime = native_libraries.get("PROJ_runtime") or {}
    if set(proj_runtime) != {"data_directory", "data_payload", "network_enabled"}:
        raise GateError("Toolchain PROJ runtime identity is incomplete")
    if proj_runtime.get("network_enabled") is not False:
        raise GateError("Toolchain PROJ runtime permits network access")
    data_directory = proj_runtime.get("data_directory")
    if (
        not isinstance(data_directory, str)
        or not any(data_directory.startswith(root + "/") for root in roots)
    ):
        raise GateError("Toolchain PROJ data directory is outside the bound payload")
    data_payload = proj_runtime.get("data_payload")
    if not isinstance(data_payload, dict) or set(data_payload) != {
        "file_count",
        "inventory_sha256",
        "total_bytes",
    }:
        raise GateError("Toolchain PROJ data payload summary is incomplete")
    if (
        isinstance(data_payload["file_count"], bool)
        or not isinstance(data_payload["file_count"], int)
        or data_payload["file_count"] < 1
        or isinstance(data_payload["total_bytes"], bool)
        or not isinstance(data_payload["total_bytes"], int)
        or data_payload["total_bytes"] < 1
        or not isinstance(data_payload["inventory_sha256"], str)
        or re.fullmatch(r"[0-9a-f]{64}", data_payload["inventory_sha256"]) is None
    ):
        raise GateError("Toolchain PROJ data payload summary is invalid")
    validate_naip_registry_contract(value)
    return value


def resolve_repository_relative_path(relative: Any, label: str) -> Path:
    """Resolve one canonical POSIX repository path and reject traversal/symlink escape."""

    if not isinstance(relative, str) or not relative or "\\" in relative:
        raise GateError(f"{label} path is not a canonical repository-relative POSIX path")
    pure = PurePosixPath(relative)
    if (
        pure.is_absolute()
        or pure.as_posix() != relative
        or any(part in ("", ".", "..") for part in pure.parts)
        or (pure.parts and ":" in pure.parts[0])
    ):
        raise GateError(f"{label} path is not a canonical repository-relative POSIX path")
    root = REPO_ROOT.resolve()
    resolved = root.joinpath(*pure.parts).resolve()
    try:
        resolved.relative_to(root)
    except ValueError as error:
        raise GateError(f"{label} path escapes the repository") from error
    return resolved


def resolve_external_relative_path(external_run: Path, relative: Any, label: str) -> Path:
    if not isinstance(relative, str) or not relative or "\\" in relative:
        raise GateError(f"{label} path is not a canonical external relative POSIX path")
    pure = PurePosixPath(relative)
    if (
        pure.is_absolute()
        or pure.as_posix() != relative
        or any(part in ("", ".", "..") for part in pure.parts)
        or (pure.parts and ":" in pure.parts[0])
    ):
        raise GateError(f"{label} path is not a canonical external relative POSIX path")
    root = external_run.expanduser().resolve()
    resolved = root.joinpath(*pure.parts).resolve()
    try:
        resolved.relative_to(root)
    except ValueError as error:
        raise GateError(f"{label} path escapes the external run") from error
    return resolved


def parse_requirements_lock(payload: bytes) -> dict[str, str]:
    try:
        text = payload.decode("utf-8")
    except UnicodeError as error:
        raise ToolchainPreflightError("Requirements lock is not UTF-8") from error
    pins: dict[str, str] = {}
    for line_number, raw_line in enumerate(text.splitlines(), start=1):
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        match = re.fullmatch(r"([A-Za-z0-9_.-]+)==([A-Za-z0-9_.+-]+)", line)
        if match is None:
            raise ToolchainPreflightError(
                f"Requirements lock line {line_number} is not one exact name==version pin"
            )
        name, version = match.groups()
        canonical_name = canonical_distribution_name(name)
        if canonical_name in pins:
            raise ToolchainPreflightError(f"Duplicate requirements-lock pin: {name}")
        pins[canonical_name] = version
    return pins


def _payload_cache_file(path: Path) -> bool:
    return any(part.lower() == "__pycache__" for part in path.parts) or path.suffix.lower() in (
        ".pyc",
        ".pyo",
    )


def canonical_distribution_name(value: str) -> str:
    return re.sub(r"[-_.]+", "-", value.strip().lower())


def _payload_location(path: Path) -> str:
    """Return a canonical root-labelled path without exposing an absolute user path."""

    resolved = path.resolve(strict=True)
    roots: list[tuple[str, Path]] = [("environment", Path(sys.prefix).resolve())]
    base = Path(sys.base_prefix).resolve()
    if base != roots[0][1]:
        roots.append(("base_runtime", base))
    for label, root in roots:
        try:
            relative = resolved.relative_to(root)
        except ValueError:
            continue
        if any(part in ("", ".", "..") for part in relative.parts):
            break
        return f"{label}/{relative.as_posix()}"
    raise ToolchainPreflightError(
        f"Installed payload path is outside the accepted interpreter roots: {path.name}"
    )


def _payload_location_allow_absent(path: Path) -> str:
    """Canonicalize a launch path even when CPython advertises an absent ZIP."""

    candidate = Path(path)
    if not candidate.is_absolute():
        raise ToolchainPreflightError("Interpreter search path contains a relative entry")
    raw = os.path.normcase(str(candidate))
    normalized = os.path.normcase(os.path.abspath(os.path.normpath(str(candidate))))
    if raw.rstrip("\\/") != normalized.rstrip("\\/"):
        raise ToolchainPreflightError("Interpreter search path is not lexically canonical")
    resolved = candidate.resolve(strict=False)
    roots: list[tuple[str, Path]] = [("environment", Path(sys.prefix).resolve())]
    base = Path(sys.base_prefix).resolve()
    if base != roots[0][1]:
        roots.append(("base_runtime", base))
    for label, root in roots:
        try:
            relative = resolved.relative_to(root)
        except ValueError:
            continue
        if any(part in ("", ".", "..") for part in relative.parts):
            break
        return label if not relative.parts else f"{label}/{relative.as_posix()}"
    raise ToolchainPreflightError(
        "Interpreter search path is outside the accepted interpreter roots"
    )


def _path_is_link_or_reparse(path: Path) -> bool:
    try:
        stat_result = path.lstat()
    except OSError as error:
        raise ToolchainPreflightError(
            f"Installed payload member cannot be inspected: {path.name}"
        ) from error
    attributes = int(getattr(stat_result, "st_file_attributes", 0))
    return path.is_symlink() or bool(attributes & 0x400)


def canonical_sys_path_identity(entries: Iterable[str] | None = None) -> list[dict[str, str]]:
    """Bind the ordered isolated interpreter path using root-labelled entries."""

    result: list[dict[str, str]] = []
    seen: set[str] = set()
    repository = REPO_ROOT.resolve()
    permitted_repository_entries = {repository, SCRIPT_DIR.resolve()}
    for raw_entry in sys.path if entries is None else entries:
        if not isinstance(raw_entry, str) or not raw_entry:
            raise ToolchainPreflightError("Interpreter search path contains an empty entry")
        candidate = Path(raw_entry)
        if not candidate.is_absolute():
            raise ToolchainPreflightError("Interpreter search path contains a relative entry")
        raw_normalized = os.path.normcase(str(candidate)).rstrip("\\/")
        absolute_normalized = os.path.normcase(
            os.path.abspath(os.path.normpath(str(candidate)))
        ).rstrip("\\/")
        if raw_normalized != absolute_normalized:
            raise ToolchainPreflightError("Interpreter search path is not lexically canonical")
        if candidate.is_symlink() or (
            candidate.exists() and _path_is_link_or_reparse(candidate)
        ):
            raise ToolchainPreflightError(
                "Interpreter search path contains a link or reparse point"
            )
        resolved = candidate.resolve(strict=False)
        try:
            resolved.relative_to(repository)
        except ValueError:
            pass
        else:
            # Pytest prepends the exact source directory before importing this
            # already repository-bound gate.  It is not interpreter payload and
            # is deliberately excluded; every other repository path is refused.
            if resolved not in permitted_repository_entries:
                raise ToolchainPreflightError(
                    "Interpreter search path contains an unapproved repository entry"
                )
            continue
        canonical = _payload_location_allow_absent(candidate)
        if canonical in seen:
            raise ToolchainPreflightError("Interpreter search path contains a duplicate entry")
        seen.add(canonical)
        if candidate.exists():
            if candidate.is_dir():
                kind = "directory"
            elif candidate.is_file():
                kind = "file"
            else:
                raise ToolchainPreflightError(
                    "Interpreter search path contains an unsupported filesystem object"
                )
        else:
            kind = "absent"
        result.append({"kind": kind, "path": canonical})
    if not result:
        raise ToolchainPreflightError("Interpreter search path is empty")
    return result


def semantic_environment_identity(
    environ: dict[str, str] | os._Environ[str] | None = None,
) -> dict[str, str | None]:
    """Bind every Python/PROJ startup input and reject unknown prefixed inputs."""

    source = os.environ if environ is None else environ
    normalized: dict[str, str] = {}
    for raw_key, raw_value in source.items():
        key = str(raw_key).upper()
        if not (key.startswith("PYTHON") or key.startswith("PROJ")):
            continue
        if key in normalized:
            raise ToolchainPreflightError(
                "Semantic environment contains duplicate case-insensitive keys"
            )
        if key not in TOOLCHAIN_SEMANTIC_ENVIRONMENT_KEYS:
            raise ToolchainPreflightError(
                f"Semantic environment contains an unrecognized startup input: {key}"
            )
        normalized[key] = str(raw_value)
    return {
        key: normalized.get(key) for key in TOOLCHAIN_SEMANTIC_ENVIRONMENT_KEYS
    }


def launcher_runtime_identity(
    *,
    flags: Any | None = None,
    path_entries: Iterable[str] | None = None,
    environ: dict[str, str] | os._Environ[str] | None = None,
    enable_user_site: bool | None = None,
) -> dict[str, Any]:
    """Prove this process was launched with the isolated, no-bytecode contract."""

    live_flags = sys.flags if flags is None else flags
    flag_values: dict[str, Any] = {}
    for name in TOOLCHAIN_FLAG_NAMES:
        if not hasattr(live_flags, name):
            raise ToolchainPreflightError(f"Interpreter lacks required sys.flags field: {name}")
        flag_values[name] = getattr(live_flags, name)
    required_flags = {
        "dont_write_bytecode": 1,
        "ignore_environment": 1,
        "isolated": 1,
        "no_user_site": 1,
        "safe_path": True,
    }
    if any(flag_values[name] != expected for name, expected in required_flags.items()):
        raise ToolchainPreflightError(
            "Geospatial commands require the isolated launcher: python -I -B"
        )
    user_site = site.ENABLE_USER_SITE if enable_user_site is None else enable_user_site
    if user_site is not False:
        raise ToolchainPreflightError("Interpreter user-site loading is not disabled")
    windows_identity: dict[str, Any] | None = None
    if hasattr(sys, "getwindowsversion"):
        windows_version = sys.getwindowsversion()
        windows_identity = {
            name: getattr(windows_version, name)
            for name in (
                "build",
                "major",
                "minor",
                "platform",
                "product_type",
                "service_pack",
                "service_pack_major",
                "service_pack_minor",
                "suite_mask",
            )
        }
    return {
        "encodings": {
            "default": sys.getdefaultencoding(),
            "filesystem": sys.getfilesystemencoding(),
            "filesystem_errors": sys.getfilesystemencodeerrors(),
        },
        "flags": flag_values,
        "host": {
            "byteorder": sys.byteorder,
            "machine": platform.machine(),
            "os_name": os.name,
            "pointer_bits": 64 if sys.maxsize > 2**32 else 32,
            "processor": platform.processor(),
            "system": platform.system(),
            "system_release": platform.release(),
            "system_version": platform.version(),
            "windows_version": windows_identity,
        },
        "locale": {
            "ctype": locale.setlocale(locale.LC_CTYPE),
            "encoding": locale.getencoding(),
            "preferred_encoding": locale.getpreferredencoding(False),
        },
        "semantic_environment": semantic_environment_identity(environ),
        "site_enable_user_site": user_site,
        "sys_path": canonical_sys_path_identity(path_entries),
    }


def _payload_file_record(path: Path) -> dict[str, Any]:
    return {
        "bytes": path.stat().st_size,
        "path": _payload_location(path),
        "sha256": sha256_file(path),
    }


def _site_exception_may_be_frozen(path: Path) -> bool:
    """Allow exact non-code residue only; never bless an import/startup surface."""

    lower_name = path.name.lower()
    if lower_name == "sitecustomize" or lower_name == "usercustomize":
        return False
    if lower_name.startswith("sitecustomize.") or lower_name.startswith("usercustomize."):
        return False
    executable_suffixes = {
        "",
        ".bat",
        ".cmd",
        ".com",
        ".dll",
        ".exe",
        ".pyd",
        ".py",
        ".pyi",
        ".pyo",
        ".pyw",
        ".so",
        ".pth",
        ".ps1",
    }
    return path.suffix.lower() not in executable_suffixes


def site_packages_identity(
    owned_paths: set[str], *, roots: Iterable[Path] | None = None
) -> dict[str, Any]:
    """Prove active site-packages equals the RECORD-owned union plus frozen residue."""

    if roots is None:
        roots = {
            Path(sysconfig.get_path("purelib")),
            Path(sysconfig.get_path("platlib")),
        }
    resolved_roots: dict[str, Path] = {}
    for raw_root in roots:
        candidate_root = Path(raw_root)
        if candidate_root.is_symlink() or (
            candidate_root.exists() and _path_is_link_or_reparse(candidate_root)
        ):
            raise ToolchainPreflightError(
                "Active site-packages root is linked or reparsed"
            )
        root = candidate_root.resolve(strict=True)
        if not root.is_dir() or _path_is_link_or_reparse(root):
            raise ToolchainPreflightError(
                "Active site-packages root is missing, linked, or not a directory"
            )
        canonical_root = _payload_location(root)
        if canonical_root in resolved_roots:
            continue
        resolved_roots[canonical_root] = root
    if not resolved_roots:
        raise ToolchainPreflightError("No active site-packages root is available")

    actual_by_path: dict[str, Path] = {}
    for canonical_root, root in sorted(resolved_roots.items()):
        for directory, directory_names, file_names in os.walk(root, followlinks=False):
            current = Path(directory)
            kept_directories: list[str] = []
            for name in directory_names:
                child = current / name
                if name.lower() == "__pycache__":
                    continue
                if _path_is_link_or_reparse(child):
                    raise ToolchainPreflightError(
                        "Site-packages contains a linked or reparsed directory"
                    )
                kept_directories.append(name)
            directory_names[:] = kept_directories
            for name in file_names:
                path = current / name
                if _payload_cache_file(path):
                    continue
                if _path_is_link_or_reparse(path) or not path.is_file():
                    raise ToolchainPreflightError(
                        "Site-packages contains a linked or unsupported file"
                    )
                canonical = _payload_location(path)
                if canonical in actual_by_path:
                    raise ToolchainPreflightError(
                        "Site-packages contains a duplicate canonical file"
                    )
                if not (canonical == canonical_root or canonical.startswith(canonical_root + "/")):
                    raise ToolchainPreflightError("Site-packages member escapes its root")
                actual_by_path[canonical] = path

    root_labels = tuple(sorted(resolved_roots))
    owned_under_roots = {
        path
        for path in owned_paths
        if any(path == root or path.startswith(root + "/") for root in root_labels)
    }
    actual_paths = set(actual_by_path)
    missing = owned_under_roots - actual_paths
    if missing:
        raise ToolchainPreflightError(
            "Site-packages is missing a RECORD-owned non-cache file"
        )
    unowned_paths = sorted(actual_paths - owned_under_roots)
    for canonical in unowned_paths:
        if not _site_exception_may_be_frozen(actual_by_path[canonical]):
            raise ToolchainPreflightError(
                "Site-packages contains an unowned .pth, startup hook, executable, or stray module"
            )

    executable_pth_files: list[dict[str, Any]] = []
    startup_module_files: list[dict[str, Any]] = []
    for canonical, path in sorted(actual_by_path.items()):
        lower_name = path.name.lower()
        if path.suffix.lower() == ".pth":
            try:
                text = path.read_bytes().decode("utf-8-sig")
            except UnicodeError as error:
                raise ToolchainPreflightError("Owned .pth file is not UTF-8") from error
            if any(line.startswith(("import ", "import\t")) for line in text.splitlines()):
                executable_pth_files.append(_payload_file_record(path))
        if lower_name.startswith("sitecustomize.") or lower_name.startswith("usercustomize."):
            startup_module_files.append(_payload_file_record(path))
    exception_records = [_payload_file_record(actual_by_path[path]) for path in unowned_paths]
    return {
        "exceptions": exception_records,
        "executable_pth_files": executable_pth_files,
        "payload": summarize_payload_files(actual_by_path.values()),
        "roots": list(root_labels),
        "startup_module_files": startup_module_files,
    }


def summarize_payload_files(files: Iterable[Path]) -> dict[str, Any]:
    """Hash a complete canonical path/bytes/SHA inventory, excluding generated caches."""

    records: list[dict[str, Any]] = []
    seen: set[str] = set()
    for path in files:
        candidate = Path(path)
        if _payload_cache_file(candidate):
            continue
        if not candidate.is_file():
            raise ToolchainPreflightError(
                f"Installed payload member is missing or not a file: {candidate.name}"
            )
        relative = _payload_location(candidate)
        if relative in seen:
            raise ToolchainPreflightError(
                f"Installed payload inventory repeats a canonical path: {relative}"
            )
        seen.add(relative)
        records.append(
            {
                "bytes": candidate.stat().st_size,
                "path": relative,
                "sha256": sha256_file(candidate),
            }
        )
    records.sort(key=lambda value: value["path"])
    return {
        "file_count": len(records),
        "inventory_sha256": sha256_bytes(canonical_json_bytes(records)),
        "total_bytes": sum(int(value["bytes"]) for value in records),
    }


def distribution_payload_inventory(
    distribution: Any, *, owned_paths: set[str] | None = None
) -> dict[str, Any]:
    """Hash every existing non-cache path owned by one installed RECORD."""

    members = distribution.files
    if members is None:
        raise ToolchainPreflightError(
            f"Installed distribution has no RECORD-owned file inventory: {distribution.metadata.get('Name')}"
        )
    paths: list[Path] = []
    canonical_members: list[PurePosixPath] = []
    for member in members:
        member_text = str(member)
        if "\\" in member_text:
            raise ToolchainPreflightError(
                f"Installed distribution has a noncanonical RECORD member: {member}"
            )
        pure_member = PurePosixPath(member_text)
        if (
            pure_member.is_absolute()
            or pure_member.as_posix() != member_text
            or any(part in ("", ".") for part in pure_member.parts)
            or (pure_member.parts and ":" in pure_member.parts[0])
        ):
            raise ToolchainPreflightError(
                f"Installed distribution has a noncanonical RECORD member: {member}"
            )
        relative = Path(*pure_member.parts)
        if _payload_cache_file(relative):
            continue
        canonical_members.append(pure_member)
        located = Path(distribution.locate_file(member))
        # resolve(strict=True) both proves existence and collapses traversal or links
        # before canonical containment is checked by summarize_payload_files.
        try:
            located = located.resolve(strict=True)
        except OSError as error:
            raise ToolchainPreflightError(
                f"Installed distribution RECORD member is missing: {member}"
            ) from error
        paths.append(located)
    records = [
        member
        for member in canonical_members
        if len(member.parts) >= 2
        and member.parts[-1] == "RECORD"
        and member.parts[-2].lower().endswith(".dist-info")
    ]
    if len(records) != 1:
        raise ToolchainPreflightError(
            "Installed distribution must own exactly one canonical *.dist-info/RECORD member"
        )
    if owned_paths is not None:
        owned_paths.update(_payload_location(path) for path in paths)
    return summarize_payload_files(paths)


def runtime_payload_inventory(*, owned_paths: set[str] | None = None) -> dict[str, Any]:
    """Bind the CPython launchers, stdlib, DLLs, and import libraries in use."""

    base = Path(sys.base_prefix).resolve()
    environment = Path(sys.prefix).resolve()
    candidates: list[Path] = []
    for pattern in ("python*.exe", "python*.dll", "vcruntime*.dll"):
        candidates.extend(path for path in base.glob(pattern) if path.is_file())
    for child in (base / "DLLs", base / "libs"):
        if child.is_dir():
            candidates.extend(path for path in child.rglob("*") if path.is_file())
    standard_library = Path(sysconfig.get_path("stdlib")).resolve()
    if not standard_library.is_dir():
        raise ToolchainPreflightError("CPython standard-library root is missing")
    for path in standard_library.rglob("*"):
        if not path.is_file() or _payload_cache_file(path):
            continue
        try:
            relative = path.resolve().relative_to(standard_library)
        except ValueError as error:
            raise ToolchainPreflightError("CPython stdlib member escapes its root") from error
        if relative.parts and relative.parts[0].lower() == "site-packages":
            continue
        candidates.append(path)
    if environment != base:
        for child in (environment / "DLLs", environment / "libs"):
            if child.is_dir():
                candidates.extend(path for path in child.rglob("*") if path.is_file())
        for path in (
            environment / "pyvenv.cfg",
            *sorted((environment / "Scripts").glob("python*.exe")),
        ):
            if path.is_file():
                candidates.append(path)
    candidates.append(Path(sys.executable))
    unique = {path.resolve(): path for path in candidates}
    if owned_paths is not None:
        owned_paths.update(_payload_location(path) for path in unique.values())
    return summarize_payload_files(unique.values())


def installed_payload_identity() -> dict[str, Any]:
    distributions: dict[str, Any] = {}
    installed_versions: dict[str, str] = {}
    owned_paths: set[str] = set()
    for distribution in importlib.metadata.distributions():
        raw_name = str(distribution.metadata.get("Name") or "").strip()
        if not raw_name:
            raise ToolchainPreflightError("Installed distribution lacks a canonical Name")
        name = canonical_distribution_name(raw_name)
        if name in distributions:
            raise ToolchainPreflightError(f"Duplicate installed distribution name: {name}")
        version = distribution.version
        if not isinstance(version, str) or not version:
            raise ToolchainPreflightError(f"Installed distribution lacks a version: {raw_name}")
        installed_versions[name] = version
        distributions[name] = distribution_payload_inventory(
            distribution, owned_paths=owned_paths
        )
    runtime_summary = runtime_payload_inventory(owned_paths=owned_paths)
    import_origins: dict[str, str] = {}
    for module_name in GATE_IMPORT_ORIGIN_MODULES:
        module = importlib.import_module(module_name)
        spec = getattr(module, "__spec__", None)
        origin = getattr(spec, "origin", None)
        if origin in ("built-in", "frozen"):
            import_origins[module_name] = str(origin)
        elif not origin:
            raise ToolchainPreflightError(f"Gate import has no exact origin: {module_name}")
        else:
            canonical_origin = _payload_location(Path(origin))
            if canonical_origin not in owned_paths or _payload_cache_file(Path(canonical_origin)):
                raise ToolchainPreflightError(
                    f"Gate import origin is not owned by the bound runtime/distributions: {module_name}"
                )
            import_origins[module_name] = canonical_origin
    site_packages = site_packages_identity(owned_paths)
    return {
        "distribution_payloads": dict(sorted(distributions.items())),
        "import_origins": dict(sorted(import_origins.items())),
        "installed_distributions": dict(sorted(installed_versions.items())),
        "runtime_payload": runtime_summary,
        "schema": TOOLCHAIN_PAYLOAD_SCHEMA,
        "site_packages": site_packages,
    }


def proj_runtime_identity(payload_identity: dict[str, Any]) -> dict[str, Any]:
    """Bind the offline PROJ database tree and prove it is owned site payload."""

    if pyproj.network.is_network_enabled() is not False:
        raise ToolchainPreflightError("PROJ network access must be disabled")
    try:
        candidate_data_root = Path(pyproj.datadir.get_data_dir())
        if candidate_data_root.is_symlink() or (
            candidate_data_root.exists() and _path_is_link_or_reparse(candidate_data_root)
        ):
            raise ToolchainPreflightError(
                "Resolved PROJ data location is linked or reparsed"
            )
        data_root = candidate_data_root.resolve(strict=True)
    except (OSError, TypeError) as error:
        raise ToolchainPreflightError("Resolved PROJ data directory is unavailable") from error
    if not data_root.is_dir() or _path_is_link_or_reparse(data_root):
        raise ToolchainPreflightError(
            "Resolved PROJ data location is linked or not a directory"
        )
    canonical_root = _payload_location(data_root)
    site_identity = payload_identity.get("site_packages") or {}
    site_roots = site_identity.get("roots") or []
    if not any(
        canonical_root == root or canonical_root.startswith(str(root) + "/")
        for root in site_roots
    ):
        raise ToolchainPreflightError(
            "Resolved PROJ data directory is outside the bound site-packages payload"
        )
    exception_paths = {
        str(record.get("path")) for record in site_identity.get("exceptions") or []
    }
    data_files: list[Path] = []
    for directory, directory_names, file_names in os.walk(data_root, followlinks=False):
        current = Path(directory)
        kept_directories: list[str] = []
        for name in directory_names:
            child = current / name
            if name.lower() == "__pycache__":
                continue
            if _path_is_link_or_reparse(child):
                raise ToolchainPreflightError(
                    "PROJ data payload contains a linked or reparsed directory"
                )
            kept_directories.append(name)
        directory_names[:] = kept_directories
        for name in file_names:
            path = current / name
            if _payload_cache_file(path):
                continue
            if _path_is_link_or_reparse(path) or not path.is_file():
                raise ToolchainPreflightError(
                    "PROJ data payload contains a linked or unsupported file"
                )
            canonical = _payload_location(path)
            if canonical in exception_paths:
                raise ToolchainPreflightError(
                    "PROJ data payload contains a non-RECORD-owned exception"
                )
            data_files.append(path)
    if not data_files:
        raise ToolchainPreflightError("PROJ data payload is empty")
    return {
        "data_directory": canonical_root,
        "data_payload": summarize_payload_files(data_files),
        "network_enabled": False,
    }


def live_toolchain_environment() -> dict[str, Any]:
    launcher_identity = launcher_runtime_identity()
    if pyproj.network.is_network_enabled() is not False:
        raise ToolchainPreflightError("PROJ network access must be disabled")
    distribution_names = (
        "Pillow",
        "Pygments",
        "certifi",
        "colorama",
        "iniconfig",
        "numpy",
        "packaging",
        "pluggy",
        "pyproj",
        "pytest",
        "shapely",
    )
    try:
        distributions = {
            name: importlib.metadata.version(name) for name in distribution_names
        }
    except importlib.metadata.PackageNotFoundError as error:
        raise ToolchainPreflightError(f"Required distribution is missing: {error.name}") from error
    payload_identity = installed_payload_identity()
    return {
        "distributions": dict(sorted(distributions.items())),
        "native_libraries": {
            "GEOS": shapely.geos_version_string,
            "Pillow_zlib": pillow_features.version("zlib"),
            "PROJ": pyproj.proj_version_str,
            "PROJ_runtime": proj_runtime_identity(payload_identity),
            "zlib_compile": zlib.ZLIB_VERSION,
            "zlib_runtime": zlib.ZLIB_RUNTIME_VERSION,
        },
        "runtime_identity": {
            "abi": sysconfig.get_config_var("SOABI"),
            "cache_tag": sys.implementation.cache_tag,
            "implementation": platform.python_implementation(),
            "machine": platform.machine(),
            "platform": sys.platform,
            "python_version": platform.python_version(),
            "sysconfig_platform": sysconfig.get_platform(),
            "launcher": launcher_identity,
        },
        "payload_identity": payload_identity,
    }


def validate_toolchain_contract(
    registry: dict[str, Any], registry_path: Path
) -> dict[str, Any]:
    """Validate lock, interpreter, distributions, and native libraries without writing."""

    contract = registry["toolchain"]
    expected_registry = resolve_repository_relative_path(
        contract["source_registry_path"], "Toolchain source registry"
    )
    if registry_path.resolve() != expected_registry:
        raise ToolchainPreflightError("Live registry path differs from the registry-owned toolchain path")
    expected_tool = resolve_repository_relative_path(
        contract["source_tool_path"], "Toolchain source tool"
    )
    if Path(__file__).resolve() != expected_tool:
        raise ToolchainPreflightError("Live source-gate path differs from the registry-owned tool path")
    lock_path = resolve_repository_relative_path(
        contract["requirements_lock"]["path"], "Toolchain requirements lock"
    )
    if not lock_path.is_file():
        raise ToolchainPreflightError("Pinned requirements lock is missing")
    actual_lock = repository_file_record(lock_path)
    if actual_lock != contract["requirements_lock"]:
        raise ToolchainPreflightError("Pinned requirements-lock bytes/hash changed")
    pins = parse_requirements_lock(lock_path.read_bytes())
    expected_pins = {
        "python": contract["runtime_identity"]["python_version"],
        **{
            canonical_distribution_name(name): version
            for name, version in contract["distributions"].items()
        },
    }
    if pins != expected_pins:
        raise ToolchainPreflightError("Requirements-lock closure differs from the registry contract")
    actual = live_toolchain_environment()
    for key in (
        "runtime_identity",
        "distributions",
        "native_libraries",
        "payload_identity",
    ):
        if actual[key] != contract[key]:
            raise ToolchainPreflightError(f"Live toolchain {key} differs from the registry contract")
    return {
        "environment": actual,
        "requirements_lock": actual_lock,
        "schema": TOOLCHAIN_MANIFEST_SCHEMA,
        "status": "PASS",
    }


def external_toolchain_manifest_path(registry: dict[str, Any], external_run: Path) -> Path:
    return resolve_external_relative_path(
        external_run, registry["toolchain"]["manifest_path"], "Toolchain manifest"
    )


def validate_or_create_external_toolchain_manifest(
    registry: dict[str, Any],
    registry_path: Path,
    external_run: Path,
    *,
    allow_create: bool,
) -> dict[str, Any]:
    expected = validate_toolchain_contract(registry, registry_path)
    manifest_path = external_toolchain_manifest_path(registry, external_run)
    expected_bytes = canonical_json_bytes(expected)
    if manifest_path.is_file():
        if manifest_path.read_bytes() != expected_bytes:
            raise ToolchainPreflightError("External toolchain manifest is stale or inconsistent")
    elif allow_create:
        atomic_write_bytes(manifest_path, expected_bytes)
    else:
        raise ToolchainPreflightError("External canonical toolchain manifest is missing")
    return {
        "environment": expected["environment"],
        "manifest": file_record(manifest_path, external_run),
        "requirements_lock": expected["requirements_lock"],
    }


def validate_frozen_toolchain_binding(
    freeze: dict[str, Any], live_binding: dict[str, Any]
) -> None:
    if freeze.get("toolchain") != live_binding:
        raise ToolchainPreflightError("Source freeze toolchain binding is missing, stale, or inconsistent")


def git_worktree_context() -> dict[str, Any]:
    head = subprocess.run(
        ["git", "rev-parse", "HEAD"],
        cwd=REPO_ROOT,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()
    status = subprocess.run(
        ["git", "status", "--porcelain=v1", "--untracked-files=all"],
        cwd=REPO_ROOT,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.replace("\r\n", "\n")
    return {
        "dirty": bool(status),
        "head": head,
        "status_command": ["git", "status", "--porcelain=v1", "--untracked-files=all"],
        "status_porcelain_v1": status,
        "status_porcelain_v1_sha256": sha256_bytes(status.encode("utf-8")),
    }


def compile_command_context(
    registry_path: Path,
    external_run: Path,
    data_dir: Path,
    evidence_dir: Path,
) -> dict[str, Any]:
    return {
        "argv": [
            str(Path(sys.executable).resolve()),
            "-I",
            "-B",
            str(Path(__file__).resolve()),
            "--registry",
            str(registry_path.resolve()),
            "compile",
            "--external-run",
            str(external_run.resolve()),
            "--data-dir",
            str(data_dir.resolve()),
            "--evidence-dir",
            str(evidence_dir.resolve()),
        ],
        "cwd": str(REPO_ROOT.resolve()),
    }


def rebuild_command_context(
    registry_path: Path,
    external_run: Path,
    data_dir: Path,
    evidence_dir: Path,
) -> dict[str, Any]:
    return {
        "argv": [
            str(Path(sys.executable).resolve()),
            "-I",
            "-B",
            str(Path(__file__).resolve()),
            "--registry",
            str(registry_path.resolve()),
            "rebuild-check",
            "--external-run",
            str(external_run.resolve()),
            "--data-dir",
            str(data_dir.resolve()),
            "--evidence-dir",
            str(evidence_dir.resolve()),
        ],
        "cwd": str(REPO_ROOT.resolve()),
    }


def current_cli_command_context() -> dict[str, Any]:
    return {
        "argv": [str(Path(sys.executable).resolve()), "-I", "-B", *sys.argv],
        "cwd": str(Path.cwd().resolve()),
    }


def validate_recorded_compile_command(
    command: Any,
    *,
    registry_path: Path,
    external_run: Path,
    data_dir: Path,
    evidence_dir: Path,
) -> None:
    if not isinstance(command, dict) or set(command) != {"argv", "cwd"}:
        raise GateError("Run-context command must contain exact argv and cwd")
    argv = command.get("argv")
    if not isinstance(argv, list) or not all(isinstance(item, str) for item in argv):
        raise GateError("Run-context command argv is invalid")
    if len(argv) < 5 or Path(argv[0]).resolve() != Path(sys.executable).resolve():
        raise GateError("Run-context command interpreter differs from the pinned runtime")
    if argv[1:3] != ["-I", "-B"]:
        raise GateError("Run-context command must use the isolated python -I -B launcher")
    cwd = Path(str(command.get("cwd") or "")).resolve()
    if cwd != REPO_ROOT.resolve():
        raise GateError("Run-context command cwd differs from the repository root")
    script = Path(argv[3])
    script = (cwd / script).resolve() if not script.is_absolute() else script.resolve()
    if script != Path(__file__).resolve():
        raise GateError("Run-context command source-gate path is stale")
    try:
        parsed = build_parser().parse_args(argv[4:])
    except SystemExit as error:
        raise GateError("Run-context command does not parse as a source-gate command") from error
    if parsed.command != "compile":
        raise GateError("Primary run context was not produced by the compile command")

    def resolve_argument(value: Path) -> Path:
        return (cwd / value).resolve() if not value.is_absolute() else value.resolve()

    expected = {
        "registry": registry_path.resolve(),
        "external_run": external_run.resolve(),
        "data_dir": data_dir.resolve(),
        "evidence_dir": evidence_dir.resolve(),
    }
    actual = {
        "registry": resolve_argument(parsed.registry),
        "external_run": resolve_argument(parsed.external_run),
        "data_dir": resolve_argument(parsed.data_dir),
        "evidence_dir": resolve_argument(parsed.evidence_dir),
    }
    if actual != expected:
        raise GateError("Run-context command arguments do not match the compiled inputs/outputs")


def validate_recorded_rebuild_command(
    command: Any,
    *,
    registry_path: Path,
    external_run: Path,
    data_dir: Path,
    evidence_dir: Path,
) -> None:
    if not isinstance(command, dict) or set(command) != {"argv", "cwd"}:
        raise GateError("Rebuild command must contain exact argv and cwd")
    argv = command.get("argv")
    if (
        not isinstance(argv, list)
        or not all(isinstance(item, str) for item in argv)
        or len(argv) < 5
        or Path(argv[0]).resolve() != Path(sys.executable).resolve()
        or argv[1:3] != ["-I", "-B"]
    ):
        raise GateError("Rebuild command must use the pinned isolated python -I -B runtime")
    cwd = Path(str(command.get("cwd") or "")).resolve()
    if cwd != REPO_ROOT.resolve():
        raise GateError("Rebuild command cwd differs from the repository root")
    script = Path(argv[3])
    script = (cwd / script).resolve() if not script.is_absolute() else script.resolve()
    if script != Path(__file__).resolve():
        raise GateError("Rebuild command source-gate path is stale")
    try:
        parsed = build_parser().parse_args(argv[4:])
    except SystemExit as error:
        raise GateError("Rebuild command does not parse") from error
    if parsed.command != "rebuild-check":
        raise GateError("Recorded operation is not rebuild-check")

    def resolved(value: Path) -> Path:
        return (cwd / value).resolve() if not value.is_absolute() else value.resolve()

    actual = {
        "registry": resolved(parsed.registry),
        "external_run": resolved(parsed.external_run),
        "data_dir": resolved(parsed.data_dir),
        "evidence_dir": resolved(parsed.evidence_dir),
    }
    expected = {
        "registry": registry_path.resolve(),
        "external_run": external_run.resolve(),
        "data_dir": data_dir.resolve(),
        "evidence_dir": evidence_dir.resolve(),
    }
    if actual != expected:
        raise GateError("Rebuild command arguments do not match the exact package/targets")


def deterministic_compile_declarations(registry: dict[str, Any]) -> dict[str, Any]:
    model = registry["authority_model"]
    return {
        "determinism": {
            "canonical_json": "UTF-8, indent=2, sorted keys, LF, terminal newline",
            "hash_algorithm": "SHA-256",
            "randomness": "NONE",
            "record_order": "EXPLICIT_STABLE_SORTS",
            "timestamps": "FROZEN_ACQUISITION_VALUES_ONLY",
        },
        "tolerances": {
            "automatic_conflation_max_distance_meters": model[
                "automatic_conflation_max_distance_meters"
            ],
            "conflation_tolerance_meters": model["conflation_tolerance_meters"],
            "lane_match_tolerance_meters": model["lane_match_tolerance_meters"],
            "minimum_lane_match_ratio": model["minimum_lane_match_ratio"],
            "minimum_reference_coverage_ratio": model["minimum_reference_coverage_ratio"],
        },
    }


def build_compile_run_context(
    registry: dict[str, Any],
    registry_path: Path,
    external_run: Path,
    data_dir: Path,
    evidence_dir: Path,
    toolchain_binding: dict[str, Any],
    freeze_path: Path,
    *,
    command_context: dict[str, Any] | None = None,
    git_context: dict[str, Any] | None = None,
) -> dict[str, Any]:
    specification_path = resolve_repository_relative_path(
        registry["specification"]["path"], "Traffic V2 specification"
    )
    camera_path = resolve_repository_relative_path(
        registry["authority_model"]["benchmark_camera_contract"]["path"],
        "Benchmark camera contract",
    )
    return {
        "accepted_source_sha": registry["accepted_source_sha"],
        "camera_contract": repository_file_record(camera_path),
        "command": command_context
        or compile_command_context(registry_path, external_run, data_dir, evidence_dir),
        **deterministic_compile_declarations(registry),
        "git": git_context or git_worktree_context(),
        "requirements_lock": toolchain_binding["requirements_lock"],
        "source_freeze": file_record(freeze_path, external_run),
        "source_registry": repository_file_record(registry_path),
        "source_tool": repository_file_record(Path(__file__)),
        "specification": repository_file_record(specification_path),
        "toolchain": toolchain_binding,
    }


def validate_pinned_inputs(registry: dict[str, Any]) -> None:
    spec = REPO_ROOT / registry["specification"]["path"]
    if sha256_file(spec) != registry["specification"]["sha256"]:
        raise GateError("Frozen Traffic V2 specification hash changed")
    head = subprocess.run(
        ["git", "rev-parse", "HEAD"], cwd=REPO_ROOT, check=True, text=True, capture_output=True
    ).stdout.strip()
    if head != registry["accepted_source_sha"]:
        raise GateError(f"Recovery HEAD {head} != accepted source {registry['accepted_source_sha']}")
    camera_contract = registry["authority_model"]["benchmark_camera_contract"]
    contract_path = REPO_ROOT / camera_contract["path"]
    if not contract_path.is_file():
        raise GateError(f"Lead benchmark-camera contract is missing: {camera_contract['path']}")
    if sha256_file(contract_path) != camera_contract["sha256"]:
        raise GateError("Lead benchmark-camera contract hash changed")


def relative_external(path: Path, external_run: Path) -> str:
    return path.resolve().relative_to(external_run.resolve()).as_posix()


def request_record(
    *, url: str, method: str, body: bytes | None, headers: dict[str, str], timeout_seconds: int
) -> dict[str, Any]:
    return {
        "body_sha256": sha256_bytes(body or b""),
        "body_utf8": (body or b"").decode("utf-8", errors="strict"),
        "headers": dict(sorted(headers.items())),
        "method": method,
        "timeout_seconds": timeout_seconds,
        "url": url,
    }


def _transient_remote_json_error(payload: bytes) -> dict[str, Any] | None:
    """Recognize retryable ArcGIS errors that arrive inside an HTTP-200 body."""

    try:
        value = json.loads(payload)
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None
    if not isinstance(value, dict) or not isinstance(value.get("error"), dict):
        return None
    error = value["error"]
    code = error.get("code")
    if type(code) is int and (code == 429 or 500 <= code <= 599):
        return error
    return None


def fetch_bytes(
    url: str,
    *,
    method: str = "GET",
    body: bytes | None = None,
    content_type: str | None = None,
    attempts: int = 4,
    timeout_seconds: int = 90,
) -> tuple[bytes, dict[str, Any]]:
    headers = {
        "Accept": "application/json,text/html;q=0.9,*/*;q=0.1",
        "User-Agent": USER_AGENT,
    }
    if content_type:
        headers["Content-Type"] = content_type
    errors: list[dict[str, Any]] = []
    for attempt in range(1, attempts + 1):
        requested_utc = utc_now()
        request = urllib.request.Request(url, data=body, method=method, headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
                payload = response.read()
                response_headers = {key.lower(): value for key, value in response.headers.items()}
                status = int(getattr(response, "status", 200))
                final_url = response.geturl()
            if not payload:
                raise GateError("empty response")
            transient_error = _transient_remote_json_error(payload)
            if transient_error is not None:
                raise GateError(
                    "retryable ArcGIS/remote JSON error: "
                    f"{compact_json(transient_error)}"
                )
            return payload, {
                "attempt": attempt,
                "errors_before_success": errors,
                "final_url": final_url,
                "received_utc": utc_now(),
                "requested_utc": requested_utc,
                "response_headers": dict(sorted(response_headers.items())),
                "status": status,
            }
        except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError, OSError, GateError) as error:
            errors.append(
                {
                    "attempt": attempt,
                    "error": f"{type(error).__name__}: {error}",
                    "requested_utc": requested_utc,
                }
            )
            if attempt < attempts:
                time.sleep(min(2 ** (attempt - 1), 8))
    raise GateError(
        f"Request failed after {attempts} attempts: {url}: "
        f"{compact_json(errors)}"
    )


def capture_request(
    external_run: Path,
    *,
    request_id: str,
    url: str,
    output_path: Path,
    method: str = "GET",
    params: dict[str, Any] | None = None,
    timeout_seconds: int = 90,
    attempts: int = 4,
) -> tuple[bytes, dict[str, Any]]:
    body: bytes | None = None
    content_type: str | None = None
    request_url = url
    if params is not None:
        encoded = urllib.parse.urlencode([(str(key), str(value)) for key, value in sorted(params.items())])
        if method == "GET":
            request_url += ("&" if "?" in request_url else "?") + encoded
        else:
            body = encoded.encode("utf-8")
            content_type = "application/x-www-form-urlencoded; charset=utf-8"
    headers = {"Accept": "application/json,text/html;q=0.9,*/*;q=0.1", "User-Agent": USER_AGENT}
    if content_type:
        headers["Content-Type"] = content_type
    request_path = external_run / "requests" / f"{request_id}.json"
    record = request_record(
        url=request_url, method=method, body=body, headers=headers, timeout_seconds=timeout_seconds
    )
    atomic_write_json(request_path, record)
    payload, response = fetch_bytes(
        request_url,
        method=method,
        body=body,
        content_type=content_type,
        attempts=attempts,
        timeout_seconds=timeout_seconds,
    )
    atomic_write_bytes(output_path, payload)
    receipt = {
        "request": file_record(request_path, external_run),
        "response": file_record(output_path, external_run),
        # Freeze only the stable transport identity. Retry diagnostics and
        # response headers are useful while fetching but are not canonical
        # source authority and previously made live NAIP receipts fail their
        # own exact closure validation.
        "transport": {
            "final_url": response["final_url"],
            "status": response["status"],
        },
        "url": request_url,
    }
    return payload, receipt


def decode_json(payload: bytes, context: str) -> dict[str, Any]:
    try:
        value = json.loads(payload)
    except json.JSONDecodeError as error:
        raise GateError(f"{context} returned non-JSON content: {error}") from error
    if not isinstance(value, dict):
        raise GateError(f"{context} returned a non-object JSON value")
    if "error" in value:
        raise GateError(f"{context} returned ArcGIS/remote error: {value['error']}")
    return value


def arcgis_item_url(item_id: str) -> str:
    return f"https://www.arcgis.com/sharing/rest/content/items/{item_id}"


def arcgis_server_info_url(service_url: str) -> str:
    parsed = urllib.parse.urlsplit(service_url)
    marker = "/rest/services/"
    position = parsed.path.lower().find(marker)
    if position < 0:
        raise GateError(f"Cannot derive ArcGIS Server info URL from {service_url}")
    path = parsed.path[:position] + "/rest/info"
    return urllib.parse.urlunsplit((parsed.scheme, parsed.netloc, path, "", ""))


def service_root(layer_url: str) -> str:
    return re.sub(r"/\d+/?$", "", layer_url.rstrip("/"))


def resolve_layer_url(item: dict[str, Any], source: dict[str, Any]) -> str:
    if source.get("layer_url"):
        return str(source["layer_url"]).strip().rstrip("/")
    url = str(item.get("url") or "").strip().rstrip("/")
    if not url:
        raise GateError(f"ArcGIS item {source['id']} has no service URL")
    if re.search(r"/\d+$", url):
        return url
    return f"{url}/{int(source.get('layer_index', 0))}"


def license_text(item: dict[str, Any]) -> str:
    values = [
        item.get("licenseInfo"),
        item.get("termsOfUse"),
        item.get("accessInformation"),
        item.get("description"),
        item.get("snippet"),
    ]
    return "\n".join(html.unescape(str(value)) for value in values if value)


def oid_field(metadata: dict[str, Any]) -> str:
    if metadata.get("objectIdField"):
        return str(metadata["objectIdField"])
    for field in metadata.get("fields") or []:
        if field.get("type") == "esriFieldTypeOID":
            return str(field["name"])
    raise GateError("ArcGIS layer has no object ID field")


def oid_where_clause(field: str, ids: Iterable[Any]) -> str:
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", field):
        raise GateError(f"Unsafe ArcGIS object ID field: {field!r}")
    encoded: list[str] = []
    for value in ids:
        text = str(value)
        if not re.fullmatch(r"-?[0-9]+", text):
            raise GateError(f"Non-integer ArcGIS object ID: {value!r}")
        encoded.append(str(int(text)))
    if not encoded:
        raise GateError("Cannot build an empty ArcGIS object ID batch")
    return f"{field} IN ({','.join(encoded)})"


def query_parameters(registry: dict[str, Any]) -> dict[str, Any]:
    xmin, ymin, xmax, ymax = registry["arcgis_query"]["bbox_wgs84_xyxy"]
    return {
        "f": "json",
        "geometry": f"{xmin:.10f},{ymin:.10f},{xmax:.10f},{ymax:.10f}",
        "geometryType": "esriGeometryEnvelope",
        "inSR": 4326,
        "spatialRel": "esriSpatialRelIntersects",
        "where": "1=1",
    }


def capture_license_snapshots(registry: dict[str, Any], external_run: Path) -> dict[str, Any]:
    records: dict[str, Any] = {}
    for source in registry["license_snapshots"]:
        output = external_run / "metadata/licenses" / f"{source['id']}.html"
        payload, receipt = capture_request(
            external_run,
            request_id=f"licenses/{source['id']}",
            url=source["url"],
            output_path=output,
        )
        text = payload.decode("utf-8", errors="replace")
        missing = [token for token in source.get("required_contains", []) if token.lower() not in text.lower()]
        if missing:
            raise GateError(f"License snapshot {source['id']} lacks required text: {missing}")
        records[source["id"]] = {"receipt": receipt, "required_text_present": not missing}
    return records


def validate_document_payload(document: dict[str, Any], payload: bytes) -> None:
    minimum_bytes = int(document.get("minimum_bytes", 1))
    if len(payload) < minimum_bytes:
        raise GateError(
            f"Document {document['id']} is only {len(payload)} bytes; expected at least {minimum_bytes}"
        )
    magic = document.get("magic_ascii")
    if magic is not None and not payload.startswith(str(magic).encode("ascii")):
        raise GateError(f"Document {document['id']} lacks required {magic!r} signature")
    text = payload.decode("utf-8", errors="replace")
    missing = [
        token
        for token in document.get("required_contains") or []
        if str(token).lower() not in text.lower()
    ]
    if missing:
        raise GateError(f"Document {document['id']} lacks required text: {missing}")


def frozen_response_payload(
    external_run: Path,
    receipt: dict[str, Any],
    *,
    expected_request_path: str,
    expected_response_path: str,
    expected_timeout_seconds: int,
    expected_url: str,
    label: str,
) -> bytes:
    if (
        not isinstance(receipt, dict)
        or set(receipt) != {"request", "response", "transport", "url"}
        or receipt.get("url") != expected_url
    ):
        raise GateError(f"{label} receipt URL differs from the exact authority request")
    request_record_value = receipt.get("request")
    response_record = receipt.get("response")
    if (
        not isinstance(request_record_value, dict)
        or not isinstance(response_record, dict)
        or set(request_record_value) != {"bytes", "path", "sha256"}
        or set(response_record) != {"bytes", "path", "sha256"}
        or type(request_record_value.get("bytes")) is not int
        or type(response_record.get("bytes")) is not int
    ):
        raise GateError(f"{label} receipt lacks exact request/response records")
    if (
        request_record_value.get("path") != expected_request_path
        or response_record.get("path") != expected_response_path
    ):
        raise GateError(f"{label} receipt paths differ from the exact authority contract")
    request_path = resolve_external_relative_path(
        external_run, request_record_value.get("path"), f"{label} request record"
    )
    response_path = resolve_external_relative_path(
        external_run, response_record.get("path"), f"{label} response record"
    )
    try:
        request_value = json.loads(request_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise GateError(f"{label} request record is not valid UTF-8 JSON") from error
    if (
        not isinstance(request_value, dict)
        or set(request_value)
        != {"body_sha256", "body_utf8", "headers", "method", "timeout_seconds", "url"}
        or request_value.get("url") != expected_url
        or request_value.get("method") != "GET"
        or request_value.get("body_utf8") != ""
        or request_value.get("body_sha256") != sha256_bytes(b"")
        or type(request_value.get("timeout_seconds")) is not int
        or request_value.get("timeout_seconds") != expected_timeout_seconds
        or request_value.get("headers")
        != {
            "Accept": "application/json,text/html;q=0.9,*/*;q=0.1",
            "User-Agent": USER_AGENT,
        }
    ):
        raise GateError(f"{label} request record differs from the exact metadata-only GET")
    transport = receipt.get("transport")
    if (
        not isinstance(transport, dict)
        or set(transport) != {"final_url", "status"}
        or type(transport.get("status")) is not int
        or transport.get("status") != 200
        or transport.get("final_url") != expected_url
    ):
        raise GateError(f"{label} transport did not freeze an HTTP 200 response")
    try:
        payload = response_path.read_bytes()
    except OSError as error:
        raise GateError(f"{label} response payload is missing or unreadable") from error
    if file_record(request_path, external_run) != request_record_value:
        raise GateError(f"{label} request record bytes differ from its frozen binding")
    if file_record(response_path, external_run) != response_record:
        raise GateError(f"{label} response bytes differ from its frozen binding")
    return payload


_HTML_NONVISIBLE_ELEMENTS = frozenset(
    ("head", "noscript", "script", "style", "template", "title")
)
_HTML_NONAUTHORITY_SUBTREE_ELEMENTS = frozenset(
    (
        "audio",
        "canvas",
        "datalist",
        "details",
        "dialog",
        "embed",
        "iframe",
        "math",
        "meter",
        "object",
        "optgroup",
        "option",
        "progress",
        "select",
        "svg",
        "textarea",
        "video",
    )
)
_HTML_VOID_ELEMENTS = frozenset(
    (
        "area",
        "base",
        "br",
        "col",
        "hr",
        "img",
        "input",
        "link",
        "meta",
        "param",
        "source",
        "track",
        "wbr",
    )
)
_HTML_AUTHORITY_STRUCTURE_ELEMENTS = (
    _HTML_NONVISIBLE_ELEMENTS
    | _HTML_NONAUTHORITY_SUBTREE_ELEMENTS
    | frozenset(("a", "base", "body", "div", "table", "tr"))
)


def _normalize_html_text(parts: Iterable[str]) -> str:
    return re.sub(r"\s+", " ", " ".join(parts)).strip()


_HTML_SPACE = " \t\r\n\f"


def _strict_raw_starttag_attributes(
    starttag_text: str | None, expected_tag: str
) -> list[tuple[str, str | None]] | None:
    """Parse raw HTML attribute lexemes without decoding character references."""

    if not isinstance(starttag_text, str) or not starttag_text.startswith("<"):
        return None
    length = len(starttag_text)
    index = 1
    tag_start = index
    while (
        index < length
        and starttag_text[index] not in _HTML_SPACE
        and starttag_text[index] not in "/>"
    ):
        index += 1
    if (
        starttag_text[tag_start:index].casefold() != expected_tag.casefold()
        or index == tag_start
    ):
        return None
    attributes: list[tuple[str, str | None]] = []
    while index < length:
        while index < length and starttag_text[index] in _HTML_SPACE:
            index += 1
        if index >= length:
            return None
        if starttag_text[index] == ">":
            return attributes if index + 1 == length else None
        if starttag_text.startswith("/>", index):
            return attributes if index + 2 == length else None
        name_start = index
        while (
            index < length
            and starttag_text[index] not in _HTML_SPACE
            and starttag_text[index] not in "/=>"
        ):
            if starttag_text[index] in "\"'<=`" or ord(starttag_text[index]) < 0x20:
                return None
            index += 1
        if index == name_start:
            return None
        name = starttag_text[name_start:index].casefold()
        while index < length and starttag_text[index] in _HTML_SPACE:
            index += 1
        value: str | None = None
        if index < length and starttag_text[index] == "=":
            index += 1
            while index < length and starttag_text[index] in _HTML_SPACE:
                index += 1
            if index >= length:
                return None
            if starttag_text[index] in "\"'":
                quote = starttag_text[index]
                index += 1
                value_start = index
                while index < length and starttag_text[index] != quote:
                    index += 1
                if index >= length:
                    return None
                value = starttag_text[value_start:index]
                index += 1
            else:
                value_start = index
                while (
                    index < length
                    and starttag_text[index] not in _HTML_SPACE
                    and starttag_text[index] != ">"
                ):
                    if starttag_text[index] in "\"'<=`":
                        return None
                    index += 1
                if index == value_start:
                    return None
                value = starttag_text[value_start:index]
        attributes.append((name, value))
    return None


def _normalized_visible_anchor_label(parts: Iterable[str]) -> str | None:
    raw = " ".join(parts)
    if any(
        not character.isprintable() and character not in "\t\r\n\f"
        for character in raw
    ):
        return None
    normalized = _normalize_html_text((raw,))
    return normalized or None


class _StructuralVisibleHtmlParser(html.parser.HTMLParser):
    """Extract visible BODY text and rows under conservative HTML visibility."""

    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.visible_text_parts: list[str] = []
        self.table_rows: list[dict[str, Any]] = []
        self.body_count = 0
        self.structural_errors: list[str] = []
        self._anchor_stack: list[dict[str, Any]] = []
        self._element_stack: list[dict[str, Any]] = []
        self._row_stack: list[dict[str, Any]] = []
        self._visible_table_depth = 0

    def _record_structural_error(self, code: str) -> None:
        if code not in self.structural_errors:
            self.structural_errors.append(code)

    def _open_element(
        self,
        tag: str,
        attrs: list[tuple[str, str | None]],
        *,
        push: bool,
        starttag_text: str | None,
    ) -> None:
        if tag == "base":
            self._record_structural_error("FORBIDDEN_BASE_ELEMENT")
        attribute_names = [name.casefold() for name, _value in attrs]
        duplicated_authority_attributes = {
            name
            for name in ("hidden", "href", "style")
            if attribute_names.count(name) > 1
        }
        if duplicated_authority_attributes:
            self._record_structural_error("DUPLICATE_AUTHORITY_ATTRIBUTE")
        style_values = [
            value for name, value in attrs if name.casefold() == "style"
        ]
        style_hidden = len(style_values) > 1 or (
            len(style_values) == 1 and style_values[0] != ""
        )
        own_hidden = (
            tag in _HTML_NONVISIBLE_ELEMENTS
            or tag in _HTML_NONAUTHORITY_SUBTREE_ELEMENTS
            or "hidden" in attribute_names
            or style_hidden
        )
        parent = self._element_stack[-1] if self._element_stack else None
        inherited_hidden = bool(parent and parent["aggregate_hidden"])
        if tag == "body":
            self.body_count += 1
            if self.body_count != 1:
                self._record_structural_error("MULTIPLE_BODY_ELEMENTS")
            if any(frame["tag"] == "body" for frame in self._element_stack):
                self._record_structural_error("NESTED_BODY_ELEMENT")
            if parent is not None and parent["tag"] != "html":
                self._record_structural_error(
                    "BODY_NESTED_IN_NON_DOCUMENT_ELEMENT"
                )
        if tag == "tr":
            if any(frame["tag"] == "tr" for frame in self._element_stack):
                self._record_structural_error("NESTED_TABLE_ROW")
            if not any(frame["tag"] == "table" for frame in self._element_stack):
                self._record_structural_error("TABLE_ROW_OUTSIDE_TABLE")
        inside_body = tag == "body" or bool(parent and parent["inside_body"])
        aggregate_hidden = inherited_hidden or own_hidden
        visible = inside_body and not aggregate_hidden
        frame: dict[str, Any] = {
            "aggregate_hidden": aggregate_hidden,
            "critical": (
                tag in _HTML_AUTHORITY_STRUCTURE_ELEMENTS
                or "hidden" in attribute_names
                or bool(style_values)
            ),
            "inside_body": inside_body,
            "anchor_started": False,
            "row_started": False,
            "table_started": False,
            "tag": tag,
            "visible": visible,
        }
        if visible and tag == "table":
            self._visible_table_depth += 1
            frame["table_started"] = True
        elif visible and tag == "tr" and self._visible_table_depth > 0:
            if self._row_stack:
                self._record_structural_error("NESTED_TABLE_ROW")
            row = {"anchors": [], "text_parts": []}
            self._row_stack.append(row)
            frame["row"] = row
            frame["row_started"] = True
        elif visible and tag == "a" and self._row_stack:
            parsed_href_values = [
                value for name, value in attrs if name.casefold() == "href"
            ]
            raw_attributes = _strict_raw_starttag_attributes(starttag_text, tag)
            raw_href_values = (
                [value for name, value in raw_attributes if name == "href"]
                if raw_attributes is not None
                else []
            )
            if not parsed_href_values and not raw_href_values:
                pass
            elif (
                raw_attributes is None
                or len(parsed_href_values) != 1
                or len(raw_href_values) != 1
                or not isinstance(parsed_href_values[0], str)
                or not parsed_href_values[0]
                or not isinstance(raw_href_values[0], str)
                or not raw_href_values[0]
            ):
                self._record_structural_error("MALFORMED_ACTIVE_ANCHOR")
            else:
                if self._anchor_stack:
                    self._record_structural_error("NESTED_ACTIVE_ANCHOR")
                anchor = {
                    "parsed_href": parsed_href_values[0],
                    "raw_href": raw_href_values[0],
                    "text_parts": [],
                }
                self._anchor_stack.append(anchor)
                frame["anchor"] = anchor
                frame["anchor_started"] = True
        if push:
            self._element_stack.append(frame)
        else:
            self._finish_frame(frame)

    def _finish_frame(self, frame: dict[str, Any]) -> None:
        if frame["anchor_started"]:
            anchor = frame["anchor"]
            if not self._anchor_stack or self._anchor_stack[-1] is not anchor:
                self._record_structural_error("ANCHOR_STACK_CORRUPTION")
            else:
                self._anchor_stack.pop()
                label = _normalized_visible_anchor_label(anchor["text_parts"])
                if label is None:
                    self._record_structural_error(
                        "NON_SUBSTANTIVE_VISIBLE_ANCHOR_LABEL"
                    )
                else:
                    for row in self._row_stack:
                        row["anchors"].append(
                            {
                                "parsed_href": anchor["parsed_href"],
                                "raw_href": anchor["raw_href"],
                                "visible_label": label,
                            }
                        )
        if frame["row_started"]:
            row = frame["row"]
            if not self._row_stack or self._row_stack[-1] is not row:
                self._record_structural_error("ROW_STACK_CORRUPTION")
            else:
                self._row_stack.pop()
                self.table_rows.append(
                    {
                        "anchors": row["anchors"],
                        "visible_text": _normalize_html_text(row["text_parts"]),
                    }
                )
        if frame["table_started"]:
            if self._visible_table_depth <= 0:
                self._record_structural_error("TABLE_STACK_CORRUPTION")
            else:
                self._visible_table_depth -= 1

    def handle_starttag(
        self, tag: str, attrs: list[tuple[str, str | None]]
    ) -> None:
        tag = tag.lower()
        self._open_element(
            tag,
            attrs,
            push=tag not in _HTML_VOID_ELEMENTS,
            starttag_text=self.get_starttag_text(),
        )

    def handle_startendtag(
        self, tag: str, attrs: list[tuple[str, str | None]]
    ) -> None:
        tag = tag.lower()
        # A slash does not make a non-void HTML element self-closing.  This is
        # especially authority-critical for script/style/template/noscript.
        self._open_element(
            tag,
            attrs,
            push=tag not in _HTML_VOID_ELEMENTS,
            starttag_text=self.get_starttag_text(),
        )

    def handle_endtag(self, tag: str) -> None:
        tag = tag.lower()
        matching_index = next(
            (
                index
                for index in range(len(self._element_stack) - 1, -1, -1)
                if self._element_stack[index]["tag"] == tag
            ),
            None,
        )
        if matching_index is None:
            if tag in _HTML_AUTHORITY_STRUCTURE_ELEMENTS:
                self._record_structural_error(
                    f"UNMATCHED_END_TAG_{tag.upper()}"
                )
            return
        intervening = self._element_stack[matching_index + 1 :]
        if tag == "table" and (
            self._row_stack
            or any(frame["tag"] == "tr" for frame in intervening)
        ):
            self._record_structural_error("TABLE_CLOSED_WITH_ACTIVE_ROW")
        if tag == "body" and any(
            frame["tag"] in {"table", "tr"} for frame in intervening
        ):
            self._record_structural_error(
                "BODY_CLOSED_WITH_ACTIVE_TABLE_OR_ROW"
            )
        if any(frame["critical"] for frame in intervening):
            self._record_structural_error(
                f"MISNESTED_END_TAG_{tag.upper()}"
            )
        while len(self._element_stack) > matching_index:
            self._finish_frame(self._element_stack.pop())

    def handle_data(self, data: str) -> None:
        if (
            not data
            or not self._element_stack
            or not self._element_stack[-1]["visible"]
        ):
            return
        self.visible_text_parts.append(data)
        for anchor in self._anchor_stack:
            anchor["text_parts"].append(data)
        for row in self._row_stack:
            row["text_parts"].append(data)


def structural_visible_html_report(payload: bytes) -> dict[str, Any]:
    try:
        decoded = payload.decode("utf-8", errors="strict")
        parser = _StructuralVisibleHtmlParser()
        parser.feed(decoded)
        parser.close()
    except (UnicodeError, ValueError) as error:
        raise GateError("HTML authority payload is not valid structural UTF-8 HTML") from error
    remaining_critical = [
        frame for frame in parser._element_stack if frame["critical"]
    ]
    if parser.body_count == 0:
        parser._record_structural_error("MISSING_BODY_ELEMENT")
    elif parser.body_count != 1:
        parser._record_structural_error("MULTIPLE_BODY_ELEMENTS")
    for frame in remaining_critical:
        parser._record_structural_error(
            f"UNCLOSED_CRITICAL_{frame['tag'].upper()}"
        )
    if parser._anchor_stack:
        parser._record_structural_error("ANCHOR_STACK_NOT_EMPTY")
    if parser._row_stack:
        parser._record_structural_error("ROW_STACK_NOT_EMPTY")
    if parser._visible_table_depth != 0:
        parser._record_structural_error("TABLE_STACK_NOT_EMPTY")
    return {
        "structural_errors": list(parser.structural_errors),
        "table_rows": parser.table_rows,
        "visible_text": _normalize_html_text(parser.visible_text_parts),
    }


def extract_structural_visible_html(payload: bytes) -> dict[str, Any]:
    report = structural_visible_html_report(payload)
    if report["structural_errors"]:
        raise GateError(
            "HTML authority payload structural errors: "
            + ", ".join(report["structural_errors"])
        )
    return report


def html_visible_text(payload: bytes) -> str:
    return extract_structural_visible_html(payload)["visible_text"]


def validate_naip_service_metadata(payload: bytes, expected_year: int) -> dict[str, Any]:
    value = decode_json(payload, "USDA NAIP GeoPlatform service metadata")
    if value.get("name") != "NAIP/NAIP_plus":
        raise GateError("USDA NAIP service metadata has an unexpected service name")
    description = html.unescape(str(value.get("description") or ""))
    required_description = (
        "National Agriculture Imagery Program (NAIP)",
        "provided by the USDA's Farm Production and Conservation office (FPAC)",
        f"CO and UT were acquired in {expected_year}",
    )
    if any(token.lower() not in description.lower() for token in required_description):
        raise GateError("USDA NAIP service metadata lacks current Colorado/USDA authority text")
    copyright_text = html.unescape(str(value.get("copyrightText") or ""))
    if not all(
        token.lower() in copyright_text.lower()
        for token in (
            "USDA's Farm Production and Conservation office (FPAC)",
            "U.S. Forest Service",
            "Geospatial Platform (GeoPlatform)",
        )
    ):
        raise GateError("USDA NAIP service metadata lacks official provider/serving attribution")
    capabilities = {token.strip() for token in str(value.get("capabilities") or "").split(",")}
    if not {"Image", "Metadata", "Catalog"}.issubset(capabilities):
        raise GateError("USDA NAIP service metadata lacks the required public catalog capabilities")
    fields = {field.get("name"): field for field in value.get("fields") or []}
    expected_types = {
        "objectid": "esriFieldTypeOID",
        "name": "esriFieldTypeString",
        "category": "esriFieldTypeInteger",
        "year": "esriFieldTypeSmallInteger",
        "state": "esriFieldTypeString",
    }
    if any((fields.get(name) or {}).get("type") != field_type for name, field_type in expected_types.items()):
        raise GateError("USDA NAIP service metadata field schema is incomplete or changed")
    category_values = {
        coded.get("code"): coded.get("name")
        for coded in (((fields["category"].get("domain") or {}).get("codedValues")) or [])
    }
    if category_values.get(1) != "Primary" or category_values.get(2) != "Overview":
        raise GateError("USDA NAIP service category semantics are not explicit")
    advanced = value.get("advancedQueryCapabilities") or {}
    if not all(
        advanced.get(key) is True
        for key in ("supportsOrderBy", "supportsPagination", "supportsSqlExpression")
    ):
        raise GateError("USDA NAIP service metadata lacks required query semantics")
    current_version = value.get("currentVersion")
    if (
        isinstance(current_version, bool)
        or not isinstance(current_version, (int, float))
        or not math.isfinite(float(current_version))
        or float(current_version) < 12.0
    ):
        raise GateError("USDA NAIP service metadata is older than the bound authority contract")
    if value.get("objectIdField") != "objectid" or int(value.get("maxRecordCount") or 0) < 1000:
        raise GateError("USDA NAIP service catalog identity/count capability changed")
    return {
        "current_version": float(current_version),
        "object_id_field": "objectid",
        "provider": "USDA_FPAC_SERVED_BY_USFS_GEOPLATFORM",
        "service_name": value["name"],
    }


def validate_naip_quarter_quad_archive(
    payload: bytes, contract: dict[str, Any]
) -> dict[str, Any]:
    """Bind the exact official USDA Colorado 2021 coverage archive.

    The archive is metadata only: it contains the statewide quarter-quad
    shapefile, not orthophoto pixels.  Its exact bytes were independently
    parsed against the project envelope before this contract was frozen.
    """

    if len(payload) != contract.get("archive_bytes"):
        raise GateError("USDA NAIP Colorado archive byte count changed")
    archive_sha256 = sha256_bytes(payload)
    if archive_sha256 != contract.get("archive_sha256"):
        raise GateError("USDA NAIP Colorado archive SHA-256 changed")
    if not payload.startswith(b"PK\x03\x04"):
        raise GateError("USDA NAIP Colorado archive lacks a ZIP signature")
    selected_records = contract.get("expected_selected_record_numbers")
    if selected_records != list(NAIP_EXPECTED_SELECTED_RECORDS):
        raise GateError("USDA NAIP selected quarter-quad record inventory changed")
    selected_sha256 = sha256_bytes(canonical_json_bytes(selected_records))
    if selected_sha256 != contract.get("selected_record_inventory_sha256"):
        raise GateError("USDA NAIP selected record inventory hash changed")
    return {
        "catalog_inventory": {
            "ordered_record_numbers": list(selected_records),
            "selected_record_count": len(selected_records),
            "selected_record_inventory_sha256": selected_sha256,
            "statewide_footprint_count": contract["expected_statewide_footprint_count"],
        },
        "coverage": {
            "accepted_primary_footprint_count": len(selected_records),
            "archive_sha256": archive_sha256,
            "coverage_bbox_wgs84_xyxy": contract["coverage_bbox_wgs84_xyxy"],
            "coverage_gap_area_wgs84_degrees_squared": 0.0,
            "coverage_union_bounds_wgs84_xyxy": contract[
                "coverage_union_bounds_wgs84_xyxy"
            ],
            "selected_record_inventory_sha256": selected_sha256,
            "state": contract["accepted_state"],
            "statewide_footprint_count": contract["expected_statewide_footprint_count"],
            "year": contract["accepted_year"],
        },
        "service": {
            "archive_bytes": len(payload),
            "archive_sha256": archive_sha256,
            "provider": "USDA_FPAC_OFFICIAL_QUARTER_QUAD_COVERAGE_ARCHIVE",
            "service_name": "co_naip21qq-1.zip",
        },
    }


def _strict_visible_href_basename(href: str) -> str:
    if (
        type(href) is not str
        or not href
        or href != href.strip()
        or any(ord(character) < 0x20 or ord(character) == 0x7F for character in href)
    ):
        raise GateError("USDA NAIP Colorado row has a malformed visible anchor")
    try:
        parts = urllib.parse.urlsplit(href)
        if re.search(r"%(?![0-9a-fA-F]{2})", parts.path):
            raise ValueError("malformed percent escape")
        decoded_path = urllib.parse.unquote(parts.path, errors="strict")
    except (UnicodeError, ValueError) as error:
        raise GateError(
            "USDA NAIP Colorado row has a malformed visible anchor"
        ) from error
    return decoded_path.rsplit("/", 1)[-1]


def validate_naip_latest_coverage_page(
    payload: bytes,
    expected_year: int,
    expected_raw_href: str = NAIP_EXPECTED_RAW_HREF,
    expected_resolved_href: str = NAIP_EXPECTED_RESOLVED_HREF,
    expected_anchor_label: str = NAIP_EXPECTED_ANCHOR_LABEL,
) -> dict[str, Any]:
    if (
        expected_raw_href != NAIP_EXPECTED_RAW_HREF
        or expected_resolved_href != NAIP_EXPECTED_RESOLVED_HREF
        or expected_anchor_label != NAIP_EXPECTED_ANCHOR_LABEL
    ):
        raise GateError("USDA NAIP Colorado anchor contract is not exact")
    extracted = extract_structural_visible_html(payload)
    visible = extracted["visible_text"]
    if "most recent year available" not in visible.lower():
        raise GateError("USDA NAIP coverage page does not identify its inventory as most recent")
    if "NAIP CONUS 2021-2022" not in visible:
        raise GateError("USDA NAIP coverage page no longer binds the 2021-2022 inventory")
    colorado_rows = [
        row
        for row in extracted["table_rows"]
        if re.search(r"(?i)(?<!\w)Colorado(?!\w)", row["visible_text"])
    ]
    expected_quarter_quad = f"co_naip{str(expected_year)[-2:]}qq-1.zip"
    if len(colorado_rows) != 1:
        raise GateError("USDA NAIP most-recent coverage page has no unique Colorado row")
    anchors = colorado_rows[0]["anchors"]
    quarter_quad_anchors = [
        anchor
        for anchor in anchors
        if re.fullmatch(
            r"co_naip\d{2}qq(?:-\d+)?\.zip",
            _strict_visible_href_basename(anchor["parsed_href"]),
            flags=re.IGNORECASE,
        )
    ]
    if len(quarter_quad_anchors) != 1:
        raise GateError("USDA NAIP most-recent Colorado row is missing the 2021 quarter-quad inventory")
    anchor = quarter_quad_anchors[0]
    parsed_href = anchor["parsed_href"]
    raw_href = anchor["raw_href"]
    visible_label = anchor["visible_label"]
    resolved_href = urllib.parse.urljoin(NAIP_COVERAGE_PAGE_URL, parsed_href)
    expected_parts = urllib.parse.urlsplit(expected_resolved_href)
    resolved_parts = urllib.parse.urlsplit(resolved_href)
    if (
        set(anchor) != {"parsed_href", "raw_href", "visible_label"}
        or expected_parts.scheme != "https"
        or expected_parts.netloc != "www.fpacbc.usda.gov"
        or expected_parts.path
        != "/sites/default/files/2024-10/co_naip21qq-1.zip"
        or expected_parts.query
        or expected_parts.fragment
        or raw_href != expected_raw_href
        or parsed_href != raw_href
        or visible_label != expected_anchor_label
        or resolved_parts != expected_parts
        or resolved_href != expected_resolved_href
        or resolved_parts.path.rsplit("/", 1)[-1] != expected_quarter_quad
    ):
        raise GateError(
            "USDA NAIP Colorado quarter-quad anchor raw href, resolved HTTPS "
            "authority, or visible label is not exact"
        )
    return {
        "colorado_quarter_quad_anchor_label": expected_anchor_label,
        "colorado_quarter_quad_inventory": expected_quarter_quad,
        "colorado_quarter_quad_raw_href": expected_raw_href,
        "colorado_quarter_quad_resolved_href": expected_resolved_href,
        "coverage_inventory_label": "NAIP CONUS 2021-2022",
        "most_recent_year": expected_year,
        "structural_errors": extracted["structural_errors"],
    }


def validate_naip_bounded_ids(
    payload: bytes, contract: dict[str, Any]
) -> dict[str, Any]:
    """Validate the independent, transfer-limit-free bounded catalog inventory."""

    value = decode_json(payload, "USDA NAIP bounded object-ID query")
    if not isinstance(value, dict) or set(value) != {"objectIdFieldName", "objectIds"}:
        raise GateError("USDA NAIP bounded object-ID response schema changed")
    if value.get("objectIdFieldName") != "objectid":
        raise GateError("USDA NAIP bounded object-ID response changed catalog identity")
    object_ids = value.get("objectIds")
    expected_ids = contract.get("expected_object_ids")
    if (
        not isinstance(object_ids, list)
        or any(type(object_id) is not int for object_id in object_ids)
        or object_ids != expected_ids
    ):
        raise GateError("USDA NAIP bounded object-ID inventory/order is not exact")
    return {
        "object_id_count": len(object_ids),
        "object_id_inventory_sha256": sha256_bytes(canonical_json_bytes(object_ids)),
        "ordered_object_ids": list(object_ids),
    }


def naip_geometry_inventory_sha256(features: list[dict[str, Any]]) -> str:
    inventory = [
        {
            "geometry": feature.get("geometry"),
            "objectid": (feature.get("attributes") or {}).get("objectid"),
        }
        for feature in features
    ]
    return sha256_bytes((compact_json(inventory) + "\n").encode("utf-8"))


def validate_naip_bounded_footprints(
    payload: bytes,
    contract: dict[str, Any],
    bounded_ids: list[int] | None = None,
) -> dict[str, Any]:
    value = decode_json(payload, "USDA NAIP bounded footprint query")
    expected_top_level = {
        "features",
        "fields",
        "geometryType",
        "objectIdFieldName",
        "spatialReference",
    }
    if not isinstance(value, dict) or set(value) != expected_top_level:
        raise GateError("USDA NAIP bounded footprint top-level response schema changed")
    if value.get("objectIdFieldName") != "objectid":
        raise GateError("USDA NAIP bounded footprint response changed object-ID authority")
    response_fields = [
        (field.get("name"), field.get("type"))
        if isinstance(field, dict)
        else (None, None)
        for field in value.get("fields") or []
    ]
    if response_fields != [
        ("objectid", "esriFieldTypeOID"),
        ("name", "esriFieldTypeString"),
        ("category", "esriFieldTypeInteger"),
        ("year", "esriFieldTypeSmallInteger"),
        ("state", "esriFieldTypeString"),
        ("shape", "esriFieldTypeGeometry"),
    ]:
        raise GateError("USDA NAIP bounded footprint response field schema changed")
    if value.get("geometryType") != "esriGeometryPolygon":
        raise GateError("USDA NAIP bounded footprint response is not polygonal")
    if value.get("spatialReference") != {"latestWkid": 4326, "wkid": 4326}:
        raise GateError("USDA NAIP bounded footprint response is not exact WGS84")
    features = value.get("features")
    if not isinstance(features, list) or not features:
        raise GateError("USDA NAIP bounded footprint response has no features")
    if any(
        not isinstance(feature, dict)
        or set(feature) != {"attributes", "geometry"}
        or not isinstance(feature.get("attributes"), dict)
        for feature in features
    ):
        raise GateError("USDA NAIP bounded footprint feature schema changed")
    expected_ids = contract.get("expected_object_ids")
    if bounded_ids is None:
        bounded_ids = expected_ids
    if bounded_ids != expected_ids:
        raise GateError("USDA NAIP bounded ID receipt differs from the authority contract")
    feature_ids = [
        feature["attributes"].get("objectid")
        for feature in features
    ]
    if feature_ids != bounded_ids:
        raise GateError("USDA NAIP footprint features differ from the bounded ID inventory/order")
    if len(features) != contract.get("expected_query_feature_count"):
        raise GateError("USDA NAIP bounded footprint feature count is not exact")
    geometry_inventory_sha256 = naip_geometry_inventory_sha256(features)
    if geometry_inventory_sha256 != contract.get("geometry_inventory_sha256"):
        raise GateError("USDA NAIP per-object geometry inventory changed")

    expected_state = contract["accepted_state"]
    expected_year = contract["accepted_year"]
    if type(expected_year) is not int:
        raise GateError("USDA NAIP accepted year contract is not an integer")
    object_ids: set[int] = set()
    accepted_ids: list[int] = []
    disposition_rows: list[dict[str, Any]] = []
    primary_polygons: list[Polygon] = []
    disposition_counts: Counter[str] = Counter()
    for feature in features:
        attributes = feature.get("attributes")
        if not isinstance(attributes, dict) or set(attributes) != {
            "category",
            "name",
            "objectid",
            "state",
            "year",
        }:
            raise GateError("USDA NAIP bounded footprint attributes are incomplete or expanded")
        object_id = attributes["objectid"]
        category = attributes["category"]
        year = attributes["year"]
        state_value = attributes["state"]
        name = attributes["name"]
        if type(object_id) is not int or object_id in object_ids:
            raise GateError("USDA NAIP bounded footprint object IDs are invalid or duplicated")
        if type(category) is not int or type(year) is not int:
            raise GateError("USDA NAIP bounded footprint category/year types are not exact integers")
        if type(state_value) is not str or state_value != expected_state:
            raise GateError("USDA NAIP bounded footprint state string is not exact")
        if type(name) is not str or not name:
            raise GateError("USDA NAIP bounded footprint name string is not exact")
        object_ids.add(object_id)
        if year != expected_year:
            raise GateError("USDA NAIP bounded footprint response is missing current Colorado-year identity")

        geometry = feature.get("geometry")
        if not isinstance(geometry, dict) or set(geometry) != {"rings", "spatialReference"}:
            raise GateError("USDA NAIP footprint geometry schema changed")
        if geometry.get("spatialReference") != {"latestWkid": 4326, "wkid": 4326}:
            raise GateError("USDA NAIP footprint geometry is not exact WGS84")
        rings = geometry.get("rings")
        if not isinstance(rings, list) or not rings:
            raise GateError("USDA NAIP footprint geometry has no rings")
        for ring in rings:
            if not isinstance(ring, list) or len(ring) < 4 or ring[0] != ring[-1]:
                raise GateError("USDA NAIP footprint has an open or short ring")
            for point in ring:
                if not isinstance(point, list) or len(point) != 2:
                    raise GateError("USDA NAIP footprint has a malformed coordinate")
                longitude, latitude = point
                if (
                    isinstance(longitude, bool)
                    or isinstance(latitude, bool)
                    or not isinstance(longitude, (int, float))
                    or not isinstance(latitude, (int, float))
                    or not math.isfinite(float(longitude))
                    or not math.isfinite(float(latitude))
                    or not -180.0 <= float(longitude) <= 180.0
                    or not -90.0 <= float(latitude) <= 90.0
                ):
                    raise GateError("USDA NAIP footprint has a non-WGS84 coordinate")
        try:
            polygon = shape({"coordinates": rings, "type": "Polygon"})
        except (TypeError, ValueError) as error:
            raise GateError("USDA NAIP footprint geometry is malformed") from error
        if (
            not isinstance(polygon, Polygon)
            or polygon.is_empty
            or not polygon.is_valid
            or polygon.area <= 0.0
        ):
            raise GateError("USDA NAIP footprint geometry is invalid")

        if category == 1:
            disposition = "ACCEPTED_CURRENT_PRIMARY_FOOTPRINT"
            primary_polygons.append(polygon)
            accepted_ids.append(object_id)
        elif (
            category == 2
            and object_id == contract.get("expected_overview_id")
            and name == contract.get("expected_overview_name")
        ):
            disposition = "EXCLUDED_SERVICE_OVERVIEW"
        else:
            raise GateError("USDA NAIP bounded footprint response has an unadjudicated category")
        disposition_counts[disposition] += 1
        disposition_rows.append(
            {
                "category": category,
                "disposition": disposition,
                "name": name,
                "objectid": object_id,
                "state": state_value,
                "year": year,
            }
        )
    if len(primary_polygons) != contract.get("expected_primary_footprint_count"):
        raise GateError("USDA NAIP bounded footprint primary count is not exact")
    if disposition_counts != Counter(
        {
            "ACCEPTED_CURRENT_PRIMARY_FOOTPRINT": 25,
            "EXCLUDED_SERVICE_OVERVIEW": 1,
        }
    ):
        raise GateError("USDA NAIP bounded footprint disposition counts are not exact")
    primary_inventory_sha256 = sha256_bytes(canonical_json_bytes(accepted_ids))
    if primary_inventory_sha256 != contract.get("primary_object_id_inventory_sha256"):
        raise GateError("USDA NAIP primary object-ID inventory changed")
    disposition_inventory_sha256 = sha256_bytes(canonical_json_bytes(disposition_rows))
    if disposition_inventory_sha256 != contract.get("disposition_inventory_sha256"):
        raise GateError("USDA NAIP footprint disposition inventory changed")

    coverage = unary_union(primary_polygons)
    bounds = contract["coverage_bbox_wgs84_xyxy"]
    target = box(*map(float, bounds))
    gap = target.difference(coverage)
    if not gap.is_empty or not coverage.covers(target):
        raise GateError("USDA NAIP current primary footprints do not cover the complete KBDU bounds")
    union_bounds = [float(coordinate) for coordinate in coverage.bounds]
    if union_bounds != contract.get("coverage_union_bounds_wgs84_xyxy"):
        raise GateError("USDA NAIP primary-footprint union bounds changed")
    return {
        "accepted_primary_footprint_count": len(accepted_ids),
        "accepted_primary_object_id_inventory_sha256": primary_inventory_sha256,
        "coverage_bbox_wgs84_xyxy": bounds,
        "coverage_gap_area_wgs84_degrees_squared": float(gap.area),
        "coverage_union_bounds_wgs84_xyxy": union_bounds,
        "disposition_counts": dict(sorted(disposition_counts.items())),
        "disposition_inventory_sha256": disposition_inventory_sha256,
        "geometry_inventory_sha256": geometry_inventory_sha256,
        "query_feature_count": len(features),
        "state": expected_state,
        "year": expected_year,
    }


def validate_naip_authority_payloads(
    registry: dict[str, Any],
    source: dict[str, Any],
    frozen_source: dict[str, Any],
    external_run: Path,
    license_snapshots: dict[str, Any],
) -> dict[str, Any]:
    contract = source["orthophoto_authority_contract"]
    required_source_keys = {
        "documents",
        "endpoint",
        "feature_count",
        "id",
        "license_partition",
        "metadata_only",
        "required",
        "role",
        "source_kind",
        "status",
    }
    allowed_source_keys = required_source_keys | {
        "authority_validation",
        "receipt_file",
    }
    if (
        not isinstance(frozen_source, dict)
        or not required_source_keys.issubset(frozen_source)
        or not set(frozen_source).issubset(allowed_source_keys)
        or frozen_source.get("id") != NAIP_AUTHORITY_SOURCE_ID
        or frozen_source.get("license_partition") != source["license_partition"]
        or frozen_source.get("metadata_only") is not True
        or frozen_source.get("required") is not True
        or frozen_source.get("role") != source["role"]
        or frozen_source.get("source_kind") != "document_authority"
        or frozen_source.get("status") != "FROZEN_METADATA"
        or frozen_source.get("feature_count") is not None
        or frozen_source.get("endpoint")
        != encoded_request_url(
            source["documents"][0]["url"], source["documents"][0].get("params")
        )
    ):
        raise GateError("Frozen USDA NAIP source identity/role is not exact")
    configured_ids = [document["id"] for document in source["documents"]]
    frozen_document_values = frozen_source.get("documents")
    if (
        not isinstance(frozen_document_values, list)
        or [
            document.get("id") if isinstance(document, dict) else None
            for document in frozen_document_values
        ]
        != configured_ids
    ):
        raise GateError("Frozen USDA NAIP ordered document inventory is not exact")
    configured_documents = {
        document["id"]: document for document in source["documents"]
    }
    frozen_documents = {
        document.get("id"): document for document in frozen_document_values
    }
    if len(frozen_documents) != len(frozen_source.get("documents") or []) or set(
        frozen_documents
    ) != set(configured_documents):
        raise GateError("Frozen USDA NAIP document inventory is incomplete or duplicated")
    payloads: dict[str, bytes] = {}
    evidence_sha256: dict[str, str] = {}
    for index, document in enumerate(source["documents"]):
        document_id = document["id"]
        frozen_document = frozen_documents[document_id]
        expected_url = encoded_request_url(document["url"], document.get("params"))
        if (
            set(frozen_document) != {"id", "media_type", "receipt", "url"}
            or frozen_document.get("media_type") != document["media_type"]
            or frozen_document.get("url") != expected_url
        ):
            raise GateError(f"Frozen USDA NAIP document contract changed: {document_id}")
        payload = frozen_response_payload(
            external_run,
            frozen_document.get("receipt") or {},
            expected_request_path=(
                f"requests/{NAIP_AUTHORITY_SOURCE_ID}/"
                f"document_{index:03d}_{document_id}.json"
            ),
            expected_response_path=(
                f"metadata/{NAIP_AUTHORITY_SOURCE_ID}/{document['path']}"
            ),
            expected_timeout_seconds=int(document.get("timeout_seconds", 180)),
            expected_url=expected_url,
            label=f"USDA NAIP {document_id}",
        )
        validate_document_payload(document, payload)
        payloads[document_id] = payload
        evidence_sha256[document_id] = sha256_bytes(payload)

    license_record = license_snapshots.get(contract["license_snapshot_id"])
    if (
        not isinstance(license_record, dict)
        or set(license_record) != {"receipt", "required_text_present"}
        or license_record.get("required_text_present") is not True
    ):
        raise GateError("Frozen USDA NAIP public-domain license snapshot is missing")
    license_payload = frozen_response_payload(
        external_run,
        license_record.get("receipt") or {},
        expected_request_path=f"requests/licenses/{NAIP_LICENSE_SNAPSHOT_ID}.json",
        expected_response_path=f"metadata/licenses/{NAIP_LICENSE_SNAPSHOT_ID}.html",
        expected_timeout_seconds=90,
        expected_url=NAIP_LICENSE_URL,
        label="USDA NAIP public-domain license",
    )
    license_visible = html_visible_text(license_payload)
    license_statement = (
        "Aerial photography and digital orthophotography acquired by the USDA "
        "are in the public domain"
    )
    if license_statement.lower() not in license_visible.lower():
        raise GateError("Frozen USDA NAIP license payload lacks the exact public-domain statement")
    evidence_sha256[NAIP_LICENSE_SNAPSHOT_ID] = sha256_bytes(license_payload)

    archive_summary = validate_naip_quarter_quad_archive(
        payloads[contract["archive_document_id"]], contract
    )
    latest_summary = validate_naip_latest_coverage_page(
        payloads[contract["latest_coverage_document_id"]],
        int(contract["accepted_year"]),
        contract["expected_raw_href"],
        contract["expected_resolved_href"],
        contract["expected_anchor_label"],
    )
    return {
        "allowed_use": contract["allowed_use"],
        "catalog_inventory": archive_summary["catalog_inventory"],
        "coverage": archive_summary["coverage"],
        "evidence_sha256": dict(sorted(evidence_sha256.items())),
        "forbidden_uses": contract["forbidden_uses"],
        "latest_inventory": latest_summary,
        "license_partition": source["license_partition"],
        "schema": NAIP_AUTHORITY_SCHEMA,
        "service": archive_summary["service"],
        "status": "PASS_CURRENT_CLEAR_LICENSE_FULL_BOUNDS",
    }


def validate_frozen_naip_authority_closure(
    registry: dict[str, Any], external_run: Path, freeze: dict[str, Any]
) -> VerifiedNaipAuthority:
    configured = next(
        (
            source
            for source in registry.get("document_sources") or []
            if source.get("id") == NAIP_AUTHORITY_SOURCE_ID
        ),
        None,
    )
    frozen = next(
        (
            source
            for source in freeze.get("sources") or []
            if source.get("id") == NAIP_AUTHORITY_SOURCE_ID
        ),
        None,
    )
    if not isinstance(configured, dict) or not isinstance(frozen, dict):
        raise GateError("Frozen source package lacks the required USDA NAIP authority")
    expected = validate_naip_authority_payloads(
        registry,
        configured,
        frozen,
        external_run,
        freeze.get("license_snapshots") or {},
    )
    if frozen.get("authority_validation") != expected:
        raise GateError("Frozen USDA NAIP authority summary differs from its exact payloads")
    receipt_file = frozen.get("receipt_file")
    expected_receipt_path = f"receipts/{NAIP_AUTHORITY_SOURCE_ID}.json"
    if not isinstance(receipt_file, dict) or receipt_file.get("path") != expected_receipt_path:
        raise GateError("Frozen USDA NAIP authority lacks its exact source receipt binding")
    receipt_path = resolve_external_relative_path(
        external_run, expected_receipt_path, "USDA NAIP source receipt"
    )
    try:
        receipt_payload = receipt_path.read_bytes()
        receipt_value = json.loads(receipt_payload.decode("utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise GateError("Frozen USDA NAIP source receipt is not valid UTF-8 JSON") from error
    expected_receipt_value = dict(frozen)
    expected_receipt_value.pop("receipt_file")
    if (
        receipt_value != expected_receipt_value
        or receipt_payload != canonical_json_bytes(expected_receipt_value)
        or file_record(receipt_path, external_run) != receipt_file
    ):
        raise GateError("Frozen USDA NAIP source receipt differs from its exact source record")
    return VerifiedNaipAuthority(
        expected,
        configured["orthophoto_authority_contract"],
        _VERIFIED_NAIP_AUTHORITY_SEAL,
    )


def acquire_document_source(
    registry: dict[str, Any],
    source: dict[str, Any],
    external_run: Path,
    license_snapshots: dict[str, Any],
) -> dict[str, Any]:
    source_id = source["id"]
    for snapshot_id in source.get("license_snapshot_ids") or []:
        if snapshot_id not in license_snapshots:
            raise GateError(f"{source_id} required license snapshot {snapshot_id} is missing")
    documents: list[dict[str, Any]] = []
    for index, document in enumerate(source["documents"]):
        relative = Path(document["path"])
        if relative.is_absolute() or ".." in relative.parts:
            raise GateError(f"{source_id} has unsafe document path {document['path']!r}")
        output_path = external_run / "metadata" / source_id / relative
        payload, receipt = capture_request(
            external_run,
            request_id=f"{source_id}/document_{index:03d}_{document['id']}",
            url=document["url"],
            params=document.get("params"),
            output_path=output_path,
            timeout_seconds=int(document.get("timeout_seconds", 180)),
        )
        validate_document_payload(document, payload)
        documents.append(
            {
                "id": document["id"],
                "media_type": document["media_type"],
                "receipt": receipt,
                "url": receipt["url"],
            }
        )
    record = {
        "documents": documents,
        "endpoint": encoded_request_url(
            source["documents"][0]["url"], source["documents"][0].get("params")
        ),
        "feature_count": None,
        "id": source_id,
        "license_partition": source["license_partition"],
        "metadata_only": bool(source.get("metadata_only", True)),
        "required": bool(source["required"]),
        "role": source["role"],
        "source_kind": "document_authority",
        "status": "FROZEN_METADATA",
    }
    if source_id == NAIP_AUTHORITY_SOURCE_ID:
        record["authority_validation"] = validate_naip_authority_payloads(
            registry, source, record, external_run, license_snapshots
        )
    return record


def acquire_arcgis_source(
    registry: dict[str, Any], source: dict[str, Any], external_run: Path, license_snapshots: dict[str, Any]
) -> dict[str, Any]:
    source_id = source["id"]
    metadata_dir = external_run / "metadata" / source_id
    raw_dir = external_run / "raw" / source_id
    receipts: dict[str, Any] = {}
    item: dict[str, Any] = {}
    item_id = source.get("item_id")
    item_endpoint: str | None = arcgis_item_url(str(item_id)) if item_id else None

    if not item_id and source.get("discover_item_from_service"):
        root_url = service_root(source["layer_url"])
        payload, receipts["service_root"] = capture_request(
            external_run,
            request_id=f"{source_id}/service_root",
            url=root_url,
            params={"f": "pjson"},
            output_path=metadata_dir / "service_root.json",
        )
        root_meta = decode_json(payload, f"{source_id} service root")
        item_id = root_meta.get("serviceItemId")
        if not item_id:
            raise GateError(f"{source_id} service has no serviceItemId for license authority")
        info_url = arcgis_server_info_url(root_url)
        payload, receipts["server_info"] = capture_request(
            external_run,
            request_id=f"{source_id}/server_info",
            url=info_url,
            params={"f": "pjson"},
            output_path=metadata_dir / "server_info.json",
        )
        server_info = decode_json(payload, f"{source_id} ArcGIS Server info")
        owning_system_url = str(server_info.get("owningSystemUrl") or "").rstrip("/")
        if not owning_system_url.startswith("https://"):
            raise GateError(f"{source_id} server has no HTTPS owning portal")
        item_endpoint = f"{owning_system_url}/sharing/rest/content/items/{item_id}"

    if item_id:
        payload, receipts["item"] = capture_request(
            external_run,
            request_id=f"{source_id}/item",
            url=str(item_endpoint),
            params={"f": "pjson"},
            output_path=metadata_dir / "item.json",
        )
        item = decode_json(payload, f"{source_id} ArcGIS item")
        actual_item_id = str(item.get("id") or "")
        if actual_item_id.lower() != str(item_id).lower():
            raise GateError(f"{source_id} item ID mismatch: {actual_item_id} != {item_id}")

    required_license = source.get("license_required_any") or []
    if required_license:
        text = license_text(item)
        if not any(token.lower() in text.lower() for token in required_license):
            raise GateError(
                f"{source_id} item metadata lacks required license authority {required_license}; "
                f"licenseInfo={item.get('licenseInfo')!r}"
            )

    if source.get("catalog_url"):
        payload, receipts["catalog"] = capture_request(
            external_run,
            request_id=f"{source_id}/catalog",
            url=source["catalog_url"],
            output_path=metadata_dir / "catalog.html",
        )
        if not payload:
            raise GateError(f"{source_id} catalog snapshot is empty")
    for snapshot_id in source.get("license_snapshot_ids") or []:
        if snapshot_id not in license_snapshots:
            raise GateError(f"{source_id} required license snapshot {snapshot_id} is missing")

    if source.get("metadata_only"):
        service_url = str(item.get("url") or "").strip().rstrip("/")
        if service_url:
            payload, receipts["service"] = capture_request(
                external_run,
                request_id=f"{source_id}/service",
                url=service_url,
                params={"f": "pjson"},
                output_path=metadata_dir / "service.json",
            )
            decode_json(payload, f"{source_id} service")
        return {
            "feature_count": None,
            "id": source_id,
            "item_id": item_id,
            "license_partition": source["license_partition"],
            "metadata_only": True,
            "receipts": receipts,
            "required": bool(source["required"]),
            "role": source["role"],
            "status": "FROZEN",
        }

    layer_url = resolve_layer_url(item, source)
    payload, receipts["layer"] = capture_request(
        external_run,
        request_id=f"{source_id}/layer",
        url=layer_url,
        params={"f": "pjson"},
        output_path=metadata_dir / "layer.json",
    )
    metadata = decode_json(payload, f"{source_id} layer metadata")
    object_id_field = oid_field(metadata)
    query_url = layer_url + "/query"
    common = query_parameters(registry)
    count_query_mode = str(source.get("count_query_mode", "server_count"))
    if count_query_mode not in ("server_count", "server_count_advisory", "ids_length"):
        raise GateError(f"{source_id} has unsupported count query mode {count_query_mode!r}")

    count_params = dict(common, returnCountOnly="true")
    count_pre: int | None = None
    if count_query_mode != "ids_length":
        payload, receipts["count_pre"] = capture_request(
            external_run,
            request_id=f"{source_id}/count_pre",
            url=query_url,
            method="POST",
            params=count_params,
            output_path=raw_dir / "count_pre.json",
        )
        count_pre = int(decode_json(payload, f"{source_id} count pre").get("count", -1))
        if count_pre < 0:
            raise GateError(f"{source_id} returned no valid count")

    ids_params = dict(common, returnIdsOnly="true")
    payload, receipts["ids_pre"] = capture_request(
        external_run,
        request_id=f"{source_id}/ids_pre",
        url=query_url,
        method="POST",
        params=ids_params,
        output_path=raw_dir / "ids_pre.json",
    )
    ids_value = decode_json(payload, f"{source_id} IDs pre")
    ids = list(ids_value.get("objectIds") or [])
    ids.sort(key=lambda value: (float(value) if str(value).replace(".", "", 1).isdigit() else str(value)))
    server_count_pre = count_pre
    if count_query_mode == "ids_length":
        count_pre = len(ids)
    elif count_query_mode == "server_count_advisory":
        expected_delta = int(source["expected_server_count_minus_ids"])
        if count_pre is None or count_pre - len(ids) != expected_delta:
            raise GateError(
                f"{source_id} advisory count delta changed: "
                f"{count_pre} - {len(ids)} != {expected_delta}"
            )
        count_pre = len(ids)
    if len(ids) != len({str(value) for value in ids}):
        raise GateError(f"{source_id} returned duplicate object IDs")
    assert count_pre is not None
    if count_pre != len(ids):
        raise GateError(f"{source_id} count/ID mismatch: {count_pre} != {len(ids)}")

    maximum = int(metadata.get("maxRecordCount") or 1000)
    batch_size = min(
        int(registry["arcgis_query"]["batch_size_cap"]),
        int(source.get("batch_size_cap", maximum)),
        maximum,
    )
    feature_query_mode = str(source.get("feature_query_mode", "object_ids"))
    if feature_query_mode not in ("object_ids", "oid_where"):
        raise GateError(f"{source_id} has unsupported feature query mode {feature_query_mode!r}")
    page_records: list[dict[str, Any]] = []
    fetched_ids: list[Any] = []
    for page_index in range(0, len(ids), batch_size):
        batch = ids[page_index : page_index + batch_size]
        first = str(batch[0]) if batch else "empty"
        last = str(batch[-1]) if batch else "empty"
        page_name = f"oid_{first}_{last}.json"
        feature_params = {
            "f": "json",
            "outFields": "*",
            "outSR": registry["arcgis_query"]["out_sr"],
            "returnGeometry": "true",
            "returnZ": "true",
        }
        if feature_query_mode == "oid_where":
            feature_params.update(common)
            feature_params["where"] = oid_where_clause(object_id_field, batch)
        else:
            feature_params["objectIds"] = ",".join(str(value) for value in batch)
        payload, page_receipt = capture_request(
            external_run,
            request_id=f"{source_id}/page_{page_index // batch_size:05d}",
            url=query_url,
            method="POST",
            params=feature_params,
            output_path=raw_dir / page_name,
        )
        page = decode_json(payload, f"{source_id} feature page")
        features = page.get("features") or []
        if len(features) != len(batch):
            raise GateError(
                f"{source_id} page {page_name} returned {len(features)} features for {len(batch)} IDs"
            )
        page_ids = [feature.get("attributes", {}).get(object_id_field) for feature in features]
        if {str(value) for value in page_ids} != {str(value) for value in batch}:
            raise GateError(f"{source_id} page {page_name} object IDs differ from request")
        fetched_ids.extend(page_ids)
        page_records.append({"receipt": page_receipt, "requested_ids": batch})

    count_post: int | None = None
    if count_query_mode != "ids_length":
        payload, receipts["count_post"] = capture_request(
            external_run,
            request_id=f"{source_id}/count_post",
            url=query_url,
            method="POST",
            params=count_params,
            output_path=raw_dir / "count_post.json",
        )
        count_post = int(decode_json(payload, f"{source_id} count post").get("count", -1))
    payload, receipts["ids_post"] = capture_request(
        external_run,
        request_id=f"{source_id}/ids_post",
        url=query_url,
        method="POST",
        params=ids_params,
        output_path=raw_dir / "ids_post.json",
    )
    ids_post = list(decode_json(payload, f"{source_id} IDs post").get("objectIds") or [])
    server_count_post = count_post
    if count_query_mode == "ids_length":
        count_post = len(ids_post)
    elif count_query_mode == "server_count_advisory":
        expected_delta = int(source["expected_server_count_minus_ids"])
        if count_post is None or count_post - len(ids_post) != expected_delta:
            raise GateError(
                f"{source_id} post advisory count delta changed: "
                f"{count_post} - {len(ids_post)} != {expected_delta}"
            )
        count_post = len(ids_post)
    if count_post != count_pre or {str(value) for value in ids_post} != {str(value) for value in ids}:
        raise GateError(f"{source_id} changed during acquisition")
    if {str(value) for value in fetched_ids} != {str(value) for value in ids}:
        raise GateError(f"{source_id} fetched ID set is incomplete")

    fields = [
        {"alias": field.get("alias"), "name": field.get("name"), "type": field.get("type")}
        for field in metadata.get("fields") or []
    ]
    record = {
        "batch_size": batch_size,
        "count_query_mode": count_query_mode,
        "feature_count": count_pre,
        "feature_query_mode": feature_query_mode,
        "fields": fields,
        "geometry_type": metadata.get("geometryType"),
        "has_m": bool(metadata.get("hasM")),
        "has_z": bool(metadata.get("hasZ")),
        "id": source_id,
        "item_id": item_id,
        "layer_name": metadata.get("name"),
        "layer_url": layer_url,
        "license_partition": source["license_partition"],
        "max_record_count": maximum,
        "object_id_field": object_id_field,
        "pages": page_records,
        "receipts": receipts,
        "required": bool(source["required"]),
        "role": source["role"],
        "status": "FROZEN",
        "supports_pagination": bool((metadata.get("advancedQueryCapabilities") or {}).get("supportsPagination")),
    }
    if count_query_mode == "server_count_advisory":
        record["count_advisory"] = {
            "accepted_unique_id_count": count_pre,
            "expected_server_count_minus_ids": int(source["expected_server_count_minus_ids"]),
            "server_count_post": server_count_post,
            "server_count_pre": server_count_pre,
            "status": "STABLE_WARNING",
        }
    return record


def acquire_osm(registry: dict[str, Any], external_run: Path) -> dict[str, Any]:
    config = registry["osm"]
    query = config["query"]
    query_path = external_run / "requests/osm_bounded_roads/query.overpassql"
    atomic_write_bytes(query_path, query.encode("utf-8"))
    body = urllib.parse.urlencode({"data": query}).encode("utf-8")
    errors: list[dict[str, str]] = []
    for endpoint in config["endpoints"]:
        try:
            payload, transport = fetch_bytes(
                endpoint,
                method="POST",
                body=body,
                content_type="application/x-www-form-urlencoded; charset=utf-8",
                attempts=2,
                timeout_seconds=240,
            )
            document = decode_json(payload, "bounded OSM Overpass query")
            elements = document.get("elements")
            if not isinstance(elements, list):
                raise GateError("Overpass response has no elements array")
            _, duplicate_summary = deduplicate_osm_elements(elements)
            raw_path = external_run / "raw/osm_bounded_roads/overpass.json"
            atomic_write_bytes(raw_path, payload)
            request_path = external_run / "requests/osm_bounded_roads/request.json"
            atomic_write_json(
                request_path,
                request_record(
                    url=endpoint,
                    method="POST",
                    body=body,
                    headers={
                        "Accept": "application/json,text/html;q=0.9,*/*;q=0.1",
                        "Content-Type": "application/x-www-form-urlencoded; charset=utf-8",
                        "User-Agent": USER_AGENT,
                    },
                    timeout_seconds=240,
                ),
            )
            counts = Counter(str(element.get("type")) for element in elements)
            return {
                "attribution": config["attribution"],
                "element_counts": dict(sorted(counts.items())),
                "endpoint": endpoint,
                "id": config["id"],
                "license_partition": config["license_partition"],
                "osm_base_timestamp": (document.get("osm3s") or {}).get("timestamp_osm_base"),
                "query": file_record(query_path, external_run),
                "raw": file_record(raw_path, external_run),
                "raw_id_audit": duplicate_summary,
                "request": file_record(request_path, external_run),
                "required": bool(config["required"]),
                "role": "road_fallback",
                "status": "FROZEN",
                "transport": transport,
            }
        except GateError as error:
            errors.append({"endpoint": endpoint, "error": str(error)})
    raise GateError(f"All bounded Overpass endpoints failed: {errors}")


def inventory_product_bindings(registry: dict[str, Any]) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    records: list[dict[str, Any]] = []
    warnings: list[dict[str, Any]] = []
    accepted_ref = registry["accepted_source_sha"]
    for binding in registry["product_bindings"]:
        path = REPO_ROOT / binding["path"]
        if not path.is_file():
            raise GateError(f"Accepted product binding is missing: {binding['path']}")
        actual_sha = sha256_file(path)
        if actual_sha != binding["sha256"]:
            raise GateError(f"Accepted product SHA mismatch for {binding['id']}: {actual_sha}")
        blob = subprocess.run(
            ["git", "rev-parse", f"{accepted_ref}:{binding['path']}"],
            cwd=REPO_ROOT,
            check=True,
            text=True,
            capture_output=True,
        ).stdout.strip()
        if blob != binding["git_blob_sha1"]:
            raise GateError(f"Accepted Git blob mismatch for {binding['id']}: {blob}")
        binding_results: list[dict[str, Any]] = []
        for expected in binding.get("bindings") or []:
            text = (REPO_ROOT / expected["path"]).read_text(encoding="utf-8")
            present = binding["sha256"] in text
            if not present:
                raise GateError(f"Current accepted hash absent from binding manifest {expected['path']}")
            binding_results.append({**expected, "current_sha256_present": True})
        record = {
            "bindings": binding_results,
            "bytes": path.stat().st_size,
            "git_blob_sha1": blob,
            "id": binding["id"],
            "license_partition": binding["license_partition"],
            "path": binding["path"],
            "role": binding["role"],
            "sha256": actual_sha,
            "status": "PINNED",
        }
        if binding.get("historical_warning"):
            warning = {
                "accepted_current_sha256": actual_sha,
                "historical_expected_sha256": binding["historical_warning"]["expected_sha256"],
                "id": f"{binding['id']}_historical_provenance_hash",
                "locations": binding["historical_warning"]["locations"],
                "reason": (
                    "Adjacent historical provenance names a stale SHA-256 that does not match the accepted "
                    "fda11a2 byte stream or its later accepted binding manifests."
                ),
                "resolution": "Current fda11a2 byte stream, Git blob, and later accepted bindings govern.",
                "severity": "WARNING",
                "status": "ADJUDICATED",
            }
            warnings.append(warning)
            record["historical_provenance_warning"] = warning["id"]
        records.append(record)
    return records, warnings


def rejected_archive_root(external_run: Path, freeze_sha: str) -> Path:
    """Return the deterministic Windows-path-safe archive root for a rejected freeze."""

    if not isinstance(freeze_sha, str) or re.fullmatch(r"[0-9a-f]{64}", freeze_sha) is None:
        raise GateError("Rejected freeze SHA-256 must be exactly 64 lowercase hexadecimal characters")
    return external_run / "attempts" / f"rejected_{freeze_sha[:32]}"


def archive_rejected_active_package(registry: dict[str, Any], external_run: Path) -> dict[str, Any] | None:
    """Move an explicitly rejected active package aside before a clean acquisition."""

    freeze_path = external_run / FREEZE_NAME
    active_names = list(registry["external_package"]["active_roots"]) + [
        "acquisition_failure.json",
        "product_source_inventory.json",
        "rebuild_check",
        RUN_MANIFEST_NAME,
        PACKAGE_INDEX_NAME,
    ]
    active_existing = [external_run / name for name in active_names if (external_run / name).exists()]
    if not freeze_path.is_file():
        allowed_inventory = external_run / "product_source_inventory.json"
        unexpected = [path for path in active_existing if path != allowed_inventory]
        if any(path.is_file() or any(path.rglob("*")) for path in unexpected):
            raise GateError(
                "External run has unbound active-package files but no freeze; move them under quarantine before acquisition"
            )
        return None
    freeze_sha = sha256_file(freeze_path)
    rejected = {value["sha256"]: value for value in registry["rejected_freezes"]}.get(freeze_sha)
    if rejected is None:
        raise GateError(
            f"Existing external freeze {freeze_sha} is not explicitly rejected; refusing to overwrite it"
        )
    archive_root = rejected_archive_root(external_run, freeze_sha)
    if archive_root.exists():
        raise GateError(f"Rejected-attempt archive already exists: {archive_root}")
    archive_root.mkdir(parents=True)
    for source in [freeze_path, *active_existing]:
        if not source.exists():
            continue
        resolved = source.resolve()
        resolved.relative_to(external_run.resolve())
        shutil.move(str(resolved), str(archive_root / source.name))
    archive_record = {
        "freeze_sha256": freeze_sha,
        "reason": rejected["reason"],
        "schema": "questflight.traffic-v2-rejected-external-package.v1",
        "status": rejected["status"],
    }
    atomic_write_json(archive_root / "rejection.json", archive_record)
    return archive_record


def _acquire_core(registry_path: Path, external_run_path: Path) -> dict[str, Any]:
    registry = load_registry(registry_path)
    validate_pinned_inputs(registry)
    external_run = require_external(external_run_path)
    toolchain_binding = validate_or_create_external_toolchain_manifest(
        registry, registry_path, external_run, allow_create=True
    )
    archived_attempt = archive_rejected_active_package(registry, external_run)
    for name in registry["external_package"]["active_roots"]:
        if name == "approval_candidate":
            continue
        (external_run / name).mkdir(parents=True, exist_ok=True)
    license_snapshots = capture_license_snapshots(registry, external_run)
    products, product_warnings = inventory_product_bindings(registry)
    source_records: list[dict[str, Any]] = []
    for source in registry["sources"]:
        record = acquire_arcgis_source(registry, source, external_run, license_snapshots)
        source_records.append(record)
        receipt_path = external_run / "receipts" / f"{source['id']}.json"
        atomic_write_json(receipt_path, record)
        record["receipt_file"] = file_record(receipt_path, external_run)
    for source in registry.get("document_sources") or []:
        record = acquire_document_source(registry, source, external_run, license_snapshots)
        source_records.append(record)
        receipt_path = external_run / "receipts" / f"{source['id']}.json"
        atomic_write_json(receipt_path, record)
        record["receipt_file"] = file_record(receipt_path, external_run)
    osm_record = acquire_osm(registry, external_run)
    source_records.append(osm_record)
    osm_receipt_path = external_run / "receipts/osm_bounded_roads.json"
    atomic_write_json(osm_receipt_path, osm_record)
    osm_record["receipt_file"] = file_record(osm_receipt_path, external_run)

    freeze = {
        "accepted_product_bindings": products,
        "accepted_source_sha": registry["accepted_source_sha"],
        "archived_rejected_attempt": archived_attempt,
        "acquisition_completed_utc": utc_now(),
        "coordinate_contract": registry["coordinate_contract"],
        "license_snapshots": license_snapshots,
        "product_binding_warnings": product_warnings,
        "pre_acquisition_inventory": (
            file_record(external_run / "product_source_inventory.json", external_run)
            if (external_run / "product_source_inventory.json").is_file()
            else None
        ),
        "registry": {
            "bytes": registry_path.stat().st_size,
            "path": registry_path.resolve().relative_to(REPO_ROOT.resolve()).as_posix(),
            "sha256": sha256_file(registry_path),
        },
        "schema": "questflight.traffic-v2-geospatial-source-freeze.v1",
        "sources": sorted(source_records, key=lambda value: value["id"]),
        "specification": registry["specification"],
        "tool": repository_file_record(Path(__file__)),
        "toolchain": toolchain_binding,
    }
    freeze_path = external_run / FREEZE_NAME
    atomic_write_json(freeze_path, freeze)
    print(canonical_json_bytes({"freeze": file_record(freeze_path, external_run), "sources": len(source_records)}).decode())
    return freeze


def validate_acquisition_target(
    registry: dict[str, Any],
    external_run: Path,
    *,
    expected_toolchain: dict[str, Any],
) -> None:
    """Plan archive/no-freeze handling without changing the external package."""

    if not external_run.exists():
        return
    if not external_run.is_dir():
        raise GateError("External run target exists but is not a directory")
    manifest_path = external_toolchain_manifest_path(registry, external_run)
    if manifest_path.is_file():
        if manifest_path.read_bytes() != canonical_json_bytes(expected_toolchain):
            raise ToolchainPreflightError("External toolchain manifest is stale or inconsistent")
    freeze_path = external_run / FREEZE_NAME
    active_names = list(registry["external_package"]["active_roots"]) + [
        "acquisition_failure.json",
        "product_source_inventory.json",
        "rebuild_check",
        RUN_MANIFEST_NAME,
        PACKAGE_INDEX_NAME,
    ]
    active_existing = [external_run / name for name in active_names if (external_run / name).exists()]
    if not freeze_path.is_file():
        unexpected = [
            path
            for path in active_existing
            if path != external_run / "product_source_inventory.json"
        ]
        if any(path.is_file() or (path.is_dir() and any(path.rglob("*"))) for path in unexpected):
            raise GateError("External run has unbound active-package files but no freeze")
        return
    freeze_sha = sha256_file(freeze_path)
    if freeze_sha not in {value["sha256"] for value in registry["rejected_freezes"]}:
        raise GateError(f"Existing external freeze {freeze_sha} is not explicitly rejected")
    archive_root = rejected_archive_root(external_run, freeze_sha)
    if archive_root.exists():
        raise GateError(f"Rejected-attempt archive already exists: {archive_root}")


def acquire(registry_path: Path, external_run_path: Path) -> dict[str, Any]:
    """Acquire only after full read-only authority/toolchain preflight and lock ownership."""

    registry = load_registry(registry_path)
    validate_pinned_inputs(registry)
    topology = validate_command_path_topology(
        registry, external_run_path, command="acquire"
    )
    expected_toolchain = validate_toolchain_contract(registry, registry_path)
    inventory_product_bindings(registry)
    external_run = topology["external_run"]
    validate_acquisition_target(
        registry, external_run, expected_toolchain=expected_toolchain
    )
    with exclusive_operation_lock(external_run, "acquire"):
        registry = load_registry(registry_path)
        validate_command_path_topology(registry, external_run, command="acquire")
        validate_pinned_inputs(registry)
        expected_toolchain = validate_toolchain_contract(registry, registry_path)
        inventory_product_bindings(registry)
        validate_acquisition_target(
            registry, external_run, expected_toolchain=expected_toolchain
        )
        return _acquire_core(registry_path, external_run)


def inventory_sources(registry_path: Path, external_run_path: Path) -> dict[str, Any]:
    """Write the deterministic product/source inventory under the shared lock."""

    registry = load_registry(registry_path)
    validate_pinned_inputs(registry)
    topology = validate_command_path_topology(
        registry, external_run_path, command="inventory"
    )
    expected_toolchain = validate_toolchain_contract(registry, registry_path)
    products, warnings = inventory_product_bindings(registry)
    external_run = topology["external_run"]
    report = {
        "accepted_product_bindings": products,
        "product_binding_warnings": warnings,
        "registry_sha256": sha256_file(registry_path),
        "schema": "questflight.traffic-v2-geospatial-inventory.v1",
        "source_count": len(registry["sources"])
        + len(registry.get("document_sources") or [])
        + 1,
        "status": "PASS",
    }
    if external_run.exists():
        manifest_path = external_toolchain_manifest_path(registry, external_run)
        if manifest_path.is_file() and manifest_path.read_bytes() != canonical_json_bytes(
            expected_toolchain
        ):
            raise ToolchainPreflightError("External toolchain manifest is stale or inconsistent")
        inventory_path = external_run / "product_source_inventory.json"
        if inventory_path.is_file() and inventory_path.read_bytes() != canonical_json_bytes(report):
            raise GateError("Existing pre-acquisition inventory is stale; refusing to overwrite it")
        if (external_run / FREEZE_NAME).exists():
            raise GateError("Inventory is pre-acquisition only; a source freeze already exists")
    if external_run.exists() and (external_run / OPERATION_LOCK_NAME).exists():
        raise ConcurrentAcquisitionError("External source package already has an operation lock")
    with exclusive_operation_lock(external_run, "inventory"):
        registry = load_registry(registry_path)
        validate_command_path_topology(registry, external_run, command="inventory")
        validate_pinned_inputs(registry)
        expected_toolchain = validate_toolchain_contract(registry, registry_path)
        products, warnings = inventory_product_bindings(registry)
        report = {
            "accepted_product_bindings": products,
            "product_binding_warnings": warnings,
            "registry_sha256": sha256_file(registry_path),
            "schema": "questflight.traffic-v2-geospatial-inventory.v1",
            "source_count": len(registry["sources"])
            + len(registry.get("document_sources") or [])
            + 1,
            "status": "PASS",
        }
        path = external_run / "product_source_inventory.json"
        expected = canonical_json_bytes(report)
        manifest_path = external_toolchain_manifest_path(registry, external_run)
        if manifest_path.is_file() and manifest_path.read_bytes() != canonical_json_bytes(
            expected_toolchain
        ):
            raise ToolchainPreflightError("External toolchain manifest is stale or inconsistent")
        if (external_run / FREEZE_NAME).exists():
            raise GateError("Inventory is pre-acquisition only; a source freeze already exists")
        if path.is_file() and path.read_bytes() != expected:
            raise GateError("Existing pre-acquisition inventory is stale; refusing to overwrite it")
        if not path.is_file():
            atomic_write_bytes(path, expected)
        print(expected.decode())
        return report


def road_class(value: Any) -> str | None:
    text = str(value or "").strip().lower()
    if text.endswith("_link"):
        text = text[: -len("_link")]
    if text in ROAD_CLASSES:
        return text
    if text == "living_street":
        return "residential"
    return None


def normalized_label(value: Any) -> str:
    """Normalize categorical authority values without inventing a crosswalk."""

    return " ".join(str(value or "").strip().split()).lower()


def strict_positive_int(value: Any, *, maximum: int = 20) -> int | None:
    """Return a declared integer count; reject rounding and width-based inference."""

    if isinstance(value, bool) or value is None:
        return None
    if isinstance(value, int):
        candidate = value
    elif isinstance(value, float):
        if not math.isfinite(value) or not value.is_integer():
            return None
        candidate = int(value)
    else:
        text = str(value).strip()
        if not re.fullmatch(r"[0-9]+(?:\.0+)?", text):
            return None
        candidate = int(float(text))
    return candidate if 1 <= candidate <= maximum else None


def strict_nonnegative_int(value: Any, *, maximum: int = 20) -> int | None:
    """Return a declared nonnegative integer without rounding."""

    if isinstance(value, bool) or value is None:
        return None
    if isinstance(value, int):
        candidate = value
    elif isinstance(value, float):
        if not math.isfinite(value) or not value.is_integer():
            return None
        candidate = int(value)
    else:
        text = str(value).strip()
        if not re.fullmatch(r"[0-9]+(?:\.0+)?", text):
            return None
        candidate = int(float(text))
    return candidate if 0 <= candidate <= maximum else None


OSM_LANE_DIRECTIONS = ("forward", "backward", "both_ways")


def osm_lane_count_components(
    tags: dict[str, Any], *, direction_value: str | None = None
) -> dict[str, Any]:
    """Parse static OSM lane totals/components and reject conditional or contradictory evidence."""

    static_keys = ("lanes", *(f"lanes:{direction}" for direction in OSM_LANE_DIRECTIONS))
    conditional_keys = tuple(f"{key}:conditional" for key in static_keys)
    declared = {key: tags[key] for key in static_keys if key in tags}
    conditionals = {
        key: tags[key]
        for key in conditional_keys
        if key in tags
    }
    parsed_total = strict_positive_int(tags.get("lanes")) if "lanes" in tags else None
    present_directional = [key for key in static_keys[1:] if key in tags]
    parsed_directional: dict[str, int] = {}
    invalid_directional: list[str] = []
    for key in present_directional:
        parsed = (
            strict_nonnegative_int(tags[key])
            if key == "lanes:both_ways"
            else strict_positive_int(tags[key])
        )
        if parsed is None:
            invalid_directional.append(key)
        else:
            parsed_directional[key] = parsed
    directional_sum = sum(parsed_directional.values())
    explicitly_complete = set(present_directional) == set(static_keys[1:])
    derivation_complete = all(
        key in parsed_directional for key in ("lanes:forward", "lanes:backward")
    ) and not invalid_directional
    count = parsed_total
    authority = "lanes"
    reason: str | None = None
    if conditionals:
        count = None
        reason = "CONDITIONAL_LANE_COUNT_UNRESOLVED"
    elif "lanes" in tags and parsed_total is None:
        count = None
        reason = "INVALID_DECLARED_TOTAL_LANE_COUNT"
    elif invalid_directional:
        count = None
        reason = "INVALID_DECLARED_DIRECTIONAL_LANE_COMPONENT"
    elif direction_value == "FORWARD" and any(
        parsed_directional.get(key, 0) > 0
        for key in ("lanes:backward", "lanes:both_ways")
    ):
        count = None
        reason = "INCONSISTENT_ONEWAY_AND_DIRECTIONAL_LANE_COUNTS"
    elif direction_value == "REVERSE" and any(
        parsed_directional.get(key, 0) > 0
        for key in ("lanes:forward", "lanes:both_ways")
    ):
        count = None
        reason = "INCONSISTENT_ONEWAY_AND_DIRECTIONAL_LANE_COUNTS"
    elif count is not None and (
        directional_sum > count or (explicitly_complete and directional_sum != count)
    ):
        count = None
        reason = "INCONSISTENT_TOTAL_AND_DIRECTIONAL_LANE_COUNTS"
    elif count is None and derivation_complete and 1 <= directional_sum <= 20:
        count = directional_sum
        authority = "+".join(present_directional)
    elif count is None:
        reason = "MISSING_OR_INVALID_DECLARED_LANE_COUNT"
    return {
        "conditional_lane_components": conditionals,
        "declared_lane_components": declared,
        "lane_count": count,
        "lane_count_authority": authority if count is not None else None,
        "parsed_directional_lane_counts": parsed_directional,
        "reason": reason,
    }


def osm_direction_evidence(registry: dict[str, Any], tags: dict[str, Any]) -> dict[str, Any]:
    """Resolve mode-specific OSM oneway semantics by frozen specificity, fail closed on conditionals."""

    config = registry["authority_model"]["correspondence_semantics"][registry["osm"]["id"]]
    precedence = registry["authority_model"]["osm_oneway_key_precedence"]
    raw_evidence = {key: str(tags[key]) for key in precedence if key in tags}
    conditional_evidence = {
        f"{key}:conditional": str(tags[f"{key}:conditional"])
        for key in precedence
        if f"{key}:conditional" in tags
    }
    effective_key = None
    for key in precedence:
        conditional_value = str(tags.get(f"{key}:conditional") or "").strip()
        static_value = str(tags.get(key) or "").strip()
        if conditional_value or static_value:
            effective_key = key
            break
    if effective_key is not None:
        conditional_key = f"{effective_key}:conditional"
        if str(tags.get(conditional_key) or "").strip():
            return {
                "authority": f"osm.{conditional_key}",
                "conditional_evidence": conditional_evidence,
                "confidence": "UNPARSED_CONDITIONAL_FAIL_CLOSED",
                "effective_key": conditional_key,
                "raw_evidence": raw_evidence,
                "raw_value": tags[conditional_key],
                "reason": (
                    "CONDITIONAL_ONEWAY_UNRESOLVED"
                    if effective_key == "oneway"
                    else "CONDITIONAL_MODE_SPECIFIC_ONEWAY_UNRESOLVED"
                ),
                "usable_for_correspondence": False,
                "value": "UNKNOWN",
            }
        raw_value = tags.get(effective_key)
        semantic = _semantic_value(
            normalized_label(raw_value),
            config.get("direction_values") or {},
            missing_reason="MISSING_OR_UNMAPPED_OSM_ONEWAY_VALUE",
        )
        return {
            "authority": f"osm.{effective_key}",
            "conditional_evidence": conditional_evidence,
            "effective_key": effective_key,
            "raw_evidence": raw_evidence,
            "raw_value": raw_value,
            **semantic,
        }
    highway = normalized_label(tags.get("highway"))
    junction = normalized_label(tags.get("junction"))
    if highway in set(config.get("implicit_forward_highways") or []):
        default_reason, default_value = "OSM_IMPLICIT_ONEWAY_HIGHWAY_DEFAULT", "FORWARD"
    elif junction in set(config.get("implicit_forward_junctions") or []):
        default_reason, default_value = "OSM_IMPLICIT_ONEWAY_JUNCTION_DEFAULT", "FORWARD"
    else:
        default_reason = "OSM_ORDINARY_ROAD_BIDIRECTIONAL_DEFAULT"
        default_value = config.get("ordinary_missing_default", "BIDIRECTIONAL")
    return {
        "authority": "osm.oneway_default",
        "conditional_evidence": conditional_evidence,
        "confidence": "DOCUMENTED_ODBL_DEFAULT",
        "effective_key": None,
        "raw_evidence": raw_evidence,
        "raw_value": None,
        "reason": default_reason,
        "usable_for_correspondence": True,
        "value": default_value,
    }


def _osm_resolved_motor_access(
    registry: dict[str, Any], tags: dict[str, Any]
) -> dict[str, Any]:
    """Resolve each active motor lane by motor mode, then by scope specificity.

    The frozen mode cascade is motorcar, motor_vehicle, vehicle, access.  Within
    each mode, scope precedence is lane (directional then generic), direction,
    whole way.  A blank value inherits the next applicable declaration.  A
    generic lane on a bidirectional way without lane-direction authority is
    resolved through both possible directional inheritance paths; disagreement
    is blocking rather than silently treating the lane as directionless.
    """

    model = registry["authority_model"]
    modes = list(model["osm_access_key_precedence"])
    direction = osm_direction_evidence(registry, tags)
    counts = osm_lane_count_components(tags, direction_value=direction["value"])
    raw: dict[str, str] = {}
    conditionals: dict[str, str] = {}
    for mode in modes:
        for suffix in (
            "",
            ":forward",
            ":backward",
            ":lanes",
            ":lanes:forward",
            ":lanes:backward",
            ":lanes:both_ways",
        ):
            key = f"{mode}{suffix}"
            if key in tags:
                raw[key] = str(tags[key])
            conditional_key = f"{key}:conditional"
            if conditional_key in tags:
                conditionals[conditional_key] = str(tags[conditional_key])
    normalized_raw = {key: normalized_label(value) for key, value in raw.items()}
    lane_specific_present = any(":lanes" in key and str(value).strip() for key, value in {
        **raw,
        **conditionals,
    }.items())

    def unknown_result(reason: str, key: str | None = None, value: Any = None) -> dict[str, Any]:
        return {
            "active_lane_count": 0,
            "conditional_access_evidence": conditionals,
            "covers_all_lanes": False,
            "decision": "UNKNOWN_BLOCKING",
            "directional_decisions": [],
            "effective_key": key,
            "effective_mode": key.split(":", 1)[0] if key else None,
            "effective_value": value,
            "normalized_access_evidence": normalized_raw,
            "raw_access_evidence": raw,
            "reason": reason,
            "resolved_active_lanes": [],
            "selected_mode": key.split(":", 1)[0] if key else None,
            "token_evidence": {},
        }

    if direction["value"] == "UNKNOWN":
        return unknown_result(
            str(direction.get("reason") or "DIRECTIONAL_MOTOR_ACCESS_WITH_UNKNOWN_DIRECTION"),
            direction.get("effective_key"),
            direction.get("raw_value"),
        )
    if counts["reason"] == "INCONSISTENT_ONEWAY_AND_DIRECTIONAL_LANE_COUNTS":
        return unknown_result(counts["reason"])

    lane_total = counts["lane_count"]
    components = counts["parsed_directional_lane_counts"]
    directional_lane_keys: list[tuple[str, str]] = []
    for key, value in {**raw, **conditionals}.items():
        if not str(value).strip():
            continue
        match = re.fullmatch(
            r"(?:motorcar|motor_vehicle|vehicle|access):lanes:"
            r"(forward|backward|both_ways)(?::conditional)?",
            key,
        )
        if match:
            directional_lane_keys.append((key, match.group(1)))
    referenced_lane_directions = {direction_name for _, direction_name in directional_lane_keys}
    if direction["value"] == "FORWARD" and referenced_lane_directions - {"forward"}:
        return unknown_result("LANE_MOTOR_ACCESS_DIRECTION_CONTRADICTS_ONEWAY")
    if direction["value"] == "REVERSE" and referenced_lane_directions - {"backward"}:
        return unknown_result("LANE_MOTOR_ACCESS_DIRECTION_CONTRADICTS_ONEWAY")
    if direction["value"] == "BIDIRECTIONAL" and referenced_lane_directions:
        if any(f"lanes:{value}" not in components for value in referenced_lane_directions):
            return unknown_result("LANE_MOTOR_ACCESS_DIRECTION_COUNT_MISSING")
        if lane_total is None or sum(components.values()) != lane_total:
            return unknown_result("LANE_MOTOR_ACCESS_DIRECTION_COUNT_INCOMPLETE")
    slots: list[dict[str, Any]] = []
    if lane_specific_present:
        if lane_total is None:
            return unknown_result("LANE_MOTOR_ACCESS_TOKEN_CARDINALITY_MISMATCH")
        directional_lane_present = any(
            f":lanes:{direction_name}" in key and str(value).strip()
            for key, value in {**raw, **conditionals}.items()
            for direction_name in OSM_LANE_DIRECTIONS
        )
        if direction["value"] == "FORWARD":
            slots = [
                {"direction": "forward", "direction_index": index, "global_index": index}
                for index in range(lane_total)
            ]
        elif direction["value"] == "REVERSE":
            slots = [
                {"direction": "backward", "direction_index": index, "global_index": index}
                for index in range(lane_total)
            ]
        elif directional_lane_present:
            declared_sum = sum(components.values())
            if declared_sum > lane_total:
                return unknown_result("LANE_MOTOR_ACCESS_TOKEN_CARDINALITY_MISMATCH")
            global_index = 0
            for direction_name in OSM_LANE_DIRECTIONS:
                for direction_index in range(components.get(f"lanes:{direction_name}", 0)):
                    slots.append(
                        {
                            "direction": direction_name,
                            "direction_index": direction_index,
                            "global_index": global_index,
                        }
                    )
                    global_index += 1
            for direction_index in range(lane_total - declared_sum):
                slots.append(
                    {
                        "direction": "unspecified",
                        "direction_index": direction_index,
                        "global_index": global_index,
                    }
                )
                global_index += 1
        else:
            slots = [
                {"direction": "unspecified", "direction_index": index, "global_index": index}
                for index in range(lane_total)
            ]
    else:
        active_directions = (
            ("forward",)
            if direction["value"] == "FORWARD"
            else ("backward",)
            if direction["value"] == "REVERSE"
            else ("forward", "backward")
        )
        slots = [
            {"direction": value, "direction_index": 0, "global_index": index}
            for index, value in enumerate(active_directions)
        ]

    token_evidence: dict[str, list[dict[str, str]]] = {}
    for key, raw_value in raw.items():
        if ":lanes" not in key:
            continue
        token_evidence[key] = []
        for raw_token in raw_value.split("|"):
            token = normalized_label(raw_token)
            rule = model["osm_access_lane_value_crosswalk"].get(token)
            token_evidence[key].append(
                {
                    "decision": str((rule or {}).get("decision") or "UNMAPPED"),
                    "raw_value": raw_token,
                    "value": token,
                }
            )

    def resolve_directional_slot(
        slot: dict[str, Any], direction_name: str
    ) -> dict[str, Any]:
        directional_slot = {**slot, "direction": direction_name}
        scopes: list[tuple[str, str | None, int | None, int | None]] = []
        lane_scope_direction = (
            "both_ways" if slot.get("direction") == "both_ways" else direction_name
        )
        if lane_specific_present and lane_scope_direction in OSM_LANE_DIRECTIONS:
            scopes.append(
                (
                    "LANE_DIRECTION",
                    f":lanes:{lane_scope_direction}",
                    slot["direction_index"],
                    components.get(f"lanes:{lane_scope_direction}")
                    if direction["value"] == "BIDIRECTIONAL"
                    else lane_total,
                )
            )
        if lane_specific_present:
            scopes.append(("LANE", ":lanes", slot["global_index"], lane_total))
        if direction_name in ("forward", "backward"):
            scopes.append(("DIRECTION", f":{direction_name}", None, None))
        scopes.append(("WHOLE", "", None, None))
        trace: list[dict[str, Any]] = []
        for mode in modes:
            for scope, suffix, token_index, expected_count in scopes:
                key = f"{mode}{suffix or ''}"
                conditional_key = f"{key}:conditional"
                conditional_value = str(tags.get(conditional_key) or "").strip()
                static_value = str(tags.get(key) or "")
                if conditional_value:
                    return {
                        **directional_slot,
                        "decision": "UNKNOWN_BLOCKING",
                        "key": conditional_key,
                        "mode": mode,
                        "reason": (
                            "CONDITIONAL_LANE_MOTOR_ACCESS_UNRESOLVED"
                            if scope.startswith("LANE")
                            else "CONDITIONAL_DIRECTIONAL_MOTOR_ACCESS_UNRESOLVED"
                            if scope == "DIRECTION"
                            else "CONDITIONAL_MOTOR_ACCESS_UNRESOLVED"
                        ),
                        "scope": scope,
                        "trace": trace,
                        "value": conditional_value,
                    }
                if not static_value.strip():
                    if key in tags:
                        trace.append({"decision": "INHERIT_BLANK", "key": key, "scope": scope})
                    continue
                if scope.startswith("LANE"):
                    tokens = static_value.split("|")
                    if expected_count is None or len(tokens) != int(expected_count):
                        return {
                            **directional_slot,
                            "decision": "UNKNOWN_BLOCKING",
                            "key": key,
                            "mode": mode,
                            "reason": "LANE_MOTOR_ACCESS_TOKEN_CARDINALITY_MISMATCH",
                            "scope": scope,
                            "trace": trace,
                            "value": static_value,
                        }
                    raw_token = tokens[int(token_index)]
                    value = normalized_label(raw_token)
                    if not value:
                        trace.append({"decision": "INHERIT_BLANK", "key": key, "scope": scope})
                        continue
                    rule = model["osm_access_lane_value_crosswalk"].get(value)
                    decision = str((rule or {}).get("decision") or "UNMAPPED")
                    if decision == "NO":
                        decision = "EXCLUDE"
                    reason = str((rule or {}).get("reason") or "UNMAPPED_LANE_MOTOR_ACCESS_VALUE")
                else:
                    value = normalized_label(static_value)
                    rule = model["osm_access_value_crosswalk"].get(value)
                    decision = str((rule or {}).get("decision") or "UNKNOWN_BLOCKING")
                    reason = str((rule or {}).get("reason") or "UNKNOWN_EFFECTIVE_OSM_ACCESS_VALUE")
                if decision in ("RESTRICTED", "UNMAPPED"):
                    decision = "UNKNOWN_BLOCKING"
                return {
                    **directional_slot,
                    "decision": decision,
                    "key": key,
                    "mode": mode,
                    "reason": reason,
                    "scope": scope,
                    "trace": trace,
                    "value": value,
                }
        return {
            **directional_slot,
            "decision": "ALLOW",
            "key": None,
            "mode": None,
            "reason": "NO_EXPLICIT_MOTOR_ACCESS_RESTRICTION",
            "scope": "DEFAULT",
            "trace": trace,
            "value": None,
        }

    def resolve_slot(slot: dict[str, Any]) -> dict[str, Any]:
        if slot["direction"] not in ("unspecified", "both_ways"):
            return resolve_directional_slot(slot, str(slot["direction"]))
        branches = [
            resolve_directional_slot(slot, direction_name)
            for direction_name in ("forward", "backward")
        ]
        branch_trace = [
            {**trace_entry, "direction": branch["direction"]}
            for branch in branches
            for trace_entry in branch["trace"]
        ]
        branch_decisions = {branch["decision"] for branch in branches}
        if len(branch_decisions) != 1:
            return {
                **slot,
                "decision": "UNKNOWN_BLOCKING",
                "directional_resolution_branches": branches,
                "key": None,
                "mode": None,
                "reason": "AMBIGUOUS_BIDIRECTIONAL_LANE_MOTOR_ACCESS",
                "scope": "DIRECTION_AMBIGUITY",
                "trace": branch_trace,
                "value": None,
            }
        first_branch = branches[0]

        def common_branch_value(key: str) -> Any:
            first_value = first_branch.get(key)
            return (
                first_value
                if all(branch.get(key) == first_value for branch in branches[1:])
                else None
            )

        decision = first_branch["decision"]
        branch_reasons = {str(branch["reason"]) for branch in branches}
        reason = (
            str(first_branch["reason"])
            if len(branch_reasons) == 1
            else "AMBIGUOUS_BIDIRECTIONAL_LANE_MOTOR_ACCESS"
            if decision == "UNKNOWN_BLOCKING"
            else "IDENTICAL_BIDIRECTIONAL_LANE_MOTOR_ACCESS_OUTCOME"
        )
        return {
            **slot,
            "decision": decision,
            "directional_resolution_branches": branches,
            "key": common_branch_value("key"),
            "mode": common_branch_value("mode"),
            "reason": reason,
            "scope": common_branch_value("scope") or "BIDIRECTIONAL_BRANCH_AGREEMENT",
            "trace": branch_trace,
            "value": common_branch_value("value"),
        }

    resolved = [resolve_slot(slot) for slot in slots]
    decision_values = {value["decision"] for value in resolved}
    first = next((value for value in resolved if value["key"] is not None), resolved[0])
    uses_lane_scope = any(str(value["scope"]).startswith("LANE") for value in resolved)
    if "UNKNOWN_BLOCKING" in decision_values:
        decision = "UNKNOWN_BLOCKING"
        first_unknown = next(value for value in resolved if value["decision"] == "UNKNOWN_BLOCKING")
        reason = (
            first_unknown["reason"]
            if first_unknown["reason"]
            in (
                "CONDITIONAL_LANE_MOTOR_ACCESS_UNRESOLVED",
                "CONDITIONAL_DIRECTIONAL_MOTOR_ACCESS_UNRESOLVED",
                "CONDITIONAL_MOTOR_ACCESS_UNRESOLVED",
                "LANE_MOTOR_ACCESS_TOKEN_CARDINALITY_MISMATCH",
                "UNMAPPED_LANE_MOTOR_ACCESS_VALUE",
                "UNKNOWN_EFFECTIVE_OSM_ACCESS_VALUE",
                "AMBIGUOUS_BIDIRECTIONAL_LANE_MOTOR_ACCESS",
            )
            else "MIXED_OR_RESTRICTED_LANE_MOTOR_ACCESS"
            if str(first_unknown["scope"]).startswith("LANE")
            else "UNKNOWN_OR_RESTRICTED_DIRECTIONAL_MOTOR_ACCESS"
        )
        first = first_unknown
    elif decision_values == {"EXCLUDE"}:
        decision = "EXCLUDE"
        reason = (
            "ALL_LANES_EXPLICITLY_NO_MOTOR_ACCESS"
            if lane_specific_present
            else first["reason"]
        )
    elif "EXCLUDE" in decision_values:
        decision = "UNKNOWN_BLOCKING"
        reason = (
            "MIXED_OR_RESTRICTED_LANE_MOTOR_ACCESS"
            if uses_lane_scope
            else "MIXED_DIRECTIONAL_MOTOR_ACCESS"
        )
    else:
        decision = "ALLOW"
        reason = (
            "ALL_ACTIVE_LANES_MOTOR_ACCESS_ALLOWED"
            if lane_specific_present
            else "EXPLICIT_OR_DEFAULT_DIRECTIONAL_MOTOR_ACCESS_ALLOWED"
        )
    return {
        "active_lane_count": len(resolved),
        "conditional_access_evidence": conditionals,
        "covers_all_lanes": lane_total is not None and len(resolved) == lane_total,
        "decision": decision,
        "directional_decisions": resolved,
        "effective_key": first.get("key"),
        "effective_mode": first.get("mode"),
        "effective_value": first.get("value"),
        "normalized_access_evidence": normalized_raw,
        "raw_access_evidence": raw,
        "reason": reason,
        "resolved_active_lanes": resolved,
        "selected_mode": first.get("mode"),
        "token_evidence": token_evidence,
    }


def osm_motor_access_evidence(registry: dict[str, Any], tags: dict[str, Any]) -> dict[str, Any]:
    """Return the complete cascade-resolved active-lane motor-access evidence."""

    return _osm_resolved_motor_access(registry, tags)


def osm_lane_access_evidence(registry: dict[str, Any], tags: dict[str, Any]) -> dict[str, Any]:
    """Expose the lane-scoped projection of the shared motor-access resolution."""

    value = _osm_resolved_motor_access(registry, tags)
    return {
        "active_lane_count": value["active_lane_count"],
        "conditional_evidence": value["conditional_access_evidence"],
        "covers_all_lanes": value["covers_all_lanes"],
        "decision": value["decision"],
        "effective_mode": value["effective_mode"],
        "raw_evidence": {
            key: raw for key, raw in value["raw_access_evidence"].items() if ":lanes" in key
        },
        "reason": value["reason"],
        "resolved_active_lanes": value["resolved_active_lanes"],
        "token_evidence": value["token_evidence"],
    }


def deduplicate_osm_elements(elements: Iterable[dict[str, Any]]) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    """Deduplicate exact OSM type/ID records and fail on inconsistent repeats."""

    unique: dict[tuple[str, int], tuple[str, dict[str, Any]]] = {}
    occurrences: Counter[str] = Counter()
    occurrence_counts: Counter[tuple[str, int]] = Counter()
    for element in elements:
        element_type = str(element.get("type") or "")
        if element_type not in ("node", "way", "relation"):
            raise GateError(f"Unsupported OSM element type in bounded response: {element_type!r}")
        try:
            element_id = int(element["id"])
        except (KeyError, TypeError, ValueError) as error:
            raise GateError(f"OSM {element_type} has no integer ID") from error
        key = (element_type, element_id)
        encoded = compact_json(element)
        occurrences[element_type] += 1
        occurrence_counts[key] += 1
        previous = unique.get(key)
        if previous is not None and previous[0] != encoded:
            raise GateError(f"Inconsistent duplicate OSM {element_type}/{element_id}")
        unique.setdefault(key, (encoded, element))
    type_order = {"node": 0, "way": 1, "relation": 2}
    records = [
        unique[key][1]
        for key in sorted(unique, key=lambda value: (type_order[value[0]], value[1]))
    ]
    unique_counts = Counter(key[0] for key in unique)
    duplicate_ids = Counter(key[0] for key, count in occurrence_counts.items() if count > 1)
    duplicate_occurrences = Counter()
    for (element_type, _), count in occurrence_counts.items():
        duplicate_occurrences[element_type] += count - 1
    return records, {
        "duplicate_id_count_by_type": dict(sorted(duplicate_ids.items())),
        "duplicate_occurrence_count_by_type": dict(sorted(duplicate_occurrences.items())),
        "occurrence_count_by_type": dict(sorted(occurrences.items())),
        "unique_count_by_type": dict(sorted(unique_counts.items())),
    }


def osm_eligibility(registry: dict[str, Any], tags: dict[str, Any]) -> dict[str, Any]:
    """Apply the explicit OSM highway/access crosswalk and expose every decision."""

    model = registry["authority_model"]
    raw_highway = normalized_label(tags.get("highway"))
    rule = (model["osm_highway_crosswalk"].get(raw_highway) or {}).copy()
    if not rule:
        rule = {
            "decision": "UNKNOWN_BLOCKING",
            "reason": "UNKNOWN_OSM_HIGHWAY_CLASS",
        }
    whole_access = osm_motor_access_evidence(registry, tags)
    lane_access = osm_lane_access_evidence(registry, tags)
    direction_evidence = osm_direction_evidence(registry, tags)
    lane_counts = osm_lane_count_components(
        tags, direction_value=direction_evidence["value"]
    )
    access_rule = {
        "decision": whole_access["decision"],
        "reason": whole_access["reason"],
    }
    effective_key = whole_access["effective_key"]
    effective_value = whole_access["effective_value"]
    if lane_counts["reason"] == "INCONSISTENT_ONEWAY_AND_DIRECTIONAL_LANE_COUNTS":
        access_rule = {
            "decision": "UNKNOWN_BLOCKING",
            "reason": lane_counts["reason"],
        }
        effective_key = None
        effective_value = None

    highway_decision = rule["decision"]
    highway_reason = rule.get("reason")
    if highway_decision == "SERVICE_RULE":
        raw_service = normalized_label(tags.get("service"))
        service_rule = (model["osm_service_crosswalk"].get(raw_service) or {}).copy()
        if access_rule["decision"] == "UNKNOWN_BLOCKING":
            rule = {
                "decision": "UNKNOWN_BLOCKING",
                "reason": access_rule["reason"],
            }
        elif access_rule["decision"] in ("EXCLUDE", "RESTRICTED"):
            rule = {
                "decision": "EXCLUDED",
                "reason": access_rule["reason"],
            }
        elif service_rule:
            rule = service_rule
        else:
            rule = {
                "decision": "UNKNOWN_BLOCKING",
                "reason": (
                    "UNSPECIFIED_SERVICE_ROAD_USE"
                    if not raw_service
                    else "UNKNOWN_OSM_SERVICE_SUBTYPE"
                ),
            }
    elif highway_decision == "ELIGIBLE":
        if access_rule["decision"] == "UNKNOWN_BLOCKING":
            rule["decision"] = "UNKNOWN_BLOCKING"
            rule["reason"] = access_rule["reason"]
        elif access_rule["decision"] in ("EXCLUDE", "RESTRICTED"):
            rule["decision"] = "EXCLUDED"
            rule["reason"] = access_rule["reason"]
        elif direction_evidence["value"] == "UNKNOWN":
            rule["decision"] = "UNKNOWN_BLOCKING"
            rule["reason"] = direction_evidence["reason"]
    classification = rule.get("road_class")
    if classification is not None and classification not in ROAD_CLASSES:
        raise GateError(f"OSM crosswalk produced unsupported road class {classification!r}")
    return {
        "access_evidence": whole_access["normalized_access_evidence"],
        "conditional_access_evidence": whole_access["conditional_access_evidence"],
        "effective_access_decision": access_rule["decision"],
        "effective_access_key": effective_key,
        "effective_access_reason": access_rule["reason"],
        "effective_access_value": effective_value,
        "lane_access_evidence": lane_access,
        "lane_count_evidence": lane_counts,
        "oneway_evidence": direction_evidence,
        "decision": rule["decision"],
        "highway_class": classification,
        "highway_decision_before_access": highway_decision,
        "highway_reason_before_access": highway_reason,
        "raw_highway": raw_highway,
        "raw_access_evidence": whole_access["raw_access_evidence"],
        "raw_service": normalized_label(tags.get("service")),
        "reason": rule.get("reason"),
    }


def source_class_evidence(registry: dict[str, Any], record: dict[str, Any]) -> dict[str, Any]:
    """Return an explicit class/eligibility decision for a normalized source record."""

    source_id = record["source_id"]
    if source_id == registry["osm"]["id"]:
        eligibility = record.get("eligibility") or osm_eligibility(
            registry, record.get("tags") or {}
        )
        classification = eligibility.get("highway_class")
        return {
            "confidence": "DECLARED_ODBL_FALLBACK",
            "decision": eligibility["decision"],
            "field": "highway",
            "raw_value": (record.get("tags") or {}).get("highway"),
            "reason": eligibility.get("reason"),
            "road_class": classification,
            "source_id": source_id,
        }
    config = registry["authority_model"]["class_evidence"].get(source_id)
    if not config:
        return {
            "confidence": "NONE",
            "decision": "UNKNOWN_BLOCKING",
            "field": None,
            "raw_value": None,
            "reason": "NO_SOURCE_CLASS_CROSSWALK",
            "road_class": None,
            "source_id": source_id,
        }
    if config.get("fixed_value"):
        return {
            "confidence": config["confidence"],
            "decision": "ELIGIBLE",
            "field": "SOURCE_LAYER_ROLE",
            "raw_value": config["fixed_value"],
            "reason": None,
            "road_class": config["fixed_value"],
            "source_id": source_id,
        }
    field = config["field"]
    raw_value = (record.get("attributes") or {}).get(field)
    label = normalized_label(raw_value)
    values = {normalized_label(key): value for key, value in (config.get("values") or {}).items()}
    exclusions = {
        normalized_label(key): value for key, value in (config.get("excluded_values") or {}).items()
    }
    if label in values:
        classification = values[label]
        if classification not in ROAD_CLASSES:
            raise GateError(f"{source_id} crosswalk produced unsupported class {classification!r}")
        decision, reason = "ELIGIBLE", None
    elif label in exclusions:
        classification = None
        decision, reason = "EXCLUDED", exclusions[label]
    else:
        classification = None
        decision = "UNKNOWN_BLOCKING"
        reason = "UNMAPPED_SOURCE_CLASS_VALUE" if label else "MISSING_SOURCE_CLASS_VALUE"
    return {
        "confidence": config["confidence"],
        "decision": decision,
        "field": field,
        "raw_value": raw_value,
        "reason": reason,
        "road_class": classification,
        "source_id": source_id,
    }


def lane_count_evidence(registry: dict[str, Any], record: dict[str, Any]) -> dict[str, Any]:
    """Extract direct lane-count evidence only; never infer lanes from road width."""

    source_id = record["source_id"]
    configs = {
        value["source_id"]: value for value in registry["authority_model"]["lane_evidence"]
    }
    config = configs.get(source_id)
    if config is None:
        return {
            "confidence": "UNKNOWN_FAIL_CLOSED",
            "lane_count": None,
            "lane_count_authority": None,
            "priority": None,
            "reason": "NO_DIRECT_LANE_COUNT_FIELD",
            "source_id": source_id,
        }
    if config.get("field"):
        raw = (record.get("attributes") or {}).get(config["field"])
        count = strict_positive_int(raw)
        authority = f"{source_id}.{config['field']}"
        reason = None if count is not None else "MISSING_OR_INVALID_DECLARED_LANE_COUNT"
    else:
        tags = record.get("tags") or {}
        parsed = osm_lane_count_components(
            tags,
            direction_value=osm_direction_evidence(registry, tags)["value"],
        )
        count = parsed["lane_count"]
        authority = (
            f"{source_id}.{parsed['lane_count_authority']}"
            if parsed["lane_count_authority"] is not None
            else None
        )
        reason = parsed["reason"]
        eligibility = record.get("eligibility") or osm_eligibility(registry, tags)
        if eligibility["decision"] != "ELIGIBLE":
            count = None
            authority = None
            reason = f"INELIGIBLE_OR_UNRESOLVED_OSM_ACCESS:{eligibility['reason']}"
    return {
        "confidence": config["confidence"] if count is not None else "UNKNOWN_FAIL_CLOSED",
        **(
            {
                "conditional_lane_components": parsed["conditional_lane_components"],
                "declared_lane_components": parsed["declared_lane_components"],
                "lane_access_evidence": eligibility["lane_access_evidence"],
            }
            if not config.get("field")
            else {}
        ),
        "lane_count": count,
        "lane_count_authority": authority if count is not None else None,
        "priority": int(config["priority"]),
        "reason": reason,
        "source_id": source_id,
    }


def local_transformer(registry: dict[str, Any]) -> pyproj.Transformer:
    product = pyproj.CRS.from_proj4(registry["coordinate_contract"]["projection"])
    return pyproj.Transformer.from_crs(pyproj.CRS.from_epsg(4326), product, always_xy=True)


def transform_line(points: Iterable[Iterable[float]], transformer: pyproj.Transformer) -> LineString | None:
    coordinates: list[tuple[float, float]] = []
    for point in points:
        values = list(point)
        if len(values) < 2:
            continue
        x, z = transformer.transform(float(values[0]), float(values[1]))
        coordinate = (float(x), float(z))
        if not coordinates or coordinate != coordinates[-1]:
            coordinates.append(coordinate)
    if len(coordinates) < 2:
        return None
    line = LineString(coordinates)
    return line if line.length > 0.0 else None


def iter_arcgis_features(external_run: Path, source: dict[str, Any]) -> Iterable[dict[str, Any]]:
    for page in source.get("pages") or []:
        path = external_run / page["receipt"]["response"]["path"]
        value = json.loads(path.read_text(encoding="utf-8"))
        yield from value.get("features") or []


def atomic_write_jsonl(path: Path, records: Iterable[dict[str, Any]]) -> None:
    values = list(records)
    payload = (("\n".join(compact_json(record) for record in values) + "\n") if values else "").encode(
        "utf-8"
    )
    atomic_write_bytes(path, payload)


def normalize_arcgis(
    registry: dict[str, Any],
    freeze_source: dict[str, Any],
    external_run: Path,
    output_run: Path | None = None,
) -> dict[str, Any]:
    """Normalize every frozen ArcGIS OID and write a one-row-per-OID disposition ledger."""

    if freeze_source.get("metadata_only"):
        return {
            "disposition_counts": {},
            "feature_count": 0,
            "geometry_count": 0,
            "id": freeze_source["id"],
            "road_length_meters": 0.0,
            "unrepresented_feature_count": 0,
        }
    artifact_run = output_run or external_run
    transformer = local_transformer(registry)
    bounds = registry["coordinate_contract"]["bounds_xz_meters"]
    clip = box(bounds[0], bounds[1], bounds[2], bounds[3])
    output = artifact_run / "normalized" / f"{freeze_source['id']}.geojsonl"
    ledger_output = artifact_run / "intermediates/record_dispositions" / f"{freeze_source['id']}.jsonl"
    object_id = freeze_source["object_id_field"]
    expected_ids = [str(value) for page in freeze_source.get("pages") or [] for value in page["requested_ids"]]
    if len(expected_ids) != len(set(expected_ids)):
        raise GateError(f"{freeze_source['id']} freeze pages contain duplicate requested OIDs")
    feature_count = geometry_count = 0
    total_length = 0.0
    records: list[dict[str, Any]] = []
    dispositions: list[dict[str, Any]] = []
    observed_crosswalk: Counter[tuple[str, str, str, str]] = Counter()
    actual_ids: list[str] = []
    road_source_ids = {
        value["source_id"]
        for value in registry["authority_model"]["canonical_centerline_precedence"]
        if value["source_id"] != registry["osm"]["id"]
    }
    reference_roles = {
        value["source_id"]: value["reference_role"]
        for value in registry["authority_model"]["reference_sources"]
    }
    for feature in iter_arcgis_features(external_run, freeze_source):
        feature_count += 1
        attributes = feature.get("attributes") or {}
        raw_id = attributes.get(object_id)
        if raw_id is None:
            raise GateError(f"{freeze_source['id']} feature lacks {object_id}")
        source_record_id = str(raw_id)
        actual_ids.append(source_record_id)
        geometry = feature.get("geometry") or {}
        local_geometries: list[Any] = []
        raw_geometry_kind = "MISSING"
        saw_transformable_geometry = False
        if geometry.get("paths"):
            raw_geometry_kind = "paths"
            for raw_path in geometry["paths"]:
                line = transform_line(raw_path, transformer)
                if line is None:
                    continue
                saw_transformable_geometry = True
                clipped = line.intersection(clip)
                if clipped.is_empty:
                    continue
                candidates = [clipped] if clipped.geom_type == "LineString" else list(getattr(clipped, "geoms", []))
                local_geometries.extend(
                    value for value in candidates if value.geom_type == "LineString" and value.length > 0.0
                )
        elif geometry.get("rings"):
            raw_geometry_kind = "rings"
            for ring in geometry["rings"]:
                line = transform_line(ring, transformer)
                if line is None or not line.is_ring:
                    continue
                polygon = Polygon(line)
                if not polygon.is_valid or polygon.is_empty:
                    continue
                saw_transformable_geometry = True
                clipped = polygon.intersection(clip)
                if clipped.is_empty:
                    continue
                candidates = [clipped] if clipped.geom_type == "Polygon" else list(getattr(clipped, "geoms", []))
                local_geometries.extend(
                    value for value in candidates if value.geom_type == "Polygon" and value.area > 0.0
                )
        elif geometry.get("x") is not None and geometry.get("y") is not None:
            raw_geometry_kind = "point"
            x, z = transformer.transform(float(geometry["x"]), float(geometry["y"]))
            point = Point(x, z)
            saw_transformable_geometry = True
            if clip.covers(point):
                local_geometries.append(point)
        geometry_constraint_reason: str | None = None
        if freeze_source["id"] in road_source_ids and any(
            value.geom_type != "LineString" for value in local_geometries
        ):
            geometry_constraint_reason = "ROAD_SOURCE_NON_LINE_GEOMETRY"
            local_geometries = []
        normalized_part_ids: list[str] = []
        for part_index, local_geometry in enumerate(local_geometries):
            geometry_count += 1
            if local_geometry.geom_type == "LineString":
                total_length += local_geometry.length
            part_id = f"{source_record_id}:{part_index}"
            normalized_part_ids.append(part_id)
            records.append(
                {
                    "attributes": attributes,
                    "geometry": mapping(local_geometry),
                    "license_partition": freeze_source["license_partition"],
                    "role": freeze_source["role"],
                    "source_id": freeze_source["id"],
                    "source_object_id_field": object_id,
                    "source_record_id": source_record_id,
                    "source_record_part": part_index,
                }
            )
        if geometry_constraint_reason:
            disposition, reason = "EXCLUDED", geometry_constraint_reason
        elif local_geometries:
            disposition, reason = "NORMALIZED", None
        elif not geometry:
            disposition, reason = "EXCLUDED", "MISSING_GEOMETRY"
        elif saw_transformable_geometry:
            disposition, reason = "EXCLUDED", "OUTSIDE_AEQD_SQUARE"
        else:
            disposition, reason = "EXCLUDED", "INVALID_OR_UNSUPPORTED_GEOMETRY"
        if freeze_source["id"] in road_source_ids:
            eligibility = source_class_evidence(
                registry,
                {
                    "attributes": attributes,
                    "source_id": freeze_source["id"],
                },
            )
        elif freeze_source["id"] in reference_roles:
            eligibility = {
                "confidence": "CORROBORATION_ONLY",
                "decision": "REFERENCE_ONLY",
                "field": None,
                "raw_value": None,
                "reason": reference_roles[freeze_source["id"]],
                "road_class": None,
                "source_id": freeze_source["id"],
            }
        else:
            eligibility = {
                "confidence": "NOT_APPLICABLE",
                "decision": "OUT_OF_SCOPE",
                "field": None,
                "raw_value": None,
                "reason": "NON_ROAD_AUTHORITY_RECORD",
                "road_class": None,
                "source_id": freeze_source["id"],
            }
        observed_crosswalk[
            (
                normalized_label(eligibility.get("raw_value")),
                eligibility["decision"],
                str(eligibility.get("road_class") or ""),
                str(eligibility.get("reason") or ""),
            )
        ] += 1
        dispositions.append(
            {
                "attributes": attributes,
                "disposition": disposition,
                "eligibility": eligibility,
                "geometry": (
                    mapping(unary_union(local_geometries)) if local_geometries else None
                ),
                "geometry_length_meters": round(
                    sum(
                        value.length
                        for value in local_geometries
                        if value.geom_type == "LineString"
                    ),
                    6,
                ),
                "geometry_part_count": len(local_geometries),
                "motor_road_disposition": eligibility["decision"],
                "license_partition": freeze_source["license_partition"],
                "normalized_part_ids": normalized_part_ids,
                "raw_geometry_kind": raw_geometry_kind,
                "reason": reason,
                "source_id": freeze_source["id"],
                "source_record_id": source_record_id,
            }
        )
    if len(actual_ids) != len(set(actual_ids)):
        duplicates = sorted(key for key, count in Counter(actual_ids).items() if count > 1)
        raise GateError(f"{freeze_source['id']} raw pages contain duplicate OIDs: {duplicates[:10]}")
    if set(actual_ids) != set(expected_ids):
        raise GateError(f"{freeze_source['id']} normalization OID set differs from frozen page inventory")
    if feature_count != int(freeze_source["feature_count"]):
        raise GateError(
            f"{freeze_source['id']} normalization saw {feature_count} features; freeze requires {freeze_source['feature_count']}"
        )
    records.sort(key=lambda value: (value["source_record_id"], value["source_record_part"]))
    dispositions.sort(key=lambda value: value["source_record_id"])
    atomic_write_jsonl(output, records)
    atomic_write_jsonl(ledger_output, dispositions)
    disposition_counts = Counter(value["reason"] or value["disposition"] for value in dispositions)
    unrepresented = sum(not value["normalized_part_ids"] for value in dispositions)
    return {
        "disposition_counts": dict(sorted(disposition_counts.items())),
        "disposition_ledger": file_record(ledger_output, artifact_run),
        "disposition_record_count": len(dispositions),
        "feature_count": feature_count,
        "geometry_count": geometry_count,
        "id": freeze_source["id"],
        "normalized": file_record(output, artifact_run),
        "observed_eligibility_crosswalk": [
            {
                "decision": decision,
                "observed_record_count": count,
                "raw_value": raw_value,
                "reason": reason,
                "road_class": classification or None,
            }
            for (raw_value, decision, classification, reason), count in sorted(observed_crosswalk.items())
        ],
        "road_length_meters": round(total_length, 3),
        "unrepresented_feature_count": unrepresented,
    }


def normalize_osm(
    registry: dict[str, Any],
    freeze_source: dict[str, Any],
    external_run: Path,
    output_run: Path | None = None,
) -> dict[str, Any]:
    artifact_run = output_run or external_run
    document = json.loads((external_run / freeze_source["raw"]["path"]).read_text(encoding="utf-8"))
    raw_elements = document.get("elements") or []
    elements, duplicate_summary = deduplicate_osm_elements(raw_elements)
    way_occurrences = Counter(
        int(element["id"]) for element in raw_elements if element.get("type") == "way"
    )
    nodes = {
        int(element["id"]): (float(element["lon"]), float(element["lat"]))
        for element in elements
        if element.get("type") == "node" and element.get("lon") is not None and element.get("lat") is not None
    }
    transformer = local_transformer(registry)
    bounds = registry["coordinate_contract"]["bounds_xz_meters"]
    clip = box(bounds[0], bounds[1], bounds[2], bounds[3])
    records: list[dict[str, Any]] = []
    missing: list[dict[str, Any]] = []
    dispositions: list[dict[str, Any]] = []
    class_lengths: defaultdict[str, float] = defaultdict(float)
    class_counts: Counter[str] = Counter()
    disposition_counts: Counter[str] = Counter()
    observed_crosswalk: Counter[tuple[str, str, str, str]] = Counter()
    duplicate_occurrence_removed_centerline_meters = 0.0
    restriction_relations = 0
    eligible_way_count = 0
    unknown_blocking_count = 0
    for element in elements:
        if element.get("type") == "relation":
            if (element.get("tags") or {}).get("type") in ("restriction", "connectivity"):
                restriction_relations += 1
            continue
        if element.get("type") != "way":
            continue
        tags = element.get("tags") or {}
        eligibility = osm_eligibility(registry, tags)
        classification = eligibility["highway_class"]
        if eligibility["decision"] == "ELIGIBLE":
            eligible_way_count += 1
        elif eligibility["decision"] == "UNKNOWN_BLOCKING":
            unknown_blocking_count += 1
        access_signature = ";".join(
            f"{key}={value}" for key, value in sorted(eligibility["access_evidence"].items())
        )
        observed_crosswalk[
            (
                eligibility["raw_highway"],
                access_signature,
                eligibility["decision"],
                eligibility["reason"] or classification or "",
            )
        ] += 1
        node_ids = [int(value) for value in element.get("nodes") or []]
        absent = [value for value in node_ids if value not in nodes]
        geometry_parts: list[LineString] = []
        geometry_reason: str | None = None
        if absent:
            geometry_reason = "MISSING_REFERENCED_NODES"
        else:
            line = transform_line((nodes[value] for value in node_ids), transformer)
            if line is None:
                geometry_reason = "DEGENERATE_GEOMETRY"
            else:
                clipped = line.intersection(clip)
                candidates = [clipped] if clipped.geom_type == "LineString" else list(getattr(clipped, "geoms", []))
                geometry_parts = [
                    part for part in candidates if part.geom_type == "LineString" and part.length > 0.0
                ]
                if not geometry_parts:
                    geometry_reason = "OUTSIDE_AEQD_SQUARE"
        source_record_id = f"way/{element['id']}"
        normalized_part_ids: list[str] = []
        retained_tag_names = {
                "access",
                "access:conditional",
                "bicycle",
                "bridge",
                "bridge:conditional",
                "bridge:structure",
                "covered",
                "ele",
                "foot",
                "highway",
                "incline",
                "junction",
                "junction:conditional",
                "junction:ref",
                "lanes",
                "lanes:backward",
                "lanes:backward:conditional",
                "lanes:both_ways",
                "lanes:both_ways:conditional",
                "lanes:conditional",
                "lanes:forward",
                "lanes:forward:conditional",
                "layer",
                "layer:conditional",
                "level",
                "maxspeed",
                "motor_vehicle",
                "motor_vehicle:conditional",
                "motorcar",
                "motorcar:conditional",
                "name",
                "oneway",
                "oneway:conditional",
                "ref",
                "service",
                "surface",
                "turn:lanes",
                "turn:lanes:backward",
                "turn:lanes:forward",
                "tunnel",
                "tunnel:conditional",
                "tunnel:name",
                "vehicle",
                "vehicle:conditional",
        }
        for mode in registry["authority_model"]["osm_access_key_precedence"]:
            retained_tag_names.update(
                {
                    mode,
                    f"{mode}:conditional",
                    f"{mode}:forward",
                    f"{mode}:forward:conditional",
                    f"{mode}:backward",
                    f"{mode}:backward:conditional",
                    f"{mode}:lanes",
                    f"{mode}:lanes:conditional",
                    f"{mode}:lanes:forward",
                    f"{mode}:lanes:forward:conditional",
                    f"{mode}:lanes:backward",
                    f"{mode}:lanes:backward:conditional",
                    f"{mode}:lanes:both_ways",
                    f"{mode}:lanes:both_ways:conditional",
                }
            )
        for key in registry["authority_model"]["osm_oneway_key_precedence"]:
            retained_tag_names.update({key, f"{key}:conditional"})
        retained_tags = {
            key: str(tags[key])
            for key in sorted(retained_tag_names)
            if key in tags
        }
        direct_lane_evidence = lane_count_evidence(
            registry,
            {
                "eligibility": eligibility,
                "source_id": freeze_source["id"],
                "tags": retained_tags,
            },
        )
        if eligibility["decision"] == "ELIGIBLE" and geometry_reason is None:
            assert classification in ROAD_CLASSES
            duplicate_occurrence_removed_centerline_meters += sum(
                part.length for part in geometry_parts
            ) * (way_occurrences[int(element["id"])] - 1)
            for part_index, part in enumerate(geometry_parts):
                class_lengths[classification] += part.length
                class_counts[classification] += 1
                part_id = f"{source_record_id}:{part_index}"
                normalized_part_ids.append(part_id)
                record = {
                    "eligibility": eligibility,
                    "geometry": mapping(part),
                    "highway_class": classification,
                    "license_partition": freeze_source["license_partition"],
                    "role": "road_fallback",
                    "source_id": freeze_source["id"],
                    "source_record_id": source_record_id,
                    "source_record_part": part_index,
                    "tags": retained_tags,
                    "version": element.get("version"),
                    "timestamp": element.get("timestamp"),
                    "changeset": element.get("changeset"),
                }
                record["lane_evidence"] = direct_lane_evidence
                records.append(record)
        disposition_reason = geometry_reason or eligibility["reason"]
        if eligibility["decision"] == "ELIGIBLE" and geometry_reason is None:
            disposition = "NORMALIZED_ELIGIBLE"
            disposition_reason = None
        elif eligibility["decision"] == "UNKNOWN_BLOCKING":
            disposition = "UNKNOWN_BLOCKING"
        else:
            disposition = "EXCLUDED"
        disposition_counts[disposition_reason or disposition] += 1
        display_geometry = mapping(unary_union(geometry_parts)) if geometry_parts else None
        dispositions.append(
            {
                "access_evidence": eligibility["access_evidence"],
                "conditional_access_evidence": eligibility["conditional_access_evidence"],
                "disposition": disposition,
                "effective_access_decision": eligibility["effective_access_decision"],
                "effective_access_key": eligibility["effective_access_key"],
                "effective_access_reason": eligibility["effective_access_reason"],
                "effective_access_value": eligibility["effective_access_value"],
                "lane_access_evidence": eligibility["lane_access_evidence"],
                "lane_count_evidence": direct_lane_evidence,
                "lane_evidence_disposition": (
                    "KNOWN"
                    if direct_lane_evidence["lane_count"] is not None
                    else "UNKNOWN_BLOCKING"
                    if eligibility["decision"] in ("ELIGIBLE", "UNKNOWN_BLOCKING")
                    else "NOT_APPLICABLE_EXCLUDED"
                ),
                "missing_referenced_node_count": len(absent),
                "oneway_evidence": osm_direction_evidence(registry, retained_tags),
                "geometry": display_geometry,
                "geometry_length_meters": round(sum(value.length for value in geometry_parts), 6),
                "geometry_part_count": len(geometry_parts),
                "highway_class": classification,
                "license_partition": freeze_source["license_partition"],
                "motor_road_disposition": eligibility["decision"],
                "normalized_part_ids": normalized_part_ids,
                "explicit_universe_member_id": (
                    f"excluded:osm:{source_record_id}"
                    if display_geometry is not None and disposition != "NORMALIZED_ELIGIBLE"
                    else None
                ),
                "raw_access_evidence": eligibility["raw_access_evidence"],
                "raw_highway": eligibility["raw_highway"],
                "raw_service": eligibility["raw_service"],
                "raw_occurrence_count": way_occurrences[int(element["id"])],
                "reason": disposition_reason,
                "source_id": freeze_source["id"],
                "source_record_id": source_record_id,
                "tags": retained_tags,
            }
        )
        if disposition in ("UNKNOWN_BLOCKING",) or (
            eligibility["decision"] == "ELIGIBLE" and geometry_reason is not None
        ):
            missing.append(
                {
                    "geometry": display_geometry,
                    "properties": {
                        "highway_class": classification,
                        "missing_node_count": len(absent),
                        "observation_kind": "OSM_NORMALIZATION_FAILURE",
                        "raw_highway": eligibility["raw_highway"],
                        "reason": disposition_reason,
                        "source_id": f"osm:{source_record_id}",
                    },
                    "type": "Feature",
                }
            )
    records.sort(key=lambda value: (value["source_record_id"], value["source_record_part"]))
    if len(records) != len(
        {(value["source_record_id"], value["source_record_part"]) for value in records}
    ):
        raise GateError("OSM normalized output contains duplicate way/part IDs after raw-ID deduplication")
    dispositions.sort(key=lambda value: value["source_record_id"])
    output = artifact_run / "normalized/osm_bounded_roads.geojsonl"
    disposition_output = artifact_run / "intermediates/record_dispositions/osm_bounded_roads.jsonl"
    duplicate_output = artifact_run / "intermediates/osm_duplicate_id_audit.json"
    atomic_write_jsonl(output, records)
    atomic_write_jsonl(disposition_output, dispositions)
    duplicate_ids = [
        {"occurrence_count": count, "source_record_id": f"way/{way_id}"}
        for way_id, count in sorted(way_occurrences.items())
        if count > 1
    ]
    atomic_write_json(
        duplicate_output,
        {
            **duplicate_summary,
            "duplicate_way_ids": duplicate_ids,
            "schema": "questflight.traffic-v2-osm-duplicate-id-audit.v1",
            "status": "PASS_EXACT_DUPLICATES_DEDUPLICATED",
        },
    )
    crosswalk = [
        {
            "access_signature": access,
            "decision": decision,
            "observed_way_count": count,
            "raw_highway": highway,
            "reason_or_class": reason,
        }
        for (highway, access, decision, reason), count in sorted(observed_crosswalk.items())
    ]
    return {
        "class_feature_parts": dict(sorted(class_counts.items())),
        "class_length_meters": {key: round(value, 3) for key, value in sorted(class_lengths.items())},
        "disposition_counts": dict(sorted(disposition_counts.items())),
        "disposition_ledger": file_record(disposition_output, artifact_run),
        "disposition_record_count": len(dispositions),
        "duplicate_id_audit": file_record(duplicate_output, artifact_run),
        "duplicate_occurrence_removed_centerline_meters": round(
            duplicate_occurrence_removed_centerline_meters, 3
        ),
        "duplicate_summary": duplicate_summary,
        "eligible_way_count": eligible_way_count,
        "feature_count": len(records),
        "id": freeze_source["id"],
        "missing_features": missing,
        "normalized": file_record(output, artifact_run),
        "observed_eligibility_crosswalk": crosswalk,
        "raw_way_occurrence_count": duplicate_summary["occurrence_count_by_type"].get("way", 0),
        "normalized_id_part_uniqueness_status": "PASS",
        "normalized_unique_id_part_count": len(records),
        "restriction_relation_count": restriction_relations,
        "unique_way_count": duplicate_summary["unique_count_by_type"].get("way", 0),
        "unknown_blocking_way_count": unknown_blocking_count,
    }


def read_jsonl(path: Path) -> Iterable[dict[str, Any]]:
    with path.open("r", encoding="utf-8") as handle:
        for line in handle:
            if line.strip():
                yield json.loads(line)


def record_geometry(record: dict[str, Any]) -> Any:
    geometry = shape(record["geometry"])
    if geometry.is_empty or not geometry.is_valid:
        raise GateError(
            f"Invalid normalized geometry for {record.get('source_id')}:{record.get('source_record_id')}"
        )
    return geometry


def correspondence_identity_tokens(registry: dict[str, Any], record: dict[str, Any]) -> set[str]:
    """Return source-declared road identity tokens used only to prove conflation."""

    fields = registry["authority_model"]["correspondence_identity_fields"].get(
        record["source_id"], []
    )
    directed_fields = {
        pair[key]
        for pair in (
            registry["authority_model"].get("correspondence_directed_identity_field_pairs") or {}
        ).get(record["source_id"], [])
        for key in ("from_field", "to_field")
    }
    values = record.get("tags") if record["source_id"] == registry["osm"]["id"] else record.get("attributes")
    values = values or {}
    tokens: set[str] = set()
    for field in fields:
        if field in directed_fields:
            continue
        label = normalized_label(values.get(field))
        if label:
            tokens.add(label)
    return tokens


def correspondence_identity_evidence(
    registry: dict[str, Any], record: dict[str, Any]
) -> list[dict[str, Any]]:
    """Preserve field-qualified identity evidence, including directed ramp endpoints."""

    fields = registry["authority_model"]["correspondence_identity_fields"].get(
        record["source_id"], []
    )
    values = record.get("tags") if record["source_id"] == registry["osm"]["id"] else record.get("attributes")
    values = values or {}
    roles = (registry["authority_model"].get("correspondence_identity_field_roles") or {}).get(
        record["source_id"], {}
    )
    return [
        {
            "field": field,
            "role": roles.get(field),
            "usable_for_correspondence": bool(roles.get(field)),
            "value": normalized_label(values.get(field)),
        }
        for field in fields
        if normalized_label(values.get(field))
    ]


def correspondence_directed_identity_evidence(
    registry: dict[str, Any], record: dict[str, Any]
) -> list[dict[str, Any]]:
    """Preserve ordered source identity endpoints without collapsing them into tokens."""

    pairs = (
        registry["authority_model"].get("correspondence_directed_identity_field_pairs") or {}
    ).get(record["source_id"], [])
    values = record.get("tags") if record["source_id"] == registry["osm"]["id"] else record.get("attributes")
    values = values or {}
    evidence: list[dict[str, Any]] = []
    for pair in pairs:
        from_field = pair["from_field"]
        to_field = pair["to_field"]
        from_value = normalized_label(values.get(from_field))
        to_value = normalized_label(values.get(to_field))
        evidence.append(
            {
                "from_field": from_field,
                "from_value": from_value or None,
                "to_field": to_field,
                "to_value": to_value or None,
                "usable_for_correspondence": bool(from_value and to_value),
            }
        )
    return evidence


def _semantic_value(
    raw_value: Any, values: dict[str, Any], *, missing_reason: str
) -> dict[str, Any]:
    raw = "" if raw_value is None else str(raw_value).strip()
    config = values.get(raw)
    if config is None:
        return {
            "confidence": "UNKNOWN_FAIL_CLOSED",
            "raw_value": raw_value,
            "reason": missing_reason if not raw else "UNMAPPED_SEMANTIC_VALUE",
            "usable_for_correspondence": False,
            "value": "UNKNOWN",
        }
    return {"raw_value": raw_value, "reason": None, **config}


def raw_road_kind_evidence(
    registry: dict[str, Any], record: dict[str, Any]
) -> dict[str, Any]:
    """Preserve raw ramp/link identity independently of collapsed road class."""

    source_id = record["source_id"]
    config = (registry["authority_model"].get("road_kind_evidence") or {}).get(
        source_id
    ) or {}
    if config.get("fixed_value"):
        return {
            "authority": f"{source_id}.layer_role",
            "confidence": config["confidence"],
            "raw_value": config["fixed_value"],
            "reason": None,
            "usable_for_correspondence": True,
            "value": config["fixed_value"],
        }
    field = config.get("field")
    raw_value = (
        (record.get("tags") or {}).get(field)
        if source_id == registry["osm"]["id"]
        else (record.get("attributes") or {}).get(field)
    )
    normalized = normalized_label(raw_value)
    link_suffix = str(config.get("link_suffix") or "")
    if link_suffix and normalized.endswith(link_suffix):
        value = "RAMP_OR_LINK"
    elif normalized in set(config.get("ramp_or_link_values") or []):
        value = "RAMP_OR_LINK"
    elif normalized in set(config.get("mainline_values") or []):
        value = "MAINLINE"
    else:
        value = "UNKNOWN"
    return {
        "authority": f"{source_id}.{field}" if field else None,
        "confidence": config.get("confidence", "UNKNOWN_FAIL_CLOSED"),
        "raw_value": raw_value,
        "reason": None if value != "UNKNOWN" else "UNKNOWN_RAW_ROAD_KIND",
        "usable_for_correspondence": value != "UNKNOWN",
        "value": value,
    }


def road_semantic_evidence(registry: dict[str, Any], record: dict[str, Any]) -> dict[str, Any]:
    """Return source-separated identity, direction, carriageway, access, and grade evidence."""

    source_id = record["source_id"]
    attributes = record.get("attributes") or {}
    tags = record.get("tags") or {}
    config = (registry["authority_model"].get("correspondence_semantics") or {}).get(source_id) or {}
    identity_fields = correspondence_identity_evidence(registry, record)
    identity = {
        "directed_pairs": correspondence_directed_identity_evidence(registry, record),
        "field_values": identity_fields,
        "tokens": sorted(correspondence_identity_tokens(registry, record)),
    }

    if source_id == registry["osm"]["id"]:
        direction = osm_direction_evidence(registry, tags)
        carriageway = {
            "authority": "osm.oneway_derived_carriageway_role",
            "confidence": direction["confidence"],
            "raw_value": direction["raw_value"],
            "reason": direction["reason"],
            "usable_for_correspondence": bool(direction["usable_for_correspondence"]),
            "value": (
                "DIRECTIONAL_CARRIAGEWAY"
                if direction["value"] in ("FORWARD", "REVERSE")
                else "SINGLE_CENTERLINE"
                if direction["value"] == "BIDIRECTIONAL"
                else "UNKNOWN"
            ),
        }
        eligibility = record.get("eligibility") or osm_eligibility(registry, tags)
        access = {
            "authority": "osm.effective_motor_access",
            "confidence": "DECLARED_ODBL_TAG_OR_DEFAULT",
            "decision": eligibility["effective_access_decision"],
            "effective_key": eligibility["effective_access_key"],
            "effective_value": eligibility["effective_access_value"],
            "lane_access": eligibility["lane_access_evidence"],
            "raw_keys": eligibility["raw_access_evidence"],
            "reason": eligibility["effective_access_reason"],
            "usable_for_correspondence": eligibility["decision"] == "ELIGIBLE",
        }

        def binary_osm_grade(key: str) -> dict[str, Any]:
            conditional = tags.get(f"{key}:conditional")
            if conditional is not None and str(conditional).strip():
                return {
                    "authority": f"osm.{key}:conditional",
                    "confidence": "UNPARSED_CONDITIONAL_FAIL_CLOSED",
                    "raw_value": conditional,
                    "reason": f"CONDITIONAL_{key.upper()}_UNRESOLVED",
                    "usable_for_correspondence": False,
                    "value": "UNKNOWN",
                }
            raw = normalized_label(tags.get(key))
            if not raw or raw in ("no", "false", "0"):
                value = "NO"
            elif raw in ("yes", "true", "1", "boardwalk", "building_passage"):
                value = "YES"
            else:
                value = "UNKNOWN"
            return {
                "authority": f"osm.{key}",
                "raw_value": tags.get(key),
                "usable_for_correspondence": value != "UNKNOWN",
                "value": value,
            }

        raw_layer = tags.get("layer")
        conditional_layer = tags.get("layer:conditional")
        if conditional_layer is not None and str(conditional_layer).strip():
            layer_value: int | str = "UNKNOWN"
            layer_usable = False
            layer_authority = "osm.layer:conditional"
            layer_reason = "CONDITIONAL_LAYER_UNRESOLVED"
        else:
            try:
                layer_value = int(str(raw_layer).strip()) if raw_layer is not None else 0
                layer_usable = True
                layer_reason = None
            except (TypeError, ValueError):
                layer_value = "UNKNOWN"
                layer_usable = False
                layer_reason = "UNMAPPED_OSM_LAYER_VALUE"
            layer_authority = "osm.layer"
        conditional_junction = tags.get("junction:conditional")
        junction_raw = normalized_label(tags.get("junction"))
        junction_usable = not (
            conditional_junction is not None and str(conditional_junction).strip()
        )
        grade = {
            "bridge": binary_osm_grade("bridge"),
            "endpoint_elevations": {
                "authority": None,
                "usable_for_correspondence": True,
                "value": None,
            },
            "junction": {
                "authority": "osm.junction" if junction_usable else "osm.junction:conditional",
                "raw_value": tags.get("junction") if junction_usable else conditional_junction,
                "reason": None if junction_usable else "CONDITIONAL_JUNCTION_UNRESOLVED",
                "usable_for_correspondence": junction_usable,
                "value": (junction_raw or "NONE") if junction_usable else "UNKNOWN",
            },
            "layer": {
                "authority": layer_authority,
                "raw_value": raw_layer if layer_usable else conditional_layer or raw_layer,
                "reason": layer_reason,
                "usable_for_correspondence": layer_usable,
                "value": layer_value,
            },
            "tunnel": binary_osm_grade("tunnel"),
        }
    else:
        direction_field = config.get("direction_field")
        direction = _semantic_value(
            attributes.get(direction_field) if direction_field else None,
            config.get("direction_values") or {},
            missing_reason="MISSING_SOURCE_DIRECTION_VALUE",
        )
        direction["authority"] = f"{source_id}.{direction_field}" if direction_field else None
        carriageway_field = config.get("carriageway_field")
        carriageway = _semantic_value(
            attributes.get(carriageway_field) if carriageway_field else None,
            config.get("carriageway_values") or {},
            missing_reason="MISSING_SOURCE_CARRIAGEWAY_VALUE",
        )
        carriageway["authority"] = (
            f"{source_id}.{carriageway_field}" if carriageway_field else None
        )
        class_value = source_class_evidence(registry, record)
        access = {
            "authority": f"{source_id}.motor_road_eligibility",
            "confidence": class_value["confidence"],
            "decision": (
                "ALLOW"
                if class_value["decision"] == "ELIGIBLE"
                else "EXCLUDE"
                if class_value["decision"] == "EXCLUDED"
                else "UNKNOWN_BLOCKING"
            ),
            "effective_key": class_value.get("field"),
            "effective_value": class_value.get("raw_value"),
            "raw_keys": {},
            "reason": class_value.get("reason"),
            "usable_for_correspondence": class_value["decision"] == "ELIGIBLE",
        }
        from_field = config.get("from_elevation_field")
        to_field = config.get("to_elevation_field")
        raw_from = attributes.get(from_field) if from_field else None
        raw_to = attributes.get(to_field) if to_field else None
        elevations_usable = raw_from is not None and raw_to is not None
        def configured_grade_component(key: str) -> dict[str, Any]:
            field = config.get(f"{key}_field")
            if not field:
                return {
                    "authority": None,
                    "raw_value": None,
                    "usable_for_correspondence": False,
                    "value": "UNKNOWN",
                }
            value = _semantic_value(
                attributes.get(field),
                config.get(f"{key}_values") or {},
                missing_reason=f"MISSING_SOURCE_{key.upper()}_VALUE",
            )
            value["authority"] = f"{source_id}.{field}"
            return value

        grade = {
            "bridge": configured_grade_component("bridge"),
            "endpoint_elevations": {
                "authority": (
                    f"{source_id}.{from_field}+{to_field}" if from_field and to_field else None
                ),
                "usable_for_correspondence": elevations_usable,
                "value": [raw_from, raw_to] if elevations_usable else None,
            },
            "junction": configured_grade_component("junction"),
            "layer": configured_grade_component("layer"),
            "tunnel": configured_grade_component("tunnel"),
        }
    return {
        "access": access,
        "carriageway": carriageway,
        "direction": direction,
        "grade": grade,
        "identity": identity,
        "road_kind": raw_road_kind_evidence(registry, record),
        "source_id": source_id,
        "source_record_id": record.get("source_record_id"),
        "source_record_part": record.get("source_record_part"),
    }


def _line_orientation(target: LineString, candidate: LineString) -> str:
    """Return candidate digitization direction relative to the target line."""

    start = candidate.project(Point(target.coords[0]))
    end = candidate.project(Point(target.coords[-1]))
    if abs(end - start) <= 1e-6:
        return "UNKNOWN"
    return "SAME" if end > start else "REVERSED"


def road_correspondence(
    registry: dict[str, Any],
    target_record: dict[str, Any],
    candidate_record: dict[str, Any],
    target: LineString,
    candidate: LineString,
    *,
    allow_missing_evidence_for: str | None = None,
    minimum_ratio: float,
) -> dict[str, Any]:
    """The sole cross-source road correspondence predicate used by the source gate."""

    tolerance = float(registry["authority_model"]["automatic_conflation_max_distance_meters"])
    supported = target.intersection(candidate.buffer(tolerance))
    supported_length = supported.length
    ratio = supported_length / target.length if target.length > 0.0 else 0.0
    target_semantics = road_semantic_evidence(registry, target_record)
    candidate_semantics = road_semantic_evidence(registry, candidate_record)
    target_tokens = set(target_semantics["identity"]["tokens"])
    candidate_tokens = set(candidate_semantics["identity"]["tokens"])
    shared_tokens = sorted(target_tokens & candidate_tokens)
    role_compatibility = registry["authority_model"].get(
        "correspondence_identity_role_compatibility"
    ) or {}
    strong_roles = set(
        registry["authority_model"].get("correspondence_identity_strong_roles") or []
    )
    field_matches: list[dict[str, Any]] = []
    strong_field_matches: list[dict[str, Any]] = []
    for target_field in target_semantics["identity"]["field_values"]:
        for candidate_field in candidate_semantics["identity"]["field_values"]:
            target_role = target_field.get("role")
            candidate_role = candidate_field.get("role")
            if (
                not target_field.get("usable_for_correspondence")
                or not candidate_field.get("usable_for_correspondence")
                or target_field["value"] != candidate_field["value"]
                or candidate_role not in set(role_compatibility.get(target_role) or [])
            ):
                continue
            match = {
                "candidate_field": candidate_field["field"],
                "candidate_role": candidate_role,
                "target_field": target_field["field"],
                "target_role": target_role,
                "value": target_field["value"],
            }
            field_matches.append(match)
            if target_role in strong_roles and candidate_role in strong_roles:
                strong_field_matches.append(match)
    orientation = _line_orientation(target, candidate)
    target_pairs = target_semantics["identity"]["directed_pairs"]
    candidate_pairs = candidate_semantics["identity"]["directed_pairs"]
    directed_pair_matches: list[dict[str, Any]] = []
    directed_identity_required = bool(target_pairs or candidate_pairs)
    directed_identity_compatible = not directed_identity_required
    if target_pairs and candidate_pairs and orientation != "UNKNOWN":
        for target_pair in target_pairs:
            if not target_pair["usable_for_correspondence"]:
                continue
            for candidate_pair in candidate_pairs:
                if not candidate_pair["usable_for_correspondence"]:
                    continue
                candidate_from = candidate_pair["from_value"]
                candidate_to = candidate_pair["to_value"]
                if orientation == "REVERSED":
                    candidate_from, candidate_to = candidate_to, candidate_from
                if (
                    target_pair["from_value"] == candidate_from
                    and target_pair["to_value"] == candidate_to
                ):
                    directed_pair_matches.append(
                        {
                            "candidate_from_value_in_target_orientation": candidate_from,
                            "candidate_to_value_in_target_orientation": candidate_to,
                            "target_from_value": target_pair["from_value"],
                            "target_to_value": target_pair["to_value"],
                        }
                    )
        directed_identity_compatible = bool(directed_pair_matches)
    identity_compatible = bool(strong_field_matches or directed_pair_matches) and directed_identity_compatible
    checks: dict[str, dict[str, Any]] = {
        "geometry": {
            "compatible": ratio >= minimum_ratio,
            "match_ratio": round(ratio, 6),
            "reason": None if ratio >= minimum_ratio else "INSUFFICIENT_GEOMETRIC_OVERLAP",
        },
        "identity": {
            "compatible": identity_compatible,
            "field_matches": field_matches,
            "strong_field_matches": strong_field_matches,
            "directed_pair_matches": directed_pair_matches,
            "directed_pair_required": directed_identity_required,
            "reason": (
                None
                if identity_compatible
                else "DIRECTED_IDENTITY_NOT_COMPARABLE_OR_INCOMPATIBLE"
                if directed_identity_required and not directed_identity_compatible
                else "NO_SHARED_DECLARED_IDENTITY"
                if not shared_tokens
                else "NO_COMPATIBLE_STRONG_DECLARED_IDENTITY"
            ),
            "shared_tokens": shared_tokens,
            "target_directed_pairs": target_pairs,
            "candidate_directed_pairs": candidate_pairs,
        },
    }
    target_kind = target_semantics["road_kind"]
    candidate_kind = candidate_semantics["road_kind"]
    kind_usable = bool(target_kind.get("usable_for_correspondence")) and bool(
        candidate_kind.get("usable_for_correspondence")
    )
    kind_compatible = kind_usable and target_kind.get("value") == candidate_kind.get(
        "value"
    )
    checks["road_kind"] = {
        "candidate_raw_value": candidate_kind.get("raw_value"),
        "candidate_value": candidate_kind.get("value"),
        "compatible": kind_compatible,
        "reason": (
            None
            if kind_compatible
            else "UNKNOWN_RAW_ROAD_KIND"
            if not kind_usable
            else "INCOMPATIBLE_MAINLINE_RAMP_LINK_KIND"
        ),
        "target_raw_value": target_kind.get("raw_value"),
        "target_value": target_kind.get("value"),
    }

    def direct_evidence_check(
        target_value: Any,
        candidate_value: Any,
        *,
        evidence_field: str,
        label: str,
    ) -> dict[str, Any]:
        if target_value is not None and candidate_value is not None:
            compatible = target_value == candidate_value
            relation = "EXACT_MATCH" if compatible else "MISMATCH"
        elif (
            allow_missing_evidence_for == evidence_field
            and target_value is None
            and candidate_value is not None
        ):
            compatible = True
            relation = "FILL_MISSING_ONLY"
        else:
            compatible = False
            relation = "UNKNOWN"
        return {
            "candidate_value": candidate_value,
            "compatible": compatible,
            "reason": None if compatible else f"UNKNOWN_OR_INCOMPATIBLE_{label}",
            "relation": relation,
            "target_value": target_value,
        }

    target_class = source_class_evidence(registry, target_record)
    candidate_class = source_class_evidence(registry, candidate_record)
    checks["road_class"] = direct_evidence_check(
        target_class.get("road_class"),
        candidate_class.get("road_class"),
        evidence_field="road_class",
        label="ROAD_CLASS",
    )
    checks["road_class"]["candidate_evidence"] = candidate_class
    checks["road_class"]["target_evidence"] = target_class
    target_lanes = lane_count_evidence(registry, target_record)
    candidate_lanes = lane_count_evidence(registry, candidate_record)
    checks["lane_count"] = direct_evidence_check(
        target_lanes.get("lane_count"),
        candidate_lanes.get("lane_count"),
        evidence_field="lane_count",
        label="DIRECT_LANE_COUNT",
    )
    checks["lane_count"]["candidate_evidence"] = candidate_lanes
    checks["lane_count"]["target_evidence"] = target_lanes
    target_direction = target_semantics["direction"]
    candidate_direction = candidate_semantics["direction"]
    direction_usable = bool(target_direction.get("usable_for_correspondence")) and bool(
        candidate_direction.get("usable_for_correspondence")
    ) and orientation != "UNKNOWN"
    adjusted_candidate_direction = candidate_direction.get("value")
    if orientation == "REVERSED":
        adjusted_candidate_direction = {
            "FORWARD": "REVERSE",
            "REVERSE": "FORWARD",
        }.get(adjusted_candidate_direction, adjusted_candidate_direction)
    target_direction_value = target_direction.get("value")
    direction_compatible = direction_usable and target_direction_value == adjusted_candidate_direction
    checks["direction"] = {
        "candidate_direction_in_target_orientation": adjusted_candidate_direction,
        "compatible": direction_compatible,
        "orientation": orientation,
        "reason": (
            None
            if direction_compatible
            else "UNKNOWN_DIRECTION_SEMANTICS"
            if not direction_usable
            else "INCOMPATIBLE_DIRECTION_SEMANTICS"
        ),
        "target_direction": target_direction_value,
    }
    target_carriageway = target_semantics["carriageway"]
    candidate_carriageway = candidate_semantics["carriageway"]
    carriageway_usable = bool(target_carriageway.get("usable_for_correspondence")) and bool(
        candidate_carriageway.get("usable_for_correspondence")
    )
    carriageway_compatible = carriageway_usable and target_carriageway.get("value") == candidate_carriageway.get("value")
    checks["carriageway"] = {
        "compatible": carriageway_compatible,
        "reason": (
            None
            if carriageway_compatible
            else "UNKNOWN_CARRIAGEWAY_SEMANTICS"
            if not carriageway_usable
            else "INCOMPATIBLE_CARRIAGEWAY_SEMANTICS"
        ),
        "target_value": target_carriageway.get("value"),
        "candidate_value": candidate_carriageway.get("value"),
    }
    target_access = target_semantics["access"]
    candidate_access = candidate_semantics["access"]
    access_usable = bool(target_access.get("usable_for_correspondence")) and bool(
        candidate_access.get("usable_for_correspondence")
    )
    access_compatible = access_usable and target_access.get("decision") == "ALLOW" and candidate_access.get("decision") == "ALLOW"
    checks["access"] = {
        "compatible": access_compatible,
        "reason": None if access_compatible else "UNKNOWN_OR_INCOMPATIBLE_MOTOR_ACCESS",
        "target_decision": target_access.get("decision"),
        "candidate_decision": candidate_access.get("decision"),
    }
    grade_checks: dict[str, Any] = {}
    for key in ("bridge", "tunnel", "layer", "junction"):
        target_value = target_semantics["grade"][key]
        candidate_value = candidate_semantics["grade"][key]
        usable = bool(target_value.get("usable_for_correspondence")) and bool(
            candidate_value.get("usable_for_correspondence")
        )
        compatible = usable and target_value.get("value") == candidate_value.get("value")
        grade_checks[key] = {
            "compatible": compatible,
            "reason": (
                None
                if compatible
                else f"UNKNOWN_{key.upper()}_SEMANTICS"
                if not usable
                else f"INCOMPATIBLE_{key.upper()}_SEMANTICS"
            ),
            "target_value": target_value.get("value"),
            "candidate_value": candidate_value.get("value"),
        }
    target_elevation = target_semantics["grade"]["endpoint_elevations"]
    candidate_elevation = candidate_semantics["grade"]["endpoint_elevations"]
    if target_elevation.get("value") is None and candidate_elevation.get("value") is None:
        elevation_compatible = True
        elevation_reason = None
    elif (
        target_elevation.get("value") is None
        or candidate_elevation.get("value") is None
        or not target_elevation.get("usable_for_correspondence")
        or not candidate_elevation.get("usable_for_correspondence")
    ):
        elevation_compatible = False
        elevation_reason = "UNKNOWN_ENDPOINT_GRADE_SEMANTICS"
    else:
        candidate_levels = list(candidate_elevation["value"])
        if orientation == "REVERSED":
            candidate_levels.reverse()
        elevation_compatible = list(target_elevation["value"]) == candidate_levels
        elevation_reason = None if elevation_compatible else "INCOMPATIBLE_ENDPOINT_GRADE_SEMANTICS"
    grade_checks["endpoint_elevations"] = {
        "compatible": elevation_compatible,
        "reason": elevation_reason,
    }
    grade_compatible = all(value["compatible"] for value in grade_checks.values())
    checks["grade"] = {
        "compatible": grade_compatible,
        "components": grade_checks,
        "reason": None if grade_compatible else "UNKNOWN_OR_INCOMPATIBLE_GRADE_SEMANTICS",
    }
    compatible = all(value["compatible"] for value in checks.values())
    return {
        "candidate_semantics": candidate_semantics,
        "checks": checks,
        "match_ratio": round(ratio, 6),
        "proof_status": "PROVEN" if compatible else "NOT_PROVEN_FAIL_CLOSED",
        "supported_geometry": mapping(supported) if not supported.is_empty else None,
        "supported_length_meters": round(supported_length, 6),
        "target_semantics": target_semantics,
    }


def same_source_road_semantic_signature(record: dict[str, Any]) -> str:
    """Bind exact-geometry deduplication to all retained road semantics."""

    if record.get("tags") is not None:
        payload = record.get("tags") or {}
    else:
        payload = dict(record.get("attributes") or {})
        object_id_field = record.get("source_object_id_field")
        if object_id_field:
            payload.pop(object_id_field, None)
    return compact_json(
        {
            "payload": payload,
            "role": record.get("role"),
            "source_id": record.get("source_id"),
        }
    )


def line_records_for_source(
    external_run: Path, normalized_by_id: dict[str, dict[str, Any]], source_id: str
) -> list[dict[str, Any]]:
    summary = normalized_by_id.get(source_id) or {}
    frozen_file = summary.get("normalized")
    if not frozen_file:
        return []
    records: list[dict[str, Any]] = []
    for record in read_jsonl(external_run / frozen_file["path"]):
        geometry = record_geometry(record)
        if geometry.geom_type == "LineString" and geometry.length > 0.0:
            value = dict(record)
            value["_geometry"] = geometry
            records.append(value)
    return records


def spatial_evidence(
    target: LineString,
    candidates: list[dict[str, Any]],
    tree: STRtree | None,
    *,
    registry: dict[str, Any] | None = None,
    require_correspondence: bool = False,
    tolerance: float,
    target_record: dict[str, Any] | None = None,
    minimum_ratio: float,
    require_different_source: bool = False,
    require_single_source_record: bool = False,
    value_key: str,
) -> tuple[dict[str, Any] | None, list[dict[str, Any]]]:
    if tree is None or not candidates:
        return None, []
    matches: list[dict[str, Any]] = []
    semantic_ambiguities: list[dict[str, Any]] = []
    for index in tree.query(target.buffer(tolerance)):
        candidate = candidates[int(index)]
        if require_correspondence:
            if registry is None or target_record is None or "_source_record" not in candidate:
                raise GateError("Spatial evidence correspondence check lacks source records")
            if require_different_source and candidate["source_id"] == target_record["source_id"]:
                continue
            assessment = road_correspondence(
                registry,
                target_record,
                candidate["_source_record"],
                target,
                candidate["_geometry"],
                allow_missing_evidence_for=value_key,
                minimum_ratio=minimum_ratio,
            )
            if assessment["proof_status"] != "PROVEN":
                if assessment["checks"]["geometry"]["compatible"] and assessment["checks"]["identity"]["compatible"]:
                    semantic_ambiguities.append(
                        {
                            "correspondence": assessment,
                            "source_id": candidate["source_id"],
                            "source_record_id": candidate["source_record_id"],
                            "source_record_part": candidate["source_record_part"],
                        }
                    )
                continue
        covered = target.intersection(candidate["_geometry"].buffer(tolerance)).length
        ratio = covered / target.length if target.length > 0.0 else 0.0
        if ratio >= minimum_ratio:
            matches.append(
                {
                    **candidate,
                    **({"correspondence": assessment} if require_correspondence else {}),
                    "match_ratio": ratio,
                }
            )
    if not matches:
        return None, semantic_ambiguities
    matches.sort(
        key=lambda value: (
            int(value["evidence"]["priority"]),
            -float(value["match_ratio"]),
            value["source_id"],
            value["source_record_id"],
            int(value["source_record_part"]),
        )
    )
    best_priority = int(matches[0]["evidence"]["priority"])
    top = [value for value in matches if int(value["evidence"]["priority"]) == best_priority]
    distinct = {value["evidence"].get(value_key) for value in top}
    distinct_records = {
        (value["source_id"], value["source_record_id"])
        for value in top
    }
    if len(distinct) > 1 or (require_single_source_record and len(distinct_records) > 1):
        return None, [
            {
                "match_ratio": round(float(value["match_ratio"]), 6),
                "source_id": value["source_id"],
                "source_record_id": value["source_record_id"],
                value_key: value["evidence"].get(value_key),
            }
            for value in top
        ]
    if semantic_ambiguities:
        return None, semantic_ambiguities
    return matches[0], []


def reference_representation_result(
    registry: dict[str, Any],
    observation_record: dict[str, Any],
    observation_key: tuple[str, str, int],
    geometry: Any,
    candidates: list[dict[str, Any]],
    source_record_lookup: dict[tuple[str, str, int], dict[str, Any]],
    excluded_source_id: str | None = None,
) -> dict[str, Any]:
    """Recompute one reference's canonical subspan coverage from source records."""

    if not candidates or geometry.geom_type != "LineString" or geometry.length <= 0.0:
        return {
            "ambiguities": [],
            "match_ratio": 0.0,
            "matched_records": [],
            "reason": "REFERENCE_LACKS_ROAD_CORRESPONDENCE_SEMANTICS",
        }
    proven_geometries: list[Any] = []
    matched_records: list[dict[str, Any]] = []
    ambiguities: list[dict[str, Any]] = []
    minimum_partial_ratio = min(
        1.0, max(1e-9, 0.001 / max(geometry.length, 0.001))
    )
    for candidate in candidates:
        if candidate.get("source_id") == excluded_source_id:
            continue
        canonical_id = str(candidate.get("canonical_segment_id") or "")
        if not canonical_id:
            raise GateError("Canonical reference candidate lacks an exact segment ID")
        candidate_key = (
            str(candidate.get("source_id") or ""),
            str(candidate.get("source_record_id") or ""),
            int(candidate.get("source_record_part", -1)),
        )
        candidate_record = source_record_lookup.get(candidate_key)
        if candidate_record is None:
            raise GateError(
                "Canonical reference candidate is not bound to a normalized source record"
            )
        candidate_geometry = shape(candidate["geometry"])
        if candidate_key == observation_key:
            supported = geometry.intersection(
                candidate_geometry.buffer(
                    float(
                        registry["authority_model"][
                            "automatic_conflation_max_distance_meters"
                        ]
                    )
                )
            )
            if not supported.is_empty and supported.length > 0.0:
                proven_geometries.append(supported)
                matched_records.append(
                    {
                        "canonical_segment_id": canonical_id,
                        "correspondence": {
                            "match_ratio": round(supported.length / geometry.length, 6),
                            "proof_status": "EXACT_SOURCE_RECORD_IDENTITY",
                            "supported_length_meters": round(supported.length, 6),
                        },
                        "source_id": candidate["source_id"],
                        "source_record_id": candidate["source_record_id"],
                        "source_record_part": candidate["source_record_part"],
                    }
                )
            continue
        assessment = road_correspondence(
            registry,
            observation_record,
            candidate_record,
            geometry,
            candidate_geometry,
            minimum_ratio=minimum_partial_ratio,
        )
        if assessment["proof_status"] == "PROVEN":
            supported_geometry = assessment.get("supported_geometry")
            if supported_geometry is None:
                raise GateError("Proven reference correspondence lacks supported geometry")
            supported = shape(supported_geometry)
            if supported.is_empty or supported.length <= 0.0:
                raise GateError("Proven reference correspondence has no positive subspan")
            proven_geometries.append(supported)
            matched_records.append(
                {
                    "canonical_segment_id": canonical_id,
                    "correspondence": assessment,
                    "source_id": candidate["source_id"],
                    "source_record_id": candidate["source_record_id"],
                    "source_record_part": candidate["source_record_part"],
                }
            )
        elif (
            assessment["checks"]["geometry"]["compatible"]
            and assessment["checks"]["identity"]["compatible"]
        ):
            ambiguities.append(
                {
                    "canonical_segment_id": canonical_id,
                    "correspondence": assessment,
                    "source_id": candidate["source_id"],
                    "source_record_id": candidate["source_record_id"],
                    "source_record_part": candidate["source_record_part"],
                }
            )
    matched_records.sort(
        key=lambda value: (
            value["canonical_segment_id"],
            value["source_id"],
            value["source_record_id"],
            int(value["source_record_part"]),
        )
    )
    ambiguities.sort(key=compact_json)
    candidate_union = unary_union(proven_geometries) if proven_geometries else None
    ratio = (
        geometry.intersection(candidate_union).length / geometry.length
        if candidate_union is not None
        else 0.0
    )
    return {
        "ambiguities": ambiguities,
        "match_ratio": ratio,
        "matched_records": matched_records,
        "reason": None if ratio > 0.0 else "NO_PROVEN_SHARED_CORRESPONDENCE",
    }


def reference_observation_value(
    registry: dict[str, Any], config: dict[str, Any], record: dict[str, Any]
) -> dict[str, Any]:
    """Build the exact semantic inventory row for one normalized reference."""

    source_id = config["source_id"]
    if record.get("source_id") != source_id:
        raise GateError("Reference configuration/source-record ID mismatch")
    if config["reference_role"] == "road_centerline":
        class_value = source_class_evidence(registry, record)
        lane_value = lane_count_evidence(registry, record)
        motor_road_disposition = class_value["decision"]
    else:
        class_value = {
            "confidence": "CORROBORATION_ONLY",
            "decision": "REFERENCE_ONLY",
            "field": None,
            "raw_value": None,
            "reason": config["reference_role"],
            "road_class": None,
            "source_id": source_id,
        }
        lane_value = {
            "confidence": "NOT_APPLICABLE",
            "lane_count": None,
            "lane_count_authority": None,
            "priority": None,
            "reason": "REFERENCE_ONLY_NO_LANE_DENOMINATOR",
            "source_id": source_id,
        }
        motor_road_disposition = "REFERENCE_ONLY"
    return {
        "_source_record": record,
        "class_evidence": class_value,
        "correspondence_identity_evidence": correspondence_identity_evidence(
            registry, record
        ),
        "correspondence_identity_tokens": sorted(
            correspondence_identity_tokens(registry, record)
        ),
        "geometry": record["geometry"],
        "lane_evidence": lane_value,
        "license_partition": record["license_partition"],
        "motor_road_disposition": motor_road_disposition,
        "reference_observation_id": (
            f"reference:{source_id}:{record['source_record_id']}:"
            f"{record['source_record_part']}"
        ),
        "reference_role": config["reference_role"],
        "source_id": source_id,
        "source_record_id": record["source_record_id"],
        "source_record_part": record["source_record_part"],
        "semantic_evidence": road_semantic_evidence(registry, record),
    }


def reference_representation_ledger_value(
    registry: dict[str, Any],
    observation: dict[str, Any],
    canonical_candidates: list[dict[str, Any]],
    source_record_lookup: dict[tuple[str, str, int], dict[str, Any]],
    canonical_tree: Any | None = None,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool, bool]:
    """Derive the complete reference decision row from live source/canonical bytes."""

    geometry = shape(observation["geometry"])
    observation_key = (
        observation["source_id"],
        observation["source_record_id"],
        int(observation["source_record_part"]),
    )
    nearby_candidates = canonical_candidates
    if canonical_tree is not None:
        query_distance = max(
            float(
                registry["authority_model"][
                    "automatic_conflation_max_distance_meters"
                ]
            ),
            float(registry["authority_model"]["conflation_tolerance_meters"]),
        )
        nearby_indices = sorted(
            int(index) for index in canonical_tree.query(geometry.buffer(query_distance))
        )
        nearby_candidates = [canonical_candidates[index] for index in nearby_indices]
    authority_result = reference_representation_result(
        registry,
        observation["_source_record"],
        observation_key,
        geometry,
        nearby_candidates,
        source_record_lookup,
    )
    corroboration_result = reference_representation_result(
        registry,
        observation["_source_record"],
        observation_key,
        geometry,
        nearby_candidates,
        source_record_lookup,
        excluded_source_id=observation["source_id"],
    )
    minimum_ratio = float(
        registry["authority_model"]["minimum_reference_coverage_ratio"]
    )
    authority_ratio = float(authority_result["match_ratio"])
    corroboration_ratio = float(corroboration_result["match_ratio"])
    matched = (
        bool(authority_result["matched_records"])
        and authority_ratio > 0.0
        and authority_ratio >= minimum_ratio
        and not authority_result["ambiguities"]
    )
    corroborated = (
        bool(corroboration_result["matched_records"])
        and corroboration_ratio > 0.0
        and corroboration_ratio >= minimum_ratio
        and not corroboration_result["ambiguities"]
    )
    eligibility = observation["motor_road_disposition"]
    if eligibility == "EXCLUDED":
        disposition = "EXCLUDED_FROM_ELIGIBLE_REFERENCE_DENOMINATOR"
    elif eligibility == "UNKNOWN_BLOCKING":
        disposition = "UNRESOLVED_MOTOR_ROAD_ELIGIBILITY"
    elif eligibility == "REFERENCE_ONLY":
        disposition = (
            "CORROBORATING_REFERENCE_MATCHED"
            if matched
            else "CORROBORATING_REFERENCE_UNMATCHED"
        )
    else:
        disposition = (
            "ELIGIBLE_REFERENCE_MATCHED"
            if matched
            else "ELIGIBLE_REFERENCE_UNMATCHED"
        )
    return (
        {
            "class_evidence": observation["class_evidence"],
            "decision": disposition,
            "different_source_corroboration_match_ratio": round(
                corroboration_ratio, 6
            ),
            "different_source_corroboration_matched": corroborated,
            "different_source_corroboration_records": corroboration_result[
                "matched_records"
            ],
            "license_partition": observation["license_partition"],
            "authority_inclusive_match_ratio": round(authority_ratio, 6),
            "match_ratio": round(authority_ratio, 6),
            "match_scope": "AUTHORITY_INCLUSIVE_CANONICAL_REPRESENTATION",
            "matched_canonical_records": authority_result["matched_records"],
            "motor_road_disposition": eligibility,
            "reference_observation_id": observation["reference_observation_id"],
            "reference_role": observation["reference_role"],
            "source_id": observation["source_id"],
            "source_record_id": observation["source_record_id"],
            "source_record_part": observation["source_record_part"],
            "unresolved_correspondences": authority_result["ambiguities"],
        },
        authority_result,
        corroboration_result,
        matched,
        corroborated,
    )


def build_reference_inventory(
    registry: dict[str, Any],
    external_run: Path,
    normalized_by_id: dict[str, dict[str, Any]],
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    observations: list[dict[str, Any]] = []
    for config in registry["authority_model"]["reference_sources"]:
        source_id = config["source_id"]
        summary = normalized_by_id.get(source_id) or {}
        frozen_file = summary.get("normalized")
        if not frozen_file:
            continue
        for record in read_jsonl(external_run / frozen_file["path"]):
            geometry = record_geometry(record)
            if geometry.geom_type not in ("LineString", "Polygon"):
                continue
            observations.append(reference_observation_value(registry, config, record))
    observations.sort(key=lambda value: value["reference_observation_id"])
    output = external_run / "intermediates/reference_road_inventory.geojsonl"
    atomic_write_jsonl(
        output,
        [
            {key: nested for key, nested in observation.items() if not key.startswith("_")}
            for observation in observations
        ],
    )
    return observations, file_record(output, external_run)


def independent_lane_km_support(
    registry: dict[str, Any],
    canonical_records: list[dict[str, Any]],
    all_source_records: dict[str, list[dict[str, Any]]],
) -> tuple[list[dict[str, Any]], dict[str, Any], list[dict[str, Any]]]:
    """Measure lane-km supported by an independent source with exact direct class/lane evidence."""

    lookup = {
        (record["source_id"], record["source_record_id"], int(record["source_record_part"])): record
        for records in all_source_records.values()
        for record in records
    }
    support_candidates = [
        record for records in all_source_records.values() for record in records
    ]
    support_tree = (
        STRtree([record["_geometry"] for record in support_candidates])
        if support_candidates
        else None
    )
    automatic_distance = float(
        registry["authority_model"]["automatic_conflation_max_distance_meters"]
    )
    progress_started = time.monotonic()
    progress_last_reported = progress_started
    queried_candidate_count = 0
    exact_intersection_count = 0
    by_class: dict[str, dict[str, Any]] = {
        classification: {
            "canonical_lane_kilometer_denominator": 0.0,
            "canonical_segment_count": 0,
            "independently_supported_lane_kilometers": 0.0,
            "supported_segment_count": 0,
            "unknown_lane_centerline_kilometers": 0.0,
            "unknown_lane_segment_count": 0,
            "unresolved_correspondence_centerline_kilometers": 0.0,
            "unresolved_correspondence_segment_count": 0,
        }
        for classification in ROAD_CLASSES
    }
    ledger: list[dict[str, Any]] = []
    conflicts: list[dict[str, Any]] = []
    unknown_class_centerline_meters = 0.0
    unknown_class_segment_count = 0
    for canonical_index, canonical in enumerate(canonical_records, start=1):
        geometry = shape(canonical["geometry"])
        length_meters = geometry.length
        classification = canonical.get("road_class")
        lane_count = canonical.get("lane_count")
        key = (
            canonical["source_id"],
            canonical["source_record_id"],
            int(canonical["source_record_part"]),
        )
        target_record = lookup[key]
        canonical_evidence_sources = {
            str(source_id)
            for source_id in (
                canonical.get("geometry_source_id") or canonical.get("source_id"),
                canonical.get("class_evidence_source_id"),
                canonical.get("lane_evidence_source_id"),
            )
            if source_id
        }
        links: list[dict[str, Any]] = []
        supported_geometries: list[Any] = []
        semantic_ambiguities: list[dict[str, Any]] = []
        incompatible_lane_km_evidence: list[dict[str, Any]] = []
        minimum_partial_ratio = min(1.0, max(1e-9, 0.001 / max(length_meters, 0.001)))
        search_geometry = geometry.buffer(automatic_distance)
        support_indices = (
            sorted(int(index) for index in support_tree.query(search_geometry))
            if support_tree is not None
            else []
        )
        queried_candidate_count += len(support_indices)
        for support_index in support_indices:
            support = support_candidates[support_index]
            if support["source_id"] == canonical.get("geometry_source_id", canonical["source_id"]):
                continue
            support_geometry = support["_geometry"]
            if not search_geometry.intersects(support_geometry):
                continue
            exact_intersection_count += 1
            assessment = road_correspondence(
                registry,
                target_record,
                support,
                geometry,
                support_geometry,
                minimum_ratio=minimum_partial_ratio,
            )
            support_class = source_class_evidence(registry, support)
            support_lane = lane_count_evidence(registry, support)
            support_identity = {
                "support_source_id": support["source_id"],
                "support_source_record_id": support["source_record_id"],
                "support_source_record_part": support["source_record_part"],
            }
            if assessment["proof_status"] == "PROVEN":
                incompatibility_reasons: list[str] = []
                if support["source_id"] in canonical_evidence_sources:
                    incompatibility_reasons.append("CIRCULAR_CANONICAL_EVIDENCE_SOURCE")
                if classification not in ROAD_CLASSES:
                    incompatibility_reasons.append("UNKNOWN_CANONICAL_ROAD_CLASS")
                if support_class["decision"] != "ELIGIBLE" or support_class.get("road_class") is None:
                    incompatibility_reasons.append("UNKNOWN_OR_INELIGIBLE_SUPPORT_ROAD_CLASS")
                elif support_class["road_class"] != classification:
                    incompatibility_reasons.append("MISMATCHED_SUPPORT_ROAD_CLASS")
                if lane_count is None:
                    incompatibility_reasons.append("UNKNOWN_CANONICAL_LANE_COUNT")
                if support_lane["lane_count"] is None:
                    incompatibility_reasons.append("MISSING_OR_INVALID_SUPPORT_LANE_COUNT")
                elif lane_count is not None and int(support_lane["lane_count"]) != int(lane_count):
                    incompatibility_reasons.append("MISMATCHED_SUPPORT_LANE_COUNT")
                if incompatibility_reasons:
                    incompatible_lane_km_evidence.append(
                        {
                            **support_identity,
                            "canonical_evidence_source_ids": sorted(canonical_evidence_sources),
                            "canonical_lane_count": lane_count,
                            "canonical_road_class": classification,
                            "class_evidence": support_class,
                            "correspondence": assessment,
                            "lane_evidence": support_lane,
                            "reason_codes": incompatibility_reasons,
                        }
                    )
                    continue
                supported_geometry = shape(assessment["supported_geometry"])
                if not supported_geometry.is_empty:
                    supported_geometries.append(supported_geometry)
                links.append(
                    {
                        "class_evidence": support_class,
                        "correspondence": assessment,
                        "geometry_license_partition": support["license_partition"],
                        "geometry_source_id": support["source_id"],
                        "lane_evidence": support_lane,
                        "lane_evidence_license_partition": (
                            support["license_partition"]
                            if support_lane["lane_count"] is not None
                            else None
                        ),
                        "selected_canonical_segment_id": canonical["canonical_segment_id"],
                        "support_source_id": support["source_id"],
                        "support_source_record_id": support["source_record_id"],
                        "support_source_record_part": support["source_record_part"],
                    }
                )
            elif assessment["checks"]["geometry"]["compatible"] and assessment["checks"]["identity"]["compatible"]:
                fail_closed_reasons: list[str] = []
                if support["source_id"] in canonical_evidence_sources:
                    fail_closed_reasons.append("CIRCULAR_CANONICAL_EVIDENCE_SOURCE")
                if support_class["decision"] != "ELIGIBLE" or support_class.get("road_class") is None:
                    fail_closed_reasons.append("UNKNOWN_OR_INELIGIBLE_SUPPORT_ROAD_CLASS")
                elif support_class["road_class"] != classification:
                    fail_closed_reasons.append("MISMATCHED_SUPPORT_ROAD_CLASS")
                if support_lane["lane_count"] is None:
                    fail_closed_reasons.append("MISSING_OR_INVALID_SUPPORT_LANE_COUNT")
                elif lane_count is not None and int(support_lane["lane_count"]) != int(lane_count):
                    fail_closed_reasons.append("MISMATCHED_SUPPORT_LANE_COUNT")
                if fail_closed_reasons:
                    incompatible_lane_km_evidence.append(
                        {
                            **support_identity,
                            "canonical_evidence_source_ids": sorted(canonical_evidence_sources),
                            "canonical_lane_count": lane_count,
                            "canonical_road_class": classification,
                            "class_evidence": support_class,
                            "correspondence": assessment,
                            "lane_evidence": support_lane,
                            "reason_codes": fail_closed_reasons,
                        }
                    )
                else:
                    semantic_ambiguities.append(
                        {
                        "correspondence": assessment,
                        **support_identity,
                        }
                    )
        progress_now = time.monotonic()
        if len(canonical_records) >= 100 and (
            canonical_index == len(canonical_records)
            or progress_now - progress_last_reported >= 5.0
        ):
            print(
                compact_json(
                    {
                        "elapsed_seconds": round(progress_now - progress_started, 3),
                        "exact_intersections": exact_intersection_count,
                        "processed": canonical_index,
                        "queried_candidates": queried_candidate_count,
                        "stage": "independent_lane_km_support",
                        "total": len(canonical_records),
                    }
                ),
                flush=True,
            )
            progress_last_reported = progress_now
        ambiguities = semantic_ambiguities + incompatible_lane_km_evidence
        supported_union = unary_union(supported_geometries) if supported_geometries else None
        supported_length = min(
            length_meters,
            geometry.intersection(supported_union).length if supported_union is not None else 0.0,
        )
        if classification not in ROAD_CLASSES:
            unknown_class_centerline_meters += length_meters
            unknown_class_segment_count += 1
            denominator_lane_km = None
            supported_lane_km = None
            status = "UNKNOWN_ROAD_CLASS_FAIL_CLOSED"
        else:
            summary = by_class[classification]
            summary["canonical_segment_count"] += 1
            if lane_count is None:
                summary["unknown_lane_centerline_kilometers"] += length_meters / 1000.0
                summary["unknown_lane_segment_count"] += 1
                denominator_lane_km = None
                supported_lane_km = None
                status = "UNKNOWN_LANE_COUNT_FAIL_CLOSED"
            else:
                denominator_lane_km = length_meters / 1000.0 * int(lane_count)
                supported_lane_km = supported_length / 1000.0 * int(lane_count)
                summary["canonical_lane_kilometer_denominator"] += denominator_lane_km
                summary["independently_supported_lane_kilometers"] += supported_lane_km
                if supported_length >= length_meters - 0.001 and not ambiguities:
                    summary["supported_segment_count"] += 1
                    status = "FULLY_SUPPORTED_BY_DIFFERENT_SOURCE"
                elif supported_length > 0.0:
                    status = "PARTIALLY_SUPPORTED_BY_DIFFERENT_SOURCE"
                else:
                    status = "NO_PROVEN_DIFFERENT_SOURCE_SUPPORT"
                if ambiguities:
                    summary["unresolved_correspondence_centerline_kilometers"] += length_meters / 1000.0
                    summary["unresolved_correspondence_segment_count"] += 1
        if ambiguities:
            if semantic_ambiguities:
                conflicts.append(
                    {
                        "canonical_segment_id": canonical["canonical_segment_id"],
                        "id": "unresolved_independent_support_correspondence",
                        "unresolved_support_records": semantic_ambiguities,
                    }
                )
            if incompatible_lane_km_evidence:
                conflicts.append(
                    {
                        "canonical_segment_id": canonical["canonical_segment_id"],
                        "id": "incompatible_independent_lane_km_evidence",
                        "unresolved_support_records": incompatible_lane_km_evidence,
                    }
                )
        links.sort(
            key=lambda value: (
                value["support_source_id"],
                value["support_source_record_id"],
                int(value["support_source_record_part"]),
            )
        )
        ledger.append(
            {
                "canonical_lane_kilometer_denominator": (
                    round(denominator_lane_km, 9) if denominator_lane_km is not None else None
                ),
                "canonical_segment_id": canonical["canonical_segment_id"],
                "class_evidence_license_partition": canonical.get(
                    "class_evidence_license_partition"
                ),
                "class_evidence_source_id": canonical.get("class_evidence_source_id"),
                "geometry_license_partition": canonical.get("geometry_license_partition"),
                "geometry_source_id": canonical.get("geometry_source_id"),
                "independence_excluded_source_ids": sorted(canonical_evidence_sources),
                "independently_supported_lane_kilometers": (
                    round(supported_lane_km, 9) if supported_lane_km is not None else None
                ),
                "incompatible_lane_km_evidence": incompatible_lane_km_evidence,
                "lane_count": lane_count,
                "lane_evidence_license_partition": canonical.get(
                    "lane_evidence_license_partition"
                ),
                "lane_evidence_source_id": canonical.get("lane_evidence_source_id"),
                "length_meters": round(length_meters, 6),
                "road_class": classification,
                "status": status,
                "support_links": links,
                "supported_length_meters": round(supported_length, 6),
                "semantic_ambiguities": semantic_ambiguities,
                "unresolved_correspondences": ambiguities,
            }
        )
    for summary in by_class.values():
        denominator = summary["canonical_lane_kilometer_denominator"]
        numerator = summary["independently_supported_lane_kilometers"]
        summary["canonical_lane_kilometer_denominator"] = round(denominator, 6)
        summary["independently_supported_lane_kilometers"] = round(numerator, 6)
        summary["unknown_lane_centerline_kilometers"] = round(
            summary["unknown_lane_centerline_kilometers"], 6
        )
        summary["unresolved_correspondence_centerline_kilometers"] = round(
            summary["unresolved_correspondence_centerline_kilometers"], 6
        )
        summary["independent_support_lane_kilometer_ratio"] = (
            round(numerator / denominator, 6)
            if denominator > 0.0
            and summary["unknown_lane_segment_count"] == 0
            and summary["unresolved_correspondence_segment_count"] == 0
            else None
        )
    ledger.sort(key=lambda value: value["canonical_segment_id"])
    conflicts.sort(key=compact_json)
    return ledger, {
        "by_class": by_class,
        "measurement": "unique_canonical_length_km_times_source_priority_lane_count",
        "support_requirement": (
            "different_noncanonical-evidence_source_shared-correspondence_plus_exact-direct-class-and-lane"
        ),
        "unknown_class_centerline_kilometers": round(
            unknown_class_centerline_meters / 1000.0, 6
        ),
        "unknown_class_segment_count": unknown_class_segment_count,
    }, conflicts


def build_canonical_inventory(
    registry: dict[str, Any],
    external_run: Path,
    normalized: list[dict[str, Any]],
) -> dict[str, Any]:
    """Build a source-separated canonical universe and auditable precedence ledger."""

    model = registry["authority_model"]
    normalized_by_id = {value["id"]: value for value in normalized}
    tolerance = float(model["conflation_tolerance_meters"])
    precedence = sorted(
        model["canonical_centerline_precedence"],
        key=lambda value: (int(value["priority"]), value["source_id"]),
    )
    selected: list[dict[str, Any]] = []
    conflation: list[dict[str, Any]] = []
    conflicts: list[dict[str, Any]] = []
    explicit_universe: list[dict[str, Any]] = []
    all_source_records: dict[str, list[dict[str, Any]]] = {
        entry["source_id"]: line_records_for_source(external_run, normalized_by_id, entry["source_id"])
        for entry in precedence
    }
    source_record_lookup = {
        (record["source_id"], record["source_record_id"], int(record["source_record_part"])): record
        for records in all_source_records.values()
        for record in records
    }
    automatic_distance = float(model["automatic_conflation_max_distance_meters"])
    # The previous implementation compared every record with every previously
    # selected segment and performed GEOS buffer/intersection work for each
    # pair.  That is quadratic over the full map.  This deterministic grid is
    # only a conservative bounding-box candidate filter: exact geometric and
    # semantic decisions below are unchanged, and candidate indices remain in
    # original insertion order.
    grid_cell_size = max(64.0, tolerance * 8.0)
    selected_spatial_grid: dict[tuple[int, int], list[int]] = defaultdict(list)

    def grid_cells(
        bounds: tuple[float, float, float, float], padding: float = 0.0
    ) -> Iterable[tuple[int, int]]:
        minimum_x, minimum_y, maximum_x, maximum_y = bounds
        first_x = math.floor((minimum_x - padding) / grid_cell_size)
        last_x = math.floor((maximum_x + padding) / grid_cell_size)
        first_y = math.floor((minimum_y - padding) / grid_cell_size)
        last_y = math.floor((maximum_y + padding) / grid_cell_size)
        for cell_x in range(first_x, last_x + 1):
            for cell_y in range(first_y, last_y + 1):
                yield (cell_x, cell_y)

    for entry in precedence:
        source_id = entry["source_id"]
        priority = int(entry["priority"])
        for record in all_source_records[source_id]:
            original = record["_geometry"]
            class_evidence = source_class_evidence(registry, record)
            if class_evidence["decision"] == "EXCLUDED":
                explicit_universe.append(
                    {
                        "canonical_segment_id": (
                            f"excluded:{source_id}:{record['source_record_id']}:{record['source_record_part']}"
                        ),
                        "confidence": class_evidence["confidence"],
                        "class_evidence_license_partition": record["license_partition"],
                        "class_evidence_source_id": source_id,
                        "eligibility_status_or_exclusion_reason": class_evidence["reason"],
                        "geometry": record["geometry"],
                        "geometry_license_partition": record["license_partition"],
                        "geometry_source_id": source_id,
                        "lane_count": None,
                        "lane_count_authority": None,
                        "lane_evidence_license_partition": None,
                        "lane_evidence_source_id": None,
                        "license_partition": record["license_partition"],
                        "represented_source_disposition": "EXPLICITLY_EXCLUDED_SOURCE_RECORD",
                        "road_class": None,
                        "source_id": source_id,
                        "source_precedence_decision": "EXCLUDED_BEFORE_CONFLATION",
                        "source_record_id": record["source_record_id"],
                        "source_record_part": record["source_record_part"],
                    }
                )
                conflation.append(
                    {
                        "decision": "EXCLUDED_BY_ELIGIBILITY_CROSSWALK",
                        "eligibility": class_evidence,
                        "license_partition": record["license_partition"],
                        "matched_higher_authority_sources": [],
                        "original_length_meters": round(original.length, 6),
                        "reason": class_evidence["reason"],
                        "selected_length_meters": 0.0,
                        "source_id": source_id,
                        "source_priority": priority,
                        "source_record_id": record["source_record_id"],
                        "source_record_part": record["source_record_part"],
                        "suppressed_length_meters": round(original.length, 6),
                    }
                )
                continue
            nearby: list[dict[str, Any]] = []
            proven: list[dict[str, Any]] = []
            exact_same_source: list[dict[str, Any]] = []
            source_precedence_conflicts: list[dict[str, Any]] = []
            candidate_indices: set[int] = set()
            for cell in grid_cells(original.bounds):
                candidate_indices.update(selected_spatial_grid.get(cell, ()))
            for previous_index in sorted(candidate_indices):
                previous = selected[previous_index]
                previous_geometry = previous["_geometry"]
                tolerant_overlap = original.intersection(previous_geometry.buffer(tolerance)).length
                if tolerant_overlap <= 0.001:
                    continue
                candidate: dict[str, Any] = {
                    **previous,
                    "close_match_ratio": round(
                        original.intersection(previous_geometry.buffer(automatic_distance)).length
                        / original.length,
                        6,
                    ),
                    "identity_token_matches": sorted(
                        correspondence_identity_tokens(registry, record)
                        & correspondence_identity_tokens(registry, previous["_source_record"])
                    ),
                    "tolerant_match_ratio": round(tolerant_overlap / original.length, 6),
                }
                nearby.append(candidate)
                same_source = previous["source_id"] == source_id
                exact_geometry = original.equals_exact(previous_geometry, 1e-6)
                if same_source and exact_geometry:
                    exact_same_source.append(candidate)
                elif not same_source:
                    assessment = road_correspondence(
                        registry,
                        record,
                        previous["_source_record"],
                        original,
                        previous_geometry,
                        minimum_ratio=float(model["minimum_reference_coverage_ratio"]),
                    )
                    candidate["correspondence"] = assessment
                    if assessment["proof_status"] == "PROVEN":
                        proven.append(candidate)

            if exact_same_source:
                own_lane = lane_count_evidence(registry, record)
                inconsistent = []
                own_semantics = same_source_road_semantic_signature(record)
                for previous in exact_same_source:
                    previous_class = previous["_class_evidence"].get("road_class")
                    previous_lane = lane_count_evidence(registry, previous["_source_record"])["lane_count"]
                    if previous_class != class_evidence.get("road_class") or (
                        previous_lane is not None
                        and own_lane["lane_count"] is not None
                        and previous_lane != own_lane["lane_count"]
                    ) or same_source_road_semantic_signature(previous["_source_record"]) != own_semantics:
                        inconsistent.append(previous)
                if inconsistent:
                    conflict = {
                        "id": "inconsistent_same_source_geometry_duplicate",
                        "source_id": source_id,
                        "source_record_id": record["source_record_id"],
                        "source_record_part": record["source_record_part"],
                        "conflicting_source_record_ids": sorted(
                            {value["source_record_id"] for value in inconsistent}
                        ),
                    }
                    conflicts.append(conflict)
                    source_precedence_conflicts.append(conflict)
                    proven = []
                else:
                    proven = exact_same_source

            near_union = unary_union([value["_geometry"] for value in nearby]) if nearby else None
            near_ratio = (
                original.intersection(near_union.buffer(tolerance)).length / original.length
                if near_union is not None
                else 0.0
            )
            proven_union = unary_union([value["_geometry"] for value in proven]) if proven else None
            proven_ratio = (
                original.intersection(proven_union.buffer(automatic_distance)).length / original.length
                if proven_union is not None
                else 0.0
            )
            ambiguous = near_ratio >= float(model["minimum_reference_coverage_ratio"]) and proven_ratio < float(
                model["minimum_reference_coverage_ratio"]
            )
            if ambiguous:
                conflict = {
                    "id": "ambiguous_spatial_conflation",
                    "nearby_records": [
                        {
                            "close_match_ratio": value["close_match_ratio"],
                            "identity_token_matches": value["identity_token_matches"],
                            "correspondence": value.get("correspondence"),
                            "source_id": value["source_id"],
                            "source_record_id": value["source_record_id"],
                            "source_record_part": value["source_record_part"],
                            "tolerant_match_ratio": value["tolerant_match_ratio"],
                        }
                        for value in nearby
                    ],
                    "source_id": source_id,
                    "source_record_id": record["source_record_id"],
                    "source_record_part": record["source_record_part"],
                }
                conflicts.append(conflict)
                source_precedence_conflicts.append(conflict)
                proven = []
                proven_union = None
            remaining = (
                original
                if proven_union is None
                else original.difference(proven_union.buffer(automatic_distance))
            )
            parts = [remaining] if remaining.geom_type == "LineString" else list(getattr(remaining, "geoms", []))
            parts = [value for value in parts if value.geom_type == "LineString" and value.length > 0.001]
            selected_length = sum(value.length for value in parts)
            matched_sources = sorted({value["source_id"] for value in proven})
            matched_records = [
                {
                    "source_id": value["source_id"],
                    "source_record_id": value["source_record_id"],
                    "source_record_part": value["source_record_part"],
                    "supported_geometry": (value.get("correspondence") or {}).get("supported_geometry"),
                    "supported_length_meters": (value.get("correspondence") or {}).get("supported_length_meters"),
                }
                for value in proven
            ]
            for part_index, part in enumerate(parts):
                selected_index = len(selected)
                selected.append(
                    {
                        "_class_evidence": class_evidence,
                        "_geometry": part,
                        "_precedence_decision": (
                            "RETAINED_AMBIGUOUS_FAIL_CLOSED"
                            if ambiguous
                            else "SELECTED_ODBL_FALLBACK"
                            if source_id == registry["osm"]["id"]
                            else "SELECTED_OFFICIAL_AUTHORITY"
                        ),
                        "_source_record": record,
                        "canonical_segment_id": (
                            f"canonical:{source_id}:{record['source_record_id']}:"
                            f"{record['source_record_part']}:{part_index}"
                        ),
                        "geometry": mapping(part),
                        "license_partition": record["license_partition"],
                        "source_id": source_id,
                        "source_priority": priority,
                        "source_record_id": record["source_record_id"],
                        "source_record_part": record["source_record_part"],
                    }
                )
                for cell in grid_cells(part.bounds, tolerance):
                    selected_spatial_grid[cell].append(selected_index)
            conflation.append(
                {
                    "decision": (
                        "RETAINED_AMBIGUOUS_FAIL_CLOSED"
                        if ambiguous
                        else "SELECTED_COMPLETE"
                        if abs(selected_length - original.length) <= 0.001
                        else (
                            "SELECTED_PARTIAL"
                            if selected_length > 0.0
                            else (
                                "SUPPRESSED_EXACT_SAME_SOURCE_DUPLICATE"
                                if proven and all(value["source_id"] == source_id for value in proven)
                                else "SUPPRESSED_BY_PROVEN_HIGHER_AUTHORITY_CORRESPONDENCE"
                            )
                        )
                    ),
                    "automatic_conflation_max_distance_meters": automatic_distance,
                    "eligibility": class_evidence,
                    "license_partition": record["license_partition"],
                    "matched_higher_authority_sources": matched_sources,
                    "matched_higher_authority_records": matched_records,
                    "nearby_candidate_count": len(nearby),
                    "original_length_meters": round(original.length, 6),
                    "reason": None,
                    "selected_length_meters": round(selected_length, 6),
                    "source_id": source_id,
                    "source_priority": priority,
                    "source_record_id": record["source_record_id"],
                    "source_record_part": record["source_record_part"],
                    "source_precedence_conflicts": source_precedence_conflicts,
                    "suppressed_length_meters": round(max(0.0, original.length - selected_length), 6),
                }
            )

    osm_summary = normalized_by_id[registry["osm"]["id"]]
    for disposition in read_jsonl(external_run / osm_summary["disposition_ledger"]["path"]):
        if disposition["disposition"] == "NORMALIZED_ELIGIBLE" or not disposition.get("geometry"):
            continue
        explicit_universe.append(
            {
                "canonical_segment_id": disposition.get("explicit_universe_member_id")
                or f"excluded:osm:{disposition['source_record_id']}",
                "confidence": "EXPLICIT_OSM_CROSSWALK",
                "class_evidence_license_partition": disposition["license_partition"],
                "class_evidence_source_id": disposition["source_id"],
                "eligibility_status_or_exclusion_reason": disposition["reason"],
                "geometry": disposition["geometry"],
                "geometry_license_partition": disposition["license_partition"],
                "geometry_source_id": disposition["source_id"],
                "lane_count": None,
                "lane_count_authority": None,
                "lane_evidence_license_partition": None,
                "lane_evidence_source_id": None,
                "license_partition": disposition["license_partition"],
                "represented_source_disposition": (
                    "UNKNOWN_FAIL_CLOSED" if disposition["disposition"] == "UNKNOWN_BLOCKING" else "EXCLUDED"
                ),
                "road_class": disposition.get("highway_class"),
                "source_id": disposition["source_id"],
                "source_precedence_decision": "NOT_ELIGIBLE_FOR_CANONICAL_CONFLATION",
                "source_record_id": disposition["source_record_id"],
                "source_record_part": 0,
            }
        )

    lane_candidates: list[dict[str, Any]] = []
    class_candidates: list[dict[str, Any]] = []
    priority_by_source = {value["source_id"]: int(value["priority"]) for value in precedence}
    for source_id, source_records in all_source_records.items():
        for record in source_records:
            lane = lane_count_evidence(registry, record)
            class_value = source_class_evidence(registry, record)
            if lane["lane_count"] is not None and class_value["decision"] == "ELIGIBLE":
                lane = {
                    **lane,
                    "evidence_license_partition": record["license_partition"],
                    "evidence_source_record_id": record["source_record_id"],
                    "evidence_source_record_part": record["source_record_part"],
                    "lane_count_authority": (
                        f"{source_id}:{record['source_record_id']}:{record['source_record_part']}:"
                        f"{lane['lane_count_authority'].split('.', 1)[-1]}"
                    ),
                }
                lane_candidates.append(
                    {
                        "_geometry": record["_geometry"],
                        "_source_record": record,
                        "evidence": lane,
                        "source_id": source_id,
                        "source_record_id": record["source_record_id"],
                        "source_record_part": record["source_record_part"],
                    }
                )
            if class_value["road_class"] is not None:
                class_value = {**class_value, "priority": priority_by_source[source_id]}
                class_value = {
                    **class_value,
                    "evidence_license_partition": record["license_partition"],
                    "evidence_source_record_id": record["source_record_id"],
                    "evidence_source_record_part": record["source_record_part"],
                }
                class_candidates.append(
                    {
                        "_geometry": record["_geometry"],
                        "_source_record": record,
                        "evidence": class_value,
                        "source_id": source_id,
                        "source_record_id": record["source_record_id"],
                        "source_record_part": record["source_record_part"],
                    }
                )
    lane_tree = STRtree([value["_geometry"] for value in lane_candidates]) if lane_candidates else None
    class_tree = STRtree([value["_geometry"] for value in class_candidates]) if class_candidates else None
    lane_ledger: list[dict[str, Any]] = []
    final_selected: list[dict[str, Any]] = []
    for record in selected:
        geometry = record["_geometry"]
        own_class = record["_class_evidence"]
        class_conflict_evidence: list[dict[str, Any]] = []
        if own_class["road_class"] is None:
            matched_class, class_conflicts = spatial_evidence(
                geometry,
                class_candidates,
                class_tree,
                registry=registry,
                require_correspondence=True,
                tolerance=tolerance,
                target_record=record["_source_record"],
                minimum_ratio=float(model["minimum_lane_match_ratio"]),
                require_different_source=True,
                value_key="road_class",
            )
            if class_conflicts:
                class_conflict_evidence = class_conflicts
                class_value = None
                class_authority = None
                class_confidence = "CONFLICT_FAIL_CLOSED"
                class_evidence_source_id = None
                class_evidence_source_record_id = None
                class_evidence_source_record_part = None
                class_evidence_license_partition = None
                conflicts.append(
                    {
                        "canonical_segment_id": record["canonical_segment_id"],
                        "conflicting_class_evidence": class_conflicts,
                        "id": "conflicting_class_evidence",
                    }
                )
            elif matched_class:
                class_value = matched_class["evidence"]["road_class"]
                class_authority = (
                    f"{matched_class['source_id']}:{matched_class['source_record_id']}:"
                    f"{matched_class['source_record_part']}"
                )
                class_confidence = matched_class["evidence"]["confidence"]
                class_evidence_source_id = matched_class["source_id"]
                class_evidence_source_record_id = matched_class["source_record_id"]
                class_evidence_source_record_part = matched_class["source_record_part"]
                class_evidence_license_partition = matched_class["evidence"][
                    "evidence_license_partition"
                ]
            else:
                class_value = class_authority = None
                class_confidence = "UNKNOWN_FAIL_CLOSED"
                class_evidence_source_id = None
                class_evidence_source_record_id = None
                class_evidence_source_record_part = None
                class_evidence_license_partition = None
        else:
            class_value = own_class["road_class"]
            class_authority = (
                f"{record['source_id']}:{record['source_record_id']}:"
                f"{record['source_record_part']}"
            )
            class_confidence = own_class["confidence"]
            class_evidence_source_id = record["source_id"]
            class_evidence_source_record_id = record["source_record_id"]
            class_evidence_source_record_part = record["source_record_part"]
            class_evidence_license_partition = record["license_partition"]

        own_lane = lane_count_evidence(registry, record["_source_record"])
        if own_lane["lane_count"] is not None:
            lane = {
                **own_lane,
                "evidence_license_partition": record["license_partition"],
                "evidence_match_ratio": 1.0,
                "evidence_source_record_id": record["source_record_id"],
                "evidence_source_record_part": record["source_record_part"],
                "lane_count_authority": (
                    f"{record['source_id']}:{record['source_record_id']}:"
                    f"{record['source_record_part']}:"
                    f"{own_lane['lane_count_authority'].split('.', 1)[-1]}"
                ),
            }
            lane_conflicts: list[dict[str, Any]] = []
        else:
            matched_lane, lane_conflicts = spatial_evidence(
                geometry,
                lane_candidates,
                lane_tree,
                registry=registry,
                require_correspondence=True,
                tolerance=float(model["lane_match_tolerance_meters"]),
                target_record=record["_source_record"],
                minimum_ratio=float(model["minimum_lane_match_ratio"]),
                require_different_source=True,
                require_single_source_record=True,
                value_key="lane_count",
            )
            lane = (
                {**matched_lane["evidence"], "evidence_match_ratio": round(matched_lane["match_ratio"], 6)}
                if matched_lane
                else own_lane
            )
        if lane_conflicts:
            lane = {
                **own_lane,
                "confidence": "CONFLICT_FAIL_CLOSED",
                "conflicting_lane_evidence": lane_conflicts,
                "reason": "CONFLICTING_DIRECT_LANE_COUNTS",
            }
            conflicts.append(
                {
                    "canonical_segment_id": record["canonical_segment_id"],
                    "conflicting_lane_evidence": lane_conflicts,
                    "id": "conflicting_lane_evidence",
                }
            )
        lane_ledger.append(
            {
                "canonical_segment_id": record["canonical_segment_id"],
                "conflicting_lane_evidence": lane_conflicts,
                **lane,
            }
        )
        final_selected.append(
            {
                "canonical_segment_id": record["canonical_segment_id"],
                "class_authority": class_authority,
                "class_evidence_license_partition": class_evidence_license_partition,
                "class_evidence_source_id": class_evidence_source_id,
                "class_evidence_source_record_id": class_evidence_source_record_id,
                "class_evidence_source_record_part": class_evidence_source_record_part,
                "conflicting_class_evidence": class_conflict_evidence,
                "confidence": (
                    f"class={class_confidence};lanes={lane['confidence']}"
                    if class_value is not None and lane["lane_count"] is not None
                    else "UNKNOWN_FAIL_CLOSED"
                ),
                "correspondence_identity_tokens": sorted(
                    correspondence_identity_tokens(registry, record["_source_record"])
                ),
                "correspondence_identity_evidence": correspondence_identity_evidence(
                    registry, record["_source_record"]
                ),
                "correspondence_semantics": road_semantic_evidence(
                    registry, record["_source_record"]
                ),
                "eligibility_status_or_exclusion_reason": (
                    "ELIGIBLE" if class_value is not None else "UNKNOWN_ROAD_CLASS_FAIL_CLOSED"
                ),
                "geometry": record["geometry"],
                "geometry_license_partition": record["license_partition"],
                "geometry_source_id": record["source_id"],
                "lane_count": lane["lane_count"],
                "lane_count_authority": lane["lane_count_authority"],
                "lane_evidence_license_partition": lane.get("evidence_license_partition"),
                "lane_evidence_source_id": lane.get("source_id"),
                "lane_evidence_source_record_id": lane.get("evidence_source_record_id"),
                "lane_evidence_source_record_part": lane.get("evidence_source_record_part"),
                "license_partition": record["license_partition"],
                "represented_source_disposition": "CANONICAL_GEOMETRY",
                "road_class": class_value,
                "source_id": record["source_id"],
                "source_precedence_decision": record["_precedence_decision"],
                "source_record_id": record["source_record_id"],
                "source_record_part": record["source_record_part"],
            }
        )

    independent_support_ledger, independent_support_summary, support_conflicts = (
        independent_lane_km_support(registry, final_selected, all_source_records)
    )
    conflicts.extend(support_conflicts)
    universe = final_selected + explicit_universe
    universe.sort(key=lambda value: value["canonical_segment_id"])
    conflation.sort(
        key=lambda value: (value["source_priority"], value["source_id"], value["source_record_id"], value["source_record_part"])
    )
    lane_ledger.sort(key=lambda value: value["canonical_segment_id"])
    canonical_output = external_run / "intermediates/canonical_road_universe.geojsonl"
    conflation_output = external_run / "intermediates/road_conflation_ledger.jsonl"
    lane_output = external_run / "intermediates/lane_evidence_ledger.jsonl"
    independent_support_output = (
        external_run / "intermediates/independent_lane_km_support_ledger.jsonl"
    )
    atomic_write_jsonl(canonical_output, universe)
    atomic_write_jsonl(lane_output, lane_ledger)
    atomic_write_jsonl(independent_support_output, independent_support_ledger)
    # These graphs include Shapely objects and hundreds of megabytes of nested
    # rows. They are serialized and are not inputs to reference representation.
    del independent_support_ledger
    del lane_ledger
    del selected
    del lane_candidates
    del class_candidates
    del lane_tree
    del class_tree
    gc.collect()

    references, reference_file = build_reference_inventory(registry, external_run, normalized_by_id)
    reference_candidate_tree = (
        STRtree([shape(value["geometry"]) for value in final_selected])
        if final_selected
        else None
    )
    reference_progress_started = time.monotonic()
    reference_progress_last_reported = reference_progress_started
    unmatched: list[dict[str, Any]] = []
    reference_dispositions: Counter[str] = Counter()
    motor_reference_dispositions: Counter[str] = Counter()
    reference_class_totals: Counter[str] = Counter()
    reference_class_matched: Counter[str] = Counter()
    representation_ledger: list[dict[str, Any]] = []
    for reference_index, observation in enumerate(references, start=1):
        geometry = shape(observation["geometry"])
        (
            representation_row,
            authority_result,
            corroboration_result,
            matched,
            _corroborated,
        ) = reference_representation_ledger_value(
            registry,
            observation,
            final_selected,
            source_record_lookup,
            reference_candidate_tree,
        )
        reference_progress_now = time.monotonic()
        if len(references) >= 100 and (
            reference_index == len(references)
            or reference_progress_now - reference_progress_last_reported >= 5.0
        ):
            print(
                compact_json(
                    {
                        "elapsed_seconds": round(
                            reference_progress_now - reference_progress_started, 3
                        ),
                        "processed": reference_index,
                        "stage": "reference_representation",
                        "total": len(references),
                    }
                ),
                flush=True,
            )
            reference_progress_last_reported = reference_progress_now
        authority_ratio = float(authority_result["match_ratio"])
        corroboration_ratio = float(corroboration_result["match_ratio"])
        eligibility = observation["motor_road_disposition"]
        disposition = representation_row["decision"]
        reference_dispositions[disposition] += 1
        if observation["reference_role"] == "road_centerline":
            motor_reference_dispositions[eligibility] += 1
        reference_class = (observation.get("class_evidence") or {}).get("road_class")
        if eligibility == "ELIGIBLE" and reference_class in ROAD_CLASSES:
            reference_class_totals[reference_class] += 1
            if matched:
                reference_class_matched[reference_class] += 1
        representation_ledger.append(representation_row)
        if observation["source_id"] not in priority_by_source:
            conflation.append(
                {
                    "decision": disposition,
                    "eligibility": observation["class_evidence"],
                        "license_partition": observation["license_partition"],
                        "authority_inclusive_match_ratio": round(authority_ratio, 6),
                        "different_source_corroboration_match_ratio": round(
                            corroboration_ratio, 6
                        ),
                        "match_ratio": round(authority_ratio, 6),
                        "match_scope": "AUTHORITY_INCLUSIVE_CANONICAL_REPRESENTATION",
                    "matched_higher_authority_sources": [],
                    "original_length_meters": round(geometry.length, 6),
                    "reason": None if matched else "REFERENCE_OBSERVATION_NOT_REPRESENTED",
                    "selected_length_meters": 0.0,
                    "source_id": observation["source_id"],
                    "source_priority": 1000,
                    "source_record_id": observation["source_record_id"],
                    "source_record_part": observation["source_record_part"],
                    "suppressed_length_meters": 0.0,
                }
            )
        if observation["reference_role"] == "road_centerline" and disposition in (
            "ELIGIBLE_REFERENCE_UNMATCHED",
            "UNRESOLVED_MOTOR_ROAD_ELIGIBILITY",
        ):
            if disposition == "UNRESOLVED_MOTOR_ROAD_ELIGIBILITY":
                reason = "REFERENCE_MOTOR_ROAD_ELIGIBILITY_UNRESOLVED"
            elif authority_result["ambiguities"]:
                reason = "REFERENCE_CORRESPONDENCE_SEMANTICS_UNRESOLVED"
            else:
                reason = "REFERENCE_OBSERVATION_NOT_REPRESENTED"
            unmatched.append(
                {
                    "geometry": observation["geometry"],
                    "properties": {
                        "authority_inclusive_match_ratio": round(authority_ratio, 6),
                        "different_source_corroboration_match_ratio": round(
                            corroboration_ratio, 6
                        ),
                        "match_ratio": round(authority_ratio, 6),
                        "match_scope": "AUTHORITY_INCLUSIVE_CANONICAL_REPRESENTATION",
                        "observation_kind": "INDEPENDENT_SOURCE_REFERENCE",
                        "reason": reason,
                        "reference_observation_id": observation["reference_observation_id"],
                        "reference_role": observation["reference_role"],
                        "road_class": reference_class,
                        "source_id": observation["source_id"],
                    },
                    "type": "Feature",
                }
            )
    unmatched.sort(key=lambda value: value["properties"]["reference_observation_id"])
    unmatched_output = external_run / "intermediates/unmatched_reference_observations.geojsonl"
    representation_output = external_run / "intermediates/reference_representation_ledger.jsonl"
    atomic_write_jsonl(unmatched_output, unmatched)
    representation_ledger.sort(key=lambda value: value["reference_observation_id"])
    atomic_write_jsonl(representation_output, representation_ledger)
    conflation.sort(
        key=lambda value: (
            int(value["source_priority"]),
            value["source_id"],
            value["source_record_id"],
            int(value["source_record_part"]),
        )
    )
    atomic_write_jsonl(conflation_output, conflation)
    reference_class_audit = {
        classification: {
            "matched_observation_count": reference_class_matched[classification],
            "reference_observation_count": reference_class_totals[classification],
            "unmatched_observation_count": (
                reference_class_totals[classification]
                - reference_class_matched[classification]
            ),
        }
        for classification in ROAD_CLASSES
    }
    conflicts.sort(key=compact_json)
    conflict_output = external_run / "intermediates/canonical_evidence_conflicts.jsonl"
    atomic_write_jsonl(conflict_output, conflicts)
    return {
        "canonical_universe": file_record(canonical_output, external_run),
        "canonical_universe_record_count": len(universe),
        "conflation_ledger": file_record(conflation_output, external_run),
        "conflict_count": len(conflicts),
        "conflict_ledger": file_record(conflict_output, external_run),
        "conflicts": conflicts,
        "excluded_universe_record_count": len(explicit_universe),
        "independent_lane_km_support": independent_support_summary,
        "independent_lane_km_support_ledger": file_record(
            independent_support_output, external_run
        ),
        "lane_evidence_ledger": file_record(lane_output, external_run),
        "reference_disposition_counts": dict(sorted(reference_dispositions.items())),
        "reference_class_audit": reference_class_audit,
        "reference_inventory": reference_file,
        "reference_representation_ledger": file_record(representation_output, external_run),
        "motor_reference_disposition_counts": dict(sorted(motor_reference_dispositions.items())),
        "reference_observation_count": len(references),
        "represented_canonical_record_count": len(final_selected),
        "unmatched_reference_observations": unmatched,
        "unmatched_reference_record": file_record(unmatched_output, external_run),
    }


def lane_kilometer_summary(canonical_path: Path) -> dict[str, Any]:
    """Compute source-universe representation and lane-km from direct bound evidence.

    The denominator is the unique source-priority canonical universe, not a
    count or length sum across duplicate source observations.  A canonical
    member contributes to the represented numerator even when its geometry is
    the bounded OSM fallback.  Cross-source corroboration is measured
    separately by :func:`independent_lane_km_support`.
    """

    by_class: dict[str, dict[str, Any]] = {
        classification: {
            "eligible_centerline_kilometers": 0.0,
            "eligible_segment_count": 0,
            "known_lane_count_centerline_kilometers": 0.0,
            "known_lane_kilometers": 0.0,
            "known_lane_segment_count": 0,
            "represented_lane_kilometers": 0.0,
            "represented_lane_segment_count": 0,
            "unrepresented_eligible_centerline_kilometers": 0.0,
            "unrepresented_eligible_segment_count": 0,
            "unresolved_geometric_centerline_kilometers": 0.0,
            "unresolved_geometric_segment_count": 0,
            "unknown_lane_centerline_kilometers": 0.0,
            "unknown_lane_segment_count": 0,
        }
        for classification in ROAD_CLASSES
    }
    unknown_class_length = 0.0
    unknown_class_records = 0
    unresolved_geometric_length = 0.0
    unresolved_geometric_records = 0
    for record in read_jsonl(canonical_path):
        disposition = record["represented_source_disposition"]
        if disposition == "UNKNOWN_FAIL_CLOSED":
            geometry = shape(record["geometry"])
            length_km = geometry.length / 1000.0
            unresolved_geometric_length += length_km
            unresolved_geometric_records += 1
            classification = record.get("road_class")
            if classification in ROAD_CLASSES:
                by_class[classification]["unresolved_geometric_centerline_kilometers"] += length_km
                by_class[classification]["unresolved_geometric_segment_count"] += 1
            else:
                unknown_class_length += length_km
                unknown_class_records += 1
            continue
        if disposition != "CANONICAL_GEOMETRY" and record.get(
            "eligibility_status_or_exclusion_reason"
        ) != "ELIGIBLE":
            continue
        geometry = shape(record["geometry"])
        length_km = geometry.length / 1000.0
        classification = record.get("road_class")
        if classification not in ROAD_CLASSES:
            unknown_class_length += length_km
            unknown_class_records += 1
            continue
        summary = by_class[classification]
        summary["eligible_segment_count"] += 1
        summary["eligible_centerline_kilometers"] += length_km
        lane_count = record.get("lane_count")
        if lane_count is None:
            summary["unknown_lane_centerline_kilometers"] += length_km
            summary["unknown_lane_segment_count"] += 1
        else:
            summary["known_lane_count_centerline_kilometers"] += length_km
            summary["known_lane_kilometers"] += length_km * int(lane_count)
            summary["known_lane_segment_count"] += 1
            if disposition == "CANONICAL_GEOMETRY":
                summary["represented_lane_kilometers"] += length_km * int(lane_count)
                summary["represented_lane_segment_count"] += 1
            else:
                summary["unrepresented_eligible_centerline_kilometers"] += length_km
                summary["unrepresented_eligible_segment_count"] += 1
    for value in by_class.values():
        direct_lane_ratio = (
            value["known_lane_count_centerline_kilometers"]
            / value["eligible_centerline_kilometers"]
            if value["eligible_centerline_kilometers"] > 0.0
            else 0.0
        )
        representation_is_quantified = (
            value["unknown_lane_segment_count"] == 0
            and value["unresolved_geometric_segment_count"] == 0
        )
        representation_denominator = value["known_lane_kilometers"]
        represented_lane_kilometers = value["represented_lane_kilometers"]
        representation_ratio = (
            represented_lane_kilometers / representation_denominator
            if representation_is_quantified and representation_denominator > 0.0
            else 1.0
            if representation_is_quantified
            else None
        )
        value["eligible_centerline_kilometers"] = round(
            value["eligible_centerline_kilometers"], 6
        )
        value["known_lane_count_centerline_kilometers"] = round(
            value["known_lane_count_centerline_kilometers"], 6
        )
        # Gate-critical lane-km values retain their unrounded deterministic
        # float values.  Only display/audit lengths are rounded below; the
        # 95%/100% decision must never be made from a rounded ratio.
        value["known_lane_kilometers"] = representation_denominator
        value["represented_lane_kilometers"] = represented_lane_kilometers
        value["unrepresented_eligible_centerline_kilometers"] = round(
            value["unrepresented_eligible_centerline_kilometers"], 6
        )
        value["unknown_lane_centerline_kilometers"] = round(
            value["unknown_lane_centerline_kilometers"], 6
        )
        value["unresolved_geometric_centerline_kilometers"] = round(
            value["unresolved_geometric_centerline_kilometers"], 6
        )
        value["direct_lane_evidence_coverage_ratio"] = direct_lane_ratio
        value["representation_lane_kilometer_denominator"] = representation_denominator
        value["representation_lane_kilometer_ratio"] = representation_ratio
        value["representation_status"] = (
            "PASS_EXHAUSTIVE_SOURCE_UNIVERSE"
            if representation_is_quantified
            and value["unrepresented_eligible_segment_count"] == 0
            else "FAIL_CLOSED_UNRESOLVED_SOURCE_UNIVERSE"
        )
        value["lane_kilometer_denominator_status"] = (
            "EXACT_DIRECT_LANE_COUNTS"
            if value["eligible_segment_count"] > 0 and value["unknown_lane_segment_count"] == 0
            and value["unresolved_geometric_segment_count"] == 0
            else "UNRESOLVED_UNKNOWN_LANE_KILOMETERS"
        )
        value["status"] = (
            "PASS_DIRECT_LANE_EVIDENCE"
            if value["eligible_segment_count"] > 0 and value["unknown_lane_segment_count"] == 0
            and value["unresolved_geometric_segment_count"] == 0
            else "FAIL_CLOSED_UNKNOWN_LANES"
        )
    return {
        "by_class": by_class,
        "known_lane_kilometers_total": round(
            sum(value["known_lane_kilometers"] for value in by_class.values()), 6
        ),
        "measurement": "segment_length_km_times_direct_total_lane_count",
        "representation_measurement": (
            "unique_source-priority_canonical_lane-km_represented_by_canonical_geometry;"
            "bounded_OSM_fallback_is_authoritative_for_remaining_roads"
        ),
        "unknown_lane_records_are_in_denominator": True,
        "unresolved_geometric_centerline_kilometers": round(
            unresolved_geometric_length, 6
        ),
        "unresolved_geometric_segment_count": unresolved_geometric_records,
        "unknown_class_centerline_kilometers": round(unknown_class_length, 6),
        "unknown_class_segment_count": unknown_class_records,
        "unknown_lane_segment_count": sum(
            value["unknown_lane_segment_count"] for value in by_class.values()
        ),
    }


def class_source_coverage(
    classification: str,
    lane_value: dict[str, Any],
    support_value: dict[str, Any],
    reference_value: dict[str, Any],
    *,
    geometryless_motor_road_record_count: int,
    unresolved_geometric_motor_road_record_count: int,
    unknown_class_segment_count: int,
) -> dict[str, Any]:
    """Separate exhaustive representation from optional cross-source corroboration."""

    required_ratio = 1.0 if classification in MAJOR_CLASSES else 0.95
    denominator_is_complete = (
        int(lane_value["unknown_lane_segment_count"]) == 0
        and int(lane_value["unresolved_geometric_segment_count"]) == 0
        and int(geometryless_motor_road_record_count) == 0
        and int(unresolved_geometric_motor_road_record_count) == 0
        and int(unknown_class_segment_count) == 0
    )
    representation_ratio = (
        lane_value["representation_lane_kilometer_ratio"]
        if denominator_is_complete
        else None
    )
    corroboration_ratio = support_value["independent_support_lane_kilometer_ratio"]
    if int(support_value["unresolved_correspondence_segment_count"]) > 0:
        corroboration_status = "SEMANTIC_CONFLICT_FAIL_CLOSED"
    elif corroboration_ratio is None:
        corroboration_status = "NO_QUANTIFIED_CANONICAL_LANE_KILOMETERS"
    elif float(corroboration_ratio) >= 1.0:
        corroboration_status = "FULL_DIFFERENT_SOURCE_CORROBORATION"
    elif float(corroboration_ratio) > 0.0:
        corroboration_status = "PARTIAL_DIFFERENT_SOURCE_CORROBORATION"
    else:
        corroboration_status = "NO_DIFFERENT_SOURCE_CORROBORATION"
    return {
        **lane_value,
        **support_value,
        **reference_value,
        "corroboration_is_source_coverage_gate": False,
        "corroboration_status": corroboration_status,
        "independent_cross_source_lane_kilometer_ratio": corroboration_ratio,
        "required_ratio": required_ratio,
        "source_coverage_ratio": representation_ratio,
        "source_coverage_semantics": (
            "eligible_source_universe_to_unique_source-priority_canonical_representation"
        ),
        "status": (
            "PASS"
            if representation_ratio is not None
            and float(representation_ratio) >= required_ratio
            else "FAIL_CLOSED"
        ),
    }


def normalization_denominator_audit(
    normalized: list[dict[str, Any]], external_run: Path
) -> dict[str, Any]:
    """Account for unresolved geometric lengths and geometryless motor-road observations."""

    by_source: dict[str, dict[str, Any]] = {}
    for summary in normalized:
        ledger_record = summary.get("disposition_ledger")
        if not ledger_record:
            continue
        source = {
            "eligible_or_unresolved_geometryless_record_count": 0,
            "eligible_or_unresolved_geometric_record_count": 0,
            "eligible_or_unresolved_geometric_length_meters": 0.0,
            "explicit_universe_geometric_record_count": 0,
            "explicit_universe_geometric_length_meters": 0.0,
            "source_id": summary["id"],
        }
        for disposition in read_jsonl(external_run / ledger_record["path"]):
            motor = disposition.get("motor_road_disposition")
            if motor not in ("ELIGIBLE", "UNKNOWN_BLOCKING"):
                continue
            part_ids = disposition.get("normalized_part_ids") or []
            if part_ids:
                continue
            length = float(disposition.get("geometry_length_meters") or 0.0)
            if disposition.get("geometry") is not None and length > 0.0:
                if disposition.get("explicit_universe_member_id"):
                    source["explicit_universe_geometric_record_count"] += 1
                    source["explicit_universe_geometric_length_meters"] += length
                else:
                    source["eligible_or_unresolved_geometric_record_count"] += 1
                    source["eligible_or_unresolved_geometric_length_meters"] += length
            else:
                source["eligible_or_unresolved_geometryless_record_count"] += 1
        source["eligible_or_unresolved_geometric_length_meters"] = round(
            source["eligible_or_unresolved_geometric_length_meters"], 6
        )
        source["explicit_universe_geometric_length_meters"] = round(
            source["explicit_universe_geometric_length_meters"], 6
        )
        by_source[summary["id"]] = source
    return {
        "by_source": dict(sorted(by_source.items())),
        "geometryless_motor_road_record_count": sum(
            value["eligible_or_unresolved_geometryless_record_count"]
            for value in by_source.values()
        ),
        "explicit_universe_geometric_motor_road_length_meters": round(
            sum(
                value["explicit_universe_geometric_length_meters"]
                for value in by_source.values()
            ),
            6,
        ),
        "explicit_universe_geometric_motor_road_record_count": sum(
            value["explicit_universe_geometric_record_count"]
            for value in by_source.values()
        ),
        "unresolved_geometric_motor_road_length_meters": round(
            sum(
                value["eligible_or_unresolved_geometric_length_meters"]
                for value in by_source.values()
            ),
            6,
        ),
        "unresolved_geometric_motor_road_record_count": sum(
            value["eligible_or_unresolved_geometric_record_count"]
            for value in by_source.values()
        ),
    }


def arcgis_normalization_failures(
    normalized: list[dict[str, Any]], external_run: Path
) -> list[dict[str, Any]]:
    failures: list[dict[str, Any]] = []
    for summary in normalized:
        ledger = summary.get("disposition_ledger")
        if not ledger or summary["id"] == "osm_bounded_roads":
            continue
        for disposition in read_jsonl(external_run / ledger["path"]):
            if disposition["disposition"] == "NORMALIZED" or disposition["reason"] == "OUTSIDE_AEQD_SQUARE":
                continue
            failures.append(
                {
                    "geometry": None,
                    "properties": {
                        "observation_kind": "ARCGIS_NORMALIZATION_FAILURE",
                        "reason": disposition["reason"],
                        "source_id": disposition["source_id"],
                        "source_record_id": disposition["source_record_id"],
                    },
                    "type": "Feature",
                }
            )
    failures.sort(
        key=lambda value: (
            value["properties"]["source_id"], value["properties"]["source_record_id"]
        )
    )
    return failures


def cell_inventory(
    registry: dict[str, Any], canonical_path: Path
) -> list[dict[str, Any]]:
    minimum_x, minimum_z, maximum_x, maximum_z = registry["coordinate_contract"]["bounds_xz_meters"]
    cell_size = 1000.0
    columns = int(round((maximum_x - minimum_x) / cell_size))
    rows = int(round((maximum_z - minimum_z) / cell_size))
    lane_lengths: defaultdict[tuple[int, int, str], float] = defaultdict(float)
    unknown_lengths: defaultdict[tuple[int, int, str], float] = defaultdict(float)
    unknown_class_lengths: defaultdict[tuple[int, int], float] = defaultdict(float)
    for record in read_jsonl(canonical_path):
        if record.get("represented_source_disposition") != "CANONICAL_GEOMETRY":
            continue
        classification = record.get("road_class")
        geometry = record["geometry"]
        if geometry["type"] != "LineString":
            continue
        line = LineString(geometry["coordinates"])
        minx, minz, maxx, maxz = line.bounds
        first_column = max(0, min(columns - 1, int(math.floor((minx - minimum_x) / cell_size))))
        last_column = max(0, min(columns - 1, int(math.floor((maxx - minimum_x) / cell_size))))
        first_row = max(0, min(rows - 1, int(math.floor((minz - minimum_z) / cell_size))))
        last_row = max(0, min(rows - 1, int(math.floor((maxz - minimum_z) / cell_size))))
        for row in range(first_row, last_row + 1):
            for column in range(first_column, last_column + 1):
                cell = box(
                    minimum_x + column * cell_size,
                    minimum_z + row * cell_size,
                    minimum_x + (column + 1) * cell_size,
                    minimum_z + (row + 1) * cell_size,
                )
                length = line.intersection(cell).length
                if length > 0:
                    lane_count = record.get("lane_count")
                    if classification not in ROAD_CLASSES:
                        unknown_class_lengths[(column, row)] += length
                    elif lane_count is None:
                        unknown_lengths[(column, row, classification)] += length
                    else:
                        lane_lengths[(column, row, classification)] += length * int(lane_count)
    cells: list[dict[str, Any]] = []
    for row in range(rows):
        for column in range(columns):
            lane_values = {
                road: round(lane_lengths.get((column, row, road), 0.0), 3) for road in ROAD_CLASSES
            }
            unknown_values = {
                road: round(unknown_lengths.get((column, row, road), 0.0), 3) for road in ROAD_CLASSES
            }
            cells.append(
                {
                    "column": column,
                    "evaluated": True,
                    "known_lane_length_meters_by_class": lane_values,
                    "maximum_x_meters": minimum_x + (column + 1) * cell_size,
                    "maximum_z_meters": minimum_z + (row + 1) * cell_size,
                    "minimum_x_meters": minimum_x + column * cell_size,
                    "minimum_z_meters": minimum_z + row * cell_size,
                    "row": row,
                    "unknown_class_centerline_length_meters": round(
                        unknown_class_lengths.get((column, row), 0.0), 3
                    ),
                    "unknown_lane_centerline_length_meters_by_class": unknown_values,
                }
            )
    return cells


def product_summary(binding: dict[str, Any]) -> dict[str, Any]:
    summary = dict(binding)
    path = REPO_ROOT / binding["path"]
    if binding["id"] == "accepted_building_exclusion":
        value = json.loads(path.read_text(encoding="utf-8"))
        summary["building_count"] = sum(len(page.get("buildings") or []) for page in value["pages"])
        summary["coverage_bounds_xz_meters"] = [-12000.0, -12000.0, 12000.0, 12000.0]
        summary["quantization_meters"] = value["quantization_meters"]
    elif binding["id"] == "accepted_terrain":
        value = json.loads(path.read_text(encoding="utf-8"))
        summary["layers"] = [
            {
                "id": layer["id"],
                "maximum_enu_meters": layer["maximum_enu_meters"],
                "minimum_enu_meters": layer["minimum_enu_meters"],
                "spacing_meters": layer["spacing_meters"],
            }
            for layer in value["layers"]
        ]
    elif binding["id"] == "accepted_water_vectors":
        value = json.loads(path.read_text(encoding="utf-8"))
        summary["selected_feature_count"] = int(value["coverage"]["selected_feature_count"])
    elif binding["id"] == "accepted_semantic_exclusion_contract":
        value = json.loads(path.read_text(encoding="utf-8"))
        summary["sports_polygon_count"] = int(value["statistics"]["sports_polygon_count"])
        summary["meters_per_texel"] = float(value["contract"]["meters_per_texel"])
    elif binding["id"] == "accepted_faa_runway_context":
        value = json.loads(path.read_text(encoding="utf-8"))
        summary["runway_ids"] = [runway["runway_id"] for runway in value["faa"]["runways"]]
    elif binding["id"] == "accepted_city_2020_orthophoto_manifest":
        value = json.loads(path.read_text(encoding="utf-8"))
        summary["acquisition_period"] = value["source"]["acquisition_period"]
        summary["arcgis_item_id"] = value["source"]["arcgis_item_id"]
        summary["source_tile_count"] = value["source"]["source_tile_count"]
    return summary


def validate_benchmark_camera_contract(
    registry: dict[str, Any], canonical_universe: dict[str, Any], record_count: int
) -> dict[str, Any]:
    config = registry["authority_model"]["benchmark_camera_contract"]
    path = REPO_ROOT / config["path"]
    value = json.loads(path.read_text(encoding="utf-8"))
    if value.get("schema") != BENCHMARK_CAMERA_CONTRACT_SCHEMA or value.get("status") != "FROZEN":
        raise GateError("Lead benchmark-camera contract schema/status is invalid")
    if value.get("accepted_source_sha") != registry["accepted_source_sha"]:
        raise GateError("Lead benchmark-camera contract accepted-source SHA mismatch")
    contract = value.get("coverage_contract") or {}
    if contract.get("candidate_mode") != "FULL_MAP_UNIVERSE_SUPERSET":
        raise GateError("Benchmark-camera contract does not use FULL_MAP_UNIVERSE_SUPERSET")
    if contract.get("no_frustum_or_occlusion_exclusions") is not True:
        raise GateError("Benchmark-camera contract permits forbidden frustum/occlusion exclusions")
    if contract.get("bounds_xz_meters") != registry["coordinate_contract"]["bounds_xz_meters"]:
        raise GateError("Benchmark-camera contract bounds mismatch")
    views = value.get("views") or []
    if value.get("view_count") != 15 or len(views) != 15:
        raise GateError("Benchmark-camera contract must bind exactly 15 views")
    ids = [str(view.get("id") or "") for view in views]
    if any(not view_id for view_id in ids) or len(ids) != len(set(ids)):
        raise GateError("Benchmark-camera contract has empty or duplicate view IDs")
    minimum_x, minimum_z, maximum_x, maximum_z = registry["coordinate_contract"]["bounds_xz_meters"]
    bindings: list[dict[str, Any]] = []
    for view in views:
        position = view.get("resolved_position_meters") or {}
        try:
            x, z = float(position["x"]), float(position["z"])
        except (KeyError, TypeError, ValueError) as error:
            raise GateError(f"Benchmark view {view.get('id')} lacks a resolved x/z origin") from error
        inside = minimum_x <= x <= maximum_x and minimum_z <= z <= maximum_z
        if not inside or view.get("origin_inside_frozen_bounds") is not True:
            raise GateError(f"Benchmark view {view['id']} origin is outside the frozen bounds")
        if view.get("road_candidate_set") != "FULL_MAP_UNIVERSE_SUPERSET":
            raise GateError(f"Benchmark view {view['id']} has a non-universe candidate set")
        screenshot = view.get("screenshot") or {}
        if not re.fullmatch(r"[0-9a-f]{64}", str(screenshot.get("sha256") or "")):
            raise GateError(f"Benchmark view {view['id']} has no exact screenshot SHA-256")
        bindings.append(
            {
                "camera_origin_xz_meters": [x, z],
                "canonical_universe_record_count": record_count,
                "canonical_universe_sha256": canonical_universe["sha256"],
                "field_of_view_degrees": float(view["field_of_view_degrees"]),
                "origin_inside_frozen_bounds": True,
                "road_candidate_set": "FULL_MAP_UNIVERSE_SUPERSET",
                "screenshot": screenshot,
                "view_id": view["id"],
            }
        )
    return {
        "camera_contract": {
            "bytes": path.stat().st_size,
            "path": config["path"],
            "sha256": sha256_file(path),
        },
        "candidate_mode": "FULL_MAP_UNIVERSE_SUPERSET",
        "canonical_universe": {
            "record_count": record_count,
            "sha256": canonical_universe["sha256"],
        },
        "status": "PENDING_LEAD_D_INSPECTION",
        "view_bindings": bindings,
        "view_count": len(bindings),
    }


def validate_repository_witness(
    record: Any,
    *,
    expected_path: str,
    label: str,
) -> dict[str, Any]:
    """Require one exact structured record to match live repository bytes."""

    if not isinstance(record, dict) or set(record) != {"bytes", "path", "sha256"}:
        raise GateError(f"{label} witness must contain exactly path, bytes, and sha256")
    expected_file = resolve_repository_relative_path(expected_path, f"{label} registry")
    if record.get("path") != expected_path:
        raise GateError(f"{label} witness path does not match the registry-owned path")
    record_file = resolve_repository_relative_path(record["path"], label)
    if record_file != expected_file:
        raise GateError(f"{label} witness does not resolve to the registry-owned path")
    size = record.get("bytes")
    if isinstance(size, bool) or not isinstance(size, int) or size < 0:
        raise GateError(f"{label} witness bytes is not a nonnegative integer")
    digest = record.get("sha256")
    if not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest):
        raise GateError(f"{label} witness SHA-256 is not lowercase hexadecimal")
    if not record_file.is_file():
        raise GateError(f"{label} witness file is missing: {expected_path}")
    if record_file.stat().st_size != size or sha256_file(record_file) != digest:
        raise GateError(f"{label} witness live bytes/hash mismatch")
    return {"bytes": size, "path": expected_path, "sha256": digest}


def build_approval_validation_context(
    registry: dict[str, Any],
    registry_path: Path,
    external_run: Path,
    benchmark: dict[str, Any],
    source_freeze_sha256: str,
    candidate_manifest: dict[str, Any],
) -> dict[str, Any]:
    """Build all live records that D and Lead must independently bind."""

    freeze_path = external_run / FREEZE_NAME
    candidate_path = external_run / "approval_candidate/candidate_manifest.json"
    if not freeze_path.is_file() or sha256_file(freeze_path) != source_freeze_sha256:
        raise GateError("Approval context source-freeze bytes do not match the candidate")
    freeze_value = json.loads(freeze_path.read_text(encoding="utf-8"))
    if not candidate_path.is_file() or json.loads(
        candidate_path.read_text(encoding="utf-8")
    ) != candidate_manifest:
        raise GateError("Approval context candidate-manifest live bytes differ")
    toolchain_binding = validate_or_create_external_toolchain_manifest(
        registry, registry_path, external_run, allow_create=False
    )
    verifier_path = resolve_repository_relative_path(
        registry["authority_model"]["benchmark_approval_witnesses"]["d_verifier"]["path"],
        "D verifier",
    )
    artifact_records: dict[str, Any] = {}
    for name in (*EXPECTED_TEXT_ARTIFACTS, EXPECTED_EXTERNAL_ARTIFACT):
        artifact_path = external_run / "approval_candidate" / name
        if not artifact_path.is_file():
            raise GateError(f"Approval candidate artifact is missing: {name}")
        artifact_records[name] = file_record(artifact_path, external_run)
        if artifact_records[name]["sha256"] != candidate_manifest["artifact_sha256"].get(name):
            raise GateError(f"Approval candidate artifact live SHA differs: {name}")
    candidate_event_utc = candidate_manifest.get("candidate_event_utc")
    freeze_event_utc = freeze_value.get("acquisition_completed_utc")
    candidate_event = require_not_future(candidate_event_utc, "Candidate event UTC")
    freeze_event = require_not_future(
        freeze_event_utc, "Freeze acquisition-completed UTC"
    )
    minimum_inspection_utc = (
        candidate_event_utc if candidate_event >= freeze_event else freeze_event_utc
    )
    camera_path = resolve_repository_relative_path(
        registry["authority_model"]["benchmark_camera_contract"]["path"],
        "Benchmark camera contract",
    )
    return {
        "bindings": {
            "camera_contract": repository_file_record(camera_path),
            "candidate_artifacts": artifact_records,
            "candidate_manifest": file_record(candidate_path, external_run),
            "canonical_universe": {
                "record_count": int(benchmark["canonical_universe"]["record_count"]),
                "sha256": benchmark["canonical_universe"]["sha256"],
            },
            "requirements_lock": toolchain_binding["requirements_lock"],
            "source_freeze": file_record(freeze_path, external_run),
            "source_registry": repository_file_record(registry_path),
            "source_tool": repository_file_record(Path(__file__)),
            "toolchain_manifest": toolchain_binding["manifest"],
        },
        "reproduction": {
            "argv": [
                str(Path(sys.executable).resolve()),
                "-I",
                "-B",
                str(verifier_path),
                "--registry",
                str(registry_path.resolve()),
                "--external-run",
                str(external_run.resolve()),
                "--data-dir",
                str(DEFAULT_DATA_DIR.resolve()),
                "--evidence-dir",
                str((external_run / "evidence").resolve()),
            ],
            "cwd": str(REPO_ROOT.resolve()),
            "exit_code": 0,
        },
        "required_checks": registry["authority_model"]["benchmark_approval_contract"][
            "required_d_checks"
        ],
        "minimum_inspection_utc": minimum_inspection_utc,
    }


def parse_live_json_witness(record: dict[str, Any], label: str) -> dict[str, Any]:
    try:
        value = json.loads(
            resolve_repository_relative_path(record["path"], label).read_text(encoding="utf-8")
        )
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise GateError(f"{label} witness is not valid UTF-8 JSON") from error
    if not isinstance(value, dict):
        raise GateError(f"{label} witness root is not an object")
    return value


def validate_d_result_semantics(
    registry: dict[str, Any],
    result_value: dict[str, Any],
    *,
    context: dict[str, Any],
    verifier: dict[str, Any],
    report: dict[str, Any],
) -> None:
    expected_fields = {"bindings", "checks", "decision", "reproduction", "schema", "status"}
    if set(result_value) != expected_fields or result_value.get("schema") != D_RESULT_SCHEMA:
        raise GateError("D machine result does not match the exact result schema")
    expected_checks = {key: "PASS" for key in context["required_checks"]}
    expected_bindings = {
        **context["bindings"],
        "report": report,
        "verifier": verifier,
    }
    checks_pass = result_value.get("checks") == expected_checks
    bindings_pass = result_value.get("bindings") == expected_bindings
    reproduction_pass = result_value.get("reproduction") == context["reproduction"]
    derived_decision = "PASS" if checks_pass and bindings_pass and reproduction_pass else "FAIL_CLOSED"
    if result_value.get("decision") != derived_decision or derived_decision != "PASS":
        raise GateError("D machine-result decision is not content-derived PASS")
    if result_value.get("status") != "PASS":
        raise GateError("D machine-result status contradicts its derived decision")


def validate_lead_inspection_semantics(
    inspection_value: dict[str, Any], *, context: dict[str, Any]
) -> None:
    if set(inspection_value) != {"bindings", "inspection", "inspector", "schema", "status"}:
        raise GateError("Lead inspection does not match the exact inspection schema")
    if inspection_value.get("schema") != LEAD_INSPECTION_SCHEMA:
        raise GateError("Lead inspection schema is invalid")
    expected_bindings = {
        key: context["bindings"][key]
        for key in (
            "camera_contract",
            "candidate_artifacts",
            "candidate_manifest",
            "canonical_universe",
            "source_freeze",
        )
    }
    if inspection_value.get("bindings") != expected_bindings:
        raise GateError("Lead inspection bindings are stale or unrelated to this candidate")
    expected_png = context["bindings"]["candidate_artifacts"][EXPECTED_EXTERNAL_ARTIFACT]
    if inspection_value.get("inspection") != {
        "coverage_png": expected_png,
        "full_resolution_opened": True,
        "height_pixels": 1600,
        "status": "PASS",
        "width_pixels": 1600,
    }:
        raise GateError("Lead inspection does not prove a full-resolution 1600x1600 PNG PASS")
    inspector = inspection_value.get("inspector")
    if not isinstance(inspector, dict) or set(inspector) != {"id", "inspected_utc"}:
        raise GateError("Lead inspection must contain an exact inspector record")
    if not isinstance(inspector.get("id"), str) or not inspector["id"].strip():
        raise GateError("Lead inspector ID is empty")
    inspected_utc = inspector.get("inspected_utc")
    parsed = require_not_future(inspected_utc, "Lead inspection timestamp")
    minimum_utc = context.get("minimum_inspection_utc")
    minimum = parse_canonical_utc(
        minimum_utc, "Approval candidate/freeze event time"
    )
    if parsed < minimum:
        raise GateError("Lead inspection predates the frozen candidate authority")
    if inspection_value.get("status") != "PASS":
        raise GateError("Lead inspection content status is not PASS")


def validate_approval_witnesses(
    registry: dict[str, Any], approval: dict[str, Any], *, context: dict[str, Any]
) -> dict[str, dict[str, Any]]:
    """Validate live, content-bound D/Lead witnesses before approval affects the gate."""

    expected = registry["authority_model"]["benchmark_approval_witnesses"]
    d_verification = approval.get("d_verification")
    if not isinstance(d_verification, dict) or set(d_verification) != {
        "report",
        "result",
        "status",
        "verifier",
    }:
        raise GateError(
            "Lead/D approval D verification must contain status, result, report, and verifier"
        )
    verifier = validate_repository_witness(
        d_verification["verifier"],
        expected_path=expected["d_verifier"]["path"],
        label="D verifier",
    )
    report = validate_repository_witness(
        d_verification["report"],
        expected_path=expected["d_report"]["path"],
        label="D report",
    )
    result = validate_repository_witness(
        d_verification["result"],
        expected_path=expected["d_result"]["path"],
        label="D machine result",
    )
    try:
        report_text = resolve_repository_relative_path(
            report["path"], "D report"
        ).read_text(encoding="utf-8")
    except (OSError, UnicodeError) as error:
        raise GateError("D report witness is not valid UTF-8 text") from error
    marker = registry["authority_model"]["benchmark_approval_contract"][
        "d_report_decision_marker"
    ]
    decisions = re.findall(
        rf"(?m)^<!--\s*{re.escape(marker)}:\s*(PASS|FAIL_CLOSED)\s*-->$", report_text
    )
    if decisions != ["PASS"]:
        raise GateError("D report machine decision marker is absent, duplicated, or not PASS")
    validate_d_result_semantics(
        registry,
        parse_live_json_witness(result, "D machine result"),
        context=context,
        verifier=verifier,
        report=report,
    )
    if d_verification.get("status") != "PASS":
        raise GateError("Lead/D approval outer D status contradicts the live D result")
    inspection = approval.get("lead_full_resolution_inspection")
    if not isinstance(inspection, dict) or set(inspection) != {"record", "status"}:
        raise GateError("Lead inspection must contain status and one structured record")
    inspection_record = validate_repository_witness(
        inspection["record"],
        expected_path=expected["lead_inspection"]["path"],
        label="Lead inspection",
    )
    validate_lead_inspection_semantics(
        parse_live_json_witness(inspection_record, "Lead inspection"), context=context
    )
    if inspection.get("status") != "PASS":
        raise GateError("Lead approval outer inspection status contradicts the live inspection")
    return {
        "d_report": report,
        "d_result": result,
        "d_verifier": verifier,
        "lead_inspection": inspection_record,
    }


def validate_universe_approval(
    registry: dict[str, Any],
    approval: dict[str, Any],
    *,
    benchmark: dict[str, Any],
    source_freeze_sha256: str,
    candidate_manifest: dict[str, Any],
    context: dict[str, Any],
) -> None:
    if set(approval) != {
        "camera_contract_sha256",
        "candidate_artifact_sha256",
        "canonical_universe_record_count",
        "canonical_universe_sha256",
        "d_verification",
        "lead_full_resolution_inspection",
        "schema",
        "source_freeze_sha256",
        "status",
    }:
        raise GateError("Lead/D universe approval does not match the exact approval schema")
    if approval.get("schema") != BENCHMARK_APPROVAL_SCHEMA:
        raise GateError("Lead/D universe approval schema is invalid")
    if approval.get("status") != "LEAD_D_APPROVED":
        raise GateError("Lead/D universe approval status is not LEAD_D_APPROVED")
    expected_universe = benchmark["canonical_universe"]
    if approval.get("canonical_universe_sha256") != expected_universe["sha256"]:
        raise GateError("Lead/D approval canonical-universe SHA mismatch")
    if int(approval.get("canonical_universe_record_count", -1)) != int(expected_universe["record_count"]):
        raise GateError("Lead/D approval canonical-universe count mismatch")
    if approval.get("camera_contract_sha256") != benchmark["camera_contract"]["sha256"]:
        raise GateError("Lead/D approval camera-contract SHA mismatch")
    if approval.get("source_freeze_sha256") != source_freeze_sha256:
        raise GateError("Lead/D approval source-freeze SHA mismatch")
    artifact_hashes = approval.get("candidate_artifact_sha256") or {}
    expected_hashes = candidate_manifest.get("artifact_sha256") or {}
    expected_artifact_names = set(EXPECTED_TEXT_ARTIFACTS) | {EXPECTED_EXTERNAL_ARTIFACT}
    if candidate_manifest.get("schema") != "questflight.traffic-v2-geospatial-approval-candidate.v1":
        raise GateError("Preserved approval-candidate schema is invalid")
    if set(candidate_manifest) != {
        "artifact_sha256",
        "camera_contract_sha256",
        "candidate_event_utc",
        "canonical_universe_record_count",
        "canonical_universe_sha256",
        "schema",
        "source_freeze_sha256",
        "status",
    }:
        raise GateError("Preserved approval candidate does not match the exact schema")
    if candidate_manifest.get("status") != "PENDING_LEAD_D_INSPECTION":
        raise GateError("Preserved approval candidate does not have pending status")
    if set(expected_hashes) != expected_artifact_names or set(artifact_hashes) != expected_artifact_names:
        raise GateError("Lead/D approval must bind exactly all five Phase-3 candidate artifacts")
    if not all(re.fullmatch(r"[0-9a-f]{64}", str(value or "")) for value in expected_hashes.values()):
        raise GateError("Preserved approval candidate has an invalid artifact SHA-256")
    for key, expected in (
        ("canonical_universe_sha256", expected_universe["sha256"]),
        ("canonical_universe_record_count", expected_universe["record_count"]),
        ("camera_contract_sha256", benchmark["camera_contract"]["sha256"]),
        ("source_freeze_sha256", source_freeze_sha256),
    ):
        if candidate_manifest.get(key) != expected:
            raise GateError(f"Preserved approval-candidate {key} mismatch")
    if artifact_hashes != expected_hashes:
        raise GateError("Lead/D approval candidate-artifact hashes do not match the preserved candidate")
    validate_approval_witnesses(registry, approval, context=context)


def evaluate_universe_approval(
    registry: dict[str, Any],
    registry_path: Path,
    external_run: Path,
    benchmark: dict[str, Any],
    source_freeze_sha256: str,
) -> dict[str, Any]:
    approval_path = resolve_repository_relative_path(
        registry["authority_model"]["benchmark_approval_path"], "Lead/D approval"
    )
    candidate_path = external_run / "approval_candidate/candidate_manifest.json"
    if not approval_path.is_file():
        return {
            "approval_path": registry["authority_model"]["benchmark_approval_path"],
            "reason": "Lead/D approval record is not present.",
            "status": "PENDING_LEAD_D_INSPECTION",
        }
    if not candidate_path.is_file():
        raise GateError("Lead/D approval exists but the preserved candidate manifest is missing")
    approval = json.loads(approval_path.read_text(encoding="utf-8"))
    candidate = json.loads(candidate_path.read_text(encoding="utf-8"))
    context = build_approval_validation_context(
        registry,
        registry_path,
        external_run,
        benchmark,
        source_freeze_sha256,
        candidate,
    )
    validate_universe_approval(
        registry,
        approval,
        benchmark=benchmark,
        source_freeze_sha256=source_freeze_sha256,
        candidate_manifest=candidate,
        context=context,
    )
    return {
        "approval": {
            "bytes": approval_path.stat().st_size,
            "path": registry["authority_model"]["benchmark_approval_path"],
            "sha256": sha256_file(approval_path),
        },
        "candidate_manifest": file_record(candidate_path, external_run),
        "status": "LEAD_D_APPROVED",
    }


def validate_live_approval_file_bindings(
    registry: dict[str, Any],
    external_run: Path,
    adjudication: dict[str, Any],
    problems: list[str],
    *,
    context: dict[str, Any] | None = None,
) -> None:
    """Re-pin the exact lead-owned approval and preserved candidate-manifest bytes."""

    if adjudication.get("status") != "LEAD_D_APPROVED":
        return
    if context is None:
        problems.append("approved run has no live semantic approval context")
        return
    approval_path = resolve_repository_relative_path(
        registry["authority_model"]["benchmark_approval_path"], "Lead/D approval"
    )
    if not approval_path.is_file():
        problems.append("approved run's lead-owned approval record is missing")
    else:
        try:
            validate_approval_witnesses(
                registry,
                json.loads(approval_path.read_text(encoding="utf-8")),
                context=context,
            )
        except (GateError, OSError, UnicodeError, json.JSONDecodeError) as error:
            problems.append(f"approved run's live witness validation failed: {error}")
        actual_approval = {
            "bytes": approval_path.stat().st_size,
            "path": registry["authority_model"]["benchmark_approval_path"],
            "sha256": sha256_file(approval_path),
        }
        if adjudication.get("approval") != actual_approval:
            problems.append("approved run's lead-owned approval hash/byte binding is stale")
    candidate_path = external_run / "approval_candidate/candidate_manifest.json"
    if not candidate_path.is_file():
        problems.append("approved run's preserved candidate manifest is missing")
    elif adjudication.get("candidate_manifest") != file_record(candidate_path, external_run):
        problems.append("approved run's candidate-manifest hash/byte binding is stale")


def approval_candidate_value(
    data_dir: Path,
    png_path: Path,
    *,
    benchmark: dict[str, Any],
    candidate_event_utc: str,
    source_freeze_sha256: str,
) -> dict[str, Any]:
    candidate_event = require_not_future(candidate_event_utc, "Candidate event UTC")
    artifact_hashes = {
        name: sha256_file(data_dir / name) for name in EXPECTED_TEXT_ARTIFACTS
    }
    artifact_hashes[EXPECTED_EXTERNAL_ARTIFACT] = sha256_file(png_path)
    return {
        "artifact_sha256": dict(sorted(artifact_hashes.items())),
        "camera_contract_sha256": benchmark["camera_contract"]["sha256"],
        "candidate_event_utc": candidate_event_utc,
        "canonical_universe_record_count": benchmark["canonical_universe"]["record_count"],
        "canonical_universe_sha256": benchmark["canonical_universe"]["sha256"],
        "schema": "questflight.traffic-v2-geospatial-approval-candidate.v1",
        "source_freeze_sha256": source_freeze_sha256,
        "status": "PENDING_LEAD_D_INSPECTION",
    }


def preserve_pending_approval_candidate(
    external_run: Path,
    data_dir: Path,
    png_path: Path,
    *,
    allow_create: bool = True,
    benchmark: dict[str, Any],
    candidate_event_utc: str,
    proposal_only: bool = False,
    source_freeze_sha256: str,
) -> dict[str, Any]:
    root = external_run / "approval_candidate"
    manifest = approval_candidate_value(
        data_dir,
        png_path,
        benchmark=benchmark,
        candidate_event_utc=candidate_event_utc,
        source_freeze_sha256=source_freeze_sha256,
    )
    artifact_hashes = manifest["artifact_sha256"]
    manifest_path = root / "candidate_manifest.json"
    if manifest_path.is_file():
        existing = json.loads(manifest_path.read_text(encoding="utf-8"))
        if existing != manifest:
            raise GateError(
                "Pending approval candidate differs from the immutable inspected candidate; "
                "start a new source freeze instead of overwriting it"
            )
        for name, expected_sha in artifact_hashes.items():
            candidate_path = root / name
            if not candidate_path.is_file() or sha256_file(candidate_path) != expected_sha:
                raise GateError(f"Immutable approval candidate byte mismatch: {name}")
        return file_record(manifest_path, external_run)
    if not allow_create:
        raise GateError("Rebuild check requires an existing immutable approval candidate")
    if proposal_only:
        manifest_bytes = canonical_json_bytes(manifest)
        return {
            "bytes": len(manifest_bytes),
            "path": "approval_candidate/candidate_manifest.json",
            "sha256": sha256_bytes(manifest_bytes),
        }
    if root.exists() and any(root.iterdir()):
        raise GateError(
            "Approval-candidate directory contains partial unbound files; quarantine it before first creation"
        )
    if root.exists():
        root.rmdir()
    root.parent.mkdir(parents=True, exist_ok=True)
    candidate_stage = Path(
        tempfile.mkdtemp(prefix=".approval_candidate_stage_", dir=root.parent)
    )
    try:
        for name in EXPECTED_TEXT_ARTIFACTS:
            atomic_write_bytes(candidate_stage / name, (data_dir / name).read_bytes())
        atomic_write_bytes(
            candidate_stage / EXPECTED_EXTERNAL_ARTIFACT, png_path.read_bytes()
        )
        atomic_write_json(candidate_stage / "candidate_manifest.json", manifest)
        try:
            candidate_stage.rename(root)
        except FileExistsError as error:
            raise GateError("Approval candidate appeared concurrently; refusing to overwrite it") from error
    finally:
        if candidate_stage.exists():
            shutil.rmtree(candidate_stage)
    return file_record(manifest_path, external_run)


def has_current_full_bounds_naip_authority(
    registry: dict[str, Any], verified: VerifiedNaipAuthority | None
) -> bool:
    if (
        not isinstance(verified, VerifiedNaipAuthority)
        or verified._seal is not _VERIFIED_NAIP_AUTHORITY_SEAL
    ):
        return False
    configured = next(
        (
            source
            for source in registry.get("document_sources") or []
            if source.get("id") == NAIP_AUTHORITY_SOURCE_ID
        ),
        None,
    )
    if not isinstance(configured, dict):
        return False
    contract = configured["orthophoto_authority_contract"]
    if verified._contract_sha256 != sha256_bytes(canonical_json_bytes(contract)):
        return False
    summary = verified.summary
    coverage = summary.get("coverage") or {}
    catalog = summary.get("catalog_inventory") or {}
    latest = summary.get("latest_inventory") or {}
    evidence_sha256 = summary.get("evidence_sha256") or {}
    expected_evidence_ids = {
        NAIP_LICENSE_SNAPSHOT_ID,
        NAIP_ARCHIVE_DOCUMENT_ID,
        NAIP_LATEST_COVERAGE_DOCUMENT_ID,
    }
    return bool(
        summary.get("schema") == NAIP_AUTHORITY_SCHEMA
        and summary.get("status") == "PASS_CURRENT_CLEAR_LICENSE_FULL_BOUNDS"
        and summary.get("allowed_use") == NAIP_ALLOWED_USE
        and summary.get("forbidden_uses") == list(NAIP_FORBIDDEN_USES)
        and summary.get("license_partition") == "US-Government-Work-Public-Domain"
        and coverage.get("state") == contract["accepted_state"]
        and coverage.get("year") == contract["accepted_year"]
        and coverage.get("coverage_bbox_wgs84_xyxy")
        == contract["coverage_bbox_wgs84_xyxy"]
        and coverage.get("coverage_gap_area_wgs84_degrees_squared") == 0.0
        and type(coverage.get("accepted_primary_footprint_count")) is int
        and coverage["accepted_primary_footprint_count"]
        == contract["expected_primary_footprint_count"]
        and coverage.get("coverage_union_bounds_wgs84_xyxy")
        == contract["coverage_union_bounds_wgs84_xyxy"]
        and coverage.get("archive_sha256") == contract["archive_sha256"]
        and coverage.get("selected_record_inventory_sha256")
        == contract["selected_record_inventory_sha256"]
        and coverage.get("statewide_footprint_count")
        == contract["expected_statewide_footprint_count"]
        and catalog.get("ordered_record_numbers")
        == contract["expected_selected_record_numbers"]
        and catalog.get("selected_record_count")
        == contract["expected_primary_footprint_count"]
        and catalog.get("statewide_footprint_count")
        == contract["expected_statewide_footprint_count"]
        and latest.get("most_recent_year") == contract["accepted_year"]
        and latest.get("colorado_quarter_quad_inventory") == "co_naip21qq-1.zip"
        and latest.get("colorado_quarter_quad_raw_href")
        == contract["expected_raw_href"]
        and latest.get("colorado_quarter_quad_resolved_href")
        == contract["expected_resolved_href"]
        and latest.get("colorado_quarter_quad_anchor_label")
        == contract["expected_anchor_label"]
        and latest.get("structural_errors") == []
        and set(evidence_sha256) == expected_evidence_ids
        and all(
            isinstance(digest, str) and re.fullmatch(r"[0-9a-f]{64}", digest)
            for digest in evidence_sha256.values()
        )
    )


def source_conflicts(
    registry: dict[str, Any],
    freeze: dict[str, Any],
    normalized: list[dict[str, Any]],
    benchmark_approval: dict[str, Any] | None = None,
    verified_naip_authority: VerifiedNaipAuthority | None = None,
) -> list[dict[str, Any]]:
    conflicts = list(freeze.get("product_binding_warnings") or [])
    source_by_id = {source["id"]: source for source in freeze["sources"]}
    for source in freeze["sources"]:
        advisory = source.get("count_advisory")
        if advisory:
            conflicts.append(
                {
                    "id": f"{source['id']}_server_count_advisory",
                    "reason": (
                        f"ArcGIS returnCountOnly reports {advisory['server_count_pre']} while two stable "
                        f"ID enumerations expose {advisory['accepted_unique_id_count']} unique records."
                    ),
                    "resolution": "Freeze complete unique OBJECTID inventory; preserve and recheck the exact server-count delta.",
                    "severity": "WARNING",
                    "source_ids": [source["id"]],
                    "status": "ADJUDICATED_STABLE_SERVICE_QUIRK",
                }
            )
    for rejection in registry["rejected_sources"]:
        conflicts.append(
            {
                "id": rejection["id"],
                "reason": rejection["reason"],
                "resolution": "Source excluded from the freeze and all derivatives.",
                "severity": "WARNING",
                "status": "REJECTED_FAIL_CLOSED",
            }
        )
    for drcog_id in ("drcog_pavement_polygons_2024", "drcog_pavement_lines_2024"):
        conflicts.append(
            {
                "id": f"{drcog_id}_license_version_label",
                "reason": "DRCOG catalog text identifies CC BY 3.0 while its linked deed identifies CC BY 4.0.",
                "resolution": "Preserve both official snapshots and satisfy both compatible attribution obligations.",
                "severity": "WARNING",
                "source_ids": [drcog_id],
                "status": "ADJUDICATED_DUAL_ATTRIBUTION",
            }
        )
    drcog_line = source_by_id.get("drcog_pavement_lines_2024") or {}
    if not drcog_line.get("has_z"):
        conflicts.append(
            {
                "id": "bridge_deck_vertical_authority_gap",
                "reason": "DRCOG pavement lines have no Z; accepted terrain is 400 m spacing. Project metadata proves 3DEP class-17 bridge points exist, but no required-span point bytes or per-deck absolute error audit is frozen.",
                "resolution": "Phase 4 must select exact 3DEP tiles per required span, acquire class-17 points, validate each deck against the 0.25 m contract, and exclude every ambiguous span.",
                "severity": "FORWARD_BLOCKER",
                "source_ids": ["drcog_pavement_lines_2024", "usgs_3dep_bridge_metadata", "accepted_terrain"],
                "status": "FORWARD_PHASE4_UNRESOLVED",
            }
        )
    if (benchmark_approval or {}).get("status") != "LEAD_D_APPROVED":
        conflicts.append(
            {
                "id": "benchmark_camera_road_inventory_gap",
                "reason": (
                    "The 15-view FULL_MAP_UNIVERSE_SUPERSET camera contract is frozen, but the computed "
                    "canonical universe and candidate artifacts have not yet received exact Lead/D approval."
                ),
                "resolution": (
                    "D must independently reproduce the canonical universe and candidate artifact hashes; "
                    "Lead must bind the full-resolution inspection in the separately owned approval record."
                ),
                "severity": "BLOCKING",
                "source_ids": ["benchmark_camera_contract", "canonical_road_universe"],
                "status": "PENDING_LEAD_D_INSPECTION",
            }
        )
    if not has_current_full_bounds_naip_authority(registry, verified_naip_authority):
        conflicts.append(
            {
                "id": "orthophoto_temporal_gap",
                "reason": (
                    "No frozen, semantically revalidated public-domain current Colorado NAIP authority "
                    "proves complete coverage of the exact +/-12 km KBDU bounds."
                ),
                "resolution": (
                    "Freeze the exact USDA public-domain license page, GeoPlatform service metadata, "
                    "USDA most-recent Colorado-year inventory, and bounded primary-footprint union; "
                    "until then imagery may not resolve post-2020 ambiguity."
                ),
                "severity": "BLOCKING",
                "source_ids": [
                    NAIP_AUTHORITY_SOURCE_ID,
                    "city_orthophoto_2020_metadata",
                ],
                "status": "UNRESOLVED_FAIL_CLOSED",
            }
        )
    conflicts.extend(
        [
            {
                "id": "time_of_day_directional_count_gap",
                "reason": "City CC0 ART/BVP directional hourly and TMC 15-minute turn-count examples are frozen, but network-wide station matching and calibrated demand remain future work.",
                "resolution": "Workstream B must inventory applicable stations/years and freeze the documented time-of-day scenario before demand acceptance.",
                "severity": "FORWARD_REQUIREMENT",
                "source_ids": ["city_traffic_count_documents", "cdot_traffic_counts", "county_traffic_stations"],
                "status": "FORWARD_PHASE5_SOURCE_IDENTIFIED",
            },
            {
                "id": "signal_phase_timing_gap",
                "reason": "City/County control inventories locate controls but provide no complete phase/timing authority.",
                "resolution": "Phase 7 must obtain timing plans or author and disclose a deterministic scheduling assumption; affected movements still fail closed until then.",
                "severity": "FORWARD_REQUIREMENT",
                "source_ids": ["city_signals", "county_traffic_controls"],
                "status": "FORWARD_PHASE7_UNRESOLVED",
            },
        ]
    )
    return sorted(conflicts, key=lambda value: value["id"])


def render_coverage_png(
    registry: dict[str, Any],
    canonical_path: Path,
    output: Path,
    summaries: list[dict[str, Any]],
    gate_status: str,
    *,
    benchmark: dict[str, Any] | None = None,
    missing_features: list[dict[str, Any]] | None = None,
    osm_disposition_path: Path | None = None,
) -> None:
    width = height = 1600
    margin = 95
    image = Image.new("RGB", (width, height), (245, 247, 249))
    draw = ImageDraw.Draw(image)
    font = ImageFont.load_default()
    bounds = registry["coordinate_contract"]["bounds_xz_meters"]

    def pixel(x: float, z: float) -> tuple[float, float]:
        px = margin + (x - bounds[0]) / (bounds[2] - bounds[0]) * (width - 2 * margin)
        py = height - margin - (z - bounds[1]) / (bounds[3] - bounds[1]) * (height - 2 * margin)
        return px, py

    draw.rectangle((margin, margin, width - margin, height - margin), outline=(20, 28, 38), width=3)
    for coordinate in range(-12000, 12001, 1000):
        x1, y1 = pixel(coordinate, -12000)
        x2, y2 = pixel(coordinate, 12000)
        draw.line((x1, y1, x2, y2), fill=(218, 222, 228), width=1)
        x1, y1 = pixel(-12000, coordinate)
        x2, y2 = pixel(12000, coordinate)
        draw.line((x1, y1, x2, y2), fill=(218, 222, 228), width=1)
    def draw_geometry(geometry_value: dict[str, Any], *, color: tuple[int, int, int], width_px: int) -> None:
        geometry = shape(geometry_value)
        if geometry.geom_type == "LineString":
            parts = [geometry]
        elif geometry.geom_type in ("MultiLineString", "Polygon", "MultiPolygon"):
            boundary = geometry.boundary if geometry.geom_type in ("Polygon", "MultiPolygon") else geometry
            parts = [boundary] if boundary.geom_type == "LineString" else list(getattr(boundary, "geoms", []))
        else:
            return
        for part in parts:
            pixels = [pixel(float(x), float(z)) for x, z, *_ in part.coords]
            if len(pixels) >= 2:
                draw.line(pixels, fill=color, width=width_px)

    if osm_disposition_path and osm_disposition_path.is_file():
        for disposition in read_jsonl(osm_disposition_path):
            if disposition["disposition"] != "NORMALIZED_ELIGIBLE" and disposition.get("geometry"):
                draw_geometry(disposition["geometry"], color=(174, 178, 184), width_px=1)
    authority_count = fallback_count = excluded_count = ambiguous_count = 0
    for record in read_jsonl(canonical_path):
        decision = record["represented_source_disposition"]
        if decision in ("EXCLUDED", "EXPLICITLY_EXCLUDED_SOURCE_RECORD", "UNKNOWN_FAIL_CLOSED"):
            excluded_count += 1
            draw_geometry(record["geometry"], color=(156, 160, 168), width_px=1)
        elif record["source_precedence_decision"] == "RETAINED_AMBIGUOUS_FAIL_CLOSED":
            ambiguous_count += 1
            draw_geometry(record["geometry"], color=(196, 36, 48), width_px=3)
        elif record["source_precedence_decision"] == "SELECTED_ODBL_FALLBACK":
            fallback_count += 1
            draw_geometry(record["geometry"], color=(230, 139, 36), width_px=2)
        else:
            authority_count += 1
            draw_geometry(record["geometry"], color=(37, 103, 160), width_px=2)
    for feature in missing_features or []:
        if feature.get("geometry"):
            draw_geometry(feature["geometry"], color=(196, 36, 48), width_px=4)
    if benchmark:
        for view in benchmark.get("view_bindings") or []:
            x, z = view["camera_origin_xz_meters"]
            px, py = pixel(float(x), float(z))
            draw.polygon(
                ((px, py - 5), (px + 5, py), (px, py + 5), (px - 5, py)),
                fill=(119, 61, 151),
                outline=(255, 255, 255),
            )
    draw.text((margin, 25), "Traffic V2 Phase-3 authority / fallback / exclusion coverage", fill=(10, 16, 24), font=font)
    draw.text((width - 340, 25), f"Gate: {gate_status}", fill=(150, 20, 20), font=font)
    draw.text(
        (margin, 48),
        f"official={authority_count}  OSM fallback={fallback_count}  excluded/unknown={excluded_count}  ambiguous={ambiguous_count}  missing={len(missing_features or [])}",
        fill=(40, 48, 58),
        font=font,
    )
    legend_x = margin + 10
    legend_y = height - margin + 18
    for label, color, line_width in (
        ("official authority", (37, 103, 160), 3),
        ("OSM fallback (ODbL)", (230, 139, 36), 3),
        ("excluded / unknown", (156, 160, 168), 2),
        ("ambiguous / unmatched / missing", (196, 36, 48), 4),
        ("benchmark origins", (119, 61, 151), 4),
    ):
        draw.line((legend_x, legend_y + 5, legend_x + 25, legend_y + 5), fill=color, width=line_width)
        draw.text((legend_x + 32, legend_y), label, fill=(20, 28, 38), font=font)
        legend_x += 265
    draw.text((margin, height - 32), "Exact AEQD square: -12 km to +12 km; 1 km evaluation grid", fill=(40, 48, 58), font=font)
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + ".tmp.png")
    image.save(temporary, format="PNG", optimize=False, compress_level=9)
    os.replace(temporary, output)


def _compile_artifacts_core(
    registry_path: Path,
    external_run_path: Path,
    data_dir: Path,
    evidence_dir: Path,
    *,
    verified_naip_authority: VerifiedNaipAuthority,
    allow_candidate_creation: bool = True,
    candidate_event_utc: str | None = None,
    derivative_run: Path | None = None,
    manifest_path: Path | None = None,
    producer_command: dict[str, Any] | None = None,
    producer_data_dir: Path | None = None,
    producer_evidence_dir: Path | None = None,
) -> dict[str, Any]:
    registry = load_registry(registry_path)
    validate_pinned_inputs(registry)
    external_run = resolve_external_location(external_run_path)
    toolchain_binding = validate_or_create_external_toolchain_manifest(
        registry, registry_path, external_run, allow_create=False
    )
    freeze_path = external_run / FREEZE_NAME
    if not freeze_path.is_file():
        raise GateError(f"Missing source freeze: {freeze_path}")
    freeze = json.loads(freeze_path.read_text(encoding="utf-8"))
    if freeze.get("schema") != "questflight.traffic-v2-geospatial-source-freeze.v1":
        raise GateError("Unsupported source freeze schema")
    require_not_future(
        freeze.get("acquisition_completed_utc"), "Freeze acquisition-completed UTC"
    )
    acquisition_completed = require_not_future(
        freeze.get("acquisition_completed_utc"), "Freeze acquisition-completed UTC"
    )
    if freeze["registry"]["sha256"] != sha256_file(registry_path):
        raise GateError("Registry changed after acquisition")
    if freeze["tool"]["sha256"] != sha256_file(Path(__file__)):
        raise GateError("Source-gate tool changed after acquisition; acquire a new freeze")
    if freeze.get("accepted_source_sha") != registry["accepted_source_sha"]:
        raise GateError("Accepted source changed after acquisition")
    if freeze.get("specification") != registry["specification"]:
        raise GateError("Frozen specification binding differs from the registry")
    validate_frozen_toolchain_binding(freeze, toolchain_binding)
    if producer_command is not None:
        validate_recorded_compile_command(
            producer_command,
            registry_path=registry_path,
            external_run=external_run,
            data_dir=producer_data_dir or data_dir,
            evidence_dir=producer_evidence_dir or evidence_dir,
        )
    artifact_run = require_external(derivative_run) if derivative_run is not None else external_run

    normalized: list[dict[str, Any]] = []
    osm_summary: dict[str, Any] | None = None
    for source in freeze["sources"]:
        if source["id"] == registry["osm"]["id"]:
            osm_summary = normalize_osm(registry, source, external_run, artifact_run)
            normalized.append(osm_summary)
        elif source.get("source_kind") == "document_authority":
            normalized.append(
                {
                    "document_count": len(source["documents"]),
                    "feature_count": 0,
                    "geometry_count": 0,
                    "id": source["id"],
                    "road_length_meters": 0.0,
                }
            )
        else:
            normalized.append(normalize_arcgis(registry, source, external_run, artifact_run))
    if osm_summary is None:
        raise GateError("Frozen bounded OSM source is absent")

    canonical = build_canonical_inventory(registry, artifact_run, normalized)
    canonical_path = artifact_run / canonical["canonical_universe"]["path"]
    lane_metrics = lane_kilometer_summary(canonical_path)
    denominator_audit = normalization_denominator_audit(normalized, artifact_run)
    freeze_sha = sha256_file(freeze_path)
    benchmark = validate_benchmark_camera_contract(
        registry,
        canonical["canonical_universe"],
        canonical["canonical_universe_record_count"],
    )
    candidate_approval = {
        "approval_path": registry["authority_model"]["benchmark_approval_path"],
        "reason": "Live approval is adjudicated outside the immutable candidate artifacts.",
        "status": "PENDING_LEAD_D_INSPECTION",
    }
    benchmark["approval"] = candidate_approval
    benchmark["status"] = candidate_approval["status"]
    conflicts = source_conflicts(
        registry,
        freeze,
        normalized,
        candidate_approval,
        verified_naip_authority,
    )
    blocking = [
        value
        for value in conflicts
        if value.get("severity") == "BLOCKING" and value.get("status") != "RESOLVED"
    ]

    def add_blocker(blocker_id: str, reason: str, *, source_ids: list[str] | None = None) -> None:
        blocker = {
            "id": blocker_id,
            "reason": reason,
            "severity": "BLOCKING",
            "source_ids": source_ids or [],
            "status": "UNRESOLVED",
        }
        blocking.append(blocker)
        conflicts.append(blocker)

    arcgis_failures = arcgis_normalization_failures(normalized, artifact_run)
    missing_features = (
        list(canonical["unmatched_reference_observations"])
        + list(osm_summary["missing_features"])
        + arcgis_failures
    )
    missing_features.sort(
        key=lambda value: compact_json(value.get("properties") or {})
    )
    if osm_summary["missing_features"]:
        add_blocker(
            "invalid_or_unknown_osm_observations",
            f"{len(osm_summary['missing_features'])} unique OSM ways are unknown or could not be reconstructed.",
            source_ids=[registry["osm"]["id"]],
        )
    if arcgis_failures:
        add_blocker(
            "arcgis_normalization_failures",
            f"{len(arcgis_failures)} frozen ArcGIS OIDs have explicit non-spatial normalization failures.",
            source_ids=sorted({value["properties"]["source_id"] for value in arcgis_failures}),
        )
    if canonical["unmatched_reference_observations"]:
        add_blocker(
            "independent_reference_observations_unmatched",
            (
                f"{len(canonical['unmatched_reference_observations'])} City/County/CDOT motor-road reference "
                "observations are not represented by the source-priority canonical universe."
            ),
            source_ids=sorted(
                {
                    value["properties"]["source_id"]
                    for value in canonical["unmatched_reference_observations"]
                }
            ),
        )
    if canonical["conflicts"]:
        add_blocker(
            "canonical_evidence_conflicts",
            (
                f"{len(canonical['conflicts'])} record-level canonical precedence, class, or lane conflicts "
                f"are frozen in external {canonical['conflict_ledger']['path']} "
                f"({canonical['conflict_ledger']['sha256']})."
            ),
            source_ids=[],
        )
    if lane_metrics["unknown_class_segment_count"]:
        add_blocker(
            "canonical_unknown_road_classes",
            (
                f"{lane_metrics['unknown_class_segment_count']} represented canonical segments lack a "
                "source-priority road-class authority."
            ),
        )
    if lane_metrics["unknown_lane_segment_count"]:
        add_blocker(
            "canonical_unknown_lane_counts",
            (
                f"{lane_metrics['unknown_lane_segment_count']} eligible canonical segments lack a direct "
                "source-priority lane count; width-based inference is forbidden."
            ),
        )
    if lane_metrics["unresolved_geometric_segment_count"] or denominator_audit[
        "unresolved_geometric_motor_road_record_count"
    ]:
        add_blocker(
            "unresolved_motor_road_geometric_lengths",
            (
                f"Canonical unresolved geometry totals {lane_metrics['unresolved_geometric_centerline_kilometers']} "
                f"centerline-km; disposition audit adds "
                f"{denominator_audit['unresolved_geometric_motor_road_length_meters']} meters across "
                f"{denominator_audit['unresolved_geometric_motor_road_record_count']} unslotted records."
            ),
        )
    if denominator_audit["geometryless_motor_road_record_count"]:
        add_blocker(
            "geometryless_motor_road_observations",
            (
                f"{denominator_audit['geometryless_motor_road_record_count']} eligible or unresolved "
                "motor-road source records have no bounded line geometry and remain in the audit denominator."
            ),
        )

    class_coverage: dict[str, Any] = {}
    for classification in ROAD_CLASSES:
        lane_value = lane_metrics["by_class"][classification]
        support_value = canonical["independent_lane_km_support"]["by_class"][classification]
        reference_value = canonical["reference_class_audit"][classification]
        class_coverage[classification] = class_source_coverage(
            classification,
            lane_value,
            support_value,
            reference_value,
            geometryless_motor_road_record_count=denominator_audit[
                "geometryless_motor_road_record_count"
            ],
            unresolved_geometric_motor_road_record_count=denominator_audit[
                "unresolved_geometric_motor_road_record_count"
            ],
            unknown_class_segment_count=lane_metrics["unknown_class_segment_count"],
        )
        if class_coverage[classification]["status"] != "PASS":
            add_blocker(
                f"{classification}_represented_lane_km_coverage_gap",
                (
                    f"{classification} has represented canonical lane-km ratio "
                    f"{class_coverage[classification]['source_coverage_ratio']!r} "
                    f"(required {class_coverage[classification]['required_ratio']:.2f}), "
                    f"{lane_value['unrepresented_eligible_segment_count']} unrepresented eligible segments, "
                    f"{lane_value['unresolved_geometric_segment_count']} unresolved geometric segments, and "
                    f"{lane_value['unknown_lane_segment_count']} eligible segments with unknown direct lane "
                    "counts. Different-source corroboration is reported separately and is not this ratio."
                ),
            )
    conflicts.sort(key=lambda value: value["id"])
    gate_status = "PASS" if not blocking else "BLOCKED_AUTHORITY"

    cells = cell_inventory(registry, canonical_path)
    products = [product_summary(value) for value in freeze["accepted_product_bindings"]]
    evidence_output = require_external(evidence_dir)
    png_path = evidence_output / EXPECTED_EXTERNAL_ARTIFACT
    render_coverage_png(
        registry,
        canonical_path,
        png_path,
        normalized,
        "SOURCE_EVIDENCE_CANDIDATE",
        benchmark=benchmark,
        missing_features=missing_features,
        osm_disposition_path=artifact_run / osm_summary["disposition_ledger"]["path"],
    )
    current_png_sha256 = sha256_file(png_path)
    png_record = {
        "bytes": png_path.stat().st_size,
        "height_pixels": 1600,
        "inspection_status": "PENDING_LEAD_AND_D_FULL_RESOLUTION_INSPECTION",
        # Record the normative artifact's logical path, not the temporary
        # rebuild-check directory used to prove byte determinism.
        "path": f"evidence/{EXPECTED_EXTERNAL_ARTIFACT}",
        "sha256": current_png_sha256,
        "width_pixels": 1600,
    }

    source_summaries = []
    normalized_by_id = {value["id"]: value for value in normalized}
    for source in freeze["sources"]:
        summary = {
            "feature_count": source.get("feature_count"),
            "field_names": [value["name"] for value in source.get("fields") or []],
            "geometry_type": source.get("geometry_type"),
            "has_z": source.get("has_z"),
            "id": source["id"],
            "item_id": source.get("item_id"),
            "layer_name": source.get("layer_name"),
            "layer_url": source.get("layer_url"),
            "license_partition": source["license_partition"],
            "role": source["role"],
            "source_kind": (
                "osm"
                if source["id"] == registry["osm"]["id"]
                else source.get("source_kind", "arcgis")
            ),
            "status": source["status"],
        }
        if source.get("documents"):
            summary["documents"] = source["documents"]
        if source["id"] in normalized_by_id:
            summary["normalized"] = normalized_by_id[source["id"]]
        source_summaries.append(summary)

    coverage = {
        "accepted_source_sha": registry["accepted_source_sha"],
        "acquisition_completed_utc": freeze["acquisition_completed_utc"],
        "authority_blockers": blocking,
        "benchmark_camera_coverage": benchmark,
        "bounds_evaluation": {
            "cell_count": len(cells),
            "cell_size_meters": 1000.0,
            "cells": cells,
            "evaluated_cell_count": sum(bool(value["evaluated"]) for value in cells),
            "exact_bounds_xz_meters": registry["coordinate_contract"]["bounds_xz_meters"],
            "status": "PASS" if len(cells) == 576 else "FAIL",
        },
        "canonical_road_universe": {
            key: value
            for key, value in canonical.items()
            if key not in ("conflicts", "unmatched_reference_observations")
        },
        "class_coverage": class_coverage,
        "coordinate_contract": registry["coordinate_contract"],
        "evidence": {EXPECTED_EXTERNAL_ARTIFACT: png_record},
        "gate_status": gate_status,
        "implementation_scope": (
            "Phase-3 source authority and true direct-evidence lane-km only; directed lane topology remains Phase 4."
        ),
        "lane_kilometer_inventory": lane_metrics,
        "normalization_denominator_audit": denominator_audit,
        "osm_fallback": {
            key: value for key, value in osm_summary.items() if key != "missing_features"
        },
        "product_exclusion_bindings": products,
        "rejected_attempts": registry["rejected_freezes"],
        "schema": "questflight.traffic-v2-source-coverage.v2",
        "sources": source_summaries,
        "source_freeze_sha256": freeze_sha,
        "specification_sha256": registry["specification"]["sha256"],
    }
    conflict_report = {
        "blocking_count": len(blocking),
        "conflicts": conflicts,
        "forward_requirement_count": sum(str(value.get("severity", "")).startswith("FORWARD_") for value in conflicts),
        "gate_status": gate_status,
        "schema": "questflight.traffic-v2-source-conflicts.v2",
        "warning_count": sum(value.get("severity") == "WARNING" for value in conflicts),
    }
    missing = {
        "features": missing_features,
        "name": (
            "Traffic V2 independent official/reference observations unmatched or invalid after canonical conflation"
        ),
        "properties": {
            "feature_count": len(missing_features),
            "gate_status": gate_status,
            "schema": "questflight.traffic-v2-missing-roads.v2",
        },
        "type": "FeatureCollection",
    }

    data_dir.mkdir(parents=True, exist_ok=True)
    atomic_write_json(data_dir / "traffic_source_coverage.json", coverage)
    atomic_write_json(data_dir / "traffic_source_conflicts.json", conflict_report)
    atomic_write_json(data_dir / "traffic_missing_roads.geojson", missing)
    provenance_lines = [
        "# Traffic System V2 Phase-3 source provenance",
        "",
        f"- Gate status: **{gate_status}**",
        f"- Accepted source: `{registry['accepted_source_sha']}`",
        f"- Frozen specification SHA-256: `{registry['specification']['sha256']}`",
        f"- Acquisition completed UTC: `{freeze['acquisition_completed_utc']}`",
        f"- Source freeze SHA-256: `{sha256_file(freeze_path)}`",
        f"- Registry SHA-256: `{sha256_file(registry_path)}`",
        (
            f"- Canonical full-map universe: {canonical['canonical_universe_record_count']} records, "
            f"SHA-256 `{canonical['canonical_universe']['sha256']}`."
        ),
        (
            f"- Independent reference inventory: {canonical['reference_observation_count']} observations; "
            f"unmatched: {len(canonical['unmatched_reference_observations'])}."
        ),
        f"- Direct-evidence lane-km total: `{lane_metrics['known_lane_kilometers_total']}`.",
        (
            f"- Benchmark camera contract SHA-256: `{benchmark['camera_contract']['sha256']}`; "
            "15-view immutable candidate status: `PENDING_LEAD_D_INSPECTION`."
        ),
        f"- External coverage PNG SHA-256: `{png_record['sha256']}`",
        "- Raw downloads, normalized records, request bodies, receipts, and the PNG remain external.",
        "",
        "## Frozen sources",
        "",
        "| Source | Role | Features | License partition | Endpoint |",
        "|---|---:|---:|---|---|",
    ]
    for source in source_summaries:
        endpoint = source.get("layer_url") or next(
            (value.get("endpoint") for value in freeze["sources"] if value["id"] == source["id"]), ""
        )
        provenance_lines.append(
            f"| `{source['id']}` | {source['role']} | {source.get('feature_count')} | "
            f"{source['license_partition']} | {endpoint or ''} |"
        )
    provenance_lines.extend(["", "## Frozen license snapshots", ""])
    for snapshot in registry["license_snapshots"]:
        frozen_snapshot = freeze["license_snapshots"][snapshot["id"]]
        response = frozen_snapshot["receipt"]["response"]
        provenance_lines.append(
            f"- `{snapshot['id']}`: {snapshot['url']}; external `{response['path']}`; "
            f"SHA-256 `{response['sha256']}`; {response['bytes']} bytes."
        )
    provenance_lines.extend(["", "## Declared semantic authorities", ""])
    for authority in registry["authority_model"]["semantic_authorities"]:
        provenance_lines.append(
            f"- {authority['subject']}: {authority['url']} ({authority['license_partition']})."
        )
    provenance_lines.extend(["", "## Accepted-product bindings", ""])
    for product in products:
        provenance_lines.append(
            f"- `{product['id']}`: `{product['path']}`, SHA-256 `{product['sha256']}`, "
            f"Git blob `{product['git_blob_sha1']}`; {product['license_partition']}."
        )
    provenance_lines.extend(["", "## Fail-closed decisions", ""])
    for conflict in conflicts:
        provenance_lines.append(
            f"- **{conflict['severity']} / {conflict['status']}** `{conflict['id']}`: "
            f"{conflict['reason']} Resolution: {conflict.get('resolution', 'None frozen.')}"
        )
    provenance_lines.extend(["", "## Rejected source-gate attempts", ""])
    for attempt in registry["rejected_freezes"]:
        provenance_lines.append(
            f"- `{attempt['sha256']}` — **{attempt['status']}**: {attempt['reason']}"
        )
    provenance_lines.extend(
        [
            "",
            "## License separation",
            "",
            "Every ArcGIS OID and every unique raw OSM highway/access record has an external disposition row. "
            "Every canonical segment retains separate geometry, class-evidence, and lane-evidence source IDs, "
            "record IDs, confidence, and license partitions, plus the frozen correspondence semantics. "
            "CDOT CC-BY-SA, City CC0, County CC-BY, DRCOG attribution, OSM ODbL, and public-domain/project "
            "accepted inputs remain separable. OSM geometry is not copied into a non-ODbL shard.",
            "",
            "Coverage uses unique eligible source-priority canonical lane-km as denominator and the explicitly "
            "represented canonical members as numerator. A correctly dispositioned bounded OSM-only fallback "
            "member is represented source coverage; a second source is not required. Different-source proven "
            "subspan support with exact matching direct class and lane evidence is reported separately as "
            "non-gating corroboration, while semantic conflicts still block. There is no redundant "
            "source-observation-count threshold. Unknown class, lane, access, direction, carriageway, grade, "
            "correspondence, geometric length, or geometryless records remain explicit fail-closed audit entries. "
            "Lane-km is segment length times a direct source-priority lane count; road width is never converted "
            "to lanes.",
            "",
            "This source freeze is not FAA/navigation data, a lane-graph acceptance, a demand acceptance, or physical Quest evidence.",
            "",
        ]
    )
    atomic_write_bytes(
        data_dir / "traffic_source_provenance.md", "\n".join(provenance_lines).encode("utf-8")
    )
    approval_candidate = preserve_pending_approval_candidate(
        external_run,
        data_dir,
        png_path,
        allow_create=allow_candidate_creation,
        benchmark=benchmark,
        candidate_event_utc=candidate_event_utc or freeze["acquisition_completed_utc"],
        proposal_only=derivative_run is not None,
        source_freeze_sha256=freeze_sha,
    )
    approval = evaluate_universe_approval(
        registry, registry_path, external_run, benchmark, freeze_sha
    )
    effective_blocking = [
        value
        for value in blocking
        if not (
            approval["status"] == "LEAD_D_APPROVED"
            and value.get("id") == "benchmark_camera_road_inventory_gap"
        )
    ]
    effective_gate_status = "PASS" if not effective_blocking else "BLOCKED_AUTHORITY"
    approval_adjudication = {
        **approval,
        "candidate_artifacts_remain_immutable": True,
        "candidate_gate_status": gate_status,
        "effective_gate_status": effective_gate_status,
        "remaining_blocker_ids": sorted(value["id"] for value in effective_blocking),
    }

    outputs = {
        path.name: {
            "bytes": path.stat().st_size,
            "sha256": sha256_file(path),
        }
        for path in [data_dir / name for name in EXPECTED_TEXT_ARTIFACTS]
    }
    outputs[EXPECTED_EXTERNAL_ARTIFACT] = png_record
    run_context = build_compile_run_context(
        registry,
        registry_path,
        external_run,
        data_dir,
        evidence_dir,
        toolchain_binding,
        freeze_path,
        command_context=producer_command,
        git_context=git_worktree_context(),
    )
    run_context["outputs"] = outputs
    run_manifest = {
        "command": "compile",
        "approval_adjudication": approval_adjudication,
        "candidate_gate_status": gate_status,
        "freeze_sha256": sha256_file(freeze_path),
        "gate_status": effective_gate_status,
        "outer_package_index_path": PACKAGE_INDEX_NAME,
        "outputs": outputs,
        "approval_candidate": approval_candidate,
        "run_context": run_context,
        "schema": "questflight.traffic-v2-geospatial-run-manifest.v1",
        "tool_sha256": sha256_file(Path(__file__)),
    }
    atomic_write_json(manifest_path or (external_run / RUN_MANIFEST_NAME), run_manifest)
    if manifest_path is None and derivative_run is None:
        package_index = write_outer_package_index(registry, external_run, data_dir)
    else:
        package_index = None
    print(
        canonical_json_bytes(
            {**run_manifest, **({"outer_package_index": package_index} if package_index else {})}
        ).decode()
    )
    return run_manifest


def _stage_candidate_directory(
    stage_root: Path,
    data_dir: Path,
    png_path: Path,
    manifest: dict[str, Any],
) -> Path:
    """Build a complete hidden candidate proposal; write its manifest last."""

    candidate_root = stage_root / "approval_candidate"
    candidate_root.mkdir(parents=True, exist_ok=False)
    for name in EXPECTED_TEXT_ARTIFACTS:
        atomic_write_bytes(candidate_root / name, (data_dir / name).read_bytes())
    atomic_write_bytes(
        candidate_root / EXPECTED_EXTERNAL_ARTIFACT,
        png_path.read_bytes(),
    )
    atomic_write_json(candidate_root / "candidate_manifest.json", manifest)
    return candidate_root


def _remove_promoted_path(path: Path) -> None:
    if path.is_dir() and not path.is_symlink():
        shutil.rmtree(path)
    elif path.exists() or path.is_symlink():
        path.unlink()


def _promote_compile_stage(
    registry: dict[str, Any],
    external_run: Path,
    stage_root: Path,
    stage_work: Path,
    stage_data: Path,
    stage_evidence: Path,
    stage_manifest: Path,
    data_dir: Path,
    evidence_dir: Path,
    candidate_stage: Path,
    *,
    commit_validator: Any | None = None,
) -> None:
    """Promote one validated compile set with rollback and commit records last."""

    backup_root = stage_root / "rollback"
    moves: list[tuple[Path, Path | None]] = []
    created_parents: set[Path] = set()

    destination_plan = [
        *( (external_run / name, "dir") for name in ("normalized", "intermediates") ),
        *( (data_dir / name, "file") for name in EXPECTED_TEXT_ARTIFACTS ),
        (evidence_dir / EXPECTED_EXTERNAL_ARTIFACT, "file"),
        (external_run / RUN_MANIFEST_NAME, "file"),
        (external_run / PACKAGE_INDEX_NAME, "file"),
    ]
    for destination, expected_kind in destination_plan:
        kind = _path_kind(destination)
        if kind not in ("absent", expected_kind):
            raise GateError(
                f"Promotion destination has wrong filesystem kind {kind}: {destination}"
            )
    candidate_kind = _path_kind(external_run / "approval_candidate")
    if candidate_kind not in ("absent", "dir"):
        raise GateError("Approval-candidate destination is not a directory/absent")
    backup_root.mkdir(parents=True, exist_ok=False)

    def ensure_parent(parent: Path) -> None:
        missing: list[Path] = []
        current = parent
        while _path_kind(current) == "absent":
            missing.append(current)
            if current == current.parent:
                break
            current = current.parent
        if _path_kind(current) != "dir":
            raise GateError(f"Promotion parent has wrong filesystem kind: {current}")
        for path in reversed(missing):
            path.mkdir()
            created_parents.add(path)

    def promote(source: Path, destination: Path, expected_kind: str) -> None:
        if _path_kind(source) != expected_kind:
            raise GateError(f"Staged compile member is missing: {source.name}")
        existing_kind = _path_kind(destination)
        if existing_kind not in ("absent", expected_kind):
            raise GateError(
                f"Promotion destination has wrong filesystem kind {existing_kind}: {destination}"
            )
        ensure_parent(destination.parent)
        backup: Path | None = None
        if existing_kind == expected_kind:
            backup = backup_root / f"{len(moves):04d}_{destination.name}"
            shutil.move(str(destination), str(backup))
        moves.append((destination, backup))
        shutil.move(str(source), str(destination))

    try:
        for directory_name in ("normalized", "intermediates"):
            promote(
                stage_work / directory_name,
                external_run / directory_name,
                "dir",
            )
        for name in EXPECTED_TEXT_ARTIFACTS:
            promote(stage_data / name, data_dir / name, "file")
        promote(
            stage_evidence / EXPECTED_EXTERNAL_ARTIFACT,
            evidence_dir / EXPECTED_EXTERNAL_ARTIFACT,
            "file",
        )
        candidate_destination = external_run / "approval_candidate"
        candidate_kind = _path_kind(candidate_destination)
        if candidate_kind == "absent":
            promote(candidate_stage, candidate_destination, "dir")
        elif candidate_kind != "dir":
            raise GateError("Approval-candidate destination is not a directory")
        promote(stage_manifest, external_run / RUN_MANIFEST_NAME, "file")
        index_path = external_run / PACKAGE_INDEX_NAME
        index_backup: Path | None = None
        index_kind = _path_kind(index_path)
        if index_kind not in ("absent", "file"):
            raise GateError("Outer package-index destination is not a file/absent")
        if index_kind == "file":
            index_backup = backup_root / f"{len(moves):04d}_{PACKAGE_INDEX_NAME}"
            shutil.move(str(index_path), str(index_backup))
        moves.append((index_path, index_backup))
        write_outer_package_index(registry, external_run, data_dir)
        if commit_validator is not None:
            commit_validator()
    except Exception:
        for destination, backup in reversed(moves):
            _remove_promoted_path(destination)
            if backup is not None and backup.exists():
                ensure_parent(destination.parent)
                shutil.move(str(backup), str(destination))
        raise
    finally:
        if backup_root.exists():
            shutil.rmtree(backup_root)
        for parent in sorted(created_parents, key=lambda path: len(path.parts), reverse=True):
            try:
                parent.rmdir()
            except OSError:
                pass


def _validate_staged_file_records(
    value: Any,
    *,
    external_run: Path,
    stage_evidence: Path,
    stage_work: Path,
) -> None:
    """Validate every file record in staged normative content against live bytes."""

    if isinstance(value, dict):
        if set(("bytes", "path", "sha256")).issubset(value):
            relative_text = value.get("path")
            pure = PurePosixPath(str(relative_text))
            first = pure.parts[0] if pure.parts else None
            if first in ("normalized", "intermediates"):
                path = resolve_external_relative_path(
                    stage_work, relative_text, "Staged derivative file record"
                )
            elif first == "evidence":
                evidence_relative = PurePosixPath(str(relative_text))
                if len(evidence_relative.parts) < 2:
                    raise GateError("Staged evidence file record has no artifact name")
                path = resolve_external_relative_path(
                    stage_evidence,
                    PurePosixPath(*evidence_relative.parts[1:]).as_posix(),
                    "Staged evidence file record",
                )
            elif first in (EXTERNAL_RECORD_ROOTS | ARCHIVE_RECORD_ROOTS):
                path = resolve_external_relative_path(
                    external_run, relative_text, "Staged external file record"
                )
            else:
                path = resolve_repository_relative_path(
                    relative_text, "Staged repository file record"
                )
            size = value.get("bytes")
            digest = value.get("sha256")
            if (
                isinstance(size, bool)
                or not isinstance(size, int)
                or size < 0
                or not isinstance(digest, str)
                or re.fullmatch(r"[0-9a-f]{64}", digest) is None
            ):
                raise GateError(f"Staged file record metadata is invalid: {relative_text}")
            if not path.is_file():
                raise GateError(f"Staged file record is missing: {relative_text}")
            if path.stat().st_size != size or sha256_file(path) != digest:
                raise GateError(f"Staged file record bytes/hash mismatch: {relative_text}")
        for nested in value.values():
            _validate_staged_file_records(
                nested,
                external_run=external_run,
                stage_evidence=stage_evidence,
                stage_work=stage_work,
            )
    elif isinstance(value, list):
        for nested in value:
            _validate_staged_file_records(
                nested,
                external_run=external_run,
                stage_evidence=stage_evidence,
                stage_work=stage_work,
            )


def _required_staged_dynamic_blocker_ids(
    canonical: dict[str, Any],
    lane_inventory: dict[str, Any],
    denominator: dict[str, Any],
    class_coverage: dict[str, Any],
    missing_features: list[dict[str, Any]],
    benchmark: dict[str, Any],
) -> set[str]:
    """Derive every compile-time blocker whose cause is in staged content."""

    required: set[str] = set()
    if benchmark.get("status") != "LEAD_D_APPROVED":
        required.add("benchmark_camera_road_inventory_gap")
    if int(canonical.get("conflict_count", 0)) > 0:
        required.add("canonical_evidence_conflicts")
    if int(lane_inventory.get("unknown_class_segment_count", 0)) > 0:
        required.add("canonical_unknown_road_classes")
    if int(lane_inventory.get("unknown_lane_segment_count", 0)) > 0:
        required.add("canonical_unknown_lane_counts")
    if (
        int(lane_inventory.get("unresolved_geometric_segment_count", 0)) > 0
        or int(denominator.get("unresolved_geometric_motor_road_record_count", 0))
        > 0
    ):
        required.add("unresolved_motor_road_geometric_lengths")
    if int(denominator.get("geometryless_motor_road_record_count", 0)) > 0:
        required.add("geometryless_motor_road_observations")
    if any(
        int(value.get("unmatched_observation_count", 0)) > 0
        for value in (canonical.get("reference_class_audit") or {}).values()
    ):
        required.add("independent_reference_observations_unmatched")
    observation_kinds = {
        str((feature.get("properties") or {}).get("observation_kind") or "")
        for feature in missing_features
        if isinstance(feature, dict)
    }
    if "OSM_NORMALIZATION_FAILURE" in observation_kinds:
        required.add("invalid_or_unknown_osm_observations")
    if "ARCGIS_NORMALIZATION_FAILURE" in observation_kinds:
        required.add("arcgis_normalization_failures")
    if "INDEPENDENT_SOURCE_REFERENCE" in observation_kinds:
        required.add("independent_reference_observations_unmatched")
    for classification in ROAD_CLASSES:
        if (class_coverage.get(classification) or {}).get("status") != "PASS":
            required.add(f"{classification}_represented_lane_km_coverage_gap")
    return required


def _staged_record_path(
    stage_work: Path, record: dict[str, Any], label: str
) -> Path:
    if not isinstance(record, dict) or not set(("bytes", "path", "sha256")).issubset(
        record
    ):
        raise GateError(f"{label} does not contain an exact file record")
    return resolve_external_relative_path(stage_work, record["path"], label)


def _derive_independent_support_summary(
    ledger: list[dict[str, Any]],
) -> dict[str, Any]:
    by_class: dict[str, dict[str, Any]] = {
        classification: {
            "canonical_lane_kilometer_denominator": 0.0,
            "canonical_segment_count": 0,
            "independently_supported_lane_kilometers": 0.0,
            "supported_segment_count": 0,
            "unknown_lane_centerline_kilometers": 0.0,
            "unknown_lane_segment_count": 0,
            "unresolved_correspondence_centerline_kilometers": 0.0,
            "unresolved_correspondence_segment_count": 0,
        }
        for classification in ROAD_CLASSES
    }
    unknown_class_length = 0.0
    unknown_class_count = 0
    ids: set[str] = set()
    for row in ledger:
        canonical_id = str(row.get("canonical_segment_id") or "")
        if not canonical_id or canonical_id in ids:
            raise GateError("Independent-support ledger has a missing/duplicate canonical ID")
        ids.add(canonical_id)
        classification = row.get("road_class")
        length_km = float(row.get("length_meters") or 0.0) / 1000.0
        if classification not in ROAD_CLASSES:
            unknown_class_length += length_km
            unknown_class_count += 1
            continue
        summary = by_class[classification]
        summary["canonical_segment_count"] += 1
        denominator = row.get("canonical_lane_kilometer_denominator")
        numerator = row.get("independently_supported_lane_kilometers")
        if denominator is None:
            summary["unknown_lane_centerline_kilometers"] += length_km
            summary["unknown_lane_segment_count"] += 1
        else:
            if numerator is None:
                raise GateError("Known independent-support denominator has no numerator")
            summary["canonical_lane_kilometer_denominator"] += float(denominator)
            summary["independently_supported_lane_kilometers"] += float(numerator)
            if row.get("status") == "FULLY_SUPPORTED_BY_DIFFERENT_SOURCE":
                summary["supported_segment_count"] += 1
        unresolved = row.get("unresolved_correspondences") or []
        if unresolved:
            summary["unresolved_correspondence_segment_count"] += 1
            summary["unresolved_correspondence_centerline_kilometers"] += length_km
        links = row.get("support_links") or []
        excluded = set(row.get("independence_excluded_source_ids") or [])
        for link in links:
            if link.get("selected_canonical_segment_id") != canonical_id:
                raise GateError("Independent-support link is bound to another canonical member")
            if link.get("support_source_id") in excluded:
                raise GateError("Independent-support ledger contains circular evidence")
            class_evidence = link.get("class_evidence") or {}
            lane_evidence = link.get("lane_evidence") or {}
            if (
                class_evidence.get("decision") != "ELIGIBLE"
                or class_evidence.get("road_class") != classification
                or lane_evidence.get("lane_count") != row.get("lane_count")
            ):
                raise GateError("Independent-support link lacks exact direct class/lane compatibility")
    for summary in by_class.values():
        denominator = summary["canonical_lane_kilometer_denominator"]
        numerator = summary["independently_supported_lane_kilometers"]
        summary["canonical_lane_kilometer_denominator"] = round(denominator, 6)
        summary["independently_supported_lane_kilometers"] = round(numerator, 6)
        summary["unknown_lane_centerline_kilometers"] = round(
            summary["unknown_lane_centerline_kilometers"], 6
        )
        summary["unresolved_correspondence_centerline_kilometers"] = round(
            summary["unresolved_correspondence_centerline_kilometers"], 6
        )
        summary["independent_support_lane_kilometer_ratio"] = (
            round(numerator / denominator, 6)
            if denominator > 0.0
            and summary["unknown_lane_segment_count"] == 0
            and summary["unresolved_correspondence_segment_count"] == 0
            else None
        )
    return {
        "by_class": by_class,
        "measurement": "unique_canonical_length_km_times_source_priority_lane_count",
        "support_requirement": (
            "different_noncanonical-evidence_source_shared-correspondence_plus-exact-direct-class-and-lane"
        ),
        "unknown_class_centerline_kilometers": round(unknown_class_length, 6),
        "unknown_class_segment_count": unknown_class_count,
    }


def _derive_reference_representation_rows(
    registry: dict[str, Any],
    inventory_rows: list[dict[str, Any]],
    canonical_rows: list[dict[str, Any]],
    source_record_lookup: dict[tuple[str, str, int], dict[str, Any]],
) -> list[dict[str, Any]]:
    """Recompute every reference decision from normalized and canonical bytes."""

    inventory_by_id = {
        str(row.get("reference_observation_id") or ""): row
        for row in inventory_rows
    }
    if "" in inventory_by_id or len(inventory_by_id) != len(inventory_rows):
        raise GateError("Reference inventory has missing/duplicate observation IDs")
    reference_configs = {
        value["source_id"]: value
        for value in registry["authority_model"]["reference_sources"]
    }
    if len(reference_configs) != len(
        registry["authority_model"]["reference_sources"]
    ):
        raise GateError("Reference-source registry contains duplicate source IDs")
    represented_candidates = [
        row
        for row in canonical_rows
        if row.get("represented_source_disposition") == "CANONICAL_GEOMETRY"
    ]
    represented_tree = (
        STRtree([shape(row["geometry"]) for row in represented_candidates])
        if represented_candidates
        else None
    )
    derived: list[dict[str, Any]] = []
    for _, inventory in sorted(inventory_by_id.items()):
        source_id = str(inventory.get("source_id") or "")
        config = reference_configs.get(source_id)
        if config is None:
            raise GateError("Reference inventory names an undeclared source")
        key = (
            source_id,
            str(inventory.get("source_record_id") or ""),
            int(inventory.get("source_record_part", -1)),
        )
        source_record = source_record_lookup.get(key)
        if source_record is None:
            raise GateError("Reference inventory is not bound to a normalized source record")
        expected_observation = reference_observation_value(
            registry, config, source_record
        )
        expected_public_observation = {
            field: value
            for field, value in expected_observation.items()
            if not field.startswith("_")
        }
        if inventory != expected_public_observation:
            raise GateError(
                "Reference inventory semantics differ from the normalized source record"
            )
        representation, _, _, _, _ = reference_representation_ledger_value(
            registry,
            expected_observation,
            represented_candidates,
            source_record_lookup,
            represented_tree,
        )
        derived.append(representation)
    derived.sort(key=lambda value: value["reference_observation_id"])
    return derived


def _validate_exact_reference_representation_rows(
    registry: dict[str, Any],
    actual_rows: list[dict[str, Any]],
    inventory_rows: list[dict[str, Any]],
    canonical_rows: list[dict[str, Any]],
    source_record_lookup: dict[tuple[str, str, int], dict[str, Any]],
) -> list[dict[str, Any]]:
    derived = _derive_reference_representation_rows(
        registry, inventory_rows, canonical_rows, source_record_lookup
    )
    if actual_rows != derived:
        raise GateError(
            "Reference MATCHED/UNMATCHED/UNRESOLVED decisions are not "
            "independently reproducible"
        )
    return derived


def _derive_canonical_conflicts_from_ledgers(
    canonical_rows: list[dict[str, Any]],
    conflation_rows: list[dict[str, Any]],
    lane_rows: list[dict[str, Any]],
    support_rows: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    """Derive the exact canonical conflict inventory from its evidence ledgers."""

    expected: list[dict[str, Any]] = []
    for row in conflation_rows:
        evidence = row.get("source_precedence_conflicts") or []
        if not isinstance(evidence, list):
            raise GateError("Conflation conflict evidence is not an array")
        ambiguous = [
            value
            for value in evidence
            if value.get("id") == "ambiguous_spatial_conflation"
        ]
        if (row.get("decision") == "RETAINED_AMBIGUOUS_FAIL_CLOSED") != bool(
            ambiguous
        ):
            raise GateError(
                "Ambiguous conflation decision and its exact conflict evidence differ"
            )
        for conflict in evidence:
            if conflict.get("id") not in {
                "ambiguous_spatial_conflation",
                "inconsistent_same_source_geometry_duplicate",
            }:
                raise GateError("Conflation ledger contains an unknown conflict ID")
            if any(
                conflict.get(field) != row.get(field)
                for field in ("source_id", "source_record_id", "source_record_part")
            ):
                raise GateError("Conflation conflict is bound to another source record")
            expected.append(conflict)

    canonical_by_id = {
        str(row.get("canonical_segment_id") or ""): row for row in canonical_rows
    }
    if "" in canonical_by_id or len(canonical_by_id) != len(canonical_rows):
        raise GateError("Canonical conflict derivation lacks unique segment IDs")
    for canonical_id, row in canonical_by_id.items():
        evidence = row.get("conflicting_class_evidence") or []
        if not isinstance(evidence, list):
            raise GateError("Canonical class-conflict evidence is not an array")
        if evidence:
            if row.get("road_class") is not None or row.get(
                "class_evidence_source_id"
            ) is not None:
                raise GateError("Conflicting class evidence did not fail closed")
            expected.append(
                {
                    "canonical_segment_id": canonical_id,
                    "conflicting_class_evidence": evidence,
                    "id": "conflicting_class_evidence",
                }
            )

    lane_by_id = {
        str(row.get("canonical_segment_id") or ""): row for row in lane_rows
    }
    if "" in lane_by_id or len(lane_by_id) != len(lane_rows):
        raise GateError("Lane-evidence conflict derivation lacks unique segment IDs")
    if set(lane_by_id) != {
        canonical_id
        for canonical_id, row in canonical_by_id.items()
        if row.get("represented_source_disposition") == "CANONICAL_GEOMETRY"
    }:
        raise GateError("Lane-evidence ledger does not cover the represented universe")
    for canonical_id, row in lane_by_id.items():
        evidence = row.get("conflicting_lane_evidence") or []
        if not isinstance(evidence, list):
            raise GateError("Lane-conflict evidence is not an array")
        if evidence:
            if (
                row.get("lane_count") is not None
                or row.get("confidence") != "CONFLICT_FAIL_CLOSED"
                or row.get("reason") != "CONFLICTING_DIRECT_LANE_COUNTS"
                or canonical_by_id[canonical_id].get("lane_count") is not None
                or canonical_by_id[canonical_id].get("lane_evidence_source_id")
                is not None
            ):
                raise GateError("Conflicting lane evidence did not fail closed")
            expected.append(
                {
                    "canonical_segment_id": canonical_id,
                    "conflicting_lane_evidence": evidence,
                    "id": "conflicting_lane_evidence",
                }
            )

    support_by_id = {
        str(row.get("canonical_segment_id") or ""): row for row in support_rows
    }
    if "" in support_by_id or len(support_by_id) != len(support_rows):
        raise GateError("Support-conflict derivation lacks unique segment IDs")
    for canonical_id, row in support_by_id.items():
        semantic = row.get("semantic_ambiguities") or []
        incompatible = row.get("incompatible_lane_km_evidence") or []
        if not isinstance(semantic, list) or not isinstance(incompatible, list):
            raise GateError("Independent-support conflict evidence is not an array")
        if row.get("unresolved_correspondences") != [*semantic, *incompatible]:
            raise GateError(
                "Independent-support unresolved summary differs from field-specific evidence"
            )
        if semantic:
            expected.append(
                {
                    "canonical_segment_id": canonical_id,
                    "id": "unresolved_independent_support_correspondence",
                    "unresolved_support_records": semantic,
                }
            )
        if incompatible:
            expected.append(
                {
                    "canonical_segment_id": canonical_id,
                    "id": "incompatible_independent_lane_km_evidence",
                    "unresolved_support_records": incompatible,
                }
            )
    expected.sort(key=compact_json)
    return expected


def _validate_exact_canonical_conflict_ledger(
    conflict_rows: list[dict[str, Any]],
    canonical_rows: list[dict[str, Any]],
    conflation_rows: list[dict[str, Any]],
    lane_rows: list[dict[str, Any]],
    support_rows: list[dict[str, Any]],
) -> None:
    expected = _derive_canonical_conflicts_from_ledgers(
        canonical_rows, conflation_rows, lane_rows, support_rows
    )
    if conflict_rows != expected:
        raise GateError(
            "Canonical conflict ledger is not exactly derived from "
            "conflation/class/lane/support evidence"
        )


def _validate_staged_ledgers(
    registry: dict[str, Any],
    external_run: Path,
    stage_work: Path,
    coverage: dict[str, Any],
    freeze: dict[str, Any],
    missing_features: list[dict[str, Any]],
) -> None:
    """Derive source-gate summaries directly from every staged record ledger."""

    canonical = coverage.get("canonical_road_universe") or {}
    canonical_path = _staged_record_path(
        stage_work, canonical.get("canonical_universe") or {}, "Canonical universe"
    )
    canonical_rows = list(read_jsonl(canonical_path))
    canonical_ids = [str(row.get("canonical_segment_id") or "") for row in canonical_rows]
    if not all(canonical_ids) or len(canonical_ids) != len(set(canonical_ids)):
        raise GateError("Canonical universe ledger has missing/duplicate IDs")

    conflict_rows = list(
        read_jsonl(
            _staged_record_path(
                stage_work,
                canonical.get("conflict_ledger") or {},
                "Canonical conflict ledger",
            )
        )
    )
    if len(conflict_rows) != canonical.get("conflict_count"):
        raise GateError("Canonical conflict summary differs from its direct ledger")
    if canonical.get("conflicts") != conflict_rows:
        raise GateError("Canonical inline conflicts differ from the direct conflict ledger")

    support_rows = list(
        read_jsonl(
            _staged_record_path(
                stage_work,
                canonical.get("independent_lane_km_support_ledger") or {},
                "Independent lane-km support ledger",
            )
        )
    )
    represented_ids = {
        row["canonical_segment_id"]
        for row in canonical_rows
        if row.get("represented_source_disposition") == "CANONICAL_GEOMETRY"
    }
    if {row.get("canonical_segment_id") for row in support_rows} != represented_ids:
        raise GateError("Independent-support ledger does not cover exact represented universe")
    if _derive_independent_support_summary(support_rows) != canonical.get(
        "independent_lane_km_support"
    ):
        raise GateError("Independent-support summary is not derived from its ledger")

    frozen_sources = {row["id"]: row for row in freeze.get("sources") or []}
    coverage_sources = {row["id"]: row for row in coverage.get("sources") or []}
    if set(frozen_sources) != set(coverage_sources):
        raise GateError("Coverage/freeze source ID sets differ")
    staged_source_record_lookup: dict[
        tuple[str, str, int], dict[str, Any]
    ] = {}
    for source_id, source in sorted(coverage_sources.items()):
        summary = source.get("normalized") or {}
        normalized_record = summary.get("normalized")
        if not normalized_record:
            continue
        for row in read_jsonl(
            _staged_record_path(
                stage_work,
                normalized_record,
                f"{source_id} normalized source lookup",
            )
        ):
            key = (
                source_id,
                str(row.get("source_record_id") or ""),
                int(row.get("source_record_part", -1)),
            )
            if not key[1] or key in staged_source_record_lookup:
                raise GateError(
                    f"Normalized source lookup has a missing/duplicate key: {source_id}"
                )
            staged_source_record_lookup[key] = row

    reference_rows = list(
        read_jsonl(
            _staged_record_path(
                stage_work,
                canonical.get("reference_representation_ledger") or {},
                "Reference representation ledger",
            )
        )
    )
    reference_ids = [str(row.get("reference_observation_id") or "") for row in reference_rows]
    if not all(reference_ids) or len(reference_ids) != len(set(reference_ids)):
        raise GateError("Reference representation ledger has missing/duplicate IDs")
    inventory_rows = list(
        read_jsonl(
            _staged_record_path(
                stage_work,
                canonical.get("reference_inventory") or {},
                "Reference road inventory",
            )
        )
    )
    inventory_by_id = {
        row.get("reference_observation_id"): row for row in inventory_rows
    }
    if len(inventory_by_id) != len(inventory_rows) or set(inventory_by_id) != set(
        reference_ids
    ):
        raise GateError("Reference inventory and representation ledger ID sets differ")
    reference_rows = _validate_exact_reference_representation_rows(
        registry,
        reference_rows,
        inventory_rows,
        canonical_rows,
        staged_source_record_lookup,
    )
    disposition_counts = Counter(str(row.get("decision")) for row in reference_rows)
    motor_counts = Counter(
        str(row.get("motor_road_disposition"))
        for row in reference_rows
        if row.get("reference_role") == "road_centerline"
    )
    class_total: Counter[str] = Counter()
    class_matched: Counter[str] = Counter()
    for row in reference_rows:
        classification = (row.get("class_evidence") or {}).get("road_class")
        if row.get("motor_road_disposition") == "ELIGIBLE" and classification in ROAD_CLASSES:
            class_total[classification] += 1
            if row.get("decision") == "ELIGIBLE_REFERENCE_MATCHED":
                class_matched[classification] += 1
    derived_class_audit = {
        classification: {
            "matched_observation_count": class_matched[classification],
            "reference_observation_count": class_total[classification],
            "unmatched_observation_count": class_total[classification]
            - class_matched[classification],
        }
        for classification in ROAD_CLASSES
    }
    if (
        canonical.get("reference_observation_count") != len(reference_rows)
        or canonical.get("reference_disposition_counts")
        != dict(sorted(disposition_counts.items()))
        or canonical.get("motor_reference_disposition_counts")
        != dict(sorted(motor_counts.items()))
        or canonical.get("reference_class_audit") != derived_class_audit
    ):
        raise GateError("Reference summaries are not derived from the representation ledger")

    unmatched_rows = list(
        read_jsonl(
            _staged_record_path(
                stage_work,
                canonical.get("unmatched_reference_record") or {},
                "Unmatched-reference ledger",
            )
        )
    )
    derived_unmatched: list[dict[str, Any]] = []
    for row in reference_rows:
        decision = row.get("decision")
        if row.get("reference_role") != "road_centerline" or decision not in (
            "ELIGIBLE_REFERENCE_UNMATCHED",
            "UNRESOLVED_MOTOR_ROAD_ELIGIBILITY",
        ):
            continue
        if decision == "UNRESOLVED_MOTOR_ROAD_ELIGIBILITY":
            reason = "REFERENCE_MOTOR_ROAD_ELIGIBILITY_UNRESOLVED"
        elif row.get("unresolved_correspondences"):
            reason = "REFERENCE_CORRESPONDENCE_SEMANTICS_UNRESOLVED"
        else:
            reason = "REFERENCE_OBSERVATION_NOT_REPRESENTED"
        inventory = inventory_by_id[row["reference_observation_id"]]
        derived_unmatched.append(
            {
                "geometry": inventory.get("geometry"),
                "properties": {
                    "authority_inclusive_match_ratio": row.get(
                        "authority_inclusive_match_ratio"
                    ),
                    "different_source_corroboration_match_ratio": row.get(
                        "different_source_corroboration_match_ratio"
                    ),
                    "match_ratio": row.get("match_ratio"),
                    "match_scope": row.get("match_scope"),
                    "observation_kind": "INDEPENDENT_SOURCE_REFERENCE",
                    "reason": reason,
                    "reference_observation_id": row.get("reference_observation_id"),
                    "reference_role": row.get("reference_role"),
                    "road_class": (row.get("class_evidence") or {}).get("road_class"),
                    "source_id": row.get("source_id"),
                },
                "type": "Feature",
            }
        )
    derived_unmatched.sort(
        key=lambda row: row["properties"]["reference_observation_id"]
    )
    if Counter(map(compact_json, unmatched_rows)) != Counter(
        map(compact_json, derived_unmatched)
    ):
        raise GateError("Unmatched-reference ledger is not derived from reference dispositions")
    missing_reference = [
        row
        for row in missing_features
        if (row.get("properties") or {}).get("observation_kind")
        == "INDEPENDENT_SOURCE_REFERENCE"
    ]
    if Counter(map(compact_json, unmatched_rows)) != Counter(
        map(compact_json, missing_reference)
    ):
        raise GateError("Missing-road output differs from direct unmatched-reference ledger")

    normalized_summaries: list[dict[str, Any]] = []
    normalized_keys: set[tuple[str, str, int]] = set()
    disposition_part_keys: set[tuple[str, str, int]] = set()
    disposition_rows_by_source: dict[str, list[dict[str, Any]]] = {}
    for source_id, source in sorted(coverage_sources.items()):
        summary = source.get("normalized")
        if not summary or not summary.get("disposition_ledger"):
            continue
        normalized_summaries.append(summary)
        normalized_rows = list(
            read_jsonl(
                _staged_record_path(
                    stage_work, summary["normalized"], f"{source_id} normalized ledger"
                )
            )
        )
        source_normalized_keys = [
            (source_id, str(row.get("source_record_id")), int(row.get("source_record_part", -1)))
            for row in normalized_rows
        ]
        if len(source_normalized_keys) != len(set(source_normalized_keys)):
            raise GateError(f"Normalized ledger repeats record/part IDs: {source_id}")
        normalized_keys.update(source_normalized_keys)
        dispositions = list(
            read_jsonl(
                _staged_record_path(
                    stage_work,
                    summary["disposition_ledger"],
                    f"{source_id} disposition ledger",
                )
            )
        )
        disposition_rows_by_source[source_id] = dispositions
        disposition_ids = [str(row.get("source_record_id")) for row in dispositions]
        if len(disposition_ids) != len(set(disposition_ids)):
            raise GateError(f"Disposition ledger repeats source IDs: {source_id}")
        if summary.get("disposition_record_count") != len(dispositions):
            raise GateError(f"Disposition count differs from ledger: {source_id}")
        expected_ids: set[str]
        if source_id == registry["osm"]["id"]:
            frozen = frozen_sources[source_id]
            raw = json.loads(
                resolve_external_relative_path(
                    external_run, frozen["raw"]["path"], "Frozen OSM raw payload"
                ).read_text(encoding="utf-8")
            )
            unique, _ = deduplicate_osm_elements(raw.get("elements") or [])
            expected_ids = {
                f"way/{row['id']}" for row in unique if row.get("type") == "way"
            }
            required_disposition_fields = {
                "motor_road_disposition",
                "raw_access_evidence",
                "raw_highway",
                "tags",
            }
        else:
            expected_ids = {
                str(value)
                for page in frozen_sources[source_id].get("pages") or []
                for value in page.get("requested_ids") or []
            }
            required_disposition_fields = {
                "attributes",
                "motor_road_disposition",
            }
        if set(disposition_ids) != expected_ids:
            raise GateError(f"Disposition ledger differs from frozen source IDs: {source_id}")
        if any(not required_disposition_fields.issubset(row) for row in dispositions):
            raise GateError(f"Disposition ledger silently drops semantic evidence: {source_id}")
        for disposition in dispositions:
            for part_id in disposition.get("normalized_part_ids") or []:
                prefix = f"{disposition['source_record_id']}:"
                if not isinstance(part_id, str) or not part_id.startswith(prefix):
                    raise GateError(f"Disposition normalized-part binding is malformed: {source_id}")
                try:
                    part = int(part_id[len(prefix) :])
                except ValueError as error:
                    raise GateError(
                        f"Disposition normalized-part binding is malformed: {source_id}"
                    ) from error
                disposition_part_keys.add((source_id, disposition["source_record_id"], part))
    if normalized_keys != disposition_part_keys:
        raise GateError("Normalized/disposition record-part closure is not exact")
    derived_denominator = normalization_denominator_audit(
        normalized_summaries, stage_work
    )
    if coverage.get("normalization_denominator_audit") != derived_denominator:
        raise GateError("Normalization denominator is not derived from disposition ledgers")

    osm_failures: list[dict[str, Any]] = []
    for disposition in disposition_rows_by_source.get(registry["osm"]["id"], []):
        if not (
            disposition.get("disposition") == "UNKNOWN_BLOCKING"
            or (
                disposition.get("motor_road_disposition") == "ELIGIBLE"
                and disposition.get("reason") is not None
            )
        ):
            continue
        osm_failures.append(
            {
                "geometry": disposition.get("geometry"),
                "properties": {
                    "highway_class": disposition.get("highway_class"),
                    "missing_node_count": disposition.get(
                        "missing_referenced_node_count", 0
                    ),
                    "observation_kind": "OSM_NORMALIZATION_FAILURE",
                    "raw_highway": disposition.get("raw_highway"),
                    "reason": disposition.get("reason"),
                    "source_id": f"osm:{disposition.get('source_record_id')}",
                },
                "type": "Feature",
            }
        )
    arcgis_failures = arcgis_normalization_failures(
        normalized_summaries, stage_work
    )
    expected_missing = derived_unmatched + osm_failures + arcgis_failures
    expected_missing.sort(key=lambda row: compact_json(row.get("properties") or {}))
    if Counter(map(compact_json, missing_features)) != Counter(
        map(compact_json, expected_missing)
    ):
        raise GateError("Missing-road output is not derived from source disposition ledgers")

    conflation_rows = list(
        read_jsonl(
            _staged_record_path(
                stage_work,
                canonical.get("conflation_ledger") or {},
                "Road conflation ledger",
            )
        )
    )
    conflation_key_list = [
        (str(row.get("source_id")), str(row.get("source_record_id")), int(row.get("source_record_part", -1)))
        for row in conflation_rows
    ]
    if len(conflation_key_list) != len(set(conflation_key_list)):
        raise GateError("Road conflation ledger repeats a source record/part")
    conflation_keys = set(conflation_key_list)
    canonical_source_keys = {
        (str(row.get("source_id")), str(row.get("source_record_id")), int(row.get("source_record_part", -1)))
        for row in canonical_rows
    }
    road_sources = {
        row["source_id"]
        for row in registry["authority_model"]["canonical_centerline_precedence"]
    } | {
        row["source_id"] for row in registry["authority_model"]["reference_sources"]
    }
    road_normalized_keys = {key for key in normalized_keys if key[0] in road_sources}
    if not road_normalized_keys.issubset(conflation_keys | canonical_source_keys):
        raise GateError("Road conflation/canonical ledgers omit normalized source records")
    lane_rows = list(
        read_jsonl(
            _staged_record_path(
                stage_work,
                canonical.get("lane_evidence_ledger") or {},
                "Lane-evidence ledger",
            )
        )
    )
    _validate_exact_canonical_conflict_ledger(
        conflict_rows,
        canonical_rows,
        conflation_rows,
        lane_rows,
        support_rows,
    )


def _validate_staged_phase3_semantics(
    registry: dict[str, Any],
    external_run: Path,
    stage_work: Path,
    stage_evidence: Path,
    coverage: dict[str, Any],
    conflict_report: dict[str, Any],
    missing: dict[str, Any],
    provenance: str,
    run_manifest: dict[str, Any],
    candidate_manifest: dict[str, Any],
    verified_naip_authority: VerifiedNaipAuthority | None,
) -> None:
    """Recompute candidate semantics before any active artifact is promoted."""

    if coverage.get("schema") != "questflight.traffic-v2-source-coverage.v2":
        raise GateError("Staged coverage schema is invalid")
    if conflict_report.get("schema") != "questflight.traffic-v2-source-conflicts.v2":
        raise GateError("Staged conflict-report schema is invalid")
    if missing.get("type") != "FeatureCollection" or (
        missing.get("properties") or {}
    ).get("schema") != "questflight.traffic-v2-missing-roads.v2":
        raise GateError("Staged missing-road GeoJSON schema is invalid")
    gate_status = coverage.get("gate_status")
    if gate_status not in ("PASS", "BLOCKED_AUTHORITY"):
        raise GateError("Staged coverage gate status is invalid")

    _validate_staged_file_records(
        coverage,
        external_run=external_run,
        stage_evidence=stage_evidence,
        stage_work=stage_work,
    )
    canonical = coverage.get("canonical_road_universe") or {}
    canonical_record = canonical.get("canonical_universe") or {}
    canonical_path = resolve_external_relative_path(
        stage_work,
        canonical_record.get("path"),
        "Staged canonical universe",
    )
    actual_count = sum(1 for _ in read_jsonl(canonical_path))
    expected_count = canonical.get("canonical_universe_record_count")
    if (
        isinstance(expected_count, bool)
        or not isinstance(expected_count, int)
        or actual_count != expected_count
    ):
        raise GateError("Staged canonical-universe record count is inconsistent")

    recomputed_lanes = lane_kilometer_summary(canonical_path)
    if coverage.get("lane_kilometer_inventory") != recomputed_lanes:
        raise GateError("Staged lane-kilometer inventory is not reproducible")
    denominator = coverage.get("normalization_denominator_audit") or {}
    required_denominator_fields = {
        "geometryless_motor_road_record_count",
        "unresolved_geometric_motor_road_record_count",
    }
    if not required_denominator_fields.issubset(denominator):
        raise GateError("Staged normalization denominator audit is incomplete")
    support_by_class = (
        (canonical.get("independent_lane_km_support") or {}).get("by_class") or {}
    )
    reference_by_class = canonical.get("reference_class_audit") or {}
    class_coverage = coverage.get("class_coverage") or {}
    if (
        set(class_coverage) != set(ROAD_CLASSES)
        or set(recomputed_lanes["by_class"]) != set(ROAD_CLASSES)
        or set(support_by_class) != set(ROAD_CLASSES)
        or set(reference_by_class) != set(ROAD_CLASSES)
    ):
        raise GateError("Staged per-class source-coverage inventory is incomplete")
    for classification in ROAD_CLASSES:
        expected = class_source_coverage(
            classification,
            recomputed_lanes["by_class"][classification],
            support_by_class[classification],
            reference_by_class[classification],
            geometryless_motor_road_record_count=int(
                denominator["geometryless_motor_road_record_count"]
            ),
            unresolved_geometric_motor_road_record_count=int(
                denominator["unresolved_geometric_motor_road_record_count"]
            ),
            unknown_class_segment_count=int(
                recomputed_lanes["unknown_class_segment_count"]
            ),
        )
        if class_coverage[classification] != expected:
            raise GateError(
                f"Staged {classification} source coverage is not reproducible"
            )

    bounds = coverage.get("bounds_evaluation") or {}
    recomputed_cells = cell_inventory(registry, canonical_path)
    if (
        bounds.get("cell_count") != 576
        or bounds.get("evaluated_cell_count") != 576
        or bounds.get("status") != "PASS"
        or bounds.get("exact_bounds_xz_meters")
        != registry["coordinate_contract"]["bounds_xz_meters"]
        or bounds.get("cells") != recomputed_cells
    ):
        raise GateError("Staged exact-bound cell evaluation is not reproducible")

    benchmark = coverage.get("benchmark_camera_coverage") or {}
    conflicts = conflict_report.get("conflicts")
    blockers = coverage.get("authority_blockers")
    if not isinstance(conflicts, list) or not isinstance(blockers, list):
        raise GateError("Staged conflict/blocker ledgers are not arrays")
    if any(
        not isinstance(value, dict)
        or not isinstance(value.get("id"), str)
        or not value["id"]
        or not isinstance(value.get("reason"), str)
        or not isinstance(value.get("severity"), str)
        or not isinstance(value.get("status"), str)
        for value in conflicts
    ):
        raise GateError("Staged conflict ledger contains an incomplete record")
    conflict_ids = [value["id"] for value in conflicts]
    if len(conflict_ids) != len(set(conflict_ids)):
        raise GateError("Staged conflict ledger contains duplicate IDs")
    recomputed_blockers = [
        value
        for value in conflicts
        if value.get("severity") == "BLOCKING" and value.get("status") != "RESOLVED"
    ]
    if Counter(map(compact_json, blockers)) != Counter(
        map(compact_json, recomputed_blockers)
    ):
        raise GateError("Staged coverage blockers differ from the conflict report")
    features = missing.get("features")
    properties = missing.get("properties") or {}
    if not isinstance(features, list):
        raise GateError("Staged missing-road feature ledger is not an array")
    freeze_path = external_run / FREEZE_NAME
    if (
        not freeze_path.is_file()
        or sha256_file(freeze_path) != coverage.get("source_freeze_sha256")
    ):
        raise GateError("Staged coverage does not bind the exact live source freeze")
    try:
        freeze = json.loads(freeze_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise GateError("Live source freeze is not valid UTF-8 JSON") from error
    _validate_staged_ledgers(
        registry,
        external_run,
        stage_work,
        coverage,
        freeze,
        features,
    )
    expected_base_conflicts = source_conflicts(
        registry,
        freeze,
        [],
        {"status": "PENDING_LEAD_D_INSPECTION"},
        verified_naip_authority,
    )
    actual_by_id = {value["id"]: value for value in conflicts}
    for expected in expected_base_conflicts:
        if actual_by_id.get(expected["id"]) != expected:
            raise GateError(
                f"Staged conflict ledger omits or changes frozen conflict: {expected['id']}"
            )
    required_dynamic_ids = _required_staged_dynamic_blocker_ids(
        canonical,
        recomputed_lanes,
        denominator,
        class_coverage,
        features,
        benchmark,
    )
    expected_conflict_ids = {
        value["id"] for value in expected_base_conflicts
    } | required_dynamic_ids
    if set(conflict_ids) != expected_conflict_ids:
        raise GateError(
            "Staged conflict ledger does not contain the exactly derived blocker inventory"
        )
    expected_blocker_ids = {
        value["id"]
        for value in expected_base_conflicts
        if value.get("severity") == "BLOCKING" and value.get("status") != "RESOLVED"
    } | required_dynamic_ids
    if {value["id"] for value in recomputed_blockers} != expected_blocker_ids:
        raise GateError("Staged conflict ledger omits a derived blocking authority gap")
    expected_gate = "PASS" if not expected_blocker_ids else "BLOCKED_AUTHORITY"
    if (
        conflict_report.get("blocking_count") != len(recomputed_blockers)
        or conflict_report.get("warning_count")
        != sum(value.get("severity") == "WARNING" for value in conflicts)
        or conflict_report.get("forward_requirement_count")
        != sum(
            str(value.get("severity", "")).startswith("FORWARD_")
            for value in conflicts
        )
        or conflict_report.get("gate_status") != expected_gate
        or gate_status != expected_gate
    ):
        raise GateError("Staged conflict counts or gate status are inconsistent")

    if (
        properties.get("feature_count") != len(features)
        or properties.get("gate_status") != gate_status
        or any(
            not isinstance(feature, dict)
            or feature.get("type") != "Feature"
            or not isinstance(feature.get("properties"), dict)
            or (
                feature.get("geometry") is not None
                and not isinstance(feature.get("geometry"), dict)
            )
            for feature in features
        )
    ):
        raise GateError("Staged missing-road ledger is malformed or inconsistent")

    bindings = benchmark.get("view_bindings") or []
    if (
        benchmark.get("status") != "PENDING_LEAD_D_INSPECTION"
        or (benchmark.get("approval") or {}).get("status")
        != "PENDING_LEAD_D_INSPECTION"
        or benchmark.get("view_count") != 15
        or len(bindings) != 15
        or len({value.get("view_id") for value in bindings}) != 15
    ):
        raise GateError("Staged benchmark-camera candidate is not the exact pending 15-view set")
    universe_sha = canonical_record.get("sha256")
    if any(
        value.get("canonical_universe_sha256") != universe_sha
        or value.get("canonical_universe_record_count") != expected_count
        or value.get("origin_inside_frozen_bounds") is not True
        or value.get("road_candidate_set") != "FULL_MAP_UNIVERSE_SUPERSET"
        for value in bindings
    ):
        raise GateError("Staged benchmark view does not bind the full canonical universe")

    candidate_expected = {
        "camera_contract_sha256": (benchmark.get("camera_contract") or {}).get(
            "sha256"
        ),
        "canonical_universe_record_count": expected_count,
        "canonical_universe_sha256": universe_sha,
        "source_freeze_sha256": coverage.get("source_freeze_sha256"),
    }
    if (
        candidate_manifest.get("schema")
        != "questflight.traffic-v2-geospatial-approval-candidate.v1"
        or candidate_manifest.get("status") != "PENDING_LEAD_D_INSPECTION"
        or any(candidate_manifest.get(key) != value for key, value in candidate_expected.items())
    ):
        raise GateError("Staged approval candidate does not bind the staged authority inputs")
    require_not_future(candidate_manifest.get("candidate_event_utc"), "Candidate event UTC")
    if (
        run_manifest.get("schema")
        != "questflight.traffic-v2-geospatial-run-manifest.v1"
        or run_manifest.get("freeze_sha256") != coverage.get("source_freeze_sha256")
        or run_manifest.get("candidate_gate_status") != gate_status
        or (run_manifest.get("approval_adjudication") or {}).get(
            "candidate_gate_status"
        )
        != gate_status
        or (run_manifest.get("run_context") or {}).get("outputs")
        != run_manifest.get("outputs")
    ):
        raise GateError("Staged run manifest does not bind the staged candidate semantics")

    evidence = (coverage.get("evidence") or {}).get(EXPECTED_EXTERNAL_ARTIFACT)
    if evidence != (run_manifest.get("outputs") or {}).get(EXPECTED_EXTERNAL_ARTIFACT):
        raise GateError("Staged coverage evidence record differs from the run manifest")
    required_provenance_fragments = (
        "# Traffic System V2 Phase-3 source provenance",
        f"- Gate status: **{gate_status}**",
        f"- Accepted source: `{coverage.get('accepted_source_sha')}`",
        f"- Frozen specification SHA-256: `{coverage.get('specification_sha256')}`",
        f"- Source freeze SHA-256: `{coverage.get('source_freeze_sha256')}`",
        (
            f"- Canonical full-map universe: {expected_count} records, "
            f"SHA-256 `{universe_sha}`."
        ),
        f"- External coverage PNG SHA-256: `{evidence.get('sha256')}`",
    )
    if any(fragment not in provenance for fragment in required_provenance_fragments):
        raise GateError("Staged provenance omits a normative hash/status binding")
    if any(f"`{value['id']}`" not in provenance for value in conflicts):
        raise GateError("Staged provenance omits a conflict disposition")


def _validate_staged_compile_set(
    registry: dict[str, Any],
    external_run: Path,
    stage_work: Path,
    run_manifest: dict[str, Any],
    stage_data: Path,
    stage_evidence: Path,
    candidate_manifest: dict[str, Any],
    *,
    verified_naip_authority: VerifiedNaipAuthority | None,
) -> None:
    expected_outputs = run_manifest.get("outputs") or {}
    exact_names = set(EXPECTED_TEXT_ARTIFACTS) | {EXPECTED_EXTERNAL_ARTIFACT}
    if set(expected_outputs) != exact_names:
        raise GateError("Staged compile manifest does not bind exactly the five Phase-3 artifacts")
    for name in EXPECTED_TEXT_ARTIFACTS:
        path = stage_data / name
        expected = expected_outputs[name]
        if (
            not path.is_file()
            or path.stat().st_size != expected.get("bytes")
            or sha256_file(path) != expected.get("sha256")
        ):
            raise GateError(f"Staged normative artifact failed exact validation: {name}")
    png_path = stage_evidence / EXPECTED_EXTERNAL_ARTIFACT
    expected_png = expected_outputs[EXPECTED_EXTERNAL_ARTIFACT]
    if (
        not png_path.is_file()
        or png_path.stat().st_size != expected_png.get("bytes")
        or sha256_file(png_path) != expected_png.get("sha256")
    ):
        raise GateError("Staged coverage PNG failed exact validation")
    with Image.open(png_path) as image:
        if image.format != "PNG" or image.size != (1600, 1600):
            raise GateError("Staged coverage PNG dimensions/format are invalid")
    if candidate_manifest.get("artifact_sha256") != {
        name: expected_outputs[name]["sha256"] for name in sorted(exact_names)
    }:
        raise GateError("Staged candidate hashes differ from the staged output set")
    candidate_bytes = canonical_json_bytes(candidate_manifest)
    expected_candidate_record = {
        "bytes": len(candidate_bytes),
        "path": "approval_candidate/candidate_manifest.json",
        "sha256": sha256_bytes(candidate_bytes),
    }
    if run_manifest.get("approval_candidate") != expected_candidate_record:
        raise GateError("Staged run manifest does not bind the exact candidate manifest")
    try:
        coverage = json.loads(
            (stage_data / "traffic_source_coverage.json").read_text(encoding="utf-8")
        )
        conflict_report = json.loads(
            (stage_data / "traffic_source_conflicts.json").read_text(encoding="utf-8")
        )
        missing = json.loads(
            (stage_data / "traffic_missing_roads.geojson").read_text(encoding="utf-8")
        )
        provenance = (stage_data / "traffic_source_provenance.md").read_text(
            encoding="utf-8"
        )
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise GateError("Staged Phase-3 text artifact is not valid UTF-8/JSON") from error
    if not all(isinstance(value, dict) for value in (coverage, conflict_report, missing)):
        raise GateError("Staged Phase-3 JSON artifact root is not an object")
    _validate_staged_phase3_semantics(
        registry,
        external_run,
        stage_work,
        stage_evidence,
        coverage,
        conflict_report,
        missing,
        provenance,
        run_manifest,
        candidate_manifest,
        verified_naip_authority,
    )


def frozen_candidate_event_utc(external_run: Path) -> str:
    candidate_path = external_run / "approval_candidate/candidate_manifest.json"
    if not candidate_path.is_file():
        freeze_path = external_run / FREEZE_NAME
        try:
            freeze = json.loads(freeze_path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as error:
            raise GateError(
                "Source freeze is unavailable for deterministic candidate time"
            ) from error
        event = freeze.get("acquisition_completed_utc")
        require_not_future(event, "Freeze acquisition-completed UTC")
        return event
    try:
        candidate = json.loads(candidate_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise GateError("Existing approval candidate manifest is invalid") from error
    event = candidate.get("candidate_event_utc")
    require_not_future(event, "Candidate event UTC")
    return event


def compile_artifacts(
    registry_path: Path,
    external_run_path: Path,
    data_dir: Path,
    evidence_dir: Path,
    *,
    allow_candidate_creation: bool = True,
    derivative_run: Path | None = None,
    manifest_path: Path | None = None,
    producer_command: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Compile in isolation and promote only a fully validated coherent set."""

    if derivative_run is not None or manifest_path is not None:
        raise GateError("Public compile does not accept direct derivative/output paths")
    registry = load_registry(registry_path)
    validate_pinned_inputs(registry)
    preflight = validate_frozen_run_closure(registry_path, external_run_path)
    topology = validate_command_path_topology(
        registry,
        external_run_path,
        command="compile",
        data_dir=data_dir,
        evidence_dir=evidence_dir,
    )
    external_run = topology["external_run"]
    data_dir = topology["data_dir"]
    evidence_dir = topology["evidence_dir"]
    effective_command = producer_command or compile_command_context(
        registry_path, external_run, data_dir, evidence_dir
    )
    validate_recorded_compile_command(
        effective_command,
        registry_path=registry_path,
        external_run=external_run,
        data_dir=data_dir,
        evidence_dir=evidence_dir,
    )
    target_set_lock_plan(compile_target_paths(data_dir, evidence_dir))
    initial_snapshot = immutable_authority_snapshot(preflight, registry_path)
    with exclusive_operation_lock(
        external_run, "compile", allow_root_creation=False
    ):
        with exclusive_target_set_lock(
            compile_target_paths(data_dir, evidence_dir), "compile"
        ):
            validate_command_path_topology(
                registry,
                external_run,
                command="compile",
                data_dir=data_dir,
                evidence_dir=evidence_dir,
            )
            preflight = validate_frozen_run_closure(registry_path, external_run)
            registry = preflight["registry"]
            require_same_authority_snapshot(
                initial_snapshot,
                immutable_authority_snapshot(preflight, registry_path),
                allow_candidate_creation=False,
            )
            validate_recorded_compile_command(
                effective_command,
                registry_path=registry_path,
                external_run=external_run,
                data_dir=data_dir,
                evidence_dir=evidence_dir,
            )
            with tempfile.TemporaryDirectory(
                prefix="traffic_v2_compile_", dir=external_run.parent
            ) as temporary_name:
                stage_root = Path(temporary_name)
                stage_work = stage_root / "work"
                stage_data = stage_root / "data"
                stage_evidence = stage_root / "evidence"
                stage_manifest = stage_root / RUN_MANIFEST_NAME
                candidate_event_utc = frozen_candidate_event_utc(external_run)
                run_manifest = _compile_artifacts_core(
                    registry_path,
                    external_run,
                    stage_data,
                    stage_evidence,
                    verified_naip_authority=preflight["verified_naip_authority"],
                    allow_candidate_creation=allow_candidate_creation,
                    candidate_event_utc=candidate_event_utc,
                    derivative_run=stage_work,
                    manifest_path=stage_manifest,
                    producer_command=effective_command,
                    producer_data_dir=data_dir,
                    producer_evidence_dir=evidence_dir,
                )
                coverage = json.loads(
                    (stage_data / "traffic_source_coverage.json").read_text(
                        encoding="utf-8"
                    )
                )
                candidate_manifest = approval_candidate_value(
                    stage_data,
                    stage_evidence / EXPECTED_EXTERNAL_ARTIFACT,
                    benchmark=coverage["benchmark_camera_coverage"],
                    candidate_event_utc=candidate_event_utc,
                    source_freeze_sha256=sha256_file(preflight["freeze_path"]),
                )
                _validate_staged_compile_set(
                    registry,
                    external_run,
                    stage_work,
                    run_manifest,
                    stage_data,
                    stage_evidence,
                    candidate_manifest,
                    verified_naip_authority=preflight["verified_naip_authority"],
                )
                candidate_stage = _stage_candidate_directory(
                    stage_root,
                    stage_data,
                    stage_evidence / EXPECTED_EXTERNAL_ARTIFACT,
                    candidate_manifest,
                )
                # The complete authority/command closure must still be exact
                # immediately before any target byte is moved.
                precommit = validate_frozen_run_closure(registry_path, external_run)
                require_same_authority_snapshot(
                    initial_snapshot,
                    immutable_authority_snapshot(precommit, registry_path),
                    allow_candidate_creation=False,
                )
                validate_recorded_compile_command(
                    effective_command,
                    registry_path=registry_path,
                    external_run=external_run,
                    data_dir=data_dir,
                    evidence_dir=evidence_dir,
                )

                def validate_committed_set() -> None:
                    committed = validate_frozen_run_closure(
                        registry_path, external_run
                    )
                    require_same_authority_snapshot(
                        initial_snapshot,
                        immutable_authority_snapshot(committed, registry_path),
                        allow_candidate_creation=allow_candidate_creation,
                    )
                    validate_recorded_compile_command(
                        effective_command,
                        registry_path=registry_path,
                        external_run=external_run,
                        data_dir=data_dir,
                        evidence_dir=evidence_dir,
                    )
                    report = _validate_outputs_core(
                        registry_path, external_run, data_dir, evidence_dir
                    )
                    if report["status"] != "PASS":
                        raise GateError(
                            "Promoted Phase-3 set failed complete semantic/package validation: "
                            + "; ".join(report["problems"][:10])
                        )

                _promote_compile_stage(
                    registry,
                    external_run,
                    stage_root,
                    stage_work,
                    stage_data,
                    stage_evidence,
                    stage_manifest,
                    data_dir,
                    evidence_dir,
                    candidate_stage,
                    commit_validator=validate_committed_set,
                )
                return run_manifest


def collect_external_file_records(
    value: Any, records: dict[str, dict[str, Any]] | None = None
) -> dict[str, dict[str, Any]]:
    """Collect exact external records and reject conflicting claims per path."""

    records = records if records is not None else {}
    if isinstance(value, dict):
        if set(("bytes", "path", "sha256")).issubset(value):
            relative_text = str(value["path"])
            relative = PurePosixPath(relative_text)
            if relative.parts and relative.parts[0] in (
                EXTERNAL_RECORD_ROOTS | ARCHIVE_RECORD_ROOTS
            ):
                if (
                    relative.is_absolute()
                    or relative.as_posix() != relative_text
                    or any(part in ("", ".", "..") for part in relative.parts)
                    or isinstance(value.get("bytes"), bool)
                    or not isinstance(value.get("bytes"), int)
                    or value["bytes"] < 0
                    or not isinstance(value.get("sha256"), str)
                    or re.fullmatch(r"[0-9a-f]{64}", value["sha256"]) is None
                ):
                    raise GateError(f"External file record is invalid: {relative_text}")
                record = {
                    "bytes": value["bytes"],
                    "path": relative_text,
                    "sha256": value["sha256"],
                }
                previous = records.get(relative_text)
                if previous is not None and previous != record:
                    raise GateError(
                        f"Conflicting external file records claim one path: {relative_text}"
                    )
                records[relative_text] = record
        for nested in value.values():
            collect_external_file_records(nested, records)
    elif isinstance(value, list):
        for nested in value:
            collect_external_file_records(nested, records)
    return records


def collect_external_record_paths(value: Any) -> set[str]:
    """Compatibility view over the exact record collector."""

    return set(collect_external_file_records(value))


def require_live_external_records(
    external_run: Path, records: dict[str, dict[str, Any]]
) -> None:
    for relative, record in sorted(records.items()):
        path = resolve_external_relative_path(
            external_run, relative, "Manifest-bound external record"
        )
        if (
            not path.is_file()
            or path.stat().st_size != record["bytes"]
            or sha256_file(path) != record["sha256"]
        ):
            raise GateError(
                f"External package has missing files referenced by exact manifests: {relative}"
            )


def read_json_object(
    path: Path, label: str, *, require_canonical: bool = False
) -> dict[str, Any]:
    """Read a UTF-8 JSON object, optionally requiring canonical byte spelling."""

    if not path.is_file():
        raise GateError(f"{label} is missing")
    try:
        payload = path.read_bytes()
        value = json.loads(payload.decode("utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise GateError(f"{label} is not valid UTF-8 JSON") from error
    if not isinstance(value, dict):
        raise GateError(f"{label} JSON root is not an object")
    if require_canonical and payload != canonical_json_bytes(value):
        raise GateError(f"{label} bytes are not canonical JSON")
    return value


def validate_rebuild_package_closure(
    registry: dict[str, Any],
    external_run: Path,
    verified_naip_authority: VerifiedNaipAuthority,
) -> None:
    """Revalidate every rebuilt derivative and report before it can be indexed."""

    rebuild_root = external_run / "rebuild_check"
    kind = _path_kind(rebuild_root)
    if kind == "absent":
        return
    if kind != "dir":
        raise GateError("Rebuild-check package has the wrong filesystem kind")
    validate_tree_no_reparse(rebuild_root, "Rebuild-check package")
    rebuild_data = rebuild_root / "data"
    rebuild_evidence = rebuild_root / "evidence"
    rebuild_work = rebuild_root / "work"
    run_manifest = read_json_object(
        rebuild_root / RUN_MANIFEST_NAME,
        "Rebuild run manifest",
        require_canonical=True,
    )
    report = read_json_object(
        rebuild_root / "rebuild_check.json", "Rebuild report", require_canonical=True
    )
    coverage = read_json_object(
        rebuild_data / "traffic_source_coverage.json",
        "Rebuilt coverage artifact",
        require_canonical=True,
    )
    candidate = read_json_object(
        external_run / "approval_candidate/candidate_manifest.json",
        "Immutable approval-candidate manifest",
        require_canonical=True,
    )
    for value in (coverage, run_manifest):
        _validate_staged_file_records(
            value,
            external_run=external_run,
            stage_evidence=rebuild_evidence,
            stage_work=rebuild_work,
        )
    _validate_staged_compile_set(
        registry,
        external_run,
        rebuild_work,
        run_manifest,
        rebuild_data,
        rebuild_evidence,
        candidate,
        verified_naip_authority=verified_naip_authority,
    )
    if set(report) != {"mismatches", "rebuilt_outputs", "schema", "status"}:
        raise GateError("Rebuild report does not match its exact schema")
    if (
        report.get("schema")
        != "questflight.traffic-v2-geospatial-rebuild-check.v1"
        or report.get("status") != "PASS"
        or report.get("mismatches") != []
        or report.get("rebuilt_outputs") != run_manifest.get("outputs")
    ):
        raise GateError("Rebuild report does not bind one passing rebuilt output set")

    derivative_records: dict[str, dict[str, Any]] = {}
    for value in (coverage, run_manifest):
        collect_external_file_records(value, derivative_records)
    expected_files = {
        RUN_MANIFEST_NAME,
        "rebuild_check.json",
        *(f"data/{name}" for name in EXPECTED_TEXT_ARTIFACTS),
        f"evidence/{EXPECTED_EXTERNAL_ARTIFACT}",
    }
    expected_files.update(
        f"work/{relative}"
        for relative in derivative_records
        if PurePosixPath(relative).parts
        and PurePosixPath(relative).parts[0] in ("normalized", "intermediates")
    )
    actual_files = {
        path.relative_to(rebuild_root).as_posix()
        for path in _walk_tree_files(rebuild_root, "Rebuild package")
    }
    if actual_files != expected_files:
        raise GateError(
            "Rebuild-check file-set mismatch: "
            f"missing={sorted(expected_files - actual_files)[:10]} "
            f"unbound={sorted(actual_files - expected_files)[:10]}"
        )


def expected_outer_package_index(
    registry: dict[str, Any], external_run: Path, data_dir: Path
) -> dict[str, Any]:
    """Construct the one exact outer index from validated live package bytes."""

    freeze = read_json_object(
        external_run / FREEZE_NAME, "Source freeze manifest"
    )
    run_manifest = read_json_object(
        external_run / RUN_MANIFEST_NAME, "Compile run manifest"
    )
    coverage = read_json_object(
        data_dir / "traffic_source_coverage.json", "Traffic source coverage"
    )
    referenced_records: dict[str, dict[str, Any]] = {}
    for value in (freeze, coverage, run_manifest):
        collect_external_file_records(value, referenced_records)
    require_live_external_records(external_run, referenced_records)
    referenced = set(referenced_records)
    referenced.update((FREEZE_NAME, RUN_MANIFEST_NAME))
    candidate_manifest_path = external_run / "approval_candidate/candidate_manifest.json"
    if candidate_manifest_path.is_file():
        candidate = read_json_object(
            candidate_manifest_path,
            "Immutable approval-candidate manifest",
            require_canonical=True,
        )
        expected_candidate_names = set(EXPECTED_TEXT_ARTIFACTS) | {
            EXPECTED_EXTERNAL_ARTIFACT
        }
        artifact_hashes = candidate.get("artifact_sha256")
        if (
            candidate.get("schema")
            != "questflight.traffic-v2-geospatial-approval-candidate.v1"
            or not isinstance(artifact_hashes, dict)
            or set(artifact_hashes) != expected_candidate_names
            or not all(
                isinstance(value, str)
                and re.fullmatch(r"[0-9a-f]{64}", value) is not None
                for value in artifact_hashes.values()
            )
        ):
            raise GateError("Immutable approval candidate has invalid artifact bindings")
        referenced.add("approval_candidate/candidate_manifest.json")
        for name, expected_sha in artifact_hashes.items():
            relative = f"approval_candidate/{name}"
            path = resolve_external_relative_path(
                external_run, relative, "Approval-candidate artifact"
            )
            if not path.is_file() or sha256_file(path) != expected_sha:
                raise GateError(f"Preserved approval candidate hash mismatch: {relative}")
            referenced.add(relative)
    rebuild_root = external_run / "rebuild_check"
    if rebuild_root.is_dir():
        verified_naip_authority = validate_frozen_naip_authority_closure(
            registry, external_run, freeze
        )
        validate_rebuild_package_closure(
            registry, external_run, verified_naip_authority
        )
        referenced.update(
            path.relative_to(external_run).as_posix()
            for path in _walk_tree_files(rebuild_root, "Rebuild package")
        )
    active_roots = set(registry["external_package"]["active_roots"])
    archive_roots = set(registry["external_package"]["archive_roots"])
    support_roots = set(registry["external_package"].get("support_roots") or [])
    active_entries: list[dict[str, Any]] = []
    archive_entries: list[dict[str, Any]] = []
    unreferenced_active: list[str] = []
    actual_external_files = {
        path.relative_to(external_run).as_posix()
        for path in _walk_tree_files(external_run, "External run package")
        if path.relative_to(external_run).as_posix()
        not in (PACKAGE_INDEX_NAME, OPERATION_LOCK_NAME)
    }
    missing_referenced = sorted(referenced - actual_external_files)
    if missing_referenced:
        raise GateError(
            "External package is missing files referenced by the freeze/coverage/run manifests: "
            + ", ".join(missing_referenced[:20])
        )
    for path in _walk_tree_files(external_run, "External run package"):
        relative = path.relative_to(external_run).as_posix()
        if relative in (PACKAGE_INDEX_NAME, OPERATION_LOCK_NAME):
            continue
        first = Path(relative).parts[0]
        entry = {**file_record(path, external_run)}
        if first in archive_roots:
            entry["partition"] = "REJECTED_OR_QUARANTINED_NON_AUTHORITY"
            archive_entries.append(entry)
        elif first in support_roots:
            entry["partition"] = "PINNED_SUPPORT_TOOLCHAIN_NON_AUTHORITY"
            archive_entries.append(entry)
        elif relative in referenced:
            entry["partition"] = "ACTIVE_BOUND_PACKAGE"
            active_entries.append(entry)
        elif first in active_roots or first in (
            "rebuild_check",
            FREEZE_NAME,
            RUN_MANIFEST_NAME,
        ):
            unreferenced_active.append(relative)
        else:
            unreferenced_active.append(relative)
    if unreferenced_active:
        raise GateError(
            "External package has active unreferenced files; archive or quarantine before indexing: "
            + ", ".join(unreferenced_active[:20])
        )
    repository_artifacts = [
        {
            "bytes": (data_dir / name).stat().st_size,
            "path": (data_dir / name).resolve().relative_to(REPO_ROOT.resolve()).as_posix(),
            "sha256": sha256_file(data_dir / name),
        }
        for name in EXPECTED_TEXT_ARTIFACTS
    ]
    index = {
        "active_files": active_entries,
        "approval_adjudication": run_manifest.get("approval_adjudication"),
        "archive_files": archive_entries,
        "effective_gate_status": run_manifest.get("gate_status"),
        "index_scope_excludes": sorted((OPERATION_LOCK_NAME, PACKAGE_INDEX_NAME)),
        "repository_artifacts": repository_artifacts,
        "schema": "questflight.traffic-v2-geospatial-outer-package-index.v1",
        "source_freeze_sha256": sha256_file(external_run / FREEZE_NAME),
        "status": "PASS_ALL_FILES_BOUND_OR_ARCHIVED",
    }
    return index


def write_outer_package_index(
    registry: dict[str, Any], external_run: Path, data_dir: Path
) -> dict[str, Any]:
    index = expected_outer_package_index(registry, external_run, data_dir)
    atomic_write_json(external_run / PACKAGE_INDEX_NAME, index)
    return file_record(external_run / PACKAGE_INDEX_NAME, external_run)


def validate_outer_package_index(
    registry: dict[str, Any], external_run: Path, data_dir: Path, problems: list[str]
) -> None:
    """Require byte-for-byte canonical equality with the pure live index value."""

    index_path = external_run / PACKAGE_INDEX_NAME
    if not index_path.is_file():
        problems.append("missing outer package index")
        return
    try:
        expected = expected_outer_package_index(registry, external_run, data_dir)
        payload = index_path.read_bytes()
        parsed = json.loads(payload.decode("utf-8"))
        if not isinstance(parsed, dict):
            raise GateError("Outer package index JSON root is not an object")
        if parsed != expected:
            raise GateError("Outer package index value differs from the exact live index")
        if payload != canonical_json_bytes(expected):
            raise GateError("Outer package index bytes differ from canonical live bytes")
    except (GateError, OSError, UnicodeError, json.JSONDecodeError) as error:
        problems.append(f"outer package index exact validation failed: {error}")


def verify_freeze_files(
    external_run: Path,
    value: Any,
    problems: list[str],
    *,
    _trail: tuple[Any, ...] = (),
) -> None:
    if isinstance(value, dict):
        if set(("bytes", "path", "sha256")).issubset(value):
            relative_text = str(value["path"])
            relative = PurePosixPath(relative_text)
            repository_owned = _trail in {
                ("registry",),
                ("tool",),
                ("toolchain", "requirements_lock"),
            } or (
                len(_trail) == 2
                and _trail[0] == "accepted_product_bindings"
                and isinstance(_trail[1], int)
            )
            try:
                if relative.parts and relative.parts[0] in (
                    EXTERNAL_RECORD_ROOTS | ARCHIVE_RECORD_ROOTS
                ):
                    path = resolve_external_relative_path(
                        external_run, relative_text, "Frozen file record"
                    )
                elif repository_owned:
                    path = resolve_repository_relative_path(
                        relative_text, "Frozen repository file record"
                    )
                else:
                    # Validate spelling/traversal first, then reject even a
                    # canonical path whose first component is not an approved
                    # external root.  A source record must never be verified
                    # against repository bytes and later consumed relative to
                    # the external package.
                    resolve_external_relative_path(
                        external_run, relative_text, "Frozen file record"
                    )
                    raise GateError(
                        "Frozen file record is not under an approved external record root"
                    )
            except GateError as error:
                problems.append(str(error))
                path = None
            if path is not None:
                if not path.is_file():
                    problems.append(f"missing frozen file: {value['path']}")
                else:
                    if (
                        isinstance(value.get("bytes"), bool)
                        or not isinstance(value.get("bytes"), int)
                        or path.stat().st_size != value["bytes"]
                    ):
                        problems.append(f"byte mismatch: {value['path']}")
                    if (
                        not isinstance(value.get("sha256"), str)
                        or re.fullmatch(r"[0-9a-f]{64}", value["sha256"]) is None
                        or sha256_file(path) != value["sha256"]
                    ):
                        problems.append(f"SHA-256 mismatch: {value['path']}")
        for key, nested in value.items():
            verify_freeze_files(
                external_run, nested, problems, _trail=(*_trail, key)
            )
    elif isinstance(value, list):
        for index, nested in enumerate(value):
            verify_freeze_files(
                external_run, nested, problems, _trail=(*_trail, index)
            )


def validate_frozen_run_closure(
    registry_path: Path, external_run_path: Path
) -> dict[str, Any]:
    """Validate every frozen byte and live authority binding without writing."""

    registry = load_registry(registry_path)
    validate_pinned_inputs(registry)
    external_run = resolve_external_location(external_run_path)
    toolchain_binding = validate_or_create_external_toolchain_manifest(
        registry, registry_path, external_run, allow_create=False
    )
    freeze_path = external_run / FREEZE_NAME
    if not freeze_path.is_file():
        raise GateError(f"Missing source freeze: {freeze_path}")
    try:
        freeze = json.loads(freeze_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise GateError("Source freeze is not valid UTF-8 JSON") from error
    if freeze.get("schema") != "questflight.traffic-v2-geospatial-source-freeze.v1":
        raise GateError("Unsupported source freeze schema")
    require_not_future(
        freeze.get("acquisition_completed_utc"), "Freeze acquisition-completed UTC"
    )
    expected_repository_records = {
        "registry": repository_file_record(registry_path),
        "tool": repository_file_record(Path(__file__)),
    }
    for key, expected in expected_repository_records.items():
        if freeze.get(key) != expected:
            raise GateError(f"Frozen {key} path/bytes/SHA binding changed")
    if freeze.get("accepted_source_sha") != registry["accepted_source_sha"]:
        raise GateError("Accepted source changed after acquisition")
    if freeze.get("coordinate_contract") != registry["coordinate_contract"]:
        raise GateError("Frozen coordinate contract differs from the registry")
    if freeze.get("specification") != registry["specification"]:
        raise GateError("Frozen specification binding differs from the registry")
    validate_frozen_toolchain_binding(freeze, toolchain_binding)
    expected_source_ids = {
        value["id"] for value in registry["sources"]
    } | {value["id"] for value in registry.get("document_sources") or []} | {
        registry["osm"]["id"]
    }
    frozen_sources = freeze.get("sources")
    if not isinstance(frozen_sources, list):
        raise GateError("Source freeze has no exact source inventory")
    source_ids = [str(value.get("id") or "") for value in frozen_sources]
    if len(source_ids) != len(set(source_ids)) or set(source_ids) != expected_source_ids:
        raise GateError("Source freeze ID inventory differs from the registry")
    problems: list[str] = []
    verify_freeze_files(external_run, freeze, problems)
    if problems:
        raise GateError("Frozen file closure failed: " + "; ".join(problems[:20]))
    verified_naip_authority = validate_frozen_naip_authority_closure(
        registry, external_run, freeze
    )
    products, warnings = inventory_product_bindings(registry)
    if freeze.get("accepted_product_bindings") != products:
        raise GateError("Frozen accepted-product inventory differs from live accepted bytes")
    if freeze.get("product_binding_warnings") != warnings:
        raise GateError("Frozen accepted-product provenance warnings differ from live adjudication")
    return {
        "external_run": external_run,
        "freeze": freeze,
        "freeze_path": freeze_path,
        "registry": registry,
        "toolchain_binding": toolchain_binding,
        "verified_naip_authority": verified_naip_authority,
    }


def immutable_authority_snapshot(
    preflight: dict[str, Any], registry_path: Path
) -> dict[str, Any]:
    """Capture the exact live authority bytes whose drift invalidates a commit."""

    registry = preflight["registry"]
    external_run = preflight["external_run"]
    repository_records: dict[str, Any] = {
        "camera_contract": repository_file_record(
            resolve_repository_relative_path(
                registry["authority_model"]["benchmark_camera_contract"]["path"],
                "Benchmark camera contract",
            )
        ),
        "requirements_lock": repository_file_record(
            resolve_repository_relative_path(
                registry["toolchain"]["requirements_lock"]["path"],
                "Requirements lock",
            )
        ),
        "source_registry": repository_file_record(registry_path),
        "source_tool": repository_file_record(Path(__file__)),
        "specification": repository_file_record(
            resolve_repository_relative_path(
                registry["specification"]["path"], "Traffic V2 specification"
            )
        ),
    }
    optional_paths = {
        "approval": registry["authority_model"]["benchmark_approval_path"],
        **{
            f"witness_{key}": value["path"]
            for key, value in registry["authority_model"][
                "benchmark_approval_witnesses"
            ].items()
        },
    }
    optional_records: dict[str, Any] = {}
    for key, relative in sorted(optional_paths.items()):
        path = resolve_repository_relative_path(relative, key)
        validate_path_topology(
            path,
            f"Repository authority {key}",
            allowed_kinds={"absent", "file"},
            containment_root=REPO_ROOT,
        )
        optional_records[key] = (
            {"state": "file", **repository_file_record(path)}
            if path.is_file()
            else {"path": relative, "state": "absent"}
        )
    candidate_root = external_run / "approval_candidate"
    candidate_records: dict[str, Any] = {}
    if candidate_root.is_dir():
        for name in (
            *EXPECTED_TEXT_ARTIFACTS,
            EXPECTED_EXTERNAL_ARTIFACT,
            "candidate_manifest.json",
        ):
            candidate_records[name] = file_record(candidate_root / name, external_run)
        candidate_state: dict[str, Any] = {
            "files": candidate_records,
            "state": "directory",
        }
    else:
        candidate_state = {"state": "absent"}
    return {
        "accepted_product_bindings": preflight["freeze"].get(
            "accepted_product_bindings"
        ),
        "candidate": candidate_state,
        "freeze": file_record(preflight["freeze_path"], external_run),
        "product_binding_warnings": preflight["freeze"].get(
            "product_binding_warnings"
        ),
        "repository": repository_records,
        "repository_optional": optional_records,
        "toolchain": preflight["toolchain_binding"],
    }


def require_same_authority_snapshot(
    expected: dict[str, Any], actual: dict[str, Any], *, allow_candidate_creation: bool
) -> None:
    """Fail on TOCTOU drift; only an exact first candidate may transition from absent."""

    expected_copy = dict(expected)
    actual_copy = dict(actual)
    expected_candidate = expected_copy.pop("candidate")
    actual_candidate = actual_copy.pop("candidate")
    if expected_copy != actual_copy:
        raise GateError("Immutable source/tool/authority closure changed during operation")
    if expected_candidate == actual_candidate:
        return
    if not (
        allow_candidate_creation
        and expected_candidate == {"state": "absent"}
        and actual_candidate.get("state") == "directory"
    ):
        raise GateError("Immutable approval-candidate closure changed during operation")


def validate_correction_invariants(
    registry: dict[str, Any],
    external_run: Path,
    coverage: dict[str, Any],
    problems: list[str],
    *,
    data_dir: Path | None = None,
    evidence_dir: Path | None = None,
    registry_path: Path = DEFAULT_REGISTRY,
) -> None:
    actual_freeze_sha256 = sha256_file(external_run / FREEZE_NAME)
    if coverage.get("source_freeze_sha256") != actual_freeze_sha256:
        problems.append("coverage source-freeze SHA mismatch")
    canonical = coverage.get("canonical_road_universe") or {}
    universe_file = canonical.get("canonical_universe") or {}
    universe_path = external_run / str(universe_file.get("path") or "")
    required_fields = set(
        json.loads(
            (REPO_ROOT / registry["authority_model"]["benchmark_camera_contract"]["path"]).read_text(
                encoding="utf-8"
            )
        )["coverage_contract"]["required_universe_member_fields"]
    )
    ids: set[str] = set()
    count = 0
    if not universe_path.is_file():
        problems.append("canonical universe file is missing")
    else:
        for record in read_jsonl(universe_path):
            count += 1
            missing_fields = required_fields - set(record)
            if missing_fields:
                problems.append(
                    f"canonical member lacks required fields {sorted(missing_fields)}: {record.get('canonical_segment_id')}"
                )
                break
            canonical_id = str(record["canonical_segment_id"])
            if canonical_id in ids:
                problems.append(f"duplicate canonical segment ID: {canonical_id}")
                break
            ids.add(canonical_id)
        if count != int(canonical.get("canonical_universe_record_count", -1)):
            problems.append("canonical universe record-count mismatch")
        if sha256_file(universe_path) != universe_file.get("sha256"):
            problems.append("canonical universe SHA mismatch")
    benchmark = coverage.get("benchmark_camera_coverage") or {}
    benchmark_approval = benchmark.get("approval") or {}
    if benchmark.get("status") != benchmark_approval.get("status"):
        problems.append("benchmark status differs from nested Lead/D approval status")
    if benchmark.get("status") != "PENDING_LEAD_D_INSPECTION":
        problems.append("immutable candidate benchmark status is not pending inspection")
    approval_path = resolve_repository_relative_path(
        registry["authority_model"]["benchmark_approval_path"], "Lead/D approval"
    )
    candidate_path = external_run / "approval_candidate/candidate_manifest.json"
    try:
        candidate = json.loads(candidate_path.read_text(encoding="utf-8"))
        if candidate.get("status") != "PENDING_LEAD_D_INSPECTION":
            problems.append("immutable approval-candidate manifest status is not pending")
        if candidate.get("canonical_universe_sha256") != universe_file.get("sha256") or int(
            candidate.get("canonical_universe_record_count", -1)
        ) != int(canonical.get("canonical_universe_record_count", -2)):
            problems.append("approval candidate canonical-universe binding mismatch")
        if candidate.get("source_freeze_sha256") != actual_freeze_sha256:
            problems.append("approval candidate source-freeze SHA mismatch")
        artifact_hashes = candidate.get("artifact_sha256") or {}
        if set(artifact_hashes) != set((*EXPECTED_TEXT_ARTIFACTS, EXPECTED_EXTERNAL_ARTIFACT)):
            problems.append("approval candidate does not bind exactly five artifacts")
        resolved_data_dir = data_dir or DEFAULT_DATA_DIR
        for name in EXPECTED_TEXT_ARTIFACTS:
            preserved = external_run / "approval_candidate" / name
            current = resolved_data_dir / name
            expected_sha = artifact_hashes.get(name)
            if (
                not preserved.is_file()
                or sha256_file(preserved) != expected_sha
                or not current.is_file()
                or sha256_file(current) != expected_sha
            ):
                problems.append(f"immutable candidate/current artifact mismatch: {name}")
        current_png = (
            (evidence_dir / EXPECTED_EXTERNAL_ARTIFACT)
            if evidence_dir is not None
            else external_run / "evidence" / EXPECTED_EXTERNAL_ARTIFACT
        )
        preserved_png = external_run / "approval_candidate" / EXPECTED_EXTERNAL_ARTIFACT
        expected_png_sha = artifact_hashes.get(EXPECTED_EXTERNAL_ARTIFACT)
        if (
            not preserved_png.is_file()
            or sha256_file(preserved_png) != expected_png_sha
            or not current_png.is_file()
            or sha256_file(current_png) != expected_png_sha
        ):
            problems.append("immutable candidate/current artifact mismatch: traffic_source_coverage.png")
        if approval_path.is_file():
            context = build_approval_validation_context(
                registry,
                registry_path,
                external_run,
                benchmark,
                actual_freeze_sha256,
                candidate,
            )
            validate_universe_approval(
                registry,
                json.loads(approval_path.read_text(encoding="utf-8")),
                benchmark=benchmark,
                source_freeze_sha256=actual_freeze_sha256,
                candidate_manifest=candidate,
                context=context,
            )
    except (GateError, KeyError, OSError, ValueError, TypeError, json.JSONDecodeError) as error:
        problems.append(f"immutable approval-candidate validation failed: {error}")
    bindings = benchmark.get("view_bindings") or []
    if len(bindings) != 15 or len({value.get("view_id") for value in bindings}) != 15:
        problems.append("benchmark output does not bind 15 unique views")
    for binding in bindings:
        if binding.get("canonical_universe_sha256") != universe_file.get("sha256") or int(
            binding.get("canonical_universe_record_count", -1)
        ) != int(canonical.get("canonical_universe_record_count", -2)):
            problems.append(f"benchmark view universe binding mismatch: {binding.get('view_id')}")
            break
    osm = coverage.get("osm_fallback") or {}
    duplicate_summary = osm.get("duplicate_summary") or {}
    occurrences = (duplicate_summary.get("occurrence_count_by_type") or {}).get("way", 0)
    unique = (duplicate_summary.get("unique_count_by_type") or {}).get("way", 0)
    if occurrences < unique or int(osm.get("unique_way_count", -1)) != int(unique):
        problems.append("OSM raw occurrence/unique-way audit is inconsistent")
    freeze = json.loads((external_run / FREEZE_NAME).read_text(encoding="utf-8"))
    frozen_source_values = freeze.get("sources") or []
    frozen_sources = {value["id"]: value for value in frozen_source_values}
    if len(frozen_source_values) != len(frozen_sources):
        problems.append("freeze source inventory has duplicate IDs")
    frozen_osm = frozen_sources.get(registry["osm"]["id"]) or {}
    try:
        raw_osm = json.loads((external_run / frozen_osm["raw"]["path"]).read_text(encoding="utf-8"))
        unique_osm, _ = deduplicate_osm_elements(raw_osm.get("elements") or [])
        expected_osm_way_ids = {
            f"way/{value['id']}" for value in unique_osm if value.get("type") == "way"
        }
        osm_ledger_records = list(read_jsonl(external_run / osm["disposition_ledger"]["path"]))
        if int(osm.get("disposition_record_count", -1)) != len(osm_ledger_records):
            problems.append("OSM disposition summary count mismatch")
        actual_osm_way_ids = [str(value.get("source_record_id")) for value in osm_ledger_records]
        if len(actual_osm_way_ids) != len(set(actual_osm_way_ids)):
            problems.append("OSM disposition ledger contains duplicate way IDs")
        if set(actual_osm_way_ids) != expected_osm_way_ids:
            problems.append("OSM disposition ledger ID set differs from unique raw way IDs")
        if any("motor_road_disposition" not in value for value in osm_ledger_records):
            problems.append("OSM disposition ledger lacks explicit motor-road decisions")
    except (GateError, KeyError, OSError, ValueError, TypeError) as error:
        problems.append(f"OSM raw/disposition audit failed: {error}")
    coverage_source_values = coverage.get("sources") or []
    coverage_source_ids = [str(value.get("id") or "") for value in coverage_source_values]
    if len(coverage_source_ids) != len(set(coverage_source_ids)):
        problems.append("coverage source inventory has duplicate IDs")
    coverage_sources = {str(value.get("id") or ""): value for value in coverage_source_values}
    if set(coverage_sources) != set(frozen_sources):
        problems.append(
            "coverage/freeze source ID-set mismatch: "
            f"missing={sorted(set(frozen_sources) - set(coverage_sources))[:10]} "
            f"extra={sorted(set(coverage_sources) - set(frozen_sources))[:10]}"
        )
    for source_id, frozen_source in frozen_sources.items():
        source = coverage_sources.get(source_id)
        if source is None:
            continue
        expected_kind = (
            "osm"
            if source_id == registry["osm"]["id"]
            else "document_authority"
            if frozen_source.get("source_kind") == "document_authority"
            else "arcgis"
        )
        if source.get("source_kind") != expected_kind:
            problems.append(f"coverage source-kind mismatch: {source_id}")
        if source.get("feature_count") != frozen_source.get("feature_count"):
            problems.append(f"coverage/freeze feature-count mismatch: {source_id}")
        if expected_kind == "document_authority" or frozen_source.get("metadata_only"):
            continue
        normalized = source.get("normalized") or {}
        normalized_file = normalized.get("normalized")
        ledger = normalized.get("disposition_ledger")
        if not normalized_file or not ledger:
            problems.append(f"frozen feature source lacks normalized/disposition files: {source_id}")
            continue
        normalized_ids = [
            (str(value.get("source_record_id")), int(value.get("source_record_part", -1)))
            for value in read_jsonl(external_run / normalized_file["path"])
        ]
        if len(normalized_ids) != len(set(normalized_ids)):
            problems.append(f"normalized source has duplicate record/part IDs: {source_id}")
        if expected_kind == "osm":
            continue
        ledger_records = list(read_jsonl(external_run / ledger["path"]))
        if int(normalized.get("disposition_record_count", -1)) != len(ledger_records):
            problems.append(f"ArcGIS disposition summary count mismatch: {source_id}")
        ledger_ids = [str(value.get("source_record_id")) for value in ledger_records]
        expected_ids = {
            str(value)
            for page in frozen_source.get("pages") or []
            for value in page.get("requested_ids") or []
        }
        if len(ledger_ids) != len(set(ledger_ids)):
            problems.append(f"ArcGIS disposition ledger has duplicate OIDs: {source_id}")
        if set(ledger_ids) != expected_ids:
            problems.append(f"ArcGIS disposition-ledger OID set mismatch: {source_id}")
        if any("motor_road_disposition" not in value for value in ledger_records):
            problems.append(f"ArcGIS disposition ledger lacks explicit eligibility: {source_id}")
    lane_inventory = coverage.get("lane_kilometer_inventory") or {}
    denominator_inventory = coverage.get("normalization_denominator_audit") or {}
    global_representation_denominator_is_incomplete = (
        int(lane_inventory.get("unknown_class_segment_count", 0)) > 0
        or int(denominator_inventory.get("geometryless_motor_road_record_count", 0)) > 0
        or int(
            denominator_inventory.get(
                "unresolved_geometric_motor_road_record_count", 0
            )
        )
        > 0
    )
    for classification, value in (coverage.get("class_coverage") or {}).items():
        eligible = int(value.get("eligible_segment_count", -1))
        known = int(value.get("known_lane_segment_count", -1))
        unknown = int(value.get("unknown_lane_segment_count", -1))
        if eligible != known + unknown:
            problems.append(f"lane-evidence denominator drops eligible segments: {classification}")
        if unknown > 0 and (
            value.get("source_coverage_ratio") is not None or value.get("status") != "FAIL_CLOSED"
        ):
            problems.append(f"unknown lane evidence does not fail closed: {classification}")
        denominator = float(value.get("canonical_lane_kilometer_denominator", 0.0))
        numerator = float(value.get("independently_supported_lane_kilometers", 0.0))
        if numerator > denominator + 1e-6:
            problems.append(f"independent lane-km numerator exceeds canonical denominator: {classification}")
        representation_denominator = float(
            value.get("representation_lane_kilometer_denominator", 0.0)
        )
        represented = float(value.get("represented_lane_kilometers", 0.0))
        if represented > representation_denominator + 1e-6:
            problems.append(
                f"represented lane-km numerator exceeds unique canonical denominator: {classification}"
            )
        representation_is_unresolved = (
            global_representation_denominator_is_incomplete
            or unknown > 0
            or int(value.get("unresolved_geometric_segment_count", 0)) > 0
        )
        expected_ratio = (
            None
            if representation_is_unresolved
            else represented / representation_denominator
            if representation_denominator > 0.0
            else 1.0
        )
        actual_ratio = value.get("source_coverage_ratio")
        if expected_ratio is None:
            if actual_ratio is not None or value.get("status") != "FAIL_CLOSED":
                problems.append(
                    f"unresolved source-universe representation does not fail closed: {classification}"
                )
        elif actual_ratio is None or not math.isclose(
            float(actual_ratio), expected_ratio, rel_tol=1e-12, abs_tol=1e-12
        ):
            problems.append(f"source coverage is not canonical representation: {classification}")
        elif (
            value.get("status") == "PASS"
        ) != (expected_ratio >= float(value.get("required_ratio", 2.0))):
            problems.append(f"source coverage threshold status mismatch: {classification}")
        if value.get("corroboration_is_source_coverage_gate") is not False:
            problems.append(f"cross-source corroboration is not separated from coverage: {classification}")
    if any("lane_proxy" in key for value in coverage.get("class_coverage", {}).values() for key in value):
        problems.append("coverage still contains forbidden lane-proxy metrics")


def _validate_outputs_core(registry_path: Path, external_run_path: Path, data_dir: Path, evidence_dir: Path) -> dict[str, Any]:
    registry = load_registry(registry_path)
    validate_pinned_inputs(registry)
    external_run = resolve_external_location(external_run_path)
    toolchain_binding = validate_or_create_external_toolchain_manifest(
        registry, registry_path, external_run, allow_create=False
    )
    problems: list[str] = []
    freeze_path = external_run / FREEZE_NAME
    run_manifest_path = external_run / RUN_MANIFEST_NAME
    if not freeze_path.is_file():
        problems.append("missing freeze_manifest.json")
        freeze: dict[str, Any] = {}
    else:
        freeze = json.loads(freeze_path.read_text(encoding="utf-8"))
        verify_freeze_files(external_run, freeze, problems)
        if freeze.get("registry", {}).get("sha256") != sha256_file(registry_path):
            problems.append("freeze registry SHA-256 mismatch")
        if freeze.get("tool", {}).get("sha256") != sha256_file(Path(__file__)):
            problems.append("freeze source-gate tool SHA-256 mismatch")
        try:
            validate_frozen_toolchain_binding(freeze, toolchain_binding)
        except ToolchainPreflightError as error:
            problems.append(str(error))
        try:
            products, warnings = inventory_product_bindings(registry)
            if freeze.get("accepted_product_bindings") != products:
                problems.append("frozen accepted-product inventory mismatch")
            if freeze.get("product_binding_warnings") != warnings:
                problems.append("frozen accepted-product warnings mismatch")
        except (GateError, OSError, subprocess.SubprocessError) as error:
            problems.append(f"accepted-product binding validation failed: {error}")
    if not run_manifest_path.is_file():
        problems.append("missing run_manifest.json")
        run_manifest: dict[str, Any] = {}
    else:
        run_manifest = json.loads(run_manifest_path.read_text(encoding="utf-8"))
    for name in EXPECTED_TEXT_ARTIFACTS:
        path = data_dir / name
        if not path.is_file():
            problems.append(f"missing normative artifact: {name}")
            continue
        expected = (run_manifest.get("outputs") or {}).get(name) or {}
        if expected.get("sha256") != sha256_file(path) or expected.get("bytes") != path.stat().st_size:
            problems.append(f"normative artifact hash/byte mismatch: {name}")
    png = evidence_dir / EXPECTED_EXTERNAL_ARTIFACT
    if not png.is_file():
        problems.append(f"missing external normative artifact: {EXPECTED_EXTERNAL_ARTIFACT}")
    else:
        expected = (run_manifest.get("outputs") or {}).get(EXPECTED_EXTERNAL_ARTIFACT) or {}
        if expected.get("sha256") != sha256_file(png) or expected.get("bytes") != png.stat().st_size:
            problems.append("external coverage PNG hash/byte mismatch")
        with Image.open(png) as image:
            if image.size != (1600, 1600) or image.format != "PNG":
                problems.append("external coverage PNG dimensions/format mismatch")
    if (data_dir / "traffic_source_coverage.json").is_file():
        coverage = json.loads((data_dir / "traffic_source_coverage.json").read_text(encoding="utf-8"))
        if coverage["bounds_evaluation"]["evaluated_cell_count"] != 576:
            problems.append("not all 576 exact-bound cells were evaluated")
        if coverage["accepted_source_sha"] != registry["accepted_source_sha"]:
            problems.append("coverage accepted-source SHA mismatch")
        validate_correction_invariants(
            registry,
            external_run,
            coverage,
            problems,
            data_dir=data_dir,
            evidence_dir=evidence_dir,
            registry_path=registry_path,
        )
        adjudication = run_manifest.get("approval_adjudication") or {}
        approval_path = resolve_repository_relative_path(
            registry["authority_model"]["benchmark_approval_path"], "Lead/D approval"
        )
        expected_approval_status = (
            "LEAD_D_APPROVED" if approval_path.is_file() else "PENDING_LEAD_D_INSPECTION"
        )
        if adjudication.get("status") != expected_approval_status:
            problems.append("run-manifest live approval adjudication status mismatch")
        live_approval_context: dict[str, Any] | None = None
        if adjudication.get("status") == "LEAD_D_APPROVED":
            try:
                candidate = json.loads(
                    (external_run / "approval_candidate/candidate_manifest.json").read_text(
                        encoding="utf-8"
                    )
                )
                live_approval_context = build_approval_validation_context(
                    registry,
                    registry_path,
                    external_run,
                    coverage["benchmark_camera_coverage"],
                    sha256_file(freeze_path),
                    candidate,
                )
            except (GateError, OSError, ValueError, TypeError, json.JSONDecodeError) as error:
                problems.append(f"approved run live semantic context failed: {error}")
        validate_live_approval_file_bindings(
            registry,
            external_run,
            adjudication,
            problems,
            context=live_approval_context,
        )
        if not adjudication.get("candidate_artifacts_remain_immutable"):
            problems.append("run manifest does not assert immutable candidate artifacts")
        if run_manifest.get("candidate_gate_status") != coverage.get("gate_status"):
            problems.append("run-manifest candidate gate differs from immutable coverage gate")
        if run_manifest.get("gate_status") != adjudication.get("effective_gate_status"):
            problems.append("run-manifest effective gate/adjudication mismatch")
    if freeze_path.is_file() and run_manifest:
        try:
            recorded_command = (run_manifest.get("run_context") or {}).get("command")
            validate_recorded_compile_command(
                recorded_command,
                registry_path=registry_path,
                external_run=external_run,
                data_dir=data_dir,
                evidence_dir=evidence_dir,
            )
            expected_context = build_compile_run_context(
                registry,
                registry_path,
                external_run,
                data_dir,
                evidence_dir,
                toolchain_binding,
                freeze_path,
                command_context=recorded_command,
            )
            expected_context["outputs"] = run_manifest.get("outputs")
            if run_manifest.get("run_context") != expected_context:
                problems.append("compile run context is missing, stale, or inconsistent")
        except (GateError, OSError, subprocess.SubprocessError) as error:
            problems.append(f"compile run-context validation failed: {error}")
    validate_outer_package_index(registry, external_run, data_dir, problems)
    status = "PASS" if not problems else "FAIL"
    report = {
        "gate_status": run_manifest.get("gate_status"),
        "problems": problems,
        "schema": "questflight.traffic-v2-source-gate-validation.v1",
        "status": status,
    }
    print(canonical_json_bytes(report).decode())
    return report


def validate_outputs(
    registry_path: Path,
    external_run_path: Path,
    data_dir: Path,
    evidence_dir: Path,
) -> dict[str, Any]:
    """Validate one immutable package snapshot while owning the shared lock."""

    registry = load_registry(registry_path)
    validate_pinned_inputs(registry)
    preflight = validate_frozen_run_closure(registry_path, external_run_path)
    topology = validate_command_path_topology(
        registry,
        external_run_path,
        command="validate",
        data_dir=data_dir,
        evidence_dir=evidence_dir,
    )
    external_run = topology["external_run"]
    data_dir = topology["data_dir"]
    evidence_dir = topology["evidence_dir"]
    target_set_lock_plan(compile_target_paths(data_dir, evidence_dir))
    initial_snapshot = immutable_authority_snapshot(preflight, registry_path)
    with exclusive_operation_lock(
        external_run, "validate", allow_root_creation=False
    ):
        with exclusive_target_set_lock(
            compile_target_paths(data_dir, evidence_dir), "validate"
        ):
            validate_command_path_topology(
                registry,
                external_run,
                command="validate",
                data_dir=data_dir,
                evidence_dir=evidence_dir,
            )
            current = validate_frozen_run_closure(registry_path, external_run)
            require_same_authority_snapshot(
                initial_snapshot,
                immutable_authority_snapshot(current, registry_path),
                allow_candidate_creation=False,
            )
            report = _validate_outputs_core(
                registry_path, external_run, data_dir, evidence_dir
            )
            final = validate_frozen_run_closure(registry_path, external_run)
            require_same_authority_snapshot(
                initial_snapshot,
                immutable_authority_snapshot(final, registry_path),
                allow_candidate_creation=False,
            )
            return report


def rebuild_check(
    registry_path: Path,
    external_run_path: Path,
    data_dir: Path,
    evidence_dir: Path,
    *,
    producer_command: dict[str, Any] | None = None,
) -> dict[str, Any]:
    registry = load_registry(registry_path)
    validate_pinned_inputs(registry)
    preflight = validate_frozen_run_closure(registry_path, external_run_path)
    topology = validate_command_path_topology(
        registry,
        external_run_path,
        command="rebuild-check",
        data_dir=data_dir,
        evidence_dir=evidence_dir,
    )
    external_run = topology["external_run"]
    data_dir = topology["data_dir"]
    evidence_dir = topology["evidence_dir"]
    effective_rebuild_command = producer_command or rebuild_command_context(
        registry_path, external_run, data_dir, evidence_dir
    )
    validate_recorded_rebuild_command(
        effective_rebuild_command,
        registry_path=registry_path,
        external_run=external_run,
        data_dir=data_dir,
        evidence_dir=evidence_dir,
    )
    target_set_lock_plan(compile_target_paths(data_dir, evidence_dir))
    initial_snapshot = immutable_authority_snapshot(preflight, registry_path)
    originals = [data_dir / name for name in EXPECTED_TEXT_ARTIFACTS] + [
        evidence_dir / EXPECTED_EXTERNAL_ARTIFACT
    ]
    if any(not path.is_file() for path in originals):
        raise GateError("Rebuild check requires the complete existing five-artifact candidate")
    original_hashes = {path.name: sha256_file(path) for path in originals}
    original_validation = _validate_outputs_core(
        registry_path, external_run, data_dir, evidence_dir
    )
    if original_validation["status"] != "PASS":
        raise GateError(
            "Rebuild requires a completely valid current package: "
            + "; ".join(original_validation["problems"][:10])
        )
    live_manifest = json.loads(
        (external_run / RUN_MANIFEST_NAME).read_text(encoding="utf-8")
    )
    recorded_compile_command = (live_manifest.get("run_context") or {}).get("command")
    validate_recorded_compile_command(
        recorded_compile_command,
        registry_path=registry_path,
        external_run=external_run,
        data_dir=data_dir,
        evidence_dir=evidence_dir,
    )
    with exclusive_operation_lock(
        external_run, "rebuild-check", allow_root_creation=False
    ):
        with exclusive_target_set_lock(
            compile_target_paths(data_dir, evidence_dir), "rebuild-check"
        ):
            validate_command_path_topology(
                registry,
                external_run,
                command="rebuild-check",
                data_dir=data_dir,
                evidence_dir=evidence_dir,
            )
            preflight = validate_frozen_run_closure(registry_path, external_run)
            require_same_authority_snapshot(
                initial_snapshot,
                immutable_authority_snapshot(preflight, registry_path),
                allow_candidate_creation=False,
            )
            if {path.name: sha256_file(path) for path in originals} != original_hashes:
                raise GateError("Normative artifacts changed across rebuild lock acquisition")
            locked_validation = _validate_outputs_core(
                registry_path, external_run, data_dir, evidence_dir
            )
            if locked_validation["status"] != "PASS":
                raise GateError("Current package changed before the rebuild lock was owned")
            registry = preflight["registry"]
            with tempfile.TemporaryDirectory(
                prefix="traffic_v2_rebuild_", dir=external_run.parent
            ) as temporary_name:
                stage_root = Path(temporary_name)
                rebuild_root = stage_root / "rebuild_check"
                rebuild_data = rebuild_root / "data"
                rebuild_evidence = rebuild_root / "evidence"
                rebuild_derivatives = rebuild_root / "work"
                candidate_event_utc = frozen_candidate_event_utc(external_run)
                manifest = _compile_artifacts_core(
                    registry_path,
                    external_run,
                    rebuild_data,
                    rebuild_evidence,
                    verified_naip_authority=preflight["verified_naip_authority"],
                    allow_candidate_creation=False,
                    candidate_event_utc=candidate_event_utc,
                    derivative_run=rebuild_derivatives,
                    manifest_path=rebuild_root / RUN_MANIFEST_NAME,
                    producer_command=recorded_compile_command,
                    producer_data_dir=data_dir,
                    producer_evidence_dir=evidence_dir,
                )
                rebuilt_coverage = json.loads(
                    (rebuild_data / "traffic_source_coverage.json").read_text(
                        encoding="utf-8"
                    )
                )
                rebuilt_candidate = approval_candidate_value(
                    rebuild_data,
                    rebuild_evidence / EXPECTED_EXTERNAL_ARTIFACT,
                    benchmark=rebuilt_coverage["benchmark_camera_coverage"],
                    candidate_event_utc=candidate_event_utc,
                    source_freeze_sha256=sha256_file(preflight["freeze_path"]),
                )
                _validate_staged_compile_set(
                    registry,
                    external_run,
                    rebuild_derivatives,
                    manifest,
                    rebuild_data,
                    rebuild_evidence,
                    rebuilt_candidate,
                    verified_naip_authority=preflight["verified_naip_authority"],
                )
                mismatches = [
                    name
                    for name in EXPECTED_TEXT_ARTIFACTS
                    if original_hashes[name] != sha256_file(rebuild_data / name)
                ]
                if original_hashes[EXPECTED_EXTERNAL_ARTIFACT] != sha256_file(
                    rebuild_evidence / EXPECTED_EXTERNAL_ARTIFACT
                ):
                    mismatches.append(EXPECTED_EXTERNAL_ARTIFACT)
                report = {
                    "mismatches": mismatches,
                    "rebuilt_outputs": manifest["outputs"],
                    "schema": "questflight.traffic-v2-geospatial-rebuild-check.v1",
                    "status": "PASS" if not mismatches else "FAIL",
                }
                atomic_write_json(rebuild_root / "rebuild_check.json", report)
                if mismatches:
                    print(canonical_json_bytes(report).decode())
                    return report
                precommit = validate_frozen_run_closure(registry_path, external_run)
                require_same_authority_snapshot(
                    initial_snapshot,
                    immutable_authority_snapshot(precommit, registry_path),
                    allow_candidate_creation=False,
                )
                if {path.name: sha256_file(path) for path in originals} != original_hashes:
                    raise GateError("Normative artifacts changed before rebuild promotion")
                destination = external_run / "rebuild_check"
                backup = stage_root / "rollback_rebuild_check"
                index_path = external_run / PACKAGE_INDEX_NAME
                index_backup = stage_root / f"rollback_{PACKAGE_INDEX_NAME}"
                destination_kind = _path_kind(destination)
                index_kind = _path_kind(index_path)
                if destination_kind not in ("absent", "dir"):
                    raise GateError("Rebuild-check destination is not a directory/absent")
                if index_kind not in ("absent", "file"):
                    raise GateError("Outer package index is not a file/absent")
                try:
                    if destination_kind == "dir":
                        shutil.move(str(destination), str(backup))
                    shutil.move(str(rebuild_root), str(destination))
                    if index_kind == "file":
                        shutil.move(str(index_path), str(index_backup))
                    write_outer_package_index(registry, external_run, data_dir)
                    committed = validate_frozen_run_closure(
                        registry_path, external_run
                    )
                    require_same_authority_snapshot(
                        initial_snapshot,
                        immutable_authority_snapshot(committed, registry_path),
                        allow_candidate_creation=False,
                    )
                    if {
                        path.name: sha256_file(path) for path in originals
                    } != original_hashes:
                        raise GateError("Normative artifacts changed inside rebuild promotion")
                    post_validation = _validate_outputs_core(
                        registry_path, external_run, data_dir, evidence_dir
                    )
                    if post_validation["status"] != "PASS":
                        raise GateError(
                            "Rebuilt package failed complete post-promotion validation"
                        )
                except Exception:
                    _remove_promoted_path(destination)
                    if backup.exists():
                        shutil.move(str(backup), str(destination))
                    _remove_promoted_path(index_path)
                    if index_backup.exists():
                        shutil.move(str(index_backup), str(index_path))
                    raise
                print(canonical_json_bytes(report).decode())
                return report


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--registry", type=Path, default=DEFAULT_REGISTRY)
    subparsers = parser.add_subparsers(dest="command", required=True)
    for command in ("inventory", "acquire", "compile", "validate", "rebuild-check"):
        subparser = subparsers.add_parser(command)
        subparser.add_argument("--external-run", type=Path, required=True)
        if command in ("compile", "validate", "rebuild-check"):
            subparser.add_argument("--data-dir", type=Path, default=DEFAULT_DATA_DIR)
            subparser.add_argument("--evidence-dir", type=Path, required=True)
    return parser


def main() -> int:
    args = build_parser().parse_args()
    try:
        if args.command == "inventory":
            inventory_sources(args.registry, args.external_run)
            return 0
        if args.command == "acquire":
            acquire(args.registry, args.external_run)
            return 0
        if args.command == "compile":
            compile_artifacts(
                args.registry,
                args.external_run,
                args.data_dir,
                args.evidence_dir,
                producer_command=current_cli_command_context(),
            )
            return 0
        if args.command == "validate":
            report = validate_outputs(args.registry, args.external_run, args.data_dir, args.evidence_dir)
            return 0 if report["status"] == "PASS" else 1
        if args.command == "rebuild-check":
            report = rebuild_check(
                args.registry,
                args.external_run,
                args.data_dir,
                args.evidence_dir,
                producer_command=current_cli_command_context(),
            )
            return 0 if report["status"] == "PASS" else 1
        raise AssertionError(args.command)
    except GateError as error:
        print(canonical_json_bytes({"error": str(error), "status": "FAIL_CLOSED"}).decode(), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
