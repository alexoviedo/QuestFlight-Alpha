from __future__ import annotations

import ast
import json
import os
from copy import deepcopy
from pathlib import Path

import pytest

import source_gate


def registry() -> dict:
    return source_gate.load_registry(source_gate.DEFAULT_REGISTRY)


def semantic_registry() -> dict:
    value = deepcopy(registry())
    model = value["authority_model"]
    for priority, source_id in ((1, "test_a"), (2, "test_b")):
        model["canonical_centerline_precedence"].append(
            {"priority": priority, "source_id": source_id}
        )
        model["correspondence_identity_fields"][source_id] = ["NAME"]
        model["correspondence_identity_field_roles"][source_id] = {
            "NAME": "primary_name"
        }
        model["class_evidence"][source_id] = {
            "confidence": "TEST_DIRECT",
            "excluded_values": {"private": "EXCLUDED_PRIVATE"},
            "field": "CLASS",
            "values": {"primary": "primary", "secondary": "secondary"},
        }
        model["road_kind_evidence"][source_id] = {
            "confidence": "TEST_DIRECT",
            "fixed_value": "MAINLINE",
        }
        model["lane_evidence"].append(
            {
                "confidence": "TEST_DIRECT",
                "field": "LANES",
                "priority": priority,
                "source_id": source_id,
            }
        )
        model["correspondence_semantics"][source_id] = {
            "bridge_field": "BRIDGE",
            "bridge_values": {
                "N": {"confidence": "TEST_DIRECT", "usable_for_correspondence": True, "value": "NO"},
                "Y": {"confidence": "TEST_DIRECT", "usable_for_correspondence": True, "value": "YES"},
            },
            "carriageway_field": "CARRIAGEWAY",
            "carriageway_values": {
                "D": {"confidence": "TEST_DIRECT", "usable_for_correspondence": True, "value": "DIRECTIONAL_CARRIAGEWAY"},
                "S": {"confidence": "TEST_DIRECT", "usable_for_correspondence": True, "value": "SINGLE_CENTERLINE"},
            },
            "direction_field": "DIRECTION",
            "direction_values": {
                "B": {"confidence": "TEST_DIRECT", "usable_for_correspondence": True, "value": "BIDIRECTIONAL"},
                "F": {"confidence": "TEST_DIRECT", "usable_for_correspondence": True, "value": "FORWARD"},
                "R": {"confidence": "TEST_DIRECT", "usable_for_correspondence": True, "value": "REVERSE"},
            },
            "junction_field": "JUNCTION",
            "junction_values": {
                "NONE": {"confidence": "TEST_DIRECT", "usable_for_correspondence": True, "value": "NONE"}
            },
            "layer_field": "LAYER",
            "layer_values": {
                "0": {"confidence": "TEST_DIRECT", "usable_for_correspondence": True, "value": 0},
                "1": {"confidence": "TEST_DIRECT", "usable_for_correspondence": True, "value": 1},
            },
            "tunnel_field": "TUNNEL",
            "tunnel_values": {
                "N": {"confidence": "TEST_DIRECT", "usable_for_correspondence": True, "value": "NO"},
                "Y": {"confidence": "TEST_DIRECT", "usable_for_correspondence": True, "value": "YES"},
            },
        }
    return value


def semantic_attributes(**overrides: object) -> dict:
    value = {
        "BRIDGE": "N",
        "CARRIAGEWAY": "S",
        "CLASS": "primary",
        "DIRECTION": "B",
        "JUNCTION": "NONE",
        "LANES": 2,
        "LAYER": "0",
        "NAME": "Main",
        "TUNNEL": "N",
    }
    value.update(overrides)
    return value


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, sort_keys=True), encoding="utf-8")


def write_jsonl(path: Path, values: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        "".join(json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n" for value in values),
        encoding="utf-8",
    )


def line_record(
    source_id: str,
    record_id: str,
    coordinates: list[list[float]],
    *,
    attributes: dict | None = None,
    tags: dict | None = None,
    license_partition: str = "TEST-LICENSE",
) -> dict:
    value = {
        "geometry": {"coordinates": coordinates, "type": "LineString"},
        "license_partition": license_partition,
        "role": "road_authority",
        "source_id": source_id,
        "source_record_id": record_id,
        "source_record_part": 0,
    }
    if attributes is not None:
        value["attributes"] = attributes
    if tags is not None:
        value["tags"] = tags
        value["highway_class"] = source_gate.osm_eligibility(registry(), tags)["highway_class"]
    return value


def normalized_summary(root: Path, source_id: str, records: list[dict]) -> dict:
    path = root / "normalized" / f"{source_id}.geojsonl"
    write_jsonl(path, records)
    return {"id": source_id, "normalized": source_gate.file_record(path, root)}


def osm_summary(root: Path, records: list[dict] | None = None, dispositions: list[dict] | None = None) -> dict:
    summary = normalized_summary(root, "osm_bounded_roads", records or [])
    ledger = root / "intermediates/record_dispositions/osm_bounded_roads.jsonl"
    write_jsonl(ledger, dispositions or [])
    summary["disposition_ledger"] = source_gate.file_record(ledger, root)
    return summary


def isolated_toolchain_fixture(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> tuple[dict, Path, Path, Path]:
    value = deepcopy(registry())
    lock_bytes = (source_gate.SCRIPT_DIR / "requirements-lock.txt").read_bytes()
    repo = tmp_path / "repo"
    tool = repo / value["toolchain"]["source_tool_path"]
    registry_path = repo / value["toolchain"]["source_registry_path"]
    lock = repo / value["toolchain"]["requirements_lock"]["path"]
    tool.parent.mkdir(parents=True, exist_ok=True)
    tool.write_text("# isolated source-gate fixture\n", encoding="utf-8")
    lock.parent.mkdir(parents=True, exist_ok=True)
    lock.write_bytes(lock_bytes)
    write_json(registry_path, value)
    camera = repo / value["authority_model"]["benchmark_camera_contract"]["path"]
    specification = repo / value["specification"]["path"]
    write_json(camera, {"schema": "camera-fixture"})
    write_json(specification, {"schema": "spec-fixture"})
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    monkeypatch.setattr(source_gate, "__file__", str(tool))
    expected_environment = {
        key: deepcopy(value["toolchain"][key])
        for key in (
            "distributions",
            "native_libraries",
            "payload_identity",
            "runtime_identity",
        )
    }
    monkeypatch.setattr(
        source_gate,
        "live_toolchain_environment",
        lambda: deepcopy(expected_environment),
    )
    return value, registry_path, repo, tool


def test_registry_and_frozen_inputs_are_exact() -> None:
    value = registry()
    assert value["accepted_source_sha"] == "fda11a25eb526824d82a47330f2be80b1410bb64"
    assert value["coordinate_contract"]["bounds_xz_meters"] == [
        -12000.0,
        -12000.0,
        12000.0,
        12000.0,
    ]
    assert value["schema"] == "questflight.traffic-v2-geospatial-source-registry.v2"
    assert {
        key: item["path"]
        for key, item in value["authority_model"]["benchmark_approval_witnesses"].items()
    } == {
        "d_report": "docs/evidence/traffic_v2/TRAFFIC_V2_PHASE3_SOURCE_GATE_VERIFICATION.md",
        "d_result": "docs/evidence/traffic_v2/TRAFFIC_V2_PHASE3_SOURCE_GATE_VERIFICATION.json",
        "d_verifier": "tools/traffic_v2/validation/verify_phase3_source_gate.py",
        "lead_inspection": "tools/traffic_v2/schemas/traffic_v2_geospatial_universe_inspection_v1.json",
    }
    assert value["toolchain"]["requirements_lock"] == {
        "bytes": 388,
        "path": "tools/traffic_v2/geospatial/requirements-lock.txt",
        "sha256": "74ea30f283e97a60ffb31611fcb81c606fb0b2c2cd35df67e0f1b6de1b7a5c24",
    }
    assert source_gate.validate_toolchain_contract(value, source_gate.DEFAULT_REGISTRY)[
        "status"
    ] == "PASS"
    assert {item["sha256"] for item in value["rejected_freezes"]} == {
        "fff0906c8cc1deb615741ea5d56ed80ee59b2e9e524b5bd70af842f345e4cd23",
        "178ac781ea094e4f09af9a38493babace8f78e8942301b86927e414f2675ca3d",
        "d90132cf96e919406f52c32abbc19bf0b1936d3578ad0ace29cc693ca2e06817",
        "f5fa99a5a607054991caf1a9fd9ebb6f2bdb0bd6380cfd80587a1d42379c5ad1",
        "c7cd7b9362d01bacbeb7c23833cae65882bceaaaab767dcb00f9b8c0904e07b6",
        "e371e027d1ef9f2fa3b7a6f534e0e23842e8f63a5cc6b7202eee85bb82eb7dc2",
        "80ccf33a89eb718ffb2e64755f41df6d900d39bca8894e790bf7fde1395d9306",
        "2211980332dbdb4e09bcbba0a38350ced92f7f14086885f1066cebdc7cc71501",
    }
    source_gate.validate_pinned_inputs(value)


def test_product_pins_and_historical_warnings() -> None:
    products, warnings = source_gate.inventory_product_bindings(registry())
    assert len(products) == 8
    assert {value["id"] for value in warnings} == {
        "accepted_building_exclusion_historical_provenance_hash",
        "accepted_terrain_historical_provenance_hash",
    }
    assert all(value["reason"] for value in warnings)
    assert all(value["status"] == "PINNED" for value in products)


@pytest.mark.parametrize(
    ("raw", "expected"),
    [
        ("motorway_link", "motorway"),
        ("living_street", "residential"),
        ("service", None),
        (None, None),
    ],
)
def test_road_class(raw: object, expected: str | None) -> None:
    assert source_gate.road_class(raw) == expected


@pytest.mark.parametrize(
    ("value", "expected"),
    [(2, 2), (2.0, 2), ("2", 2), ("2.00", 2), (0, None), (2.5, None), ("2 lanes", None)],
)
def test_strict_lane_count_never_rounds_or_infers(value: object, expected: int | None) -> None:
    assert source_gate.strict_positive_int(value) == expected


def test_projection_origin_and_query_envelope() -> None:
    value = registry()
    transformer = source_gate.local_transformer(value)
    x, z = transformer.transform(-105.22608958, 40.03936527)
    assert x == pytest.approx(0.0, abs=1e-6)
    assert z == pytest.approx(0.0, abs=1e-6)
    query = source_gate.query_parameters(value)
    assert query["geometry"] == "-105.3669180624,39.9312054950,-105.0852610976,40.1473524046"


def test_fetch_retries_arcgis_503_embedded_in_http_200_json(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class Response:
        def __init__(self, payload: bytes) -> None:
            self.payload = payload
            self.headers: dict[str, str] = {}
            self.status = 200

        def __enter__(self) -> "Response":
            return self

        def __exit__(self, *_args: object) -> None:
            return None

        def geturl(self) -> str:
            return "https://example.test/MapServer/0"

        def read(self) -> bytes:
            return self.payload

    responses = iter(
        (
            Response(
                source_gate.canonical_json_bytes(
                    {
                        "error": {
                            "code": 503,
                            "details": [],
                            "message": "temporarily unavailable",
                        }
                    }
                )
            ),
            Response(b'{"currentVersion":11.5}'),
        )
    )
    calls: list[str] = []

    def fake_urlopen(request: object, *, timeout: int) -> Response:
        calls.append(f"{getattr(request, 'full_url')}:{timeout}")
        return next(responses)

    monkeypatch.setattr(source_gate.urllib.request, "urlopen", fake_urlopen)
    monkeypatch.setattr(source_gate.time, "sleep", lambda _seconds: None)

    payload, transport = source_gate.fetch_bytes(
        "https://example.test/MapServer/0", attempts=2, timeout_seconds=15
    )

    assert payload == b'{"currentVersion":11.5}'
    assert calls == [
        "https://example.test/MapServer/0:15",
        "https://example.test/MapServer/0:15",
    ]
    assert transport["attempt"] == 2
    assert "retryable ArcGIS/remote JSON error" in transport["errors_before_success"][0][
        "error"
    ]


def test_repository_cannot_be_used_as_external_run() -> None:
    with pytest.raises(source_gate.GateError, match="inside the repository"):
        source_gate.require_external(source_gate.REPO_ROOT / "forbidden")


def test_acquisition_lock_rejects_concurrent_writer_and_cleans_own_lock(tmp_path: Path) -> None:
    with source_gate.exclusive_acquisition_lock(tmp_path) as lock:
        assert lock.is_file()
        with pytest.raises(source_gate.ConcurrentAcquisitionError, match="already has"):
            with source_gate.exclusive_acquisition_lock(tmp_path):
                raise AssertionError("unreachable")
    assert not (tmp_path / source_gate.ACQUISITION_LOCK_NAME).exists()


def test_one_operation_lock_rejects_every_package_operation(tmp_path: Path) -> None:
    with source_gate.exclusive_operation_lock(tmp_path, "compile") as lock:
        assert lock.is_file()
        for operation in ("inventory", "acquire", "compile", "validate", "rebuild-check"):
            with pytest.raises(source_gate.ConcurrentAcquisitionError, match="operation lock"):
                with source_gate.exclusive_operation_lock(tmp_path, operation):
                    raise AssertionError("unreachable")
    assert not (tmp_path / source_gate.OPERATION_LOCK_NAME).exists()


def test_owned_lock_cleanup_never_unlinks_a_replacement(tmp_path: Path) -> None:
    lock = tmp_path / "operation.lock"
    lock.write_bytes(b"owned")
    owned_stat = lock.stat(follow_symlinks=False)
    owned_identity = (int(owned_stat.st_dev), int(owned_stat.st_ino))
    replacement = tmp_path / "replacement.lock"
    replacement.write_bytes(b"replacement")
    source_gate.os.replace(replacement, lock)

    source_gate._unlink_owned_lock(lock, owned_identity)

    assert lock.read_bytes() == b"replacement"
    replacement_stat = lock.stat(follow_symlinks=False)
    source_gate._unlink_owned_lock(
        lock,
        (int(replacement_stat.st_dev), int(replacement_stat.st_ino)),
    )
    assert not lock.exists()


@pytest.mark.parametrize(
    "freeze_sha",
    [
        "",
        "a" * 63,
        "a" * 65,
        "A" * 64,
        "g" * 64,
        None,
    ],
)
def test_rejected_archive_root_requires_exact_lowercase_sha256(
    tmp_path: Path, freeze_sha: object
) -> None:
    with pytest.raises(source_gate.GateError, match="64 lowercase hexadecimal"):
        source_gate.rejected_archive_root(tmp_path, freeze_sha)  # type: ignore[arg-type]


def test_archive_preflight_and_archive_share_short_root_and_preserve_full_hash(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    external = tmp_path / "external"
    external.mkdir()
    freeze_path = external / source_gate.FREEZE_NAME
    freeze_path.write_bytes(b"explicitly rejected freeze\n")
    freeze_sha = source_gate.sha256_file(freeze_path)
    reason = "Fixture freeze is explicitly rejected."
    status = "REJECTED_FAIL_CLOSED"
    value = deepcopy(registry())
    value["rejected_freezes"] = [
        {"reason": reason, "sha256": freeze_sha, "status": status}
    ]
    request = external / "requests" / "fixture" / "request.json"
    request.parent.mkdir(parents=True)
    request.write_bytes(b"{}\n")
    expected_root = source_gate.rejected_archive_root(external, freeze_sha)
    real_rejected_archive_root = source_gate.rejected_archive_root
    calls: list[tuple[Path, str]] = []

    def recording_rejected_archive_root(run: Path, digest: str) -> Path:
        calls.append((run, digest))
        return real_rejected_archive_root(run, digest)

    monkeypatch.setattr(
        source_gate, "rejected_archive_root", recording_rejected_archive_root
    )

    source_gate.validate_acquisition_target(value, external, expected_toolchain={})
    archive_record = source_gate.archive_rejected_active_package(value, external)

    expected_record = {
        "freeze_sha256": freeze_sha,
        "reason": reason,
        "schema": "questflight.traffic-v2-rejected-external-package.v1",
        "status": status,
    }
    assert calls == [(external, freeze_sha), (external, freeze_sha)]
    assert expected_root.name == f"rejected_{freeze_sha[:32]}"
    assert expected_root != external / "attempts" / f"rejected_{freeze_sha}"
    assert archive_record == expected_record
    assert json.loads((expected_root / "rejection.json").read_text(encoding="utf-8")) == expected_record
    assert (expected_root / source_gate.FREEZE_NAME).read_bytes() == b"explicitly rejected freeze\n"
    assert (expected_root / "requests" / "fixture" / "request.json").read_bytes() == b"{}\n"


def test_rejected_archive_prefix_collision_fails_preflight_and_archive(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    external = tmp_path / "external"
    external.mkdir()
    freeze_path = external / source_gate.FREEZE_NAME
    freeze_path.write_bytes(b"colliding rejected freeze\n")
    shared_prefix = "1" * 32
    freeze_sha = shared_prefix + "2" * 32
    colliding_sha = shared_prefix + "3" * 32
    collision_root = source_gate.rejected_archive_root(external, colliding_sha)
    collision_root.mkdir(parents=True)
    value = deepcopy(registry())
    value["rejected_freezes"] = [
        {
            "reason": "Fixture prefix collision.",
            "sha256": freeze_sha,
            "status": "REJECTED_FAIL_CLOSED",
        }
    ]
    monkeypatch.setattr(source_gate, "sha256_file", lambda _path: freeze_sha)

    assert source_gate.rejected_archive_root(external, freeze_sha) == collision_root
    with pytest.raises(source_gate.GateError, match="archive already exists"):
        source_gate.validate_acquisition_target(value, external, expected_toolchain={})
    with pytest.raises(source_gate.GateError, match="archive already exists"):
        source_gate.archive_rejected_active_package(value, external)
    assert freeze_path.read_bytes() == b"colliding rejected freeze\n"
    assert not (collision_root / "rejection.json").exists()


def test_short_rejected_archive_root_fits_current_legacy_windows_path_budget() -> None:
    external = Path(
        r"C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts"
        r"\traffic_system_v2_20260821_170722\workstream_a"
    )
    freeze_sha = "178ac781ea094e4f09af9a38493babace8f78e8942301b86927e414f2675ca3d"
    longest_rejected_relative_path = Path(
        r"requests\usgs_3dep_bridge_metadata"
        r"\document_007_current_lidar_base_specification_media_page.json"
    )
    legacy_full_hash_path = (
        external
        / "attempts"
        / f"rejected_{freeze_sha}"
        / longest_rejected_relative_path
    )
    shortened_path = (
        source_gate.rejected_archive_root(external, freeze_sha)
        / longest_rejected_relative_path
    )

    assert len(str(legacy_full_hash_path)) == 281
    assert len(str(shortened_path)) == 249
    assert len(str(shortened_path)) < 260


@pytest.mark.parametrize(
    ("record_path", "expected"),
    [
        ("raw/../../outside.bin", "canonical external relative"),
        ("raw/payload.bin", "SHA-256 mismatch"),
    ],
)
def test_frozen_closure_rejects_unsafe_or_tampered_records_without_writes(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    record_path: str,
    expected: str,
) -> None:
    repo = tmp_path / "repo"
    external = tmp_path / "external"
    registry_path = repo / "tools/traffic_v2/geospatial/source_registry.json"
    tool = repo / "tools/traffic_v2/geospatial/source_gate.py"
    registry_path.parent.mkdir(parents=True)
    registry_path.write_text("{}\n", encoding="utf-8")
    tool.write_text("# tool fixture\n", encoding="utf-8")
    external.mkdir()
    payload = external / "raw/payload.bin"
    payload.parent.mkdir(parents=True)
    payload.write_bytes(b"payload")
    value = {
        "accepted_source_sha": "a" * 40,
        "coordinate_contract": {"bounds": [-1, -1, 1, 1]},
        "document_sources": [],
        "osm": {"id": "osm"},
        "sources": [],
        "specification": {"path": "spec.json", "sha256": "b" * 64},
    }
    toolchain = {"environment": {}, "manifest": {}, "requirements_lock": {}}
    record = {
        "bytes": payload.stat().st_size,
        "path": record_path,
        "sha256": "0" * 64,
    }
    freeze = {
        "accepted_product_bindings": [],
        "accepted_source_sha": value["accepted_source_sha"],
        "acquisition_completed_utc": source_gate.utc_now(),
        "coordinate_contract": value["coordinate_contract"],
        "product_binding_warnings": [],
        "registry": source_gate.file_record(registry_path, repo),
        "schema": "questflight.traffic-v2-geospatial-source-freeze.v1",
        "sources": [{"id": "osm", "raw": record}],
        "specification": value["specification"],
        "tool": source_gate.file_record(tool, repo),
        "toolchain": toolchain,
    }
    write_json(external / source_gate.FREEZE_NAME, freeze)
    before = {
        path.relative_to(external).as_posix(): path.read_bytes()
        for path in external.rglob("*")
        if path.is_file()
    }
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    monkeypatch.setattr(source_gate, "__file__", str(tool))
    monkeypatch.setattr(source_gate, "load_registry", lambda _path: value)
    monkeypatch.setattr(source_gate, "validate_pinned_inputs", lambda _value: None)
    monkeypatch.setattr(
        source_gate,
        "validate_or_create_external_toolchain_manifest",
        lambda *_args, **_kwargs: toolchain,
    )
    monkeypatch.setattr(source_gate, "inventory_product_bindings", lambda _value: ([], []))
    with pytest.raises(source_gate.GateError, match=expected):
        source_gate.validate_frozen_run_closure(registry_path, external)
    assert before == {
        path.relative_to(external).as_posix(): path.read_bytes()
        for path in external.rglob("*")
        if path.is_file()
    }


@pytest.mark.parametrize("command", ["compile", "validate", "rebuild-check"])
def test_frozen_commands_fail_preflight_before_creating_any_path(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    command: str,
) -> None:
    external = tmp_path / "nonexistent-external"
    data = tmp_path / "data"
    evidence = tmp_path / "evidence"
    monkeypatch.setattr(
        source_gate,
        "validate_frozen_run_closure",
        lambda *_args, **_kwargs: (_ for _ in ()).throw(
            source_gate.ToolchainPreflightError("preflight sentinel")
        ),
    )
    function = {
        "compile": source_gate.compile_artifacts,
        "validate": source_gate.validate_outputs,
        "rebuild-check": source_gate.rebuild_check,
    }[command]
    with pytest.raises(source_gate.ToolchainPreflightError, match="preflight sentinel"):
        function(source_gate.DEFAULT_REGISTRY, external, data, evidence)
    assert not external.exists()
    assert not data.exists()
    assert not evidence.exists()


@pytest.mark.parametrize("command", ["inventory", "acquire"])
def test_pre_acquisition_commands_fail_pinned_input_preflight_without_writes(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    command: str,
) -> None:
    external = tmp_path / "must-not-exist"
    monkeypatch.setattr(source_gate, "load_registry", lambda _path: {})
    monkeypatch.setattr(
        source_gate,
        "validate_pinned_inputs",
        lambda _value: (_ for _ in ()).throw(source_gate.GateError("pin sentinel")),
    )
    function = source_gate.inventory_sources if command == "inventory" else source_gate.acquire
    with pytest.raises(source_gate.GateError, match="pin sentinel"):
        function(source_gate.DEFAULT_REGISTRY, external)
    assert not external.exists()


def test_toolchain_contract_and_external_manifest_are_exact(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    value, registry_path, _, _ = isolated_toolchain_fixture(tmp_path, monkeypatch)
    validated = source_gate.validate_toolchain_contract(value, registry_path)
    assert validated["status"] == "PASS"
    assert validated["environment"]["runtime_identity"]["python_version"] == "3.13.13"
    assert validated["environment"]["native_libraries"] == value["toolchain"][
        "native_libraries"
    ]
    external = tmp_path / "external"
    binding = source_gate.validate_or_create_external_toolchain_manifest(
        value, registry_path, external, allow_create=True
    )
    assert binding["requirements_lock"] == value["toolchain"]["requirements_lock"]
    assert source_gate.validate_or_create_external_toolchain_manifest(
        value, registry_path, external, allow_create=False
    ) == binding
    freeze = {"toolchain": deepcopy(binding)}
    source_gate.validate_frozen_toolchain_binding(freeze, binding)
    freeze["toolchain"]["environment"]["native_libraries"]["GEOS"] = "0.0.0"
    with pytest.raises(source_gate.ToolchainPreflightError, match="freeze toolchain binding"):
        source_gate.validate_frozen_toolchain_binding(freeze, binding)


def test_toolchain_altered_lock_and_wrong_runtime_fail_before_external_write(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    value, registry_path, repo, _ = isolated_toolchain_fixture(tmp_path, monkeypatch)
    lock = repo / value["toolchain"]["requirements_lock"]["path"]
    original_lock = lock.read_bytes()
    lock.write_bytes(original_lock + b"\n")
    external = tmp_path / "must-not-exist"
    with pytest.raises(source_gate.ToolchainPreflightError, match="requirements-lock bytes/hash"):
        source_gate.validate_or_create_external_toolchain_manifest(
            value, registry_path, external, allow_create=True
        )
    assert not external.exists()

    lock.write_bytes(original_lock)
    actual = source_gate.live_toolchain_environment()
    wrong = deepcopy(actual)
    wrong["runtime_identity"]["python_version"] = "3.13.12"
    monkeypatch.setattr(source_gate, "live_toolchain_environment", lambda: wrong)
    with pytest.raises(source_gate.ToolchainPreflightError, match="runtime_identity"):
        source_gate.validate_or_create_external_toolchain_manifest(
            value, registry_path, external, allow_create=True
        )
    assert not external.exists()


def test_toolchain_missing_or_stale_external_manifest_is_fail_closed(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    value, registry_path, _, _ = isolated_toolchain_fixture(tmp_path, monkeypatch)
    external = tmp_path / "external"
    with pytest.raises(source_gate.ToolchainPreflightError, match="manifest is missing"):
        source_gate.validate_or_create_external_toolchain_manifest(
            value, registry_path, external, allow_create=False
        )
    source_gate.validate_or_create_external_toolchain_manifest(
        value, registry_path, external, allow_create=True
    )
    manifest = external / value["toolchain"]["manifest_path"]
    manifest.write_bytes(manifest.read_bytes() + b"stale")
    with pytest.raises(source_gate.ToolchainPreflightError, match="stale or inconsistent"):
        source_gate.validate_or_create_external_toolchain_manifest(
            value, registry_path, external, allow_create=False
        )


def test_payload_inventory_detects_modified_bytes_and_excludes_generated_cache(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    owned = tmp_path / "package.py"
    record = tmp_path / "fixture.dist-info/RECORD"
    cache = tmp_path / "__pycache__/package.cpython-313.pyc"
    owned.write_bytes(b"first")
    record.parent.mkdir(parents=True)
    record.write_bytes(b"package.py,,\nfixture.dist-info/RECORD,,\n")
    cache.parent.mkdir(parents=True)
    cache.write_bytes(b"generated")
    monkeypatch.setattr(
        source_gate,
        "_payload_location",
        lambda path: f"environment/{Path(path).resolve().relative_to(tmp_path.resolve()).as_posix()}",
    )
    before = source_gate.summarize_payload_files((owned, record, cache))
    assert before["file_count"] == 2
    owned.write_bytes(b"other")
    after = source_gate.summarize_payload_files((owned, record, cache))
    assert after["inventory_sha256"] != before["inventory_sha256"]
    assert after["total_bytes"] == before["total_bytes"]


def test_payload_cache_exclusion_is_case_insensitive() -> None:
    for value in (
        Path("__PYCACHE__/native.bin"),
        Path("Mixed/__PyCache__/native.bin"),
        Path("module.PYC"),
        Path("module.PYO"),
    ):
        assert source_gate._payload_cache_file(value)


def test_gate_import_origin_contract_covers_every_direct_import() -> None:
    tree = ast.parse(Path(source_gate.__file__).read_text(encoding="utf-8"))
    direct_modules: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            direct_modules.update(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module != "__future__":
            direct_modules.add(str(node.module))
    assert direct_modules <= set(source_gate.GATE_IMPORT_ORIGIN_MODULES)
    assert {
        "PIL.Image",
        "PIL.ImageDraw",
        "PIL.ImageFont",
        "PIL.features",
        "shapely.geometry",
        "shapely.ops",
        "shapely.strtree",
    } <= set(source_gate.GATE_IMPORT_ORIGIN_MODULES)


def test_distribution_payload_requires_every_record_member_and_binds_record_itself(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    class FakeDistribution:
        version = "1.0"
        metadata = {"Name": "fixture"}

        def __init__(self, members: list[str]) -> None:
            self.files = members

        def locate_file(self, member: object) -> Path:
            return tmp_path / str(member)

    package = tmp_path / "fixture/__init__.py"
    record = tmp_path / "fixture-1.0.dist-info/RECORD"
    package.parent.mkdir(parents=True)
    package.write_bytes(b"payload")
    record.parent.mkdir(parents=True)
    record.write_bytes(b"fixture/__init__.py,,\nfixture-1.0.dist-info/RECORD,,\n")
    monkeypatch.setattr(
        source_gate,
        "_payload_location",
        lambda path: f"environment/{Path(path).resolve().relative_to(tmp_path.resolve()).as_posix()}",
    )
    exact = source_gate.distribution_payload_inventory(
        FakeDistribution(["fixture/__init__.py", "fixture-1.0.dist-info/RECORD"])
    )
    assert exact["file_count"] == 2
    extra = tmp_path / "fixture/native.pyd"
    extra.write_bytes(b"native")
    expanded = source_gate.distribution_payload_inventory(
        FakeDistribution(
            ["fixture/__init__.py", "fixture/native.pyd", "fixture-1.0.dist-info/RECORD"]
        )
    )
    assert expanded["file_count"] == 3
    assert expanded["inventory_sha256"] != exact["inventory_sha256"]
    with pytest.raises(source_gate.ToolchainPreflightError, match="RECORD member is missing"):
        source_gate.distribution_payload_inventory(
            FakeDistribution(["fixture/__init__.py", "fixture/missing.pyd", "fixture-1.0.dist-info/RECORD"])
        )
    with pytest.raises(source_gate.ToolchainPreflightError, match="exactly one canonical"):
        source_gate.distribution_payload_inventory(FakeDistribution(["fixture/__init__.py"]))


def test_installed_distribution_names_are_pep503_unique_and_versions_are_required(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class FakeDistribution:
        files = []

        def __init__(self, name: str, version: str | None) -> None:
            self.metadata = {"Name": name}
            self.version = version

    monkeypatch.setattr(
        source_gate.importlib.metadata,
        "distributions",
        lambda: [FakeDistribution("a.b", "1"), FakeDistribution("a-b", "1")],
    )
    monkeypatch.setattr(
        source_gate,
        "distribution_payload_inventory",
        lambda *_args, **_kwargs: {
            "file_count": 1,
            "inventory_sha256": "0" * 64,
            "total_bytes": 1,
        },
    )
    with pytest.raises(source_gate.ToolchainPreflightError, match="Duplicate installed"):
        source_gate.installed_payload_identity()

    monkeypatch.setattr(
        source_gate.importlib.metadata,
        "distributions",
        lambda: [FakeDistribution("fixture", None)],
    )
    with pytest.raises(source_gate.ToolchainPreflightError, match="lacks a version"):
        source_gate.installed_payload_identity()


def test_runtime_payload_binds_virtual_environment_dlls(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    base = tmp_path / "base"
    environment = tmp_path / "environment"
    stdlib = base / "Lib"
    for path, payload in (
        (base / "python313.dll", b"runtime"),
        (base / "python.exe", b"launcher"),
        (stdlib / "module.py", b"stdlib"),
        (environment / "pyvenv.cfg", b"venv"),
        (environment / "Scripts/python.exe", b"venv-launcher"),
        (environment / "DLLs/venv_runtime.dll", b"native-a"),
    ):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)
    monkeypatch.setattr(source_gate.sys, "base_prefix", str(base))
    monkeypatch.setattr(source_gate.sys, "prefix", str(environment))
    monkeypatch.setattr(source_gate.sys, "executable", str(environment / "Scripts/python.exe"))
    monkeypatch.setattr(source_gate.sysconfig, "get_path", lambda _name: str(stdlib))

    def fixture_location(path: Path) -> str:
        resolved = Path(path).resolve()
        try:
            return "environment/" + resolved.relative_to(environment.resolve()).as_posix()
        except ValueError:
            return "base_runtime/" + resolved.relative_to(base.resolve()).as_posix()

    monkeypatch.setattr(source_gate, "_payload_location", fixture_location)
    before = source_gate.runtime_payload_inventory()
    (environment / "DLLs/venv_runtime.dll").write_bytes(b"native-b")
    after = source_gate.runtime_payload_inventory()
    assert before["inventory_sha256"] != after["inventory_sha256"]


def test_import_origin_must_be_owned_by_a_bound_payload(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(source_gate.importlib.metadata, "distributions", lambda: [])
    monkeypatch.setattr(
        source_gate,
        "runtime_payload_inventory",
        lambda **_kwargs: {
            "file_count": 1,
            "inventory_sha256": "0" * 64,
            "total_bytes": 1,
        },
    )
    with pytest.raises(source_gate.ToolchainPreflightError, match="not owned"):
        source_gate.installed_payload_identity()


@pytest.mark.parametrize(
    ("mutation", "message"),
    [
        ("installed_set", "payload_identity"),
        ("import_origin", "payload_identity"),
        ("runtime_payload", "payload_identity"),
    ],
)
def test_toolchain_payload_identity_mismatch_fails_before_external_write(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    mutation: str,
    message: str,
) -> None:
    value, registry_path, _, _ = isolated_toolchain_fixture(tmp_path, monkeypatch)
    environment = source_gate.live_toolchain_environment()
    changed = deepcopy(environment)
    if mutation == "installed_set":
        changed["payload_identity"]["installed_distributions"]["shadow-package"] = "1.0"
    elif mutation == "import_origin":
        changed["payload_identity"]["import_origins"]["shapely"] = (
            "environment/shadow/shapely/__init__.py"
        )
    else:
        changed["payload_identity"]["runtime_payload"]["inventory_sha256"] = "0" * 64
    monkeypatch.setattr(source_gate, "live_toolchain_environment", lambda: changed)
    external = tmp_path / "must-not-exist"
    with pytest.raises(source_gate.ToolchainPreflightError, match=message):
        source_gate.validate_or_create_external_toolchain_manifest(
            value, registry_path, external, allow_create=True
        )
    assert not external.exists()


def test_compile_run_context_binds_all_inputs_and_declared_tolerances(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    value, registry_path, _, _ = isolated_toolchain_fixture(tmp_path, monkeypatch)
    external = tmp_path / "external"
    binding = source_gate.validate_or_create_external_toolchain_manifest(
        value, registry_path, external, allow_create=True
    )
    freeze = external / source_gate.FREEZE_NAME
    write_json(freeze, {"toolchain": binding})
    context = source_gate.build_compile_run_context(
        value,
        registry_path,
        external,
        tmp_path / "data",
        tmp_path / "evidence",
        binding,
        freeze,
        git_context={
            "dirty": True,
            "head": value["accepted_source_sha"],
            "status_command": ["git", "status"],
            "status_porcelain_v1": "?? fixture\n",
            "status_porcelain_v1_sha256": source_gate.sha256_bytes(b"?? fixture\n"),
        },
    )
    assert set(context) == {
        "accepted_source_sha",
        "camera_contract",
        "command",
        "determinism",
        "git",
        "requirements_lock",
        "source_freeze",
        "source_registry",
        "source_tool",
        "specification",
        "tolerances",
        "toolchain",
    }
    assert context["requirements_lock"] == value["toolchain"]["requirements_lock"]
    assert context["toolchain"] == binding
    assert context["git"]["dirty"] is True
    assert context["command"]["argv"][1:3] == ["-I", "-B"]
    assert context["command"]["argv"][6] == "compile"
    source_gate.validate_recorded_compile_command(
        context["command"],
        registry_path=registry_path,
        external_run=external,
        data_dir=tmp_path / "data",
        evidence_dir=tmp_path / "evidence",
    )
    stale_command = deepcopy(context["command"])
    stale_command["argv"][stale_command["argv"].index("--external-run") + 1] = str(
        tmp_path / "other-run"
    )
    with pytest.raises(source_gate.GateError, match="arguments do not match"):
        source_gate.validate_recorded_compile_command(
            stale_command,
            registry_path=registry_path,
            external_run=external,
            data_dir=tmp_path / "data",
            evidence_dir=tmp_path / "evidence",
        )


def test_live_approval_context_uses_later_candidate_or_freeze_event(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    value, registry_path, _, _ = isolated_toolchain_fixture(tmp_path, monkeypatch)
    external = tmp_path / "external"
    source_gate.validate_or_create_external_toolchain_manifest(
        value, registry_path, external, allow_create=True
    )
    freeze = external / source_gate.FREEZE_NAME
    write_json(
        freeze,
        {
            "acquisition_completed_utc": "2026-08-21T00:00:00Z",
            "schema": "fixture",
        },
    )
    candidate_root = external / "approval_candidate"
    candidate_root.mkdir()
    artifact_hashes: dict[str, str] = {}
    for name in (*source_gate.EXPECTED_TEXT_ARTIFACTS, source_gate.EXPECTED_EXTERNAL_ARTIFACT):
        path = candidate_root / name
        path.write_bytes(name.encode())
        artifact_hashes[name] = source_gate.sha256_file(path)
    candidate = {
        "artifact_sha256": artifact_hashes,
        "camera_contract_sha256": "b" * 64,
        "candidate_event_utc": "2026-08-22T00:00:00Z",
        "canonical_universe_record_count": 7,
        "canonical_universe_sha256": "a" * 64,
        "schema": "questflight.traffic-v2-geospatial-approval-candidate.v1",
        "source_freeze_sha256": source_gate.sha256_file(freeze),
        "status": "PENDING_LEAD_D_INSPECTION",
    }
    write_json(candidate_root / "candidate_manifest.json", candidate)
    context = source_gate.build_approval_validation_context(
        value,
        registry_path,
        external,
        {
            "camera_contract": {"sha256": "b" * 64},
            "canonical_universe": {"record_count": 7, "sha256": "a" * 64},
        },
        source_gate.sha256_file(freeze),
        candidate,
    )
    assert context["minimum_inspection_utc"] == "2026-08-22T00:00:00Z"


def test_main_dispatches_acquisition_without_reading_an_exception_local(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    calls: list[tuple[Path, Path]] = []

    def fake_acquire(registry_path: Path, external_run: Path) -> None:
        calls.append((registry_path, external_run))

    monkeypatch.setattr(source_gate, "acquire", fake_acquire)
    monkeypatch.setattr(
        source_gate.sys,
        "argv",
        ["source_gate.py", "acquire", "--external-run", str(tmp_path)],
    )

    assert source_gate.main() == 0
    assert calls == [(source_gate.DEFAULT_REGISTRY, tmp_path)]


def test_concurrent_acquisition_failure_does_not_write_into_the_active_package(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    def reject_concurrent_writer(_registry_path: Path, _external_run: Path) -> None:
        raise source_gate.ConcurrentAcquisitionError("writer already active")

    monkeypatch.setattr(source_gate, "acquire", reject_concurrent_writer)
    monkeypatch.setattr(
        source_gate.sys,
        "argv",
        ["source_gate.py", "acquire", "--external-run", str(tmp_path)],
    )
    monkeypatch.setattr(
        source_gate,
        "atomic_write_json",
        lambda *_args, **_kwargs: pytest.fail("concurrent failure must not write package bytes"),
    )

    assert source_gate.main() == 2


def test_enterprise_portal_info_url_is_derived_from_server_root() -> None:
    assert source_gate.arcgis_server_info_url(
        "https://gis.bouldercolorado.gov/ags_svr3/rest/services/trans/Signals/MapServer"
    ) == "https://gis.bouldercolorado.gov/ags_svr3/rest/info"


def test_arcgis_item_service_url_whitespace_is_normalized() -> None:
    assert source_gate.resolve_layer_url(
        {"url": " https://example.test/FeatureServer "},
        {"id": "example", "layer_index": 2},
    ) == "https://example.test/FeatureServer/2"


def test_object_id_where_clause_is_numeric_and_fail_closed() -> None:
    assert source_gate.oid_where_clause("AssetId", [3, 1, 2]) == "AssetId IN (3,1,2)"
    with pytest.raises(source_gate.GateError, match="Unsafe"):
        source_gate.oid_where_clause("Asset Id", [1])
    with pytest.raises(source_gate.GateError, match="Non-integer"):
        source_gate.oid_where_clause("OBJECTID", ["1 OR 1=1"])


def test_service_specific_query_workarounds_are_narrow() -> None:
    sources = {value["id"]: value for value in registry()["sources"]}
    assert sources["city_signals"]["feature_query_mode"] == "oid_where"
    assert sources["city_signals"]["batch_size_cap"] == 100
    assert sources["county_traffic_stations"]["count_query_mode"] == "ids_length"
    assert sources["drcog_pavement_polygons_2024"]["count_query_mode"] == "server_count_advisory"
    assert sources["drcog_pavement_polygons_2024"]["expected_server_count_minus_ids"] == 2


def test_exact_osm_duplicates_are_deduplicated_and_inconsistent_repeats_fail() -> None:
    node = {"id": 1, "lat": 40.0, "lon": -105.0, "type": "node", "version": 1}
    way = {"id": 10, "nodes": [1], "tags": {"highway": "primary"}, "type": "way", "version": 1}
    unique, audit = source_gate.deduplicate_osm_elements([node, node, way, way])
    assert len(unique) == 2
    assert audit["occurrence_count_by_type"] == {"node": 2, "way": 2}
    assert audit["unique_count_by_type"] == {"node": 1, "way": 1}
    assert audit["duplicate_occurrence_count_by_type"] == {"node": 1, "way": 1}
    changed = deepcopy(way)
    changed["version"] = 2
    with pytest.raises(source_gate.GateError, match="Inconsistent duplicate OSM way/10"):
        source_gate.deduplicate_osm_elements([way, changed])


@pytest.mark.parametrize(
    ("tags", "decision", "reason", "road_class"),
    [
        ({"highway": "primary"}, "ELIGIBLE", None, "primary"),
        ({"highway": "service"}, "UNKNOWN_BLOCKING", "UNSPECIFIED_SERVICE_ROAD_USE", None),
        ({"highway": "service", "service": "driveway"}, "EXCLUDED", "EXCLUDED_SERVICE_DRIVEWAY", None),
        ({"highway": "busway"}, "EXCLUDED", "EXCLUDED_TRANSIT_ONLY", None),
        ({"highway": "unclassified"}, "UNKNOWN_BLOCKING", "UNKNOWN_GATE_ROAD_CLASS", None),
        ({"access": "no", "highway": "primary"}, "EXCLUDED", "EXCLUDED_NO_MOTOR_ACCESS", "primary"),
        (
            {"access": "no", "highway": "motorway_link", "motor_vehicle": "designated"},
            "ELIGIBLE",
            None,
            "motorway",
        ),
        (
            {"access": "aircraft_only", "highway": "primary"},
            "UNKNOWN_BLOCKING",
            "UNKNOWN_EFFECTIVE_OSM_ACCESS_VALUE",
            "primary",
        ),
        ({"highway": "future-new-class"}, "UNKNOWN_BLOCKING", "UNKNOWN_OSM_HIGHWAY_CLASS", None),
    ],
)
def test_osm_highway_and_access_crosswalk_is_explicit(
    tags: dict, decision: str, reason: str | None, road_class: str | None
) -> None:
    value = source_gate.osm_eligibility(registry(), tags)
    assert value["decision"] == decision
    assert value["reason"] == reason
    assert value["highway_class"] == road_class


def test_osm_access_specificity_and_raw_evidence_are_frozen() -> None:
    value = source_gate.osm_eligibility(
        registry(),
        {
            "access": "no",
            "highway": "motorway_link",
            "motor_vehicle": "designated",
            "vehicle": "delivery",
        },
    )
    assert value["decision"] == "ELIGIBLE"
    assert value["access_evidence"] == {
        "access": "no",
        "motor_vehicle": "designated",
        "vehicle": "delivery",
    }
    assert value["effective_access_key"] == "motor_vehicle"
    assert value["effective_access_value"] == "designated"
    assert value["effective_access_decision"] == "ALLOW"


def test_conditional_osm_access_and_direction_are_fail_closed() -> None:
    value = source_gate.osm_eligibility(
        registry(),
        {
            "access": "yes",
            "access:conditional": "no @ (snow)",
            "highway": "primary",
        },
    )
    assert value["decision"] == "UNKNOWN_BLOCKING"
    assert value["effective_access_key"] == "access:conditional"
    assert value["effective_access_value"] == "no @ (snow)"
    assert value["effective_access_reason"] == "CONDITIONAL_MOTOR_ACCESS_UNRESOLVED"
    assert value["conditional_access_evidence"] == {
        "access:conditional": "no @ (snow)"
    }

    record = line_record(
        "osm_bounded_roads",
        "way/conditional",
        [[0.0, 0.0], [100.0, 0.0]],
        tags={
            "bridge": "no",
            "bridge:conditional": "yes @ (flood)",
            "highway": "primary",
            "name": "Main",
            "oneway": "no",
            "oneway:conditional": "yes @ (07:00-09:00)",
        },
    )
    semantics = source_gate.road_semantic_evidence(registry(), record)
    assert semantics["direction"]["value"] == "UNKNOWN"
    assert semantics["direction"]["reason"] == "CONDITIONAL_ONEWAY_UNRESOLVED"
    assert not semantics["direction"]["usable_for_correspondence"]
    assert semantics["grade"]["bridge"]["reason"] == "CONDITIONAL_BRIDGE_UNRESOLVED"
    assert not semantics["grade"]["bridge"]["usable_for_correspondence"]


def test_osm_lane_components_include_both_ways_and_conflicts_fail_closed() -> None:
    complete = line_record(
        "osm_bounded_roads",
        "way/lanes",
        [[0.0, 0.0], [100.0, 0.0]],
        tags={
            "highway": "primary",
            "lanes:backward": "1",
            "lanes:both_ways": "1",
            "lanes:forward": "1",
        },
    )
    evidence = source_gate.lane_count_evidence(registry(), complete)
    assert evidence["lane_count"] == 3
    assert evidence["lane_count_authority"].endswith(
        "lanes:forward+lanes:backward+lanes:both_ways"
    )
    assert evidence["declared_lane_components"]["lanes:both_ways"] == "1"

    inconsistent = deepcopy(complete)
    inconsistent["tags"].update(
        {"lanes": "4", "lanes:backward": "3", "lanes:both_ways": "0", "lanes:forward": "2"}
    )
    conflict = source_gate.lane_count_evidence(registry(), inconsistent)
    assert conflict["lane_count"] is None
    assert conflict["confidence"] == "UNKNOWN_FAIL_CLOSED"
    assert conflict["reason"] == "INCONSISTENT_TOTAL_AND_DIRECTIONAL_LANE_COUNTS"


@pytest.mark.parametrize(
    "partial",
    [
        {"lanes": "4", "lanes:forward": "5"},
        {"lanes": "4", "lanes:both_ways": "1", "lanes:forward": "4"},
    ],
)
def test_osm_partial_directional_lane_sum_cannot_exceed_declared_total(
    partial: dict[str, str],
) -> None:
    record = line_record(
        "osm_bounded_roads",
        "way/partial-exceeds",
        [[0.0, 0.0], [100.0, 0.0]],
        tags={"highway": "primary", **partial},
    )
    evidence = source_gate.lane_count_evidence(registry(), record)
    assert evidence["lane_count"] is None
    assert evidence["confidence"] == "UNKNOWN_FAIL_CLOSED"
    assert evidence["reason"] == "INCONSISTENT_TOTAL_AND_DIRECTIONAL_LANE_COUNTS"


@pytest.mark.parametrize(
    ("key", "invalid_value"),
    [
        ("lanes:forward", "0"),
        ("lanes:backward", "1.5"),
        ("lanes:both_ways", "-1"),
    ],
)
def test_osm_present_invalid_directional_lane_component_is_fail_closed(
    key: str, invalid_value: str
) -> None:
    record = line_record(
        "osm_bounded_roads",
        "way/invalid-component",
        [[0.0, 0.0], [100.0, 0.0]],
        tags={"highway": "primary", "lanes": "4", key: invalid_value},
    )
    evidence = source_gate.lane_count_evidence(registry(), record)
    assert evidence["lane_count"] is None
    assert evidence["confidence"] == "UNKNOWN_FAIL_CLOSED"
    assert evidence["reason"] == "INVALID_DECLARED_DIRECTIONAL_LANE_COMPONENT"


def test_osm_valid_partial_directional_lane_components_preserve_declared_total() -> None:
    record = line_record(
        "osm_bounded_roads",
        "way/valid-partial",
        [[0.0, 0.0], [100.0, 0.0]],
        tags={
            "highway": "primary",
            "lanes": "4",
            "lanes:backward": "1",
            "lanes:forward": "2",
        },
    )
    evidence = source_gate.lane_count_evidence(registry(), record)
    assert evidence["lane_count"] == 4
    assert evidence["confidence"] == "DECLARED_ODBL_FALLBACK"
    assert evidence["lane_count_authority"] == "osm_bounded_roads.lanes"
    assert evidence["reason"] is None

    for record_id, tags in (
        ("way/total-only", {"highway": "primary", "lanes": "4"}),
        (
            "way/complete-exact",
            {
                "highway": "primary",
                "lanes": "4",
                "lanes:backward": "1",
                "lanes:both_ways": "1",
                "lanes:forward": "2",
            },
        ),
    ):
        valid = source_gate.lane_count_evidence(
            registry(),
            line_record(
                "osm_bounded_roads",
                record_id,
                [[0.0, 0.0], [100.0, 0.0]],
                tags=tags,
            ),
        )
        assert valid["lane_count"] == 4
        assert valid["lane_count_authority"] == "osm_bounded_roads.lanes"
        assert valid["reason"] is None


@pytest.mark.parametrize(
    ("tags", "decision", "reason"),
    [
        (
            {"access:lanes": "no|no", "highway": "primary", "lanes": "2"},
            "EXCLUDED",
            "ALL_LANES_EXPLICITLY_NO_MOTOR_ACCESS",
        ),
        (
            {"access:lanes": "yes|no", "highway": "primary", "lanes": "2"},
            "UNKNOWN_BLOCKING",
            "MIXED_OR_RESTRICTED_LANE_MOTOR_ACCESS",
        ),
        (
            {"access:lanes": "private|private", "highway": "primary", "lanes": "2"},
            "UNKNOWN_BLOCKING",
            "MIXED_OR_RESTRICTED_LANE_MOTOR_ACCESS",
        ),
        (
            {"access:lanes": "yes|future", "highway": "primary", "lanes": "2"},
            "UNKNOWN_BLOCKING",
            "UNMAPPED_LANE_MOTOR_ACCESS_VALUE",
        ),
        (
            {"access:lanes": "yes", "highway": "primary", "lanes": "2"},
            "UNKNOWN_BLOCKING",
            "LANE_MOTOR_ACCESS_TOKEN_CARDINALITY_MISMATCH",
        ),
        (
            {
                "access:lanes:conditional": "no|no @ (snow)",
                "highway": "primary",
                "lanes": "2",
            },
            "UNKNOWN_BLOCKING",
            "CONDITIONAL_LANE_MOTOR_ACCESS_UNRESOLVED",
        ),
    ],
)
def test_osm_lane_access_is_explicit_and_fail_closed(
    tags: dict[str, str], decision: str, reason: str
) -> None:
    value = source_gate.osm_eligibility(registry(), tags)
    assert value["decision"] == decision
    assert value["reason"] == reason
    evidence = source_gate.lane_count_evidence(
        registry(),
        line_record(
            "osm_bounded_roads",
            "way/lane-access",
            [[0.0, 0.0], [100.0, 0.0]],
            tags=tags,
        ),
    )
    if decision != "ELIGIBLE":
        assert evidence["lane_count"] is None
        assert evidence["confidence"] == "UNKNOWN_FAIL_CLOSED"


def test_osm_lane_access_specificity_and_directional_consistency() -> None:
    value = source_gate.osm_eligibility(
        registry(),
        {
            "access:lanes": "no|no",
            "highway": "primary",
            "lanes": "2",
            "motor_vehicle:lanes": "no|no",
            "motorcar:lanes": "yes|yes",
        },
    )
    assert value["decision"] == "ELIGIBLE"
    lane_access = value["lane_access_evidence"]
    assert lane_access["effective_mode"] == "motorcar"
    assert set(lane_access["raw_evidence"]) == {
        "access:lanes",
        "motor_vehicle:lanes",
        "motorcar:lanes",
    }

    narrower_mode_is_more_specific = source_gate.osm_eligibility(
        registry(),
        {
            "access:lanes": "future|future",
            "highway": "primary",
            "lanes": "2",
            "motorcar": "yes",
        },
    )
    assert narrower_mode_is_more_specific["decision"] == "ELIGIBLE"
    assert narrower_mode_is_more_specific["effective_access_key"] == "motorcar"
    assert all(
        lane["decision"] == "ALLOW" and lane["mode"] == "motorcar"
        for lane in narrower_mode_is_more_specific["lane_access_evidence"][
            "resolved_active_lanes"
        ]
    )

    directional = source_gate.osm_eligibility(
        registry(),
        {
            "access:lanes:backward": "no",
            "access:lanes:forward": "yes",
            "highway": "primary",
            "lanes": "2",
            "lanes:backward": "1",
            "lanes:forward": "1",
            "oneway": "no",
        },
    )
    assert directional["decision"] == "UNKNOWN_BLOCKING"
    assert directional["reason"] == "MIXED_OR_RESTRICTED_LANE_MOTOR_ACCESS"

    directional_lane_override = source_gate.osm_eligibility(
        registry(),
        {
            "access:lanes": "no|no",
            "access:lanes:backward": "yes",
            "access:lanes:forward": "yes",
            "highway": "primary",
            "lanes": "2",
            "lanes:backward": "1",
            "lanes:forward": "1",
        },
    )
    assert directional_lane_override["decision"] == "ELIGIBLE"
    assert all(
        value["scope"] == "LANE_DIRECTION"
        for value in directional_lane_override["lane_access_evidence"][
            "resolved_active_lanes"
        ]
    )


def test_osm_access_cascades_every_active_lane_across_scope_and_mode() -> None:
    mixed = source_gate.osm_eligibility(
        registry(),
        {
            "highway": "primary",
            "lanes": "2",
            "lanes:backward": "1",
            "lanes:forward": "1",
            "motor_vehicle": "no",
            "motorcar:lanes:forward": "yes",
            "oneway": "no",
        },
    )
    assert mixed["decision"] == "UNKNOWN_BLOCKING"
    assert mixed["reason"] == "MIXED_OR_RESTRICTED_LANE_MOTOR_ACCESS"
    active = mixed["lane_access_evidence"]["resolved_active_lanes"]
    assert {(value["direction"], value["decision"]) for value in active} == {
        ("forward", "ALLOW"),
        ("backward", "EXCLUDE"),
    }

    blank_lane_inherits = source_gate.osm_eligibility(
        registry(),
        {
            "highway": "primary",
            "lanes": "2",
            "motor_vehicle:lanes": "no|no",
            "motorcar:lanes": "|yes",
        },
    )
    assert blank_lane_inherits["decision"] == "UNKNOWN_BLOCKING"
    resolved = blank_lane_inherits["lane_access_evidence"]["resolved_active_lanes"]
    assert [value["decision"] for value in resolved] == ["EXCLUDE", "ALLOW"]
    assert any(value["trace"] for value in resolved)

    blank_whole_inherits = source_gate.osm_eligibility(
        registry(),
        {
            "highway": "primary",
            "motor_vehicle": "designated",
            "motorcar": "   ",
        },
    )
    assert blank_whole_inherits["decision"] == "ELIGIBLE"
    assert blank_whole_inherits["effective_access_key"] == "motor_vehicle"

    all_excluded = source_gate.osm_eligibility(
        registry(),
        {
            "access": "yes",
            "highway": "primary",
            "lanes": "2",
            "motorcar:lanes": "no|no",
        },
    )
    assert all_excluded["decision"] == "EXCLUDED"
    assert all_excluded["reason"] == "ALL_LANES_EXPLICITLY_NO_MOTOR_ACCESS"


def test_osm_mode_major_generic_lanes_resolve_both_direction_paths() -> None:
    evidence = source_gate.osm_eligibility(
        registry(),
        {
            "highway": "primary",
            "lanes": "2",
            "motor_vehicle:backward": "yes",
            "motor_vehicle:forward": "no",
            "motor_vehicle:lanes": "|yes",
        },
    )
    assert evidence["decision"] == "UNKNOWN_BLOCKING"
    assert evidence["reason"] == "AMBIGUOUS_BIDIRECTIONAL_LANE_MOTOR_ACCESS"
    lanes = evidence["lane_access_evidence"]["resolved_active_lanes"]
    assert [lane["decision"] for lane in lanes] == ["UNKNOWN_BLOCKING", "ALLOW"]
    assert {
        (branch["direction"], branch["decision"], branch["key"])
        for branch in lanes[0]["directional_resolution_branches"]
    } == {
        ("backward", "ALLOW", "motor_vehicle:backward"),
        ("forward", "EXCLUDE", "motor_vehicle:forward"),
    }
    assert {
        (branch["direction"], branch["decision"], branch["key"])
        for branch in lanes[1]["directional_resolution_branches"]
    } == {
        ("backward", "ALLOW", "motor_vehicle:lanes"),
        ("forward", "ALLOW", "motor_vehicle:lanes"),
    }

    identical = source_gate.osm_eligibility(
        registry(),
        {
            "highway": "primary",
            "lanes": "2",
            "motor_vehicle:backward": "yes",
            "motor_vehicle:forward": "yes",
            "motor_vehicle:lanes": "|",
        },
    )
    assert identical["decision"] == "ELIGIBLE"
    assert all(
        {
            (branch["direction"], branch["decision"])
            for branch in lane["directional_resolution_branches"]
        }
        == {("backward", "ALLOW"), ("forward", "ALLOW")}
        for lane in identical["lane_access_evidence"]["resolved_active_lanes"]
    )


def test_osm_narrower_directional_mode_precedes_broader_lane_mode() -> None:
    evidence = source_gate.osm_eligibility(
        registry(),
        {
            "access:lanes": "yes|yes",
            "highway": "primary",
            "lanes": "2",
            "motorcar:forward": "no",
        },
    )
    assert evidence["decision"] == "UNKNOWN_BLOCKING"
    assert evidence["reason"] == "AMBIGUOUS_BIDIRECTIONAL_LANE_MOTOR_ACCESS"
    for lane in evidence["lane_access_evidence"]["resolved_active_lanes"]:
        assert {
            (branch["direction"], branch["decision"], branch["key"])
            for branch in lane["directional_resolution_branches"]
        } == {
            ("backward", "ALLOW", "access:lanes"),
            ("forward", "EXCLUDE", "motorcar:forward"),
        }


def test_osm_narrower_mode_conditional_precedes_broader_lane_mode() -> None:
    evidence = source_gate.osm_eligibility(
        registry(),
        {
            "access:lanes": "yes|yes",
            "highway": "primary",
            "lanes": "2",
            "motor_vehicle:conditional": "no @ (snow)",
        },
    )
    assert evidence["decision"] == "UNKNOWN_BLOCKING"
    assert evidence["effective_access_key"] == "motor_vehicle:conditional"
    assert evidence["reason"] == "CONDITIONAL_MOTOR_ACCESS_UNRESOLVED"
    assert all(
        lane["decision"] == "UNKNOWN_BLOCKING"
        and lane["key"] == "motor_vehicle:conditional"
        and {
            (branch["direction"], branch["decision"], branch["key"])
            for branch in lane["directional_resolution_branches"]
        }
        == {
            ("backward", "UNKNOWN_BLOCKING", "motor_vehicle:conditional"),
            ("forward", "UNKNOWN_BLOCKING", "motor_vehicle:conditional"),
        }
        for lane in evidence["lane_access_evidence"]["resolved_active_lanes"]
    )


def test_osm_both_ways_lane_resolves_static_forward_and_backward_branches() -> None:
    tags = {
        "access:lanes": "yes|yes|yes",
        "highway": "primary",
        "lanes": "3",
        "lanes:backward": "1",
        "lanes:both_ways": "1",
        "lanes:forward": "1",
        "motor_vehicle:backward": "no",
        "motor_vehicle:forward": "no",
        "oneway": "no",
    }
    evidence = source_gate.osm_eligibility(registry(), tags)
    assert evidence["decision"] == "EXCLUDED"
    both_ways = next(
        lane
        for lane in evidence["lane_access_evidence"]["resolved_active_lanes"]
        if lane["global_index"] == 2
    )
    assert {
        (branch["direction"], branch["decision"], branch["key"])
        for branch in both_ways["directional_resolution_branches"]
    } == {
        ("backward", "EXCLUDE", "motor_vehicle:backward"),
        ("forward", "EXCLUDE", "motor_vehicle:forward"),
    }


def test_osm_both_ways_conditional_branch_blocks_and_erases_known_lane_count() -> None:
    tags = {
        "access:lanes": "yes|yes|yes",
        "highway": "primary",
        "lanes": "3",
        "lanes:backward": "1",
        "lanes:both_ways": "1",
        "lanes:forward": "1",
        "motor_vehicle:backward:conditional": "no @ (snow)",
        "motor_vehicle:forward": "no",
        "oneway": "no",
    }
    evidence = source_gate.osm_eligibility(registry(), tags)
    assert evidence["decision"] == "UNKNOWN_BLOCKING"
    both_ways = next(
        lane
        for lane in evidence["lane_access_evidence"]["resolved_active_lanes"]
        if lane["global_index"] == 2
    )
    assert both_ways["decision"] == "UNKNOWN_BLOCKING"
    assert any(
        branch["reason"] == "CONDITIONAL_DIRECTIONAL_MOTOR_ACCESS_UNRESOLVED"
        for branch in both_ways["directional_resolution_branches"]
    )
    lane_evidence = source_gate.lane_count_evidence(
        registry(),
        {
            "source_id": registry()["osm"]["id"],
            "source_record_id": "way/1",
            "source_record_part": 0,
            "tags": tags,
        },
    )
    assert lane_evidence["lane_count"] is None
    assert lane_evidence["confidence"] == "UNKNOWN_FAIL_CLOSED"


@pytest.mark.parametrize(
    ("tags", "reason"),
    [
        (
            {
                "access:lanes:forward": "yes",
                "highway": "primary",
                "lanes": "2",
                "oneway": "no",
            },
            "LANE_MOTOR_ACCESS_DIRECTION_COUNT_MISSING",
        ),
        (
            {
                "access:lanes:forward": "yes",
                "highway": "primary",
                "lanes": "2",
                "lanes:forward": "1",
                "oneway": "no",
            },
            "LANE_MOTOR_ACCESS_DIRECTION_COUNT_INCOMPLETE",
        ),
        (
            {
                "access:lanes:backward": "no|no",
                "highway": "primary",
                "lanes": "2",
                "oneway": "yes",
            },
            "LANE_MOTOR_ACCESS_DIRECTION_CONTRADICTS_ONEWAY",
        ),
    ],
)
def test_osm_directional_lane_access_requires_consistent_lane_authority(
    tags: dict[str, str], reason: str
) -> None:
    evidence = source_gate.osm_eligibility(registry(), tags)
    assert evidence["decision"] == "UNKNOWN_BLOCKING"
    assert evidence["reason"] == reason


def test_osm_whole_and_directional_access_cascade_per_active_direction() -> None:
    explicit_override = source_gate.osm_eligibility(
        registry(),
        {
            "access": "no",
            "highway": "primary",
            "motor_vehicle": "designated",
        },
    )
    assert explicit_override["decision"] == "ELIGIBLE"
    assert explicit_override["effective_access_key"] == "motor_vehicle"

    excluded = source_gate.osm_eligibility(
        registry(),
        {
            "access:backward": "no",
            "access:forward": "no",
            "highway": "primary",
            "oneway": "no",
        },
    )
    assert excluded["decision"] == "EXCLUDED"

    mixed = source_gate.osm_eligibility(
        registry(),
        {
            "access:backward": "no",
            "access:forward": "no",
            "highway": "primary",
            "motor_vehicle:forward": "designated",
            "oneway": "no",
        },
    )
    assert mixed["decision"] == "UNKNOWN_BLOCKING"
    assert mixed["reason"] == "MIXED_DIRECTIONAL_MOTOR_ACCESS"

    unknown = source_gate.osm_eligibility(
        registry(),
        {"access:forward": "future", "highway": "primary", "oneway": "no"},
    )
    assert unknown["decision"] == "UNKNOWN_BLOCKING"


@pytest.mark.parametrize(
    "tags",
    [
        {"highway": "primary", "lanes": "4", "lanes:backward": "2", "lanes:forward": "2", "oneway": "yes"},
        {"highway": "primary", "lanes": "4", "lanes:backward": "2", "lanes:forward": "2", "oneway": "-1"},
        {"highway": "primary", "lanes": "3", "lanes:both_ways": "1", "lanes:forward": "2", "oneway": "yes"},
    ],
)
def test_osm_oneway_and_directional_lane_components_cannot_contradict(
    tags: dict[str, str],
) -> None:
    eligibility = source_gate.osm_eligibility(registry(), tags)
    assert eligibility["decision"] == "UNKNOWN_BLOCKING"
    assert eligibility["reason"] == "INCONSISTENT_ONEWAY_AND_DIRECTIONAL_LANE_COUNTS"
    evidence = source_gate.lane_count_evidence(
        registry(),
        line_record(
            "osm_bounded_roads",
            "way/oneway-lane-conflict",
            [[0.0, 0.0], [100.0, 0.0]],
            tags=tags,
        ),
    )
    assert evidence["lane_count"] is None
    assert evidence["reason"].startswith("INELIGIBLE_OR_UNRESOLVED_OSM_ACCESS:")


def test_osm_conditional_lane_counts_and_mode_specific_oneway_fail_closed() -> None:
    conditional_lanes = line_record(
        "osm_bounded_roads",
        "way/conditional-lanes",
        [[0.0, 0.0], [100.0, 0.0]],
        tags={"highway": "primary", "lanes": "2", "lanes:conditional": "3 @ (Mo-Fr)"},
    )
    lanes = source_gate.lane_count_evidence(registry(), conditional_lanes)
    assert lanes["lane_count"] is None
    assert lanes["reason"] == "CONDITIONAL_LANE_COUNT_UNRESOLVED"

    static = line_record(
        "osm_bounded_roads",
        "way/mode-oneway",
        [[0.0, 0.0], [100.0, 0.0]],
        tags={
            "highway": "primary",
            "oneway": "no",
            "oneway:motor_vehicle": "yes",
        },
    )
    direction = source_gate.road_semantic_evidence(registry(), static)["direction"]
    assert direction["effective_key"] == "oneway:motor_vehicle"
    assert direction["value"] == "FORWARD"

    conditional_tags = {
        "highway": "primary",
        "lanes": "2",
        "oneway": "no",
        "oneway:motorcar:conditional": "yes @ (07:00-09:00)",
        "oneway:motor_vehicle": "yes",
    }
    eligibility = source_gate.osm_eligibility(registry(), conditional_tags)
    assert eligibility["decision"] == "UNKNOWN_BLOCKING"
    assert eligibility["reason"] == "CONDITIONAL_MODE_SPECIFIC_ONEWAY_UNRESOLVED"
    semantics = source_gate.road_semantic_evidence(
        registry(),
        line_record(
            "osm_bounded_roads",
            "way/conditional-mode-oneway",
            [[0.0, 0.0], [100.0, 0.0]],
            tags=conditional_tags,
        ),
    )
    assert semantics["direction"]["value"] == "UNKNOWN"
    assert not semantics["direction"]["usable_for_correspondence"]


def test_osm_whole_way_directional_access_uses_specificity_and_blocks_mixed() -> None:
    directional_override = source_gate.osm_eligibility(
        registry(),
        {
            "access": "no",
            "access:backward": "yes",
            "access:forward": "yes",
            "highway": "primary",
        },
    )
    assert directional_override["decision"] == "ELIGIBLE"

    specific = source_gate.osm_eligibility(
        registry(),
        {
            "access:backward": "no",
            "access:forward": "no",
            "highway": "primary",
            "motorcar:backward": "yes",
            "motorcar:forward": "yes",
        },
    )
    assert specific["decision"] == "ELIGIBLE"
    assert specific["effective_access_key"] == "motorcar:forward"

    mixed = source_gate.osm_eligibility(
        registry(),
        {"access:backward": "yes", "access:forward": "no", "highway": "primary"},
    )
    assert mixed["decision"] == "UNKNOWN_BLOCKING"
    assert mixed["reason"] == "MIXED_DIRECTIONAL_MOTOR_ACCESS"

    conditional = source_gate.osm_eligibility(
        registry(),
        {
            "highway": "primary",
            "motor_vehicle:forward": "yes",
            "motor_vehicle:forward:conditional": "no @ (snow)",
        },
    )
    assert conditional["decision"] == "UNKNOWN_BLOCKING"
    assert conditional["reason"] == "CONDITIONAL_DIRECTIONAL_MOTOR_ACCESS_UNRESOLVED"


def test_mode_specific_oneway_uncertainty_blocks_cross_source_correspondence() -> None:
    value = semantic_registry()
    osm_record = line_record(
        "osm_bounded_roads",
        "way/mode-conditional",
        [[0.0, 0.0], [100.0, 0.0]],
        tags={
            "bridge": "no",
            "highway": "primary",
            "junction": "no",
            "layer": "0",
            "name": "Main",
            "oneway:motorcar:conditional": "yes @ (07:00-09:00)",
            "tunnel": "no",
        },
    )
    authority = line_record(
        "test_a",
        "test/1",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes=semantic_attributes(CARRIAGEWAY="D", DIRECTION="F"),
    )
    proof = source_gate.road_correspondence(
        value,
        osm_record,
        authority,
        source_gate.LineString([[0.0, 0.0], [100.0, 0.0]]),
        source_gate.LineString([[0.0, 0.0], [100.0, 0.0]]),
        minimum_ratio=0.8,
    )
    assert proof["proof_status"] == "NOT_PROVEN_FAIL_CLOSED"
    assert proof["checks"]["direction"]["reason"] == "UNKNOWN_DIRECTION_SEMANTICS"


def test_unspecified_and_unknown_service_roads_never_shrink_the_denominator() -> None:
    unspecified = source_gate.osm_eligibility(registry(), {"highway": "service"})
    unknown = source_gate.osm_eligibility(
        registry(), {"highway": "service", "service": "future_subtype"}
    )
    restricted = source_gate.osm_eligibility(
        registry(), {"access": "customers", "highway": "service"}
    )
    assert (unspecified["decision"], unspecified["reason"]) == (
        "UNKNOWN_BLOCKING",
        "UNSPECIFIED_SERVICE_ROAD_USE",
    )
    assert (unknown["decision"], unknown["reason"]) == (
        "UNKNOWN_BLOCKING",
        "UNKNOWN_OSM_SERVICE_SUBTYPE",
    )
    assert (restricted["decision"], restricted["effective_access_decision"]) == (
        "UNKNOWN_BLOCKING",
        "UNKNOWN_BLOCKING",
    )


def test_osm_normalization_accounts_for_every_unique_way(tmp_path: Path) -> None:
    nodes = [
        {"id": 1, "lat": 40.039, "lon": -105.227, "type": "node", "version": 1},
        {"id": 2, "lat": 40.039, "lon": -105.226, "type": "node", "version": 1},
    ]
    ways = [
        {
            "id": 10,
            "nodes": [1, 2],
            "tags": {
                "highway": "primary",
                "lanes": "2",
                "level": "0",
            },
            "type": "way",
            "version": 1,
        },
        {"id": 11, "nodes": [1, 2], "tags": {"highway": "service"}, "type": "way", "version": 1},
        {"id": 12, "nodes": [1, 2], "tags": {"highway": "busway"}, "type": "way", "version": 1},
        {"id": 13, "nodes": [1, 2], "tags": {"highway": "unclassified"}, "type": "way", "version": 1},
        {"id": 14, "nodes": [1, 2], "tags": {"access": "no", "highway": "primary"}, "type": "way", "version": 1},
        {
            "id": 15,
            "nodes": [1, 2],
            "tags": {"access:conditional": "no @ (snow)", "highway": "primary"},
            "type": "way",
            "version": 1,
        },
    ]
    raw = tmp_path / "raw/osm/overpass.json"
    write_json(raw, {"elements": nodes + ways + [deepcopy(ways[0])]})
    summary = source_gate.normalize_osm(
        registry(),
        {
            "id": "osm_bounded_roads",
            "license_partition": "ODbL-1.0",
            "raw": source_gate.file_record(raw, tmp_path),
        },
        tmp_path,
    )
    assert summary["raw_way_occurrence_count"] == 7
    assert summary["unique_way_count"] == 6
    assert summary["feature_count"] == 1
    assert summary["duplicate_occurrence_removed_centerline_meters"] > 0.0
    dispositions = list(source_gate.read_jsonl(tmp_path / summary["disposition_ledger"]["path"]))
    assert len(dispositions) == 6
    assert len({item["source_record_id"] for item in dispositions}) == 6
    assert all(item["motor_road_disposition"] in {"ELIGIBLE", "EXCLUDED", "UNKNOWN_BLOCKING"} for item in dispositions)
    assert all("effective_access_decision" in item for item in dispositions)
    eligible = list(source_gate.read_jsonl(tmp_path / summary["normalized"]["path"]))
    assert eligible[0]["tags"]["level"] == "0"
    conditional = next(item for item in dispositions if item["source_record_id"] == "way/15")
    assert conditional["tags"]["access:conditional"] == "no @ (snow)"
    assert conditional["conditional_access_evidence"] == {
        "access:conditional": "no @ (snow)"
    }
    assert conditional["explicit_universe_member_id"] == "excluded:osm:way/15"
    assert summary["unknown_blocking_way_count"] == 3


def test_osm_lane_and_mode_evidence_is_retained_in_normalization_dispositions(
    tmp_path: Path,
) -> None:
    nodes = [
        {"id": 1, "lat": 40.039, "lon": -105.227, "type": "node", "version": 1},
        {"id": 2, "lat": 40.039, "lon": -105.226, "type": "node", "version": 1},
    ]
    ways = [
        {
            "id": 21,
            "nodes": [1, 2],
            "tags": {
                "access:lanes": "no|no",
                "highway": "primary",
                "lanes": "2",
                "motorcar:lanes": "yes|yes",
                "oneway:motorcar": "no",
            },
            "type": "way",
        },
        {
            "id": 22,
            "nodes": [1, 2],
            "tags": {"access:lanes": "yes|no", "highway": "primary", "lanes": "2"},
            "type": "way",
        },
        {
            "id": 23,
            "nodes": [1, 2],
            "tags": {
                "highway": "primary",
                "lanes": "2",
                "motor_vehicle:forward:conditional": "no @ (snow)",
                "motor_vehicle:lanes:forward:conditional": "no @ (snow)",
                "oneway:motor_vehicle:conditional": "yes @ (07:00-09:00)",
            },
            "type": "way",
        },
        {
            "id": 24,
            "nodes": [1, 2],
            "tags": {
                "highway": "primary",
                "lanes": "2",
                "lanes:conditional": "3 @ (Mo-Fr)",
                "lanes:forward:conditional": "2 @ (Mo-Fr)",
            },
            "type": "way",
        },
    ]
    raw = tmp_path / "raw/osm/overpass.json"
    write_json(raw, {"elements": nodes + ways})
    summary = source_gate.normalize_osm(
        registry(),
        {
            "id": "osm_bounded_roads",
            "license_partition": "ODbL-1.0",
            "raw": source_gate.file_record(raw, tmp_path),
        },
        tmp_path,
    )
    dispositions = {
        item["source_record_id"]: item
        for item in source_gate.read_jsonl(tmp_path / summary["disposition_ledger"]["path"])
    }
    assert dispositions["way/21"]["motor_road_disposition"] == "ELIGIBLE"
    assert dispositions["way/21"]["tags"]["motorcar:lanes"] == "yes|yes"
    assert dispositions["way/21"]["tags"]["oneway:motorcar"] == "no"
    assert dispositions["way/22"]["motor_road_disposition"] == "UNKNOWN_BLOCKING"
    assert dispositions["way/22"]["lane_access_evidence"]["effective_mode"] == "access"
    assert dispositions["way/23"]["motor_road_disposition"] == "UNKNOWN_BLOCKING"
    assert (
        dispositions["way/23"]["tags"]["motor_vehicle:lanes:forward:conditional"]
        == "no @ (snow)"
    )
    assert (
        dispositions["way/23"]["tags"]["motor_vehicle:forward:conditional"]
        == "no @ (snow)"
    )
    assert (
        dispositions["way/23"]["oneway_evidence"]["effective_key"]
        == "oneway:motor_vehicle:conditional"
    )
    assert dispositions["way/24"]["motor_road_disposition"] == "ELIGIBLE"
    assert dispositions["way/24"]["tags"]["lanes:forward:conditional"] == "2 @ (Mo-Fr)"
    assert dispositions["way/24"]["lane_evidence_disposition"] == "UNKNOWN_BLOCKING"
    assert (
        dispositions["way/24"]["lane_count_evidence"]["reason"]
        == "CONDITIONAL_LANE_COUNT_UNRESOLVED"
    )


def test_arcgis_normalization_emits_one_disposition_per_frozen_oid(tmp_path: Path) -> None:
    raw = tmp_path / "raw/city/page.json"
    write_json(
        raw,
        {
            "features": [
                {
                    "attributes": {"OBJECTID": 1, "STREETCLASS": "Residential", "NUMLANES": 2},
                    "geometry": {"paths": [[[-105.227, 40.039], [-105.226, 40.039]]]},
                },
                {"attributes": {"OBJECTID": 2, "STREETCLASS": "Private"}},
                {
                    "attributes": {"OBJECTID": 3, "STREETCLASS": "Unmapped future class"},
                    "geometry": {"paths": []},
                },
            ]
        },
    )
    summary = source_gate.normalize_arcgis(
        registry(),
        {
            "feature_count": 3,
            "id": "city_street_centerlines",
            "license_partition": "CC0-1.0",
            "object_id_field": "OBJECTID",
            "pages": [
                {
                    "receipt": {"response": source_gate.file_record(raw, tmp_path)},
                    "requested_ids": [1, 2, 3],
                }
            ],
            "role": "road_authority",
        },
        tmp_path,
    )
    dispositions = list(source_gate.read_jsonl(tmp_path / summary["disposition_ledger"]["path"]))
    assert [item["source_record_id"] for item in dispositions] == ["1", "2", "3"]
    assert [item["motor_road_disposition"] for item in dispositions] == [
        "ELIGIBLE",
        "EXCLUDED",
        "UNKNOWN_BLOCKING",
    ]
    assert summary["unrepresented_feature_count"] == 2


def test_same_source_exact_geometry_duplicates_are_not_double_counted(tmp_path: Path) -> None:
    city_records = [
        line_record(
            "city_street_centerlines",
            record_id,
            [[-100.0, 0.0], [100.0, 0.0]],
            attributes={"NUMLANES": 4, "STREETCLASS": "Principal Arterial", "STREETNAME": "Main"},
            license_partition="CC0-1.0",
        )
        for record_id in ("1", "2")
    ]
    result = source_gate.build_canonical_inventory(
        registry(),
        tmp_path,
        [normalized_summary(tmp_path, "city_street_centerlines", city_records), osm_summary(tmp_path)],
    )
    canonical = list(source_gate.read_jsonl(tmp_path / result["canonical_universe"]["path"]))
    represented = [item for item in canonical if item["represented_source_disposition"] == "CANONICAL_GEOMETRY"]
    assert len(represented) == 1
    conflation = list(source_gate.read_jsonl(tmp_path / result["conflation_ledger"]["path"]))
    assert any(item["decision"] == "SUPPRESSED_EXACT_SAME_SOURCE_DUPLICATE" for item in conflation)


def test_same_source_exact_geometry_with_opposing_direction_is_retained_and_blocks(
    tmp_path: Path,
) -> None:
    osm_records = [
        line_record(
            "osm_bounded_roads",
            f"way/{record_id}",
            [[-100.0, 0.0], [100.0, 0.0]],
            tags={
                "highway": "primary",
                "lanes": "2",
                "name": "Main",
                "oneway": direction,
            },
            license_partition="ODbL-1.0",
        )
        for record_id, direction in (("1", "yes"), ("2", "-1"))
    ]
    result = source_gate.build_canonical_inventory(
        registry(),
        tmp_path,
        [osm_summary(tmp_path, osm_records)],
    )
    canonical = list(source_gate.read_jsonl(tmp_path / result["canonical_universe"]["path"]))
    represented = [
        item for item in canonical if item["represented_source_disposition"] == "CANONICAL_GEOMETRY"
    ]
    assert len(represented) == 2
    assert any(
        item["id"] == "inconsistent_same_source_geometry_duplicate"
        for item in result["conflicts"]
    )


def test_frozen_direction_crosswalks_distinguish_authority_from_assumption() -> None:
    value = registry()
    city_ft = line_record(
        "city_street_centerlines",
        "1",
        [[0.0, 0.0], [10.0, 0.0]],
        attributes={"CENTERLINE": "Dual", "ONEWAY": "FT", "STREETCLASS": "Primary"},
    )
    city_n = deepcopy(city_ft)
    city_n["attributes"]["ONEWAY"] = "N"
    county = line_record(
        "county_street_centerlines",
        "2",
        [[0.0, 0.0], [10.0, 0.0]],
        attributes={"FEATURETYPECODE": "A30", "FROMELEVATION": 0, "ONEWAYCODE": "F", "TOELEVATION": 0},
    )
    osm_motorway = line_record(
        "osm_bounded_roads",
        "way/3",
        [[0.0, 0.0], [10.0, 0.0]],
        tags={"highway": "motorway", "name": "Main"},
    )
    assert source_gate.road_semantic_evidence(value, city_ft)["direction"]["value"] == "FORWARD"
    assert source_gate.road_semantic_evidence(value, city_ft)["direction"]["usable_for_correspondence"]
    assert not source_gate.road_semantic_evidence(value, city_n)["direction"]["usable_for_correspondence"]
    county_direction = source_gate.road_semantic_evidence(value, county)["direction"]
    assert county_direction["value"] == "FORWARD"
    assert county_direction["confidence"] == "UNDOCUMENTED_CODE_ASSUMPTION_FAIL_CLOSED"
    assert not county_direction["usable_for_correspondence"]
    osm_direction = source_gate.road_semantic_evidence(value, osm_motorway)["direction"]
    assert (osm_direction["value"], osm_direction["reason"]) == (
        "FORWARD",
        "OSM_IMPLICIT_ONEWAY_HIGHWAY_DEFAULT",
    )


def test_shared_correspondence_never_uses_exact_xz_without_all_semantics() -> None:
    value = semantic_registry()
    target = line_record(
        "test_a", "1", [[0.0, 0.0], [100.0, 0.0]], attributes=semantic_attributes()
    )
    matching = line_record(
        "test_b", "2", [[0.0, 0.0], [100.0, 0.0]], attributes=semantic_attributes()
    )
    proven = source_gate.road_correspondence(
        value,
        target,
        matching,
        source_gate.record_geometry(target),
        source_gate.record_geometry(matching),
        minimum_ratio=0.95,
    )
    assert proven["proof_status"] == "PROVEN"

    no_identity = deepcopy(matching)
    no_identity["attributes"]["NAME"] = "Other"
    exact_without_identity = source_gate.road_correspondence(
        value,
        target,
        no_identity,
        source_gate.record_geometry(target),
        source_gate.record_geometry(no_identity),
        minimum_ratio=0.95,
    )
    assert exact_without_identity["proof_status"] == "NOT_PROVEN_FAIL_CLOSED"
    assert exact_without_identity["checks"]["identity"]["reason"] == "NO_SHARED_DECLARED_IDENTITY"

    opposite = deepcopy(matching)
    opposite["attributes"]["DIRECTION"] = "F"
    target["attributes"]["DIRECTION"] = "R"
    direction_conflict = source_gate.road_correspondence(
        value,
        target,
        opposite,
        source_gate.record_geometry(target),
        source_gate.record_geometry(opposite),
        minimum_ratio=0.95,
    )
    assert direction_conflict["checks"]["direction"]["reason"] == "INCOMPATIBLE_DIRECTION_SEMANTICS"

    target["attributes"]["DIRECTION"] = "B"
    layer_conflict = deepcopy(matching)
    layer_conflict["attributes"]["LAYER"] = "1"
    grade_conflict = source_gate.road_correspondence(
        value,
        target,
        layer_conflict,
        source_gate.record_geometry(target),
        source_gate.record_geometry(layer_conflict),
        minimum_ratio=0.95,
    )
    assert grade_conflict["checks"]["grade"]["components"]["layer"]["reason"] == "INCOMPATIBLE_LAYER_SEMANTICS"

    class_conflict_record = deepcopy(matching)
    class_conflict_record["attributes"]["CLASS"] = "secondary"
    class_conflict = source_gate.road_correspondence(
        value,
        target,
        class_conflict_record,
        source_gate.record_geometry(target),
        source_gate.record_geometry(class_conflict_record),
        minimum_ratio=0.95,
    )
    assert class_conflict["checks"]["road_class"]["relation"] == "MISMATCH"
    assert class_conflict["proof_status"] == "NOT_PROVEN_FAIL_CLOSED"

    lane_conflict_record = deepcopy(matching)
    lane_conflict_record["attributes"]["LANES"] = 3
    lane_conflict = source_gate.road_correspondence(
        value,
        target,
        lane_conflict_record,
        source_gate.record_geometry(target),
        source_gate.record_geometry(lane_conflict_record),
        minimum_ratio=0.95,
    )
    assert lane_conflict["checks"]["lane_count"]["relation"] == "MISMATCH"
    assert lane_conflict["proof_status"] == "NOT_PROVEN_FAIL_CLOSED"


def test_endpoint_elevation_cannot_correspond_to_source_without_endpoint_evidence() -> None:
    value = semantic_registry()
    value["authority_model"]["correspondence_semantics"]["test_a"].update(
        {"from_elevation_field": "FROM_ELEVATION", "to_elevation_field": "TO_ELEVATION"}
    )
    target = line_record(
        "test_a",
        "authority",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes=semantic_attributes(FROM_ELEVATION=0, TO_ELEVATION=0),
    )
    osm_without_endpoint_evidence = line_record(
        "osm_bounded_roads",
        "way/1",
        [[0.0, 0.0], [100.0, 0.0]],
        tags={"highway": "primary", "lanes": "2", "name": "Main"},
    )
    assessment = source_gate.road_correspondence(
        value,
        target,
        osm_without_endpoint_evidence,
        source_gate.record_geometry(target),
        source_gate.record_geometry(osm_without_endpoint_evidence),
        minimum_ratio=0.95,
    )
    endpoint = assessment["checks"]["grade"]["components"]["endpoint_elevations"]
    assert endpoint == {
        "compatible": False,
        "reason": "UNKNOWN_ENDPOINT_GRADE_SEMANTICS",
    }
    assert assessment["proof_status"] == "NOT_PROVEN_FAIL_CLOSED"


def test_mainline_and_ramp_link_never_correspond_or_donate_lane_evidence(
    tmp_path: Path,
) -> None:
    value = semantic_registry()
    value["authority_model"]["road_kind_evidence"]["test_b"] = {
        "confidence": "TEST_DIRECT",
        "fixed_value": "RAMP_OR_LINK",
    }
    mainline = line_record(
        "test_a",
        "mainline",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes=semantic_attributes(LANES=None),
    )
    ramp = line_record(
        "test_b",
        "ramp",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes=semantic_attributes(LANES=2),
    )
    assessment = source_gate.road_correspondence(
        value,
        mainline,
        ramp,
        source_gate.record_geometry(mainline),
        source_gate.record_geometry(ramp),
        allow_missing_evidence_for="lane_count",
        minimum_ratio=0.95,
    )
    assert assessment["checks"]["road_kind"]["reason"] == (
        "INCOMPATIBLE_MAINLINE_RAMP_LINK_KIND"
    )
    assert assessment["proof_status"] == "NOT_PROVEN_FAIL_CLOSED"

    result = source_gate.build_canonical_inventory(
        value,
        tmp_path,
        [
            normalized_summary(tmp_path, "test_a", [mainline]),
            normalized_summary(tmp_path, "test_b", [ramp]),
            osm_summary(tmp_path),
        ],
    )
    canonical = list(
        source_gate.read_jsonl(tmp_path / result["canonical_universe"]["path"])
    )
    represented = [
        item
        for item in canonical
        if item["represented_source_disposition"] == "CANONICAL_GEOMETRY"
    ]
    assert len(represented) == 2
    selected = next(item for item in represented if item["source_id"] == "test_a")
    assert selected["lane_count"] is None
    assert any(item["id"] == "ambiguous_spatial_conflation" for item in result["conflicts"])

    osm_link = line_record(
        "osm_bounded_roads",
        "way/link",
        [[0.0, 0.0], [100.0, 0.0]],
        tags={"highway": "primary_link", "lanes": "1", "name": "Main"},
    )
    kind = source_gate.raw_road_kind_evidence(registry(), osm_link)
    assert kind["raw_value"] == "primary_link"
    assert kind["value"] == "RAMP_OR_LINK"


def test_donation_allows_missing_only_for_the_field_being_donated() -> None:
    value = semantic_registry()
    target_missing_lane = line_record(
        "test_a",
        "target",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes=semantic_attributes(LANES=None),
    )
    candidate_missing_class = line_record(
        "test_b",
        "candidate",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes=semantic_attributes(CLASS=None, LANES=2),
    )
    lane_assessment = source_gate.road_correspondence(
        value,
        target_missing_lane,
        candidate_missing_class,
        source_gate.record_geometry(target_missing_lane),
        source_gate.record_geometry(candidate_missing_class),
        allow_missing_evidence_for="lane_count",
        minimum_ratio=0.95,
    )
    assert lane_assessment["checks"]["lane_count"]["relation"] == "FILL_MISSING_ONLY"
    assert lane_assessment["checks"]["road_class"]["relation"] == "UNKNOWN"
    assert lane_assessment["proof_status"] == "NOT_PROVEN_FAIL_CLOSED"

    target_missing_class = line_record(
        "test_a",
        "target-class",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes=semantic_attributes(CLASS=None, LANES=2),
    )
    candidate_missing_lane = line_record(
        "test_b",
        "candidate-class",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes=semantic_attributes(CLASS="primary", LANES=None),
    )
    class_assessment = source_gate.road_correspondence(
        value,
        target_missing_class,
        candidate_missing_lane,
        source_gate.record_geometry(target_missing_class),
        source_gate.record_geometry(candidate_missing_lane),
        allow_missing_evidence_for="road_class",
        minimum_ratio=0.95,
    )
    assert class_assessment["checks"]["road_class"]["relation"] == "FILL_MISSING_ONLY"
    assert class_assessment["checks"]["lane_count"]["relation"] == "UNKNOWN"
    assert class_assessment["proof_status"] == "NOT_PROVEN_FAIL_CLOSED"


def test_directed_identity_endpoints_are_never_collapsed_into_unordered_tokens() -> None:
    value = semantic_registry()
    for source_id in ("test_a", "test_b"):
        value["authority_model"]["correspondence_identity_fields"][source_id] = [
            "NAME",
            "FROMFEATURE",
            "TOFEATURE",
        ]
        value["authority_model"].setdefault(
            "correspondence_directed_identity_field_pairs", {}
        )[source_id] = [
            {"from_field": "FROMFEATURE", "to_field": "TOFEATURE"}
        ]
    target = line_record(
        "test_a",
        "ramp-a",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes=semantic_attributes(FROMFEATURE="A", TOFEATURE="B"),
    )
    reversed_endpoints = line_record(
        "test_b",
        "ramp-b",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes=semantic_attributes(FROMFEATURE="B", TOFEATURE="A"),
    )
    assert source_gate.correspondence_identity_tokens(value, target) == {"main"}
    incompatible = source_gate.road_correspondence(
        value,
        target,
        reversed_endpoints,
        source_gate.record_geometry(target),
        source_gate.record_geometry(reversed_endpoints),
        minimum_ratio=0.95,
    )
    assert incompatible["proof_status"] == "NOT_PROVEN_FAIL_CLOSED"
    assert incompatible["checks"]["identity"]["reason"] == (
        "DIRECTED_IDENTITY_NOT_COMPARABLE_OR_INCOMPATIBLE"
    )

    reversed_geometry = deepcopy(reversed_endpoints)
    reversed_geometry["geometry"]["coordinates"].reverse()
    proven = source_gate.road_correspondence(
        value,
        target,
        reversed_geometry,
        source_gate.record_geometry(target),
        source_gate.record_geometry(reversed_geometry),
        minimum_ratio=0.95,
    )
    assert proven["proof_status"] == "PROVEN"
    assert proven["checks"]["identity"]["directed_pair_matches"]


def test_cross_source_opposing_direction_is_retained_and_blocks(tmp_path: Path) -> None:
    value = semantic_registry()
    first = line_record(
        "test_a",
        "1",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes=semantic_attributes(DIRECTION="F", CARRIAGEWAY="D"),
    )
    opposite = line_record(
        "test_b",
        "2",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes=semantic_attributes(DIRECTION="R", CARRIAGEWAY="D"),
    )
    result = source_gate.build_canonical_inventory(
        value,
        tmp_path,
        [
            normalized_summary(tmp_path, "test_a", [first]),
            normalized_summary(tmp_path, "test_b", [opposite]),
            osm_summary(tmp_path),
        ],
    )
    canonical = list(source_gate.read_jsonl(tmp_path / result["canonical_universe"]["path"]))
    represented = [item for item in canonical if item["represented_source_disposition"] == "CANONICAL_GEOMETRY"]
    assert len(represented) == 2
    assert any(item["id"] == "ambiguous_spatial_conflation" for item in result["conflicts"])


def test_city_single_centerline_cannot_swallow_two_opposing_osm_carriageways(
    tmp_path: Path,
) -> None:
    city = line_record(
        "city_street_centerlines",
        "1",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes={
            "CENTERLINE": "Single",
            "NUMLANES": 4,
            "ONEWAY": "B",
            "STREETCLASS": "Principal Arterial",
            "STREETNAME": "Main",
        },
        license_partition="CC0-1.0",
    )
    osm_records = [
        line_record(
            "osm_bounded_roads",
            f"way/{index}",
            [[0.0, 0.0], [100.0, 0.0]],
            tags={"highway": "primary", "lanes": "2", "name": "Main", "oneway": direction},
            license_partition="ODbL-1.0",
        )
        for index, direction in ((2, "yes"), (3, "-1"))
    ]
    result = source_gate.build_canonical_inventory(
        registry(),
        tmp_path,
        [
            normalized_summary(tmp_path, "city_street_centerlines", [city]),
            osm_summary(tmp_path, osm_records),
        ],
    )
    canonical = list(source_gate.read_jsonl(tmp_path / result["canonical_universe"]["path"]))
    represented = [item for item in canonical if item["represented_source_disposition"] == "CANONICAL_GEOMETRY"]
    assert len(represented) == 3
    assert {item["source_id"] for item in represented} == {
        "city_street_centerlines",
        "osm_bounded_roads",
    }


def test_unique_official_road_counts_as_authority_inclusive_representation(
    tmp_path: Path,
) -> None:
    city = line_record(
        "city_street_centerlines",
        "1",
        [[-100.0, 0.0], [100.0, 0.0]],
        attributes={
            "CENTERLINE": "Single",
            "NUMLANES": 4,
            "ONEWAY": "B",
            "STREETCLASS": "Principal Arterial",
            "STREETNAME": "Main",
        },
        license_partition="CC0-1.0",
    )
    result = source_gate.build_canonical_inventory(
        registry(),
        tmp_path,
        [normalized_summary(tmp_path, "city_street_centerlines", [city]), osm_summary(tmp_path)],
    )
    ledger = list(source_gate.read_jsonl(tmp_path / result["reference_representation_ledger"]["path"]))
    city_reference = next(item for item in ledger if item["source_id"] == "city_street_centerlines")
    assert city_reference["authority_inclusive_match_ratio"] == 1.0
    assert city_reference["match_ratio"] == 1.0
    assert city_reference["different_source_corroboration_match_ratio"] == 0.0
    assert not city_reference["different_source_corroboration_matched"]
    assert city_reference["decision"] == "ELIGIBLE_REFERENCE_MATCHED"
    assert result["reference_class_audit"]["primary"]["unmatched_observation_count"] == 0
    assert result["unmatched_reference_observations"] == []


def test_parallel_road_inside_tolerance_is_retained_and_blocks_ambiguous_conflation(tmp_path: Path) -> None:
    city = line_record(
        "city_street_centerlines",
        "1",
        [[-100.0, 0.0], [100.0, 0.0]],
        attributes={"NUMLANES": 4, "STREETCLASS": "Principal Arterial", "STREETNAME": "Main"},
        license_partition="CC0-1.0",
    )
    osm = line_record(
        "osm_bounded_roads",
        "way/2",
        [[-100.0, 1.0], [100.0, 1.0]],
        tags={"highway": "residential", "lanes": "2", "name": "Other"},
        license_partition="ODbL-1.0",
    )
    result = source_gate.build_canonical_inventory(
        registry(),
        tmp_path,
        [normalized_summary(tmp_path, "city_street_centerlines", [city]), osm_summary(tmp_path, [osm])],
    )
    canonical = list(source_gate.read_jsonl(tmp_path / result["canonical_universe"]["path"]))
    assert len([item for item in canonical if item["represented_source_disposition"] == "CANONICAL_GEOMETRY"]) == 2
    assert any(item["id"] == "ambiguous_spatial_conflation" for item in result["conflicts"])


def test_excluded_private_record_cannot_donate_lane_count(tmp_path: Path) -> None:
    excluded_city = line_record(
        "city_street_centerlines",
        "private",
        [[-100.0, 0.0], [100.0, 0.0]],
        attributes={"NUMLANES": 4, "STREETCLASS": "Private", "STREETNAME": "Private"},
        license_partition="CC0-1.0",
    )
    county = line_record(
        "county_street_centerlines",
        "public",
        [[-100.0, 0.0], [100.0, 0.0]],
        attributes={"FEATURETYPECODE": "A40", "NAME": "Public"},
        license_partition="CC-BY-4.0",
    )
    result = source_gate.build_canonical_inventory(
        registry(),
        tmp_path,
        [
            normalized_summary(tmp_path, "city_street_centerlines", [excluded_city]),
            normalized_summary(tmp_path, "county_street_centerlines", [county]),
            osm_summary(tmp_path),
        ],
    )
    canonical = list(source_gate.read_jsonl(tmp_path / result["canonical_universe"]["path"]))
    represented = next(item for item in canonical if item["source_id"] == "county_street_centerlines")
    assert represented["lane_count"] is None
    assert represented["lane_count_authority"] is None


def test_nearby_different_road_cannot_donate_lane_count(tmp_path: Path) -> None:
    county = line_record(
        "county_street_centerlines",
        "public",
        [[-100.0, 0.0], [100.0, 0.0]],
        attributes={"FEATURETYPECODE": "A40", "NAME": "Public"},
        license_partition="CC-BY-4.0",
    )
    osm = line_record(
        "osm_bounded_roads",
        "way/2",
        [[-100.0, 1.0], [100.0, 1.0]],
        tags={"highway": "residential", "lanes": "4", "name": "Other"},
        license_partition="ODbL-1.0",
    )
    result = source_gate.build_canonical_inventory(
        registry(),
        tmp_path,
        [normalized_summary(tmp_path, "county_street_centerlines", [county]), osm_summary(tmp_path, [osm])],
    )
    canonical = list(source_gate.read_jsonl(tmp_path / result["canonical_universe"]["path"]))
    county_result = next(
        item
        for item in canonical
        if item["source_id"] == "county_street_centerlines"
        and item["represented_source_disposition"] == "CANONICAL_GEOMETRY"
    )
    assert county_result["lane_count"] is None
    assert county_result["lane_count_authority"] is None


def test_donated_lane_source_cannot_independently_support_its_own_evidence(
    tmp_path: Path,
) -> None:
    value = semantic_registry()
    selected = line_record(
        "test_a",
        "1",
        [[0.0, 0.0], [1000.0, 0.0]],
        attributes=semantic_attributes(LANES=None),
        license_partition="TEST-A",
    )
    support = line_record(
        "test_b",
        "2",
        [[0.0, 0.0], [1000.0, 0.0]],
        attributes=semantic_attributes(LANES=2),
        license_partition="TEST-B",
    )
    result = source_gate.build_canonical_inventory(
        value,
        tmp_path,
        [
            normalized_summary(tmp_path, "test_a", [selected]),
            normalized_summary(tmp_path, "test_b", [support]),
            osm_summary(tmp_path),
        ],
    )
    canonical = list(source_gate.read_jsonl(tmp_path / result["canonical_universe"]["path"]))
    represented = [item for item in canonical if item["represented_source_disposition"] == "CANONICAL_GEOMETRY"]
    assert len(represented) == 2
    selected_result = next(item for item in represented if item["source_id"] == "test_a")
    assert selected_result["lane_count"] == 2
    assert selected_result["geometry_source_id"] == "test_a"
    assert selected_result["geometry_license_partition"] == "TEST-A"
    assert selected_result["lane_evidence_source_id"] == "test_b"
    assert selected_result["lane_evidence_license_partition"] == "TEST-B"
    assert any(item["id"] == "ambiguous_spatial_conflation" for item in result["conflicts"])
    support_summary = result["independent_lane_km_support"]["by_class"]["primary"]
    assert support_summary["canonical_lane_kilometer_denominator"] == 4.0
    assert support_summary["independently_supported_lane_kilometers"] == 0.0
    assert support_summary["independent_support_lane_kilometer_ratio"] is None
    ledger = list(
        source_gate.read_jsonl(tmp_path / result["independent_lane_km_support_ledger"]["path"])
    )
    selected_ledger = next(
        item for item in ledger if item["canonical_segment_id"] == selected_result["canonical_segment_id"]
    )
    assert selected_ledger["support_links"] == []
    assert selected_ledger["independence_excluded_source_ids"] == ["test_a", "test_b"]
    conflict = next(
        item
        for item in result["conflicts"]
        if item["id"] == "incompatible_independent_lane_km_evidence"
    )
    assert "CIRCULAR_CANONICAL_EVIDENCE_SOURCE" in conflict["unresolved_support_records"][0][
        "reason_codes"
    ]


def test_independent_support_unions_partial_subspans_without_double_counting() -> None:
    value = semantic_registry()
    target = line_record(
        "test_a",
        "1",
        [[0.0, 0.0], [1000.0, 0.0]],
        attributes=semantic_attributes(LANES=2),
        license_partition="TEST-A",
    )
    target["_geometry"] = source_gate.record_geometry(target)
    supports = [
        line_record(
            "test_b",
            str(index),
            coordinates,
            attributes=semantic_attributes(LANES=2),
            license_partition="TEST-B",
        )
        for index, coordinates in (
            (2, [[0.0, 0.0], [600.0, 0.0]]),
            (3, [[400.0, 0.0], [1000.0, 0.0]]),
        )
    ]
    for support in supports:
        support["_geometry"] = source_gate.record_geometry(support)
    canonical = {
        "canonical_segment_id": "canonical:test_a:1:0:0",
        "class_evidence_license_partition": "TEST-A",
        "class_evidence_source_id": "test_a",
        "geometry": target["geometry"],
        "geometry_license_partition": "TEST-A",
        "geometry_source_id": "test_a",
        "lane_count": 2,
        "lane_evidence_license_partition": "TEST-A",
        "lane_evidence_source_id": "test_a",
        "road_class": "primary",
        "source_id": "test_a",
        "source_record_id": "1",
        "source_record_part": 0,
    }
    ledger, summary, conflicts = source_gate.independent_lane_km_support(
        value,
        [canonical],
        {"test_a": [target], "test_b": supports},
    )
    assert conflicts == []
    assert ledger[0]["supported_length_meters"] == pytest.approx(1000.0, abs=1e-6)
    assert len(ledger[0]["support_links"]) == 2
    assert summary["by_class"]["primary"]["canonical_lane_kilometer_denominator"] == 2.0
    assert summary["by_class"]["primary"]["independently_supported_lane_kilometers"] == 2.0


@pytest.mark.parametrize(
    ("support_class", "support_lanes", "expected_reason"),
    [
        ("primary", 1, "MISMATCHED_SUPPORT_LANE_COUNT"),
        ("secondary", 6, "MISMATCHED_SUPPORT_ROAD_CLASS"),
        ("primary", None, "MISSING_OR_INVALID_SUPPORT_LANE_COUNT"),
    ],
)
def test_independent_lane_km_rejects_incompatible_direct_support_evidence(
    support_class: str,
    support_lanes: int | None,
    expected_reason: str,
) -> None:
    value = semantic_registry()
    target = line_record(
        "test_a",
        "1",
        [[0.0, 0.0], [1000.0, 0.0]],
        attributes=semantic_attributes(CLASS="primary", LANES=6),
        license_partition="TEST-A",
    )
    support = line_record(
        "test_b",
        "2",
        [[0.0, 0.0], [1000.0, 0.0]],
        attributes=semantic_attributes(CLASS=support_class, LANES=support_lanes),
        license_partition="TEST-B",
    )
    target["_geometry"] = source_gate.record_geometry(target)
    support["_geometry"] = source_gate.record_geometry(support)
    canonical = {
        "canonical_segment_id": "canonical:test_a:1:0:0",
        "class_evidence_license_partition": "TEST-A",
        "class_evidence_source_id": "test_a",
        "geometry": target["geometry"],
        "geometry_license_partition": "TEST-A",
        "geometry_source_id": "test_a",
        "lane_count": 6,
        "lane_evidence_license_partition": "TEST-A",
        "lane_evidence_source_id": "test_a",
        "road_class": "primary",
        "source_id": "test_a",
        "source_record_id": "1",
        "source_record_part": 0,
    }
    ledger, summary, conflicts = source_gate.independent_lane_km_support(
        value,
        [canonical],
        {"test_a": [target], "test_b": [support]},
    )
    primary = summary["by_class"]["primary"]
    assert primary["canonical_lane_kilometer_denominator"] == 6.0
    assert primary["independently_supported_lane_kilometers"] == 0.0
    assert primary["independent_support_lane_kilometer_ratio"] is None
    assert ledger[0]["support_links"] == []
    conflict = next(
        item for item in conflicts if item["id"] == "incompatible_independent_lane_km_evidence"
    )
    assert expected_reason in conflict["unresolved_support_records"][0]["reason_codes"]


def test_independent_lane_km_accepts_exact_direct_class_and_lane_support() -> None:
    value = semantic_registry()
    records = []
    for source_id, record_id in (("test_a", "1"), ("test_b", "2")):
        record = line_record(
            source_id,
            record_id,
            [[0.0, 0.0], [1000.0, 0.0]],
            attributes=semantic_attributes(CLASS="primary", LANES=6),
            license_partition=f"{source_id.upper()}-LICENSE",
        )
        record["_geometry"] = source_gate.record_geometry(record)
        records.append(record)
    target, support = records
    canonical = {
        "canonical_segment_id": "canonical:test_a:1:0:0",
        "class_evidence_license_partition": "TEST_A-LICENSE",
        "class_evidence_source_id": "test_a",
        "geometry": target["geometry"],
        "geometry_license_partition": "TEST_A-LICENSE",
        "geometry_source_id": "test_a",
        "lane_count": 6,
        "lane_evidence_license_partition": "TEST_A-LICENSE",
        "lane_evidence_source_id": "test_a",
        "road_class": "primary",
        "source_id": "test_a",
        "source_record_id": "1",
        "source_record_part": 0,
    }
    ledger, summary, conflicts = source_gate.independent_lane_km_support(
        value,
        [canonical],
        {"test_a": [target], "test_b": [support]},
    )
    assert conflicts == []
    assert ledger[0]["support_links"][0]["support_source_record_id"] == "2"
    assert summary["by_class"]["primary"]["independently_supported_lane_kilometers"] == 6.0
    assert summary["by_class"]["primary"]["independent_support_lane_kilometer_ratio"] == 1.0


def test_independent_lane_km_index_matches_full_scan_oracle(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    value = semantic_registry()
    tolerance = float(
        value["authority_model"]["automatic_conflation_max_distance_meters"]
    )
    targets: list[dict] = []
    supports: list[dict] = []
    canonical: list[dict] = []
    target_y_values = (0.0, 100.0, 200.0, 300.0)
    support_offsets = (0.0, tolerance, tolerance * 0.9, tolerance * 2.0)
    for index, (target_y, support_offset) in enumerate(
        zip(target_y_values, support_offsets, strict=True)
    ):
        name = f"Indexed Oracle Road {index}"
        target = line_record(
            "test_a",
            f"target-{index}",
            [[-100.0, target_y], [100.0, target_y]],
            attributes=semantic_attributes(NAME=name),
            license_partition="TEST-A",
        )
        support = line_record(
            "test_b",
            f"support-{index}",
            [[-100.0, target_y + support_offset], [100.0, target_y + support_offset]],
            attributes=semantic_attributes(NAME=name),
            license_partition="TEST-B",
        )
        target["_geometry"] = source_gate.record_geometry(target)
        support["_geometry"] = source_gate.record_geometry(support)
        targets.append(target)
        supports.append(support)
        canonical.append(
            {
                "canonical_segment_id": f"canonical:test_a:target-{index}:0:0",
                "class_evidence_license_partition": "TEST-A",
                "class_evidence_source_id": "test_a",
                "geometry": target["geometry"],
                "geometry_license_partition": "TEST-A",
                "geometry_source_id": "test_a",
                "lane_count": 2,
                "lane_evidence_license_partition": "TEST-A",
                "lane_evidence_source_id": "test_a",
                "road_class": "primary",
                "source_id": "test_a",
                "source_record_id": f"target-{index}",
                "source_record_part": 0,
            }
        )
    for index in range(24):
        far = line_record(
            "test_b",
            f"far-{index}",
            [[10000.0 + index * 10.0, 10000.0], [10005.0 + index * 10.0, 10000.0]],
            attributes=semantic_attributes(NAME=f"Far Road {index}"),
            license_partition="TEST-B",
        )
        far["_geometry"] = source_gate.record_geometry(far)
        supports.append(far)

    source_records = {"test_a": targets, "test_b": supports}
    indexed = source_gate.independent_lane_km_support(
        value, canonical, source_records
    )

    class FullScanTree:
        def __init__(self, geometries: list[object]) -> None:
            self.count = len(geometries)

        def query(self, _geometry: object) -> list[int]:
            return list(range(self.count))

    monkeypatch.setattr(source_gate, "STRtree", FullScanTree)
    brute_force = source_gate.independent_lane_km_support(
        value, canonical, source_records
    )
    assert source_gate.canonical_json_bytes(indexed) == source_gate.canonical_json_bytes(
        brute_force
    )


def test_grade_ambiguity_blocks_lane_donation_and_independent_support(tmp_path: Path) -> None:
    value = semantic_registry()
    selected = line_record(
        "test_a",
        "1",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes=semantic_attributes(LANES=None, LAYER="0"),
    )
    different_grade = line_record(
        "test_b",
        "2",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes=semantic_attributes(LANES=4, LAYER="1"),
    )
    result = source_gate.build_canonical_inventory(
        value,
        tmp_path,
        [
            normalized_summary(tmp_path, "test_a", [selected]),
            normalized_summary(tmp_path, "test_b", [different_grade]),
            osm_summary(tmp_path),
        ],
    )
    canonical = list(source_gate.read_jsonl(tmp_path / result["canonical_universe"]["path"]))
    selected_result = next(item for item in canonical if item["source_id"] == "test_a")
    assert selected_result["lane_count"] is None
    assert any(item["id"] == "conflicting_lane_evidence" for item in result["conflicts"])
    assert any(
        item["id"] == "unresolved_independent_support_correspondence"
        for item in result["conflicts"]
    )


def test_large_drcog_polygon_is_not_fully_matched_by_tiny_touching_road(tmp_path: Path) -> None:
    city = line_record(
        "city_street_centerlines",
        "1",
        [[0.0, 0.0], [1.0, 0.0]],
        attributes={"NUMLANES": 2, "STREETCLASS": "Residential", "STREETNAME": "Tiny"},
        license_partition="CC0-1.0",
    )
    polygon = {
        "attributes": {},
        "geometry": {
            "coordinates": [[[0.0, 0.0], [100.0, 0.0], [100.0, 100.0], [0.0, 100.0], [0.0, 0.0]]],
            "type": "Polygon",
        },
        "license_partition": "DRCOG-ATTRIBUTION",
        "role": "surface_median_authority",
        "source_id": "drcog_pavement_polygons_2024",
        "source_record_id": "surface-1",
        "source_record_part": 0,
    }
    result = source_gate.build_canonical_inventory(
        registry(),
        tmp_path,
        [
            normalized_summary(tmp_path, "city_street_centerlines", [city]),
            normalized_summary(tmp_path, "drcog_pavement_polygons_2024", [polygon]),
            osm_summary(tmp_path),
        ],
    )
    ledger = list(source_gate.read_jsonl(tmp_path / result["reference_representation_ledger"]["path"]))
    surface = next(item for item in ledger if item["source_id"] == "drcog_pavement_polygons_2024")
    assert surface["match_ratio"] < 0.95
    assert surface["decision"] == "CORROBORATING_REFERENCE_UNMATCHED"


def test_lane_kilometers_include_unknown_records_in_denominator(tmp_path: Path) -> None:
    path = tmp_path / "canonical.geojsonl"
    common = {
        "eligibility_status_or_exclusion_reason": "ELIGIBLE",
        "license_partition": "TEST",
        "represented_source_disposition": "CANONICAL_GEOMETRY",
        "road_class": "primary",
        "source_id": "test",
        "source_precedence_decision": "SELECTED_OFFICIAL_AUTHORITY",
        "source_record_part": 0,
    }
    write_jsonl(
        path,
        [
            {
                **common,
                "canonical_segment_id": "known",
                "geometry": {"coordinates": [[0.0, 0.0], [1000.0, 0.0]], "type": "LineString"},
                "lane_count": 2,
                "lane_count_authority": "test:known:0:lanes",
                "source_record_id": "known",
            },
            {
                **common,
                "canonical_segment_id": "unknown",
                "geometry": {"coordinates": [[0.0, 10.0], [500.0, 10.0]], "type": "LineString"},
                "lane_count": None,
                "lane_count_authority": None,
                "source_record_id": "unknown",
            },
        ],
    )
    summary = source_gate.lane_kilometer_summary(path)["by_class"]["primary"]
    assert summary["eligible_segment_count"] == 2
    assert summary["known_lane_segment_count"] == 1
    assert summary["unknown_lane_segment_count"] == 1
    assert summary["known_lane_kilometers"] == 2.0
    assert summary["direct_lane_evidence_coverage_ratio"] == pytest.approx(2 / 3, abs=1e-6)
    assert summary["representation_lane_kilometer_ratio"] is None
    assert summary["status"] == "FAIL_CLOSED_UNKNOWN_LANES"
    coverage = source_gate.class_source_coverage(
        "primary",
        summary,
        {
            "independent_support_lane_kilometer_ratio": None,
            "unresolved_correspondence_segment_count": 0,
        },
        {},
        geometryless_motor_road_record_count=0,
        unresolved_geometric_motor_road_record_count=0,
        unknown_class_segment_count=0,
    )
    assert coverage["source_coverage_ratio"] is None
    assert coverage["status"] == "FAIL_CLOSED"


def test_osm_only_authoritative_fallback_counts_as_represented_lane_km(
    tmp_path: Path,
) -> None:
    osm = line_record(
        "osm_bounded_roads",
        "way/101",
        [[0.0, 0.0], [1000.0, 0.0]],
        tags={"highway": "primary", "lanes": "2", "name": "Fallback Road"},
        license_partition="ODbL-1.0",
    )
    canonical = source_gate.build_canonical_inventory(
        registry(),
        tmp_path,
        [osm_summary(tmp_path, [osm])],
    )
    lane_value = source_gate.lane_kilometer_summary(
        tmp_path / canonical["canonical_universe"]["path"]
    )["by_class"]["primary"]
    support_value = canonical["independent_lane_km_support"]["by_class"]["primary"]
    coverage = source_gate.class_source_coverage(
        "primary",
        lane_value,
        support_value,
        canonical["reference_class_audit"]["primary"],
        geometryless_motor_road_record_count=0,
        unresolved_geometric_motor_road_record_count=0,
        unknown_class_segment_count=0,
    )
    assert lane_value["representation_lane_kilometer_denominator"] == 2.0
    assert lane_value["represented_lane_kilometers"] == 2.0
    assert coverage["source_coverage_ratio"] == 1.0
    assert coverage["status"] == "PASS"
    assert coverage["independent_cross_source_lane_kilometer_ratio"] == 0.0
    assert coverage["corroboration_status"] == "NO_DIFFERENT_SOURCE_CORROBORATION"
    assert coverage["corroboration_is_source_coverage_gate"] is False


@pytest.mark.parametrize(
    ("represented_meters", "expected_ratio", "expected_status"),
    [
        (949.0, 0.949, "FAIL_CLOSED"),
        (949.9996, 0.9499996, "FAIL_CLOSED"),
        (950.0, 0.95, "PASS"),
        (960.0, 0.96, "PASS"),
    ],
)
def test_local_road_representation_threshold_uses_known_unrepresented_lane_km(
    tmp_path: Path,
    represented_meters: float,
    expected_ratio: float,
    expected_status: str,
) -> None:
    path = tmp_path / "canonical.geojsonl"
    common = {
        "eligibility_status_or_exclusion_reason": "ELIGIBLE",
        "lane_count": 1,
        "road_class": "tertiary",
    }
    write_jsonl(
        path,
        [
            {
                **common,
                "canonical_segment_id": "represented",
                "geometry": {
                    "coordinates": [[0.0, 0.0], [represented_meters, 0.0]],
                    "type": "LineString",
                },
                "represented_source_disposition": "CANONICAL_GEOMETRY",
            },
            {
                **common,
                "canonical_segment_id": "known-unrepresented",
                "geometry": {
                    "coordinates": [
                        [represented_meters, 0.0],
                        [1000.0, 0.0],
                    ],
                    "type": "LineString",
                },
                "represented_source_disposition": "KNOWN_UNREPRESENTED",
            },
        ],
    )
    lane_value = source_gate.lane_kilometer_summary(path)["by_class"]["tertiary"]
    coverage = source_gate.class_source_coverage(
        "tertiary",
        lane_value,
        {
            "independent_support_lane_kilometer_ratio": 0.0,
            "unresolved_correspondence_segment_count": 0,
        },
        {},
        geometryless_motor_road_record_count=0,
        unresolved_geometric_motor_road_record_count=0,
        unknown_class_segment_count=0,
    )
    assert coverage["representation_lane_kilometer_denominator"] == 1.0
    assert coverage["represented_lane_kilometers"] == represented_meters / 1000.0
    assert coverage["source_coverage_ratio"] == pytest.approx(expected_ratio, abs=1e-12)
    assert coverage["status"] == expected_status


def test_major_road_representation_threshold_remains_exactly_one(tmp_path: Path) -> None:
    path = tmp_path / "canonical.geojsonl"
    write_jsonl(
        path,
        [
            {
                "canonical_segment_id": "represented",
                "eligibility_status_or_exclusion_reason": "ELIGIBLE",
                "geometry": {
                    "coordinates": [[0.0, 0.0], [999.0, 0.0]],
                    "type": "LineString",
                },
                "lane_count": 1,
                "represented_source_disposition": "CANONICAL_GEOMETRY",
                "road_class": "primary",
            },
            {
                "canonical_segment_id": "known-unrepresented",
                "eligibility_status_or_exclusion_reason": "ELIGIBLE",
                "geometry": {
                    "coordinates": [[999.0, 0.0], [1000.0, 0.0]],
                    "type": "LineString",
                },
                "lane_count": 1,
                "represented_source_disposition": "KNOWN_UNREPRESENTED",
                "road_class": "primary",
            },
        ],
    )
    lane_value = source_gate.lane_kilometer_summary(path)["by_class"]["primary"]
    coverage = source_gate.class_source_coverage(
        "primary",
        lane_value,
        {
            "independent_support_lane_kilometer_ratio": 0.0,
            "unresolved_correspondence_segment_count": 0,
        },
        {},
        geometryless_motor_road_record_count=0,
        unresolved_geometric_motor_road_record_count=0,
        unknown_class_segment_count=0,
    )
    assert coverage["source_coverage_ratio"] == 0.999
    assert coverage["required_ratio"] == 1.0
    assert coverage["status"] == "FAIL_CLOSED"


def test_major_road_ratio_just_below_one_never_rounds_to_pass(tmp_path: Path) -> None:
    path = tmp_path / "canonical.geojsonl"
    common = {
        "eligibility_status_or_exclusion_reason": "ELIGIBLE",
        "lane_count": 1,
        "road_class": "primary",
    }
    write_jsonl(
        path,
        [
            {
                **common,
                "canonical_segment_id": "represented",
                "geometry": {
                    "coordinates": [[0.0, 0.0], [999.9996, 0.0]],
                    "type": "LineString",
                },
                "represented_source_disposition": "CANONICAL_GEOMETRY",
            },
            {
                **common,
                "canonical_segment_id": "known-unrepresented",
                "geometry": {
                    "coordinates": [[999.9996, 0.0], [1000.0, 0.0]],
                    "type": "LineString",
                },
                "represented_source_disposition": "KNOWN_UNREPRESENTED",
            },
        ],
    )
    lane_value = source_gate.lane_kilometer_summary(path)["by_class"]["primary"]
    coverage = source_gate.class_source_coverage(
        "primary",
        lane_value,
        {
            "independent_support_lane_kilometer_ratio": 0.0,
            "unresolved_correspondence_segment_count": 0,
        },
        {},
        geometryless_motor_road_record_count=0,
        unresolved_geometric_motor_road_record_count=0,
        unknown_class_segment_count=0,
    )
    assert coverage["source_coverage_ratio"] == pytest.approx(0.9999996)
    assert coverage["status"] == "FAIL_CLOSED"


def test_unresolved_geometric_lengths_and_geometryless_records_are_audited(
    tmp_path: Path,
) -> None:
    canonical = tmp_path / "canonical.geojsonl"
    write_jsonl(
        canonical,
        [
            {
                "canonical_segment_id": "unknown-osm",
                "geometry": {"coordinates": [[0.0, 0.0], [500.0, 0.0]], "type": "LineString"},
                "represented_source_disposition": "UNKNOWN_FAIL_CLOSED",
                "road_class": "primary",
            }
        ],
    )
    lane = source_gate.lane_kilometer_summary(canonical)
    assert lane["unresolved_geometric_centerline_kilometers"] == 0.5
    assert lane["by_class"]["primary"]["unresolved_geometric_segment_count"] == 1
    assert lane["by_class"]["primary"]["status"] == "FAIL_CLOSED_UNKNOWN_LANES"

    disposition_path = tmp_path / "intermediates/record_dispositions/test.jsonl"
    write_jsonl(
        disposition_path,
        [
            {
                "geometry": None,
                "geometry_length_meters": 0.0,
                "motor_road_disposition": "ELIGIBLE",
                "normalized_part_ids": [],
            },
            {
                "geometry": {"coordinates": [[0.0, 0.0], [250.0, 0.0]], "type": "LineString"},
                "geometry_length_meters": 250.0,
                "motor_road_disposition": "UNKNOWN_BLOCKING",
                "normalized_part_ids": [],
            },
            {
                "explicit_universe_member_id": "excluded:osm:way/3",
                "geometry": {"coordinates": [[0.0, 10.0], [100.0, 10.0]], "type": "LineString"},
                "geometry_length_meters": 100.0,
                "motor_road_disposition": "UNKNOWN_BLOCKING",
                "normalized_part_ids": [],
            },
        ],
    )
    audit = source_gate.normalization_denominator_audit(
        [
            {
                "disposition_ledger": source_gate.file_record(disposition_path, tmp_path),
                "id": "test",
            }
        ],
        tmp_path,
    )
    assert audit["geometryless_motor_road_record_count"] == 1
    assert audit["unresolved_geometric_motor_road_record_count"] == 1
    assert audit["unresolved_geometric_motor_road_length_meters"] == 250.0
    assert audit["explicit_universe_geometric_motor_road_record_count"] == 1
    assert audit["explicit_universe_geometric_motor_road_length_meters"] == 100.0


def test_benchmark_contract_binds_same_full_universe_to_all_fifteen_views() -> None:
    universe = {"bytes": 1, "path": "intermediates/universe.geojsonl", "sha256": "a" * 64}
    result = source_gate.validate_benchmark_camera_contract(registry(), universe, 123)
    assert result["status"] == "PENDING_LEAD_D_INSPECTION"
    assert result["view_count"] == 15
    assert {item["canonical_universe_sha256"] for item in result["view_bindings"]} == {"a" * 64}
    assert {item["canonical_universe_record_count"] for item in result["view_bindings"]} == {123}
    assert all(item["origin_inside_frozen_bounds"] for item in result["view_bindings"])


def write_approval_witnesses(repo: Path, context: dict) -> dict[str, dict]:
    value = registry()
    witness_paths = value["authority_model"]["benchmark_approval_witnesses"]
    verifier = repo / witness_paths["d_verifier"]["path"]
    report = repo / witness_paths["d_report"]["path"]
    result = repo / witness_paths["d_result"]["path"]
    inspection = repo / witness_paths["lead_inspection"]["path"]
    verifier.parent.mkdir(parents=True, exist_ok=True)
    report.parent.mkdir(parents=True, exist_ok=True)
    verifier.write_text("# independent verifier fixture\n", encoding="utf-8")
    marker = value["authority_model"]["benchmark_approval_contract"][
        "d_report_decision_marker"
    ]
    report.write_text(
        f"# Independent Phase-3 verification\n\n<!-- {marker}: PASS -->\n",
        encoding="utf-8",
    )
    verifier_record = source_gate.file_record(verifier, repo)
    report_record = source_gate.file_record(report, repo)
    write_json(
        result,
        {
            "bindings": {
                **context["bindings"],
                "report": report_record,
                "verifier": verifier_record,
            },
            "checks": {key: "PASS" for key in context["required_checks"]},
            "decision": "PASS",
            "reproduction": context["reproduction"],
            "schema": source_gate.D_RESULT_SCHEMA,
            "status": "PASS",
        },
    )
    lead_bindings = {
        key: context["bindings"][key]
        for key in (
            "camera_contract",
            "candidate_artifacts",
            "candidate_manifest",
            "canonical_universe",
            "source_freeze",
        )
    }
    write_json(
        inspection,
        {
            "bindings": lead_bindings,
            "inspection": {
                "coverage_png": context["bindings"]["candidate_artifacts"][
                    source_gate.EXPECTED_EXTERNAL_ARTIFACT
                ],
                "full_resolution_opened": True,
                "height_pixels": 1600,
                "status": "PASS",
                "width_pixels": 1600,
            },
            "inspector": {
                "id": "lead-fixture",
                "inspected_utc": "2026-08-22T00:00:00Z",
            },
            "schema": source_gate.LEAD_INSPECTION_SCHEMA,
            "status": "PASS",
        },
    )
    return {
        "d_report": report_record,
        "d_result": source_gate.file_record(result, repo),
        "d_verifier": verifier_record,
        "lead_inspection": source_gate.file_record(inspection, repo),
    }


def approval_fixture(repo: Path) -> tuple[dict, dict, dict, dict]:
    artifact_hashes = {
        name: str(index + 1) * 64
        for index, name in enumerate(
            (*source_gate.EXPECTED_TEXT_ARTIFACTS, source_gate.EXPECTED_EXTERNAL_ARTIFACT)
        )
    }
    benchmark = {
        "camera_contract": {"sha256": "b" * 64},
        "canonical_universe": {"record_count": 7, "sha256": "a" * 64},
    }
    candidate = {
        "artifact_sha256": artifact_hashes,
        "camera_contract_sha256": "b" * 64,
        "candidate_event_utc": "2026-08-21T00:00:00Z",
        "canonical_universe_record_count": 7,
        "canonical_universe_sha256": "a" * 64,
        "schema": "questflight.traffic-v2-geospatial-approval-candidate.v1",
        "source_freeze_sha256": "c" * 64,
        "status": "PENDING_LEAD_D_INSPECTION",
    }
    context = {
        "bindings": {
            "camera_contract": {
                "bytes": 1,
                "path": "tools/traffic_v2/schemas/camera.json",
                "sha256": "b" * 64,
            },
            "candidate_artifacts": {
                name: {
                    "bytes": 1,
                    "path": f"approval_candidate/{name}",
                    "sha256": digest,
                }
                for name, digest in artifact_hashes.items()
            },
            "candidate_manifest": {
                "bytes": 1,
                "path": "approval_candidate/candidate_manifest.json",
                "sha256": "7" * 64,
            },
            "canonical_universe": {"record_count": 7, "sha256": "a" * 64},
            "requirements_lock": {
                "bytes": 388,
                "path": "tools/traffic_v2/geospatial/requirements-lock.txt",
                "sha256": "8" * 64,
            },
            "source_freeze": {"bytes": 1, "path": "freeze_manifest.json", "sha256": "c" * 64},
            "source_registry": {
                "bytes": 1,
                "path": "tools/traffic_v2/geospatial/source_registry.json",
                "sha256": "9" * 64,
            },
            "source_tool": {
                "bytes": 1,
                "path": "tools/traffic_v2/geospatial/source_gate.py",
                "sha256": "d" * 64,
            },
            "toolchain_manifest": {
                "bytes": 1,
                "path": "toolchain/traffic_v2_geospatial_toolchain_manifest.json",
                "sha256": "e" * 64,
            },
        },
        "reproduction": {
            "argv": ["python.exe", "verify_phase3_source_gate.py"],
            "cwd": str(repo),
            "exit_code": 0,
        },
        "required_checks": registry()["authority_model"]["benchmark_approval_contract"][
            "required_d_checks"
        ],
        "minimum_inspection_utc": "2026-08-21T00:00:00Z",
    }
    witnesses = write_approval_witnesses(repo, context)
    approval = {
        "camera_contract_sha256": "b" * 64,
        "candidate_artifact_sha256": artifact_hashes,
        "canonical_universe_record_count": 7,
        "canonical_universe_sha256": "a" * 64,
        "d_verification": {
            "report": witnesses["d_report"],
            "result": witnesses["d_result"],
            "status": "PASS",
            "verifier": witnesses["d_verifier"],
        },
        "lead_full_resolution_inspection": {
            "record": witnesses["lead_inspection"],
            "status": "PASS",
        },
        "schema": source_gate.BENCHMARK_APPROVAL_SCHEMA,
        "source_freeze_sha256": "c" * 64,
        "status": "LEAD_D_APPROVED",
    }
    return benchmark, candidate, approval, context


def test_approval_requires_exact_five_candidate_artifacts(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    repo = tmp_path / "repo"
    benchmark, candidate, approval, context = approval_fixture(repo)
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    value = registry()
    source_gate.validate_universe_approval(
        value,
        approval,
        benchmark=benchmark,
        source_freeze_sha256="c" * 64,
        candidate_manifest=candidate,
        context=context,
    )
    candidate["artifact_sha256"] = {}
    approval["candidate_artifact_sha256"] = {}
    with pytest.raises(source_gate.GateError, match="exactly all five"):
        source_gate.validate_universe_approval(
            value,
            approval,
            benchmark=benchmark,
            source_freeze_sha256="c" * 64,
            candidate_manifest=candidate,
            context=context,
        )


def test_approval_witness_absence_is_fail_closed(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    repo = tmp_path / "repo"
    _, _, approval, context = approval_fixture(repo)
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    value = registry()
    verifier = repo / value["authority_model"]["benchmark_approval_witnesses"]["d_verifier"]["path"]
    verifier.unlink()
    with pytest.raises(source_gate.GateError, match="D verifier witness file is missing"):
        source_gate.validate_approval_witnesses(value, approval, context=context)


@pytest.mark.parametrize(
    "bad_path",
    [
        "tools/traffic_v2/validation/not_the_frozen_verifier.py",
        "../outside-the-repository.py",
    ],
)
def test_approval_witness_wrong_or_traversal_path_is_rejected(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    bad_path: str,
) -> None:
    repo = tmp_path / "repo"
    _, _, approval, context = approval_fixture(repo)
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    value = registry()
    approval["d_verification"]["verifier"]["path"] = bad_path
    with pytest.raises(source_gate.GateError, match="does not match the registry-owned path"):
        source_gate.validate_approval_witnesses(value, approval, context=context)


def test_registry_owned_witness_path_cannot_traverse_repository(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    repo = tmp_path / "repo"
    _, _, approval, context = approval_fixture(repo)
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    value = registry()
    value["authority_model"]["benchmark_approval_witnesses"]["d_verifier"]["path"] = (
        "../outside-the-repository.py"
    )
    approval["d_verification"]["verifier"]["path"] = "../outside-the-repository.py"
    with pytest.raises(source_gate.GateError, match="canonical repository-relative POSIX path"):
        source_gate.validate_approval_witnesses(value, approval, context=context)


@pytest.mark.parametrize(
    ("mutation", "expected"),
    [
        ("bytes", "live bytes/hash mismatch"),
        ("wrong_lowercase_sha", "live bytes/hash mismatch"),
        ("uppercase_sha", "not lowercase hexadecimal"),
    ],
)
def test_approval_witness_wrong_bytes_or_sha_is_fail_closed(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    mutation: str,
    expected: str,
) -> None:
    repo = tmp_path / "repo"
    _, _, approval, context = approval_fixture(repo)
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    value = registry()
    verifier = approval["d_verification"]["verifier"]
    if mutation == "bytes":
        verifier["bytes"] += 1
    elif mutation == "wrong_lowercase_sha":
        verifier["sha256"] = "0" * 64
    else:
        verifier["sha256"] = "A" * 64
    with pytest.raises(source_gate.GateError, match=expected):
        source_gate.validate_approval_witnesses(value, approval, context=context)


@pytest.mark.parametrize(
    ("section", "expected"),
    [
        ("d_verification", "outer D status contradicts"),
        ("lead_full_resolution_inspection", "outer inspection status contradicts"),
    ],
)
def test_approval_witness_status_must_be_pass(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    section: str,
    expected: str,
) -> None:
    repo = tmp_path / "repo"
    _, _, approval, context = approval_fixture(repo)
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    approval[section]["status"] = "FAIL"
    with pytest.raises(source_gate.GateError, match=expected):
        source_gate.validate_approval_witnesses(registry(), approval, context=context)


def test_lead_inspection_live_record_content_must_be_pass(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    repo = tmp_path / "repo"
    _, _, approval, context = approval_fixture(repo)
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    value = registry()
    inspection_path = repo / value["authority_model"]["benchmark_approval_witnesses"][
        "lead_inspection"
    ]["path"]
    write_json(inspection_path, {"schema": "unit-test-inspection", "status": "FAIL"})
    approval["lead_full_resolution_inspection"]["record"] = source_gate.file_record(
        inspection_path, repo
    )
    with pytest.raises(source_gate.GateError, match="exact inspection schema"):
        source_gate.validate_approval_witnesses(value, approval, context=context)


def test_approval_requires_live_machine_readable_d_result(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    repo = tmp_path / "repo"
    _, _, approval, context = approval_fixture(repo)
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    value = registry()
    result_path = repo / value["authority_model"]["benchmark_approval_witnesses"][
        "d_result"
    ]["path"]
    result_path.unlink()
    with pytest.raises(source_gate.GateError, match="D machine result witness file is missing"):
        source_gate.validate_approval_witnesses(value, approval, context=context)


@pytest.mark.parametrize(
    ("mutation", "expected"),
    [
        ("check_fail", "content-derived PASS"),
        ("exit_nonzero", "content-derived PASS"),
        ("decision_fail", "content-derived PASS"),
        ("freeze", "content-derived PASS"),
        ("camera", "content-derived PASS"),
        ("universe", "content-derived PASS"),
        ("candidate_manifest", "content-derived PASS"),
        ("candidate_png", "content-derived PASS"),
        ("toolchain", "content-derived PASS"),
        ("source_tool", "content-derived PASS"),
        ("source_registry", "content-derived PASS"),
        ("requirements_lock", "content-derived PASS"),
        ("extra_field", "exact result schema"),
    ],
)
def test_d_machine_result_cannot_approve_stale_or_contradictory_evidence(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    mutation: str,
    expected: str,
) -> None:
    repo = tmp_path / "repo"
    _, _, approval, context = approval_fixture(repo)
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    value = registry()
    result_path = repo / value["authority_model"]["benchmark_approval_witnesses"][
        "d_result"
    ]["path"]
    result = json.loads(result_path.read_text(encoding="utf-8"))
    if mutation == "check_fail":
        result["checks"][context["required_checks"][0]] = "FAIL"
    elif mutation == "exit_nonzero":
        result["reproduction"]["exit_code"] = 1
    elif mutation == "decision_fail":
        result["decision"] = "FAIL_CLOSED"
        result["status"] = "FAIL_CLOSED"
    elif mutation == "freeze":
        result["bindings"]["source_freeze"]["sha256"] = "0" * 64
    elif mutation == "camera":
        result["bindings"]["camera_contract"]["sha256"] = "0" * 64
    elif mutation == "universe":
        result["bindings"]["canonical_universe"]["record_count"] += 1
    elif mutation == "candidate_manifest":
        result["bindings"]["candidate_manifest"]["sha256"] = "0" * 64
    elif mutation == "candidate_png":
        result["bindings"]["candidate_artifacts"][
            source_gate.EXPECTED_EXTERNAL_ARTIFACT
        ]["sha256"] = "0" * 64
    elif mutation == "toolchain":
        result["bindings"]["toolchain_manifest"]["sha256"] = "0" * 64
    elif mutation in ("source_tool", "source_registry", "requirements_lock"):
        result["bindings"][mutation]["sha256"] = "0" * 64
    else:
        result["untrusted_outer_claim"] = "PASS"
    write_json(result_path, result)
    approval["d_verification"]["result"] = source_gate.file_record(result_path, repo)
    with pytest.raises(source_gate.GateError, match=expected):
        source_gate.validate_approval_witnesses(value, approval, context=context)


def test_d_markdown_fail_decision_cannot_be_masked_by_outer_pass(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    repo = tmp_path / "repo"
    _, _, approval, context = approval_fixture(repo)
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    value = registry()
    report_path = repo / value["authority_model"]["benchmark_approval_witnesses"][
        "d_report"
    ]["path"]
    marker = value["authority_model"]["benchmark_approval_contract"][
        "d_report_decision_marker"
    ]
    report_path.write_text(f"<!-- {marker}: FAIL_CLOSED -->\n", encoding="utf-8")
    approval["d_verification"]["report"] = source_gate.file_record(report_path, repo)
    with pytest.raises(source_gate.GateError, match="decision marker"):
        source_gate.validate_approval_witnesses(value, approval, context=context)


@pytest.mark.parametrize(
    "binding",
    ["source_freeze", "camera_contract", "canonical_universe", "candidate_manifest", "candidate_png"],
)
def test_lead_inspection_cannot_be_reused_for_another_candidate(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    binding: str,
) -> None:
    repo = tmp_path / "repo"
    _, _, approval, context = approval_fixture(repo)
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    value = registry()
    inspection_path = repo / value["authority_model"]["benchmark_approval_witnesses"][
        "lead_inspection"
    ]["path"]
    inspection = json.loads(inspection_path.read_text(encoding="utf-8"))
    if binding == "candidate_png":
        inspection["bindings"]["candidate_artifacts"][
            source_gate.EXPECTED_EXTERNAL_ARTIFACT
        ]["sha256"] = "0" * 64
    elif binding == "canonical_universe":
        inspection["bindings"][binding]["record_count"] += 1
    else:
        inspection["bindings"][binding]["sha256"] = "0" * 64
    write_json(inspection_path, inspection)
    approval["lead_full_resolution_inspection"]["record"] = source_gate.file_record(
        inspection_path, repo
    )
    with pytest.raises(source_gate.GateError, match="stale or unrelated"):
        source_gate.validate_approval_witnesses(value, approval, context=context)


@pytest.mark.parametrize("mutation", ["dimensions", "full_resolution", "inspector"])
def test_lead_inspection_requires_full_resolution_and_inspector_record(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    mutation: str,
) -> None:
    repo = tmp_path / "repo"
    _, _, approval, context = approval_fixture(repo)
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    value = registry()
    inspection_path = repo / value["authority_model"]["benchmark_approval_witnesses"][
        "lead_inspection"
    ]["path"]
    inspection = json.loads(inspection_path.read_text(encoding="utf-8"))
    if mutation == "dimensions":
        inspection["inspection"]["width_pixels"] = 1599
    elif mutation == "full_resolution":
        inspection["inspection"]["full_resolution_opened"] = False
    else:
        inspection["inspector"] = {"id": "", "inspected_utc": "2026-08-22T00:00:00Z"}
    write_json(inspection_path, inspection)
    approval["lead_full_resolution_inspection"]["record"] = source_gate.file_record(
        inspection_path, repo
    )
    with pytest.raises(source_gate.GateError, match="full-resolution|inspector"):
        source_gate.validate_approval_witnesses(value, approval, context=context)


def test_lead_inspection_timestamp_must_follow_candidate_and_not_be_future_dated(
    tmp_path: Path,
) -> None:
    repo = tmp_path / "repo"
    _, _, _, context = approval_fixture(repo)
    witness_path = repo / registry()["authority_model"]["benchmark_approval_witnesses"][
        "lead_inspection"
    ]["path"]
    inspection = json.loads(witness_path.read_text(encoding="utf-8"))
    inspection["inspector"]["inspected_utc"] = "2026-08-20T23:59:59Z"
    with pytest.raises(source_gate.GateError, match="predates"):
        source_gate.validate_lead_inspection_semantics(inspection, context=context)
    inspection["inspector"]["inspected_utc"] = (
        source_gate.dt.datetime.now(source_gate.dt.timezone.utc)
        + source_gate.dt.timedelta(days=1)
    ).isoformat(timespec="microseconds").replace("+00:00", "Z")
    with pytest.raises(source_gate.GateError, match="future-dated"):
        source_gate.validate_lead_inspection_semantics(inspection, context=context)


@pytest.mark.parametrize("witness_id", ["d_result", "lead_inspection"])
def test_machine_witness_mutation_after_compile_is_detected(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    witness_id: str,
) -> None:
    repo = tmp_path / "repo"
    _, _, approval, context = approval_fixture(repo)
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    value = registry()
    path = repo / value["authority_model"]["benchmark_approval_witnesses"][witness_id]["path"]
    path.write_bytes(path.read_bytes() + b"tampered")
    with pytest.raises(source_gate.GateError, match="live bytes/hash mismatch"):
        source_gate.validate_approval_witnesses(value, approval, context=context)


def test_pending_candidate_is_create_once_compare_only(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    data = tmp_path / "data"
    evidence = tmp_path / "evidence"
    data.mkdir()
    evidence.mkdir()
    for index, name in enumerate(source_gate.EXPECTED_TEXT_ARTIFACTS):
        (data / name).write_bytes(f"artifact-{index}".encode())
    png = evidence / source_gate.EXPECTED_EXTERNAL_ARTIFACT
    png.write_bytes(b"png-candidate")
    benchmark, _, _, _ = approval_fixture(tmp_path / "repo")
    first = source_gate.preserve_pending_approval_candidate(
        tmp_path,
        data,
        png,
        benchmark=benchmark,
        candidate_event_utc="2026-08-21T00:00:00Z",
        source_freeze_sha256="c" * 64,
    )
    candidate_files = [
        tmp_path / "approval_candidate" / name
        for name in (*source_gate.EXPECTED_TEXT_ARTIFACTS, source_gate.EXPECTED_EXTERNAL_ARTIFACT)
    ]
    before = {path.name: path.read_bytes() for path in candidate_files}
    monkeypatch.setattr(
        source_gate,
        "atomic_write_bytes",
        lambda *_args, **_kwargs: pytest.fail("repeat comparison must not write candidate bytes"),
    )
    second = source_gate.preserve_pending_approval_candidate(
        tmp_path,
        data,
        png,
        benchmark=benchmark,
        candidate_event_utc="2026-08-21T00:00:00Z",
        source_freeze_sha256="c" * 64,
    )
    assert first == second
    assert before == {path.name: path.read_bytes() for path in candidate_files}
    (data / source_gate.EXPECTED_TEXT_ARTIFACTS[0]).write_bytes(b"divergent")
    with pytest.raises(source_gate.GateError, match="immutable inspected candidate"):
        source_gate.preserve_pending_approval_candidate(
            tmp_path,
            data,
            png,
            benchmark=benchmark,
            candidate_event_utc="2026-08-21T00:00:00Z",
            source_freeze_sha256="c" * 64,
        )
    assert before == {path.name: path.read_bytes() for path in candidate_files}


def test_first_candidate_creation_failure_never_exposes_partial_candidate(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    data = tmp_path / "data"
    evidence = tmp_path / "evidence"
    data.mkdir()
    evidence.mkdir()
    for index, name in enumerate(source_gate.EXPECTED_TEXT_ARTIFACTS):
        (data / name).write_bytes(f"artifact-{index}".encode())
    png = evidence / source_gate.EXPECTED_EXTERNAL_ARTIFACT
    png.write_bytes(b"png")
    benchmark, _, _, _ = approval_fixture(tmp_path / "repo")
    real_write = source_gate.atomic_write_bytes
    calls = 0

    def fail_mid_candidate(path: Path, payload: bytes) -> None:
        nonlocal calls
        calls += 1
        if calls == 3:
            raise OSError("injected candidate-stage failure")
        real_write(path, payload)

    monkeypatch.setattr(source_gate, "atomic_write_bytes", fail_mid_candidate)
    with pytest.raises(OSError, match="injected"):
        source_gate.preserve_pending_approval_candidate(
            tmp_path,
            data,
            png,
            benchmark=benchmark,
            candidate_event_utc="2026-08-21T00:00:00Z",
            source_freeze_sha256="c" * 64,
        )
    assert not (tmp_path / "approval_candidate").exists()
    assert not list(tmp_path.glob(".approval_candidate_stage_*"))


@pytest.mark.parametrize("candidate_preexists", [False, True])
def test_staged_compile_promotion_rolls_back_every_active_byte_on_index_failure(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    candidate_preexists: bool,
) -> None:
    external = tmp_path / "external"
    data = tmp_path / "data"
    evidence = tmp_path / "evidence"
    stage = tmp_path / "stage"
    stage_work = stage / "work"
    stage_data = stage / "data"
    stage_evidence = stage / "evidence"
    stage_manifest = stage / source_gate.RUN_MANIFEST_NAME
    candidate_stage = stage / "approval_candidate"
    for directory in (
        external / "normalized",
        external / "intermediates",
        data,
        evidence,
        stage_work / "normalized",
        stage_work / "intermediates",
        stage_data,
        stage_evidence,
        candidate_stage,
    ):
        directory.mkdir(parents=True, exist_ok=True)
    (external / "normalized/old.bin").write_bytes(b"old-normalized")
    (external / "intermediates/old.bin").write_bytes(b"old-intermediate")
    for name in source_gate.EXPECTED_TEXT_ARTIFACTS:
        (data / name).write_bytes(b"old-" + name.encode())
        (stage_data / name).write_bytes(b"new-" + name.encode())
    (evidence / source_gate.EXPECTED_EXTERNAL_ARTIFACT).write_bytes(b"old-png")
    (stage_evidence / source_gate.EXPECTED_EXTERNAL_ARTIFACT).write_bytes(b"new-png")
    (stage_work / "normalized/new.bin").write_bytes(b"new-normalized")
    (stage_work / "intermediates/new.bin").write_bytes(b"new-intermediate")
    stage_manifest.write_bytes(b"new-manifest")
    (external / source_gate.RUN_MANIFEST_NAME).write_bytes(b"old-manifest")
    (external / source_gate.PACKAGE_INDEX_NAME).write_bytes(b"old-index")
    (candidate_stage / "candidate_manifest.json").write_bytes(b"new-candidate")
    if candidate_preexists:
        active_candidate = external / "approval_candidate"
        active_candidate.mkdir()
        (active_candidate / "candidate_manifest.json").write_bytes(b"old-candidate")

    before_external = {
        path.relative_to(external).as_posix(): path.read_bytes()
        for path in external.rglob("*")
        if path.is_file()
    }
    before_data = {path.name: path.read_bytes() for path in data.iterdir()}
    before_evidence = {
        path.name: path.read_bytes() for path in evidence.iterdir() if path.is_file()
    }

    def fail_index(_registry: dict, run: Path, _data: Path) -> None:
        source_gate.atomic_write_bytes(run / source_gate.PACKAGE_INDEX_NAME, b"partial-index")
        raise OSError("injected index failure")

    monkeypatch.setattr(source_gate, "write_outer_package_index", fail_index)
    with pytest.raises(OSError, match="injected index failure"):
        source_gate._promote_compile_stage(
            {},
            external,
            stage,
            stage_work,
            stage_data,
            stage_evidence,
            stage_manifest,
            data,
            evidence,
            candidate_stage,
        )
    assert before_external == {
        path.relative_to(external).as_posix(): path.read_bytes()
        for path in external.rglob("*")
        if path.is_file()
    }
    assert before_data == {path.name: path.read_bytes() for path in data.iterdir()}
    assert before_evidence == {
        path.name: path.read_bytes() for path in evidence.iterdir() if path.is_file()
    }


def test_staged_compile_semantically_rejects_self_consistent_malformed_artifacts(
    tmp_path: Path,
) -> None:
    value = registry()
    external = tmp_path / "external"
    stage_work = tmp_path / "stage/work"
    stage_data = tmp_path / "stage/data"
    stage_evidence = tmp_path / "stage/evidence"
    for directory in (external, stage_work, stage_data, stage_evidence):
        directory.mkdir(parents=True, exist_ok=True)
    canonical_path = stage_work / "intermediates/canonical.geojsonl"
    write_jsonl(
        canonical_path,
        [
            {
                "canonical_segment_id": "canonical:test:1:0:0",
                "eligibility_status_or_exclusion_reason": "ELIGIBLE",
                "geometry": {
                    "coordinates": [[0.0, 0.0], [1000.0, 0.0]],
                    "type": "LineString",
                },
                "lane_count": 2,
                "represented_source_disposition": "CANONICAL_GEOMETRY",
                "road_class": "primary",
                "source_precedence_decision": "SELECTED_OFFICIAL_AUTHORITY",
            }
        ],
    )
    conflict_ledger = stage_work / "intermediates/conflicts.jsonl"
    support_ledger = stage_work / "intermediates/support.jsonl"
    lane_ledger = stage_work / "intermediates/lanes.jsonl"
    reference_inventory = stage_work / "intermediates/references.jsonl"
    reference_representation = stage_work / "intermediates/reference_representation.jsonl"
    unmatched_reference = stage_work / "intermediates/unmatched.jsonl"
    conflation_ledger = stage_work / "intermediates/conflation.jsonl"
    for empty_ledger in (
        conflict_ledger,
        reference_inventory,
        reference_representation,
        unmatched_reference,
        conflation_ledger,
    ):
        write_jsonl(empty_ledger, [])
    write_jsonl(
        support_ledger,
        [
            {
                "canonical_lane_kilometer_denominator": 2.0,
                "canonical_segment_id": "canonical:test:1:0:0",
                "independence_excluded_source_ids": ["test"],
                "independently_supported_lane_kilometers": 0.0,
                "lane_count": 2,
                "length_meters": 1000.0,
                "road_class": "primary",
                "status": "NO_PROVEN_DIFFERENT_SOURCE_SUPPORT",
                "support_links": [],
                "unresolved_correspondences": [],
            }
        ],
    )
    write_jsonl(
        lane_ledger,
        [
            {
                "canonical_segment_id": "canonical:test:1:0:0",
                "confidence": "DECLARED",
                "conflicting_lane_evidence": [],
                "lane_count": 2,
                "reason": None,
            }
        ],
    )
    png = stage_evidence / source_gate.EXPECTED_EXTERNAL_ARTIFACT
    source_gate.Image.new("RGB", (1600, 1600), (255, 255, 255)).save(png, "PNG")
    png_record = {
        "bytes": png.stat().st_size,
        "height_pixels": 1600,
        "inspection_status": "PENDING_LEAD_AND_D_FULL_RESOLUTION_INSPECTION",
        "path": f"evidence/{source_gate.EXPECTED_EXTERNAL_ARTIFACT}",
        "sha256": source_gate.sha256_file(png),
        "width_pixels": 1600,
    }
    lane_inventory = source_gate.lane_kilometer_summary(canonical_path)
    support_summary = source_gate._derive_independent_support_summary(
        list(source_gate.read_jsonl(support_ledger))
    )
    support_by_class = support_summary["by_class"]
    references = {
        classification: {
            "matched_observation_count": 0,
            "reference_observation_count": 0,
            "unmatched_observation_count": 0,
        }
        for classification in source_gate.ROAD_CLASSES
    }
    denominator = source_gate.normalization_denominator_audit([], stage_work)
    class_coverage: dict[str, dict] = {}
    for classification in source_gate.ROAD_CLASSES:
        lane_value = lane_inventory["by_class"][classification]
        support = support_by_class[classification]
        class_coverage[classification] = source_gate.class_source_coverage(
            classification,
            lane_value,
            support,
            references[classification],
            geometryless_motor_road_record_count=0,
            unresolved_geometric_motor_road_record_count=0,
            unknown_class_segment_count=0,
        )
    freeze_value = {
        "product_binding_warnings": [],
        "sources": [{"has_z": True, "id": "drcog_pavement_lines_2024"}],
    }
    write_json(external / source_gate.FREEZE_NAME, freeze_value)
    freeze_sha = source_gate.sha256_file(external / source_gate.FREEZE_NAME)
    universe_record = source_gate.file_record(canonical_path, stage_work)
    base_conflicts = source_gate.source_conflicts(
        value,
        freeze_value,
        [],
        {"status": "PENDING_LEAD_D_INSPECTION"},
    )
    blockers = [
        item
        for item in base_conflicts
        if item["severity"] == "BLOCKING" and item["status"] != "RESOLVED"
    ]
    blocker = next(
        item
        for item in blockers
        if item["id"] == "benchmark_camera_road_inventory_gap"
    )
    view_bindings = [
        {
            "canonical_universe_record_count": 1,
            "canonical_universe_sha256": universe_record["sha256"],
            "origin_inside_frozen_bounds": True,
            "road_candidate_set": "FULL_MAP_UNIVERSE_SUPERSET",
            "view_id": f"view-{index:02d}",
        }
        for index in range(15)
    ]
    coverage = {
        "accepted_source_sha": value["accepted_source_sha"],
        "authority_blockers": blockers,
        "benchmark_camera_coverage": {
            "approval": {"status": "PENDING_LEAD_D_INSPECTION"},
            "camera_contract": {"sha256": "b" * 64},
            "canonical_universe": {
                "record_count": 1,
                "sha256": universe_record["sha256"],
            },
            "status": "PENDING_LEAD_D_INSPECTION",
            "view_bindings": view_bindings,
            "view_count": 15,
        },
        "bounds_evaluation": {
            "cell_count": 576,
            "cells": source_gate.cell_inventory(value, canonical_path),
            "evaluated_cell_count": 576,
            "exact_bounds_xz_meters": value["coordinate_contract"][
                "bounds_xz_meters"
            ],
            "status": "PASS",
        },
            "canonical_road_universe": {
                "canonical_universe": universe_record,
                "canonical_universe_record_count": 1,
                "conflict_count": 0,
                "conflict_ledger": source_gate.file_record(conflict_ledger, stage_work),
                "conflicts": [],
                "conflation_ledger": source_gate.file_record(conflation_ledger, stage_work),
                "independent_lane_km_support": support_summary,
                "independent_lane_km_support_ledger": source_gate.file_record(
                    support_ledger, stage_work
                ),
                "lane_evidence_ledger": source_gate.file_record(
                    lane_ledger, stage_work
                ),
                "motor_reference_disposition_counts": {},
                "reference_class_audit": references,
                "reference_disposition_counts": {},
                "reference_inventory": source_gate.file_record(
                    reference_inventory, stage_work
                ),
                "reference_observation_count": 0,
                "reference_representation_ledger": source_gate.file_record(
                    reference_representation, stage_work
                ),
                "unmatched_reference_record": source_gate.file_record(
                    unmatched_reference, stage_work
                ),
            },
        "class_coverage": class_coverage,
        "evidence": {source_gate.EXPECTED_EXTERNAL_ARTIFACT: png_record},
        "gate_status": "BLOCKED_AUTHORITY",
        "lane_kilometer_inventory": lane_inventory,
        "normalization_denominator_audit": denominator,
            "schema": "questflight.traffic-v2-source-coverage.v2",
            "sources": [
                {
                    "id": "drcog_pavement_lines_2024",
                    "source_kind": "arcgis",
                }
            ],
        "source_freeze_sha256": freeze_sha,
        "specification_sha256": value["specification"]["sha256"],
    }
    conflict_report = {
        "blocking_count": len(blockers),
        "conflicts": base_conflicts,
        "forward_requirement_count": sum(
            item["severity"].startswith("FORWARD_") for item in base_conflicts
        ),
        "gate_status": "BLOCKED_AUTHORITY",
        "schema": "questflight.traffic-v2-source-conflicts.v2",
        "warning_count": sum(
            item["severity"] == "WARNING" for item in base_conflicts
        ),
    }
    missing = {
        "features": [],
        "properties": {
            "feature_count": 0,
            "gate_status": "BLOCKED_AUTHORITY",
            "schema": "questflight.traffic-v2-missing-roads.v2",
        },
        "type": "FeatureCollection",
    }
    provenance = "\n".join(
        (
            "# Traffic System V2 Phase-3 source provenance",
            "",
            "- Gate status: **BLOCKED_AUTHORITY**",
            f"- Accepted source: `{value['accepted_source_sha']}`",
            f"- Frozen specification SHA-256: `{value['specification']['sha256']}`",
            f"- Source freeze SHA-256: `{freeze_sha}`",
            (
                "- Canonical full-map universe: 1 records, SHA-256 "
                f"`{universe_record['sha256']}`."
            ),
            f"- External coverage PNG SHA-256: `{png_record['sha256']}`",
            *(
                f"- **{item['severity']}** `{item['id']}`: {item['reason']}"
                for item in base_conflicts
            ),
            "",
        )
    )

    def bind_and_validate(
        coverage_value: dict,
        conflict_value: dict,
        missing_value: dict,
        provenance_value: str,
    ) -> None:
        candidate_gate_status = coverage_value["gate_status"]
        write_json(stage_data / "traffic_source_coverage.json", coverage_value)
        write_json(stage_data / "traffic_source_conflicts.json", conflict_value)
        write_json(stage_data / "traffic_missing_roads.geojson", missing_value)
        (stage_data / "traffic_source_provenance.md").write_text(
            provenance_value, encoding="utf-8"
        )
        outputs = {
            name: {
                "bytes": (stage_data / name).stat().st_size,
                "sha256": source_gate.sha256_file(stage_data / name),
            }
            for name in source_gate.EXPECTED_TEXT_ARTIFACTS
        }
        outputs[source_gate.EXPECTED_EXTERNAL_ARTIFACT] = png_record
        candidate = {
            "artifact_sha256": {
                name: outputs[name]["sha256"] for name in sorted(outputs)
            },
            "camera_contract_sha256": "b" * 64,
            "candidate_event_utc": "2026-08-21T00:00:00Z",
            "canonical_universe_record_count": 1,
            "canonical_universe_sha256": universe_record["sha256"],
            "schema": "questflight.traffic-v2-geospatial-approval-candidate.v1",
            "source_freeze_sha256": freeze_sha,
            "status": "PENDING_LEAD_D_INSPECTION",
        }
        candidate_bytes = source_gate.canonical_json_bytes(candidate)
        run_manifest = {
            "approval_adjudication": {
                "candidate_gate_status": candidate_gate_status
            },
            "approval_candidate": {
                "bytes": len(candidate_bytes),
                "path": "approval_candidate/candidate_manifest.json",
                "sha256": source_gate.sha256_bytes(candidate_bytes),
            },
            "candidate_gate_status": candidate_gate_status,
            "freeze_sha256": freeze_sha,
            "outputs": outputs,
            "run_context": {"outputs": outputs},
            "schema": "questflight.traffic-v2-geospatial-run-manifest.v1",
        }
        source_gate._validate_staged_compile_set(
            value,
            external,
            stage_work,
            run_manifest,
            stage_data,
            stage_evidence,
            candidate,
            verified_naip_authority=None,
        )

    bind_and_validate(coverage, conflict_report, missing, provenance)
    malformed_values = []
    malformed_conflicts = deepcopy(conflict_report)
    malformed_conflicts["blocking_count"] = 0
    malformed_values.append((coverage, malformed_conflicts, missing, provenance))
    malformed_missing = deepcopy(missing)
    malformed_missing["properties"]["feature_count"] = 1
    malformed_values.append((coverage, conflict_report, malformed_missing, provenance))
    malformed_values.append(
        (
            coverage,
            conflict_report,
            missing,
            provenance.replace(f"- Source freeze SHA-256: `{freeze_sha}`\n", ""),
        )
    )
    omitted_coverage = deepcopy(coverage)
    omitted_coverage["authority_blockers"] = []
    omitted_coverage["gate_status"] = "PASS"
    omitted_conflicts = deepcopy(conflict_report)
    omitted_conflicts["conflicts"] = [
        item for item in omitted_conflicts["conflicts"] if item["id"] != blocker["id"]
    ]
    omitted_conflicts["blocking_count"] = 0
    omitted_conflicts["gate_status"] = "PASS"
    omitted_missing = deepcopy(missing)
    omitted_missing["properties"]["gate_status"] = "PASS"
    omitted_provenance = provenance.replace(
        "- Gate status: **BLOCKED_AUTHORITY**", "- Gate status: **PASS**"
    )
    omitted_provenance = "\n".join(
        line
        for line in omitted_provenance.splitlines()
        if f"`{blocker['id']}`" not in line
    )
    malformed_values.append(
        (
            omitted_coverage,
            omitted_conflicts,
            omitted_missing,
            omitted_provenance,
        )
    )
    coherent_support = deepcopy(coverage)
    primary_support = coherent_support["canonical_road_universe"][
        "independent_lane_km_support"
    ]["by_class"]["primary"]
    primary_support["independently_supported_lane_kilometers"] = 2.0
    primary_support["independent_support_lane_kilometer_ratio"] = 1.0
    primary_support["supported_segment_count"] = 1
    coherent_support["class_coverage"]["primary"] = source_gate.class_source_coverage(
        "primary",
        coherent_support["lane_kilometer_inventory"]["by_class"]["primary"],
        primary_support,
        coherent_support["canonical_road_universe"]["reference_class_audit"]["primary"],
        geometryless_motor_road_record_count=0,
        unresolved_geometric_motor_road_record_count=0,
        unknown_class_segment_count=0,
    )
    with pytest.raises(source_gate.GateError, match="Independent-support summary"):
        bind_and_validate(coherent_support, conflict_report, missing, provenance)
    for values in malformed_values:
        with pytest.raises(source_gate.GateError, match="Staged"):
            bind_and_validate(*deepcopy(values))

    invented_unmatched = {
        "geometry": {"coordinates": [[0.0, 0.0], [1.0, 0.0]], "type": "LineString"},
        "properties": {
            "observation_kind": "INDEPENDENT_SOURCE_REFERENCE",
            "reference_observation_id": "invented:1",
        },
        "type": "Feature",
    }
    write_jsonl(unmatched_reference, [invented_unmatched])
    coherent_unmatched_coverage = deepcopy(coverage)
    coherent_unmatched_coverage["canonical_road_universe"][
        "unmatched_reference_record"
    ] = source_gate.file_record(unmatched_reference, stage_work)
    coherent_unmatched_missing = deepcopy(missing)
    coherent_unmatched_missing["features"] = [invented_unmatched]
    coherent_unmatched_missing["properties"]["feature_count"] = 1
    with pytest.raises(source_gate.GateError, match="Unmatched-reference ledger"):
        bind_and_validate(
            coherent_unmatched_coverage,
            conflict_report,
            coherent_unmatched_missing,
            provenance,
        )


def test_reference_match_cannot_be_forged_without_exact_canonical_subspan() -> None:
    value = registry()
    config = next(
        item
        for item in value["authority_model"]["reference_sources"]
        if item["source_id"] == "city_street_centerlines"
    )
    record = {
        "attributes": {
            "CENTERLINE": "Single",
            "FULLNAME": "Main Street",
            "NUMLANES": 2,
            "ONEWAY": "B",
            "STREETCLASS": "Principal Arterial",
        },
        "geometry": {
            "coordinates": [[0.0, 0.0], [100.0, 0.0]],
            "type": "LineString",
        },
        "license_partition": "city_cc0",
        "source_id": "city_street_centerlines",
        "source_record_id": "1",
        "source_record_part": 0,
    }
    observation = source_gate.reference_observation_value(value, config, record)
    inventory = {
        key: nested
        for key, nested in observation.items()
        if not key.startswith("_")
    }
    canonical = {
        "canonical_segment_id": "canonical:city_street_centerlines:1:0:0",
        "geometry": record["geometry"],
        "represented_source_disposition": "CANONICAL_GEOMETRY",
        "source_id": record["source_id"],
        "source_record_id": record["source_record_id"],
        "source_record_part": record["source_record_part"],
    }
    lookup = {(record["source_id"], record["source_record_id"], 0): record}
    matched = source_gate._derive_reference_representation_rows(
        value, [inventory], [canonical], lookup
    )
    assert matched[0]["decision"] == "ELIGIBLE_REFERENCE_MATCHED"
    assert matched[0]["match_ratio"] == 1.0
    assert matched[0]["matched_canonical_records"]

    with pytest.raises(source_gate.GateError, match="not independently reproducible"):
        source_gate._validate_exact_reference_representation_rows(
            value,
            matched,
            [inventory],
            [],
            lookup,
        )


def test_reference_strtree_is_byte_equivalent_to_full_scan_and_excludes_far_rows() -> None:
    value = registry()
    config = next(
        item
        for item in value["authority_model"]["reference_sources"]
        if item["source_id"] == "city_street_centerlines"
    )
    record = line_record(
        "city_street_centerlines",
        "near",
        [[0.0, 0.0], [100.0, 0.0]],
        attributes={
            "CENTERLINE": "Single",
            "FULLNAME": "Main Street",
            "NUMLANES": 2,
            "ONEWAY": "B",
            "STREETCLASS": "Principal Arterial",
        },
        license_partition="city_cc0",
    )
    far_record = deepcopy(record)
    far_record["source_record_id"] = "far"
    far_record["geometry"] = {
        "coordinates": [[10000.0, 10000.0], [10100.0, 10000.0]],
        "type": "LineString",
    }
    observation = source_gate.reference_observation_value(value, config, record)
    candidates = [
        {
            "canonical_segment_id": f"canonical:city:{candidate['source_record_id']}",
            "geometry": candidate["geometry"],
            "source_id": candidate["source_id"],
            "source_record_id": candidate["source_record_id"],
            "source_record_part": candidate["source_record_part"],
        }
        for candidate in (record, far_record)
    ]
    lookup = {
        (
            candidate["source_id"],
            candidate["source_record_id"],
            candidate["source_record_part"],
        ): candidate
        for candidate in (record, far_record)
    }
    tree = source_gate.STRtree(
        [source_gate.shape(candidate["geometry"]) for candidate in candidates]
    )
    queried = [
        int(index)
        for index in tree.query(source_gate.shape(record["geometry"]).buffer(8.0))
    ]
    assert queried == [0]
    full_scan = source_gate.reference_representation_ledger_value(
        value, observation, candidates, lookup
    )
    indexed = source_gate.reference_representation_ledger_value(
        value, observation, candidates, lookup, tree
    )
    assert indexed == full_scan


def test_canonical_conflict_rows_are_exactly_derived_and_cannot_be_forged() -> None:
    ambiguous = {
        "id": "ambiguous_spatial_conflation",
        "nearby_records": [{"source_id": "higher", "source_record_id": "9"}],
        "source_id": "city_street_centerlines",
        "source_record_id": "1",
        "source_record_part": 0,
    }
    class_evidence = [{"source_id": "county", "road_class": "primary"}]
    lane_evidence = [{"source_id": "county", "lane_count": 4}]
    support_evidence = [{"support_source_id": "osm", "reason_codes": ["MISMATCH"]}]
    canonical_id = "canonical:city_street_centerlines:1:0:0"
    canonical_rows = [
        {
            "canonical_segment_id": canonical_id,
            "class_evidence_source_id": None,
            "conflicting_class_evidence": class_evidence,
            "lane_count": None,
            "lane_evidence_source_id": None,
            "represented_source_disposition": "CANONICAL_GEOMETRY",
            "road_class": None,
        }
    ]
    conflation_rows = [
        {
            "decision": "RETAINED_AMBIGUOUS_FAIL_CLOSED",
            "source_id": "city_street_centerlines",
            "source_precedence_conflicts": [ambiguous],
            "source_record_id": "1",
            "source_record_part": 0,
        }
    ]
    lane_rows = [
        {
            "canonical_segment_id": canonical_id,
            "confidence": "CONFLICT_FAIL_CLOSED",
            "conflicting_lane_evidence": lane_evidence,
            "lane_count": None,
            "reason": "CONFLICTING_DIRECT_LANE_COUNTS",
        }
    ]
    support_rows = [
        {
            "canonical_segment_id": canonical_id,
            "incompatible_lane_km_evidence": support_evidence,
            "semantic_ambiguities": [],
            "unresolved_correspondences": support_evidence,
        }
    ]
    derived = source_gate._derive_canonical_conflicts_from_ledgers(
        canonical_rows, conflation_rows, lane_rows, support_rows
    )
    assert {row["id"] for row in derived} == {
        "ambiguous_spatial_conflation",
        "conflicting_class_evidence",
        "conflicting_lane_evidence",
        "incompatible_independent_lane_km_evidence",
    }
    source_gate._validate_exact_canonical_conflict_ledger(
        derived, canonical_rows, conflation_rows, lane_rows, support_rows
    )
    with pytest.raises(source_gate.GateError, match="not exactly derived"):
        source_gate._validate_exact_canonical_conflict_ledger(
            derived[:-1], canonical_rows, conflation_rows, lane_rows, support_rows
        )
    forged = deepcopy(derived)
    forged[0] = {**forged[0], "id": "forged_conflict"}
    with pytest.raises(source_gate.GateError, match="not exactly derived"):
        source_gate._validate_exact_canonical_conflict_ledger(
            forged, canonical_rows, conflation_rows, lane_rows, support_rows
        )


def test_staged_dynamic_blocker_derivation_cannot_omit_failed_coverage() -> None:
    class_coverage = {
        classification: {"status": "PASS"}
        for classification in source_gate.ROAD_CLASSES
    }
    class_coverage["tertiary"] = {"status": "FAIL_CLOSED"}
    required = source_gate._required_staged_dynamic_blocker_ids(
        {
            "conflict_count": 1,
            "reference_class_audit": {
                "tertiary": {"unmatched_observation_count": 1}
            },
        },
        {
            "unknown_class_segment_count": 1,
            "unknown_lane_segment_count": 1,
            "unresolved_geometric_segment_count": 1,
        },
        {
            "geometryless_motor_road_record_count": 1,
            "unresolved_geometric_motor_road_record_count": 1,
        },
        class_coverage,
        [
            {
                "properties": {
                    "observation_kind": "OSM_NORMALIZATION_FAILURE"
                }
            },
            {
                "properties": {
                    "observation_kind": "ARCGIS_NORMALIZATION_FAILURE"
                }
            },
        ],
        {"status": "PENDING_LEAD_D_INSPECTION"},
    )
    assert required == {
        "arcgis_normalization_failures",
        "benchmark_camera_road_inventory_gap",
        "canonical_evidence_conflicts",
        "canonical_unknown_lane_counts",
        "canonical_unknown_road_classes",
        "geometryless_motor_road_observations",
        "independent_reference_observations_unmatched",
        "invalid_or_unknown_osm_observations",
        "tertiary_represented_lane_km_coverage_gap",
        "unresolved_motor_road_geometric_lengths",
    }


def test_pending_to_approved_lifecycle_never_changes_candidate_bytes(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    repo = tmp_path / "repo"
    external = tmp_path / "external"
    data = tmp_path / "data"
    evidence = tmp_path / "evidence"
    data.mkdir()
    evidence.mkdir()
    for index, name in enumerate(source_gate.EXPECTED_TEXT_ARTIFACTS):
        (data / name).write_bytes(f"artifact-{index}".encode())
    png = evidence / source_gate.EXPECTED_EXTERNAL_ARTIFACT
    png.write_bytes(b"png-candidate")
    benchmark, _, approval, context = approval_fixture(repo)
    source_gate.preserve_pending_approval_candidate(
        external,
        data,
        png,
        benchmark=benchmark,
        candidate_event_utc="2026-08-21T00:00:00Z",
        source_freeze_sha256="c" * 64,
    )
    candidate_manifest = json.loads(
        (external / "approval_candidate/candidate_manifest.json").read_text(encoding="utf-8")
    )
    approval["candidate_artifact_sha256"] = candidate_manifest["artifact_sha256"]
    context["bindings"]["candidate_manifest"] = source_gate.file_record(
        external / "approval_candidate/candidate_manifest.json", external
    )
    context["bindings"]["candidate_artifacts"] = {
        name: source_gate.file_record(external / "approval_candidate" / name, external)
        for name in (*source_gate.EXPECTED_TEXT_ARTIFACTS, source_gate.EXPECTED_EXTERNAL_ARTIFACT)
    }
    witnesses = write_approval_witnesses(repo, context)
    approval["d_verification"].update(
        report=witnesses["d_report"],
        result=witnesses["d_result"],
        verifier=witnesses["d_verifier"],
    )
    approval["lead_full_resolution_inspection"]["record"] = witnesses["lead_inspection"]
    approval_path = repo / "tools/traffic_v2/schemas/approval.json"
    write_json(approval_path, approval)
    value = deepcopy(registry())
    value["authority_model"]["benchmark_approval_path"] = "tools/traffic_v2/schemas/approval.json"
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    monkeypatch.setattr(
        source_gate,
        "build_approval_validation_context",
        lambda *_args, **_kwargs: context,
    )
    before = {
        path.relative_to(external).as_posix(): source_gate.sha256_file(path)
        for path in (external / "approval_candidate").iterdir()
        if path.is_file()
    }
    adjudication = source_gate.evaluate_universe_approval(
        value,
        repo / "tools/traffic_v2/geospatial/source_registry.json",
        external,
        benchmark,
        "c" * 64,
    )
    after = {
        path.relative_to(external).as_posix(): source_gate.sha256_file(path)
        for path in (external / "approval_candidate").iterdir()
        if path.is_file()
    }
    assert adjudication["status"] == "LEAD_D_APPROVED"
    assert before == after
    problems: list[str] = []
    source_gate.validate_live_approval_file_bindings(
        value, external, adjudication, problems, context=context
    )
    assert problems == []
    report_path = repo / value["authority_model"]["benchmark_approval_witnesses"]["d_report"]["path"]
    report_path.write_text("# tampered after compile\n", encoding="utf-8")
    stale_problems: list[str] = []
    source_gate.validate_live_approval_file_bindings(
        value, external, adjudication, stale_problems, context=context
    )
    assert any(
        problem.startswith("approved run's live witness validation failed: D report witness live bytes/hash mismatch")
        for problem in stale_problems
    )
    assert before == {
        path.relative_to(external).as_posix(): source_gate.sha256_file(path)
        for path in (external / "approval_candidate").iterdir()
        if path.is_file()
    }


def test_rebuild_check_routes_every_write_below_rebuild_root(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    external = tmp_path / "external"
    data = tmp_path / "data"
    evidence = tmp_path / "evidence"
    candidate_root = external / "approval_candidate"
    candidate_root.mkdir(parents=True)
    data.mkdir()
    evidence.mkdir()
    for name in source_gate.EXPECTED_TEXT_ARTIFACTS:
        if name == "traffic_source_coverage.json":
            write_json(data / name, {"benchmark_camera_coverage": {}})
        else:
            (data / name).write_bytes(name.encode())
        (candidate_root / name).write_bytes((data / name).read_bytes())
    (evidence / source_gate.EXPECTED_EXTERNAL_ARTIFACT).write_bytes(b"png")
    (candidate_root / source_gate.EXPECTED_EXTERNAL_ARTIFACT).write_bytes(b"png")
    write_json(candidate_root / "candidate_manifest.json", {"candidate_event_utc": "2026-08-21T00:00:00Z"})
    write_json(
        external / source_gate.FREEZE_NAME,
        {"acquisition_completed_utc": "2026-08-21T00:00:00Z"},
    )
    write_json(
        external / source_gate.RUN_MANIFEST_NAME,
        {
            "run_context": {
                "command": source_gate.compile_command_context(
                    source_gate.DEFAULT_REGISTRY, external, data, evidence
                )
            }
        },
    )

    def fake_compile_core(
        _registry_path: Path,
        passed_external: Path,
        rebuild_data: Path,
        rebuild_evidence: Path,
        **kwargs: object,
    ) -> dict:
        assert passed_external == external
        assert kwargs["allow_candidate_creation"] is False
        assert Path(kwargs["derivative_run"]).parent.name == "rebuild_check"
        assert not Path(kwargs["derivative_run"]).is_relative_to(external)
        assert Path(kwargs["manifest_path"]).parent.name == "rebuild_check"
        for name in source_gate.EXPECTED_TEXT_ARTIFACTS:
            destination = rebuild_data / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes((data / name).read_bytes())
        rebuilt_png = rebuild_evidence / source_gate.EXPECTED_EXTERNAL_ARTIFACT
        rebuilt_png.parent.mkdir(parents=True, exist_ok=True)
        rebuilt_png.write_bytes((evidence / source_gate.EXPECTED_EXTERNAL_ARTIFACT).read_bytes())
        return {"outputs": {}}

    monkeypatch.setattr(
        source_gate,
        "validate_frozen_run_closure",
        lambda *_args, **_kwargs: {
            "external_run": external,
            "freeze_path": external / source_gate.FREEZE_NAME,
            "registry": registry(),
            "verified_naip_authority": None,
        },
    )
    monkeypatch.setattr(source_gate, "immutable_authority_snapshot", lambda *_args, **_kwargs: {})
    monkeypatch.setattr(source_gate, "require_same_authority_snapshot", lambda *_args, **_kwargs: None)
    monkeypatch.setattr(
        source_gate,
        "_validate_outputs_core",
        lambda *_args, **_kwargs: {"problems": [], "status": "PASS"},
    )
    monkeypatch.setattr(source_gate, "_validate_staged_compile_set", lambda *_args, **_kwargs: None)
    monkeypatch.setattr(
        source_gate,
        "approval_candidate_value",
        lambda *_args, **_kwargs: {"schema": "synthetic-candidate"},
    )
    monkeypatch.setattr(source_gate, "_compile_artifacts_core", fake_compile_core)
    monkeypatch.setattr(
        source_gate,
        "write_outer_package_index",
        lambda _registry, run, _data: source_gate.atomic_write_json(
            run / source_gate.PACKAGE_INDEX_NAME, {"schema": "test-index"}
        ),
    )
    report = source_gate.rebuild_check(
        source_gate.DEFAULT_REGISTRY,
        external,
        data,
        evidence,
    )
    assert report["status"] == "PASS"
    assert (candidate_root / "candidate_manifest.json").is_file()
    assert (external / "rebuild_check/rebuild_check.json").is_file()
    assert (external / source_gate.PACKAGE_INDEX_NAME).is_file()


def test_validator_compares_exact_raw_and_disposition_id_sets(tmp_path: Path) -> None:
    value = deepcopy(registry())
    value["authority_model"]["benchmark_approval_path"] = "tools/traffic_v2/schemas/unit-test-no-approval.json"
    raw_osm = tmp_path / "raw/osm.json"
    write_json(
        raw_osm,
        {
            "elements": [
                {"id": 1, "lat": 40.0, "lon": -105.0, "type": "node"},
                {"id": 10, "nodes": [1], "tags": {"highway": "primary"}, "type": "way"},
            ]
        },
    )
    osm_ledger = tmp_path / "intermediates/osm_dispositions.jsonl"
    write_jsonl(osm_ledger, [])
    osm_normalized = tmp_path / "normalized/osm.geojsonl"
    write_jsonl(osm_normalized, [])
    arcgis_ledger = tmp_path / "intermediates/city_dispositions.jsonl"
    write_jsonl(
        arcgis_ledger,
        [
            {"motor_road_disposition": "ELIGIBLE", "source_record_id": "1"},
            {"motor_road_disposition": "ELIGIBLE", "source_record_id": "1"},
        ],
    )
    city_normalized = tmp_path / "normalized/city.geojsonl"
    write_jsonl(city_normalized, [])
    universe = tmp_path / "intermediates/universe.geojsonl"
    write_jsonl(universe, [])
    freeze = {
        "sources": [
            {"id": "osm_bounded_roads", "raw": source_gate.file_record(raw_osm, tmp_path)},
            {"feature_count": 2, "id": "city_street_centerlines", "pages": [{"requested_ids": [1, 2]}]},
        ]
    }
    write_json(tmp_path / source_gate.FREEZE_NAME, freeze)
    universe_record = source_gate.file_record(universe, tmp_path)
    benchmark = source_gate.validate_benchmark_camera_contract(value, universe_record, 0)
    benchmark["approval"] = {
        "approval_path": value["authority_model"]["benchmark_approval_path"],
        "status": "PENDING_LEAD_D_INSPECTION",
    }
    coverage = {
        "benchmark_camera_coverage": benchmark,
        "canonical_road_universe": {
            "canonical_universe": universe_record,
            "canonical_universe_record_count": 0,
        },
        "class_coverage": {},
        "osm_fallback": {
            "disposition_ledger": source_gate.file_record(osm_ledger, tmp_path),
            "duplicate_summary": {
                "occurrence_count_by_type": {"way": 1},
                "unique_count_by_type": {"way": 1},
            },
            "unique_way_count": 1,
        },
        "source_freeze_sha256": source_gate.sha256_file(tmp_path / source_gate.FREEZE_NAME),
        "sources": [
            {
                "feature_count": None,
                "id": "osm_bounded_roads",
                "normalized": {
                    "disposition_ledger": source_gate.file_record(osm_ledger, tmp_path),
                    "normalized": source_gate.file_record(osm_normalized, tmp_path),
                },
                "source_kind": "osm",
            },
            {
                "feature_count": 2,
                "id": "city_street_centerlines",
                "normalized": {
                    "disposition_ledger": source_gate.file_record(arcgis_ledger, tmp_path),
                    "normalized": source_gate.file_record(city_normalized, tmp_path),
                },
                "source_kind": "arcgis",
            }
        ],
    }
    problems: list[str] = []
    source_gate.validate_correction_invariants(value, tmp_path, coverage, problems)
    assert "OSM disposition ledger ID set differs from unique raw way IDs" in problems
    assert "ArcGIS disposition ledger has duplicate OIDs: city_street_centerlines" in problems
    assert "ArcGIS disposition-ledger OID set mismatch: city_street_centerlines" in problems
    omitted = deepcopy(coverage)
    omitted["sources"] = [item for item in omitted["sources"] if item["id"] != "city_street_centerlines"]
    omitted_problems: list[str] = []
    source_gate.validate_correction_invariants(value, tmp_path, omitted, omitted_problems)
    assert any(problem.startswith("coverage/freeze source ID-set mismatch") for problem in omitted_problems)


def test_outer_package_index_includes_nested_file_named_package_index(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    repo = tmp_path / "repo"
    data = repo / "docs/data/traffic_v2"
    external = tmp_path / "external"
    data.mkdir(parents=True)
    external.mkdir()
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    nested = external / "raw/package_index.json"
    write_json(nested, {"this": "is a raw source payload, not the outer index"})
    for name in source_gate.EXPECTED_TEXT_ARTIFACTS:
        if name == "traffic_source_coverage.json":
            write_json(data / name, {"nested_raw": source_gate.file_record(nested, external)})
        else:
            (data / name).write_text(name, encoding="utf-8")
    write_json(external / source_gate.FREEZE_NAME, {"schema": "test"})
    write_json(external / source_gate.RUN_MANIFEST_NAME, {"schema": "test"})
    source_gate.write_outer_package_index(registry(), external, data)
    problems: list[str] = []
    source_gate.validate_outer_package_index(value := registry(), external, data, problems)
    assert problems == []
    assert value["external_package"]["outer_index_path"] == source_gate.PACKAGE_INDEX_NAME


def test_outer_package_index_rejects_traversal_entry(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    repo = tmp_path / "repo"
    external = tmp_path / "external"
    data = repo / "docs/data/traffic_v2"
    external.mkdir(parents=True)
    data.mkdir(parents=True)
    write_json(external / source_gate.FREEZE_NAME, {"schema": "test"})
    write_json(external / source_gate.RUN_MANIFEST_NAME, {"schema": "test"})
    for name in source_gate.EXPECTED_TEXT_ARTIFACTS:
        (data / name).write_text(name, encoding="utf-8")
    write_json(
        external / source_gate.PACKAGE_INDEX_NAME,
        {
            "active_files": [
                {
                    "bytes": 1,
                    "partition": "ACTIVE_BOUND_PACKAGE",
                    "path": "raw/../../outside.bin",
                    "sha256": "0" * 64,
                }
            ],
            "archive_files": [],
            "index_scope_excludes": sorted(
                (source_gate.OPERATION_LOCK_NAME, source_gate.PACKAGE_INDEX_NAME)
            ),
            "repository_artifacts": [],
            "schema": "questflight.traffic-v2-geospatial-outer-package-index.v1",
            "source_freeze_sha256": source_gate.sha256_file(
                external / source_gate.FREEZE_NAME
            ),
            "status": "PASS_ALL_FILES_BOUND_OR_ARCHIVED",
        },
    )
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    problems: list[str] = []
    source_gate.validate_outer_package_index(registry(), external, data, problems)
    assert any("outer package index exact validation failed" in problem for problem in problems)


def test_outer_package_index_binds_clean_pre_acquisition_inventory(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    repo = tmp_path / "repo"
    data = repo / "docs/data/traffic_v2"
    external = tmp_path / "external"
    data.mkdir(parents=True)
    external.mkdir()
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    inventory = external / "product_source_inventory.json"
    write_json(inventory, {"schema": "questflight.traffic-v2-geospatial-inventory.v1"})
    for name in source_gate.EXPECTED_TEXT_ARTIFACTS:
        write_json(data / name, {})
    write_json(
        external / source_gate.FREEZE_NAME,
        {"pre_acquisition_inventory": source_gate.file_record(inventory, external)},
    )
    write_json(external / source_gate.RUN_MANIFEST_NAME, {})
    source_gate.write_outer_package_index(registry(), external, data)
    index = json.loads(
        (external / source_gate.PACKAGE_INDEX_NAME).read_text(encoding="utf-8")
    )
    active = {item["path"] for item in index["active_files"]}
    assert "product_source_inventory.json" in active
    problems: list[str] = []
    source_gate.validate_outer_package_index(registry(), external, data, problems)
    assert problems == []


def test_outer_package_index_rejects_missing_manifest_referenced_file(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    repo = tmp_path / "repo"
    data = repo / "docs/data/traffic_v2"
    external = tmp_path / "external"
    data.mkdir(parents=True)
    external.mkdir()
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    missing_record = {
        "bytes": 10,
        "path": "intermediates/missing.json",
        "sha256": "a" * 64,
    }
    for name in source_gate.EXPECTED_TEXT_ARTIFACTS:
        if name == "traffic_source_coverage.json":
            write_json(data / name, {"missing_intermediate": missing_record})
        else:
            (data / name).write_text(name, encoding="utf-8")
    write_json(external / source_gate.FREEZE_NAME, {"schema": "test"})
    write_json(external / source_gate.RUN_MANIFEST_NAME, {"schema": "test"})
    with pytest.raises(source_gate.GateError, match="missing files referenced"):
        source_gate.write_outer_package_index(registry(), external, data)
    active_paths = [source_gate.FREEZE_NAME, source_gate.RUN_MANIFEST_NAME]
    index = {
        "active_files": [
            {
                **source_gate.file_record(external / relative, external),
                "partition": "ACTIVE_BOUND_PACKAGE",
            }
            for relative in active_paths
        ],
        "archive_files": [],
        "index_scope_excludes": [source_gate.PACKAGE_INDEX_NAME],
        "repository_artifacts": [
            {
                "bytes": (data / name).stat().st_size,
                "path": (data / name).relative_to(repo).as_posix(),
                "sha256": source_gate.sha256_file(data / name),
            }
            for name in source_gate.EXPECTED_TEXT_ARTIFACTS
        ],
        "schema": "questflight.traffic-v2-geospatial-outer-package-index.v1",
        "source_freeze_sha256": source_gate.sha256_file(external / source_gate.FREEZE_NAME),
        "status": "PASS_ALL_FILES_BOUND_OR_ARCHIVED",
    }
    write_json(external / source_gate.PACKAGE_INDEX_NAME, index)
    problems: list[str] = []
    source_gate.validate_outer_package_index(registry(), external, data, problems)
    assert any(
        "missing files referenced" in problem
        and "intermediates/missing.json" in problem
        for problem in problems
    )


def test_outer_package_index_is_exact_canonical_live_value(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    repo = tmp_path / "repo"
    data = repo / "docs/data/traffic_v2"
    external = tmp_path / "external"
    data.mkdir(parents=True)
    external.mkdir()
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    ledger = external / "intermediates/ledger.jsonl"
    ledger.parent.mkdir()
    ledger.write_text('{"id":"one"}\n', encoding="utf-8")
    for name in source_gate.EXPECTED_TEXT_ARTIFACTS:
        if name == "traffic_source_coverage.json":
            write_json(data / name, {"ledger": source_gate.file_record(ledger, external)})
        else:
            (data / name).write_text(name, encoding="utf-8")
    write_json(external / source_gate.FREEZE_NAME, {"schema": "test"})
    write_json(external / source_gate.RUN_MANIFEST_NAME, {"schema": "test"})
    source_gate.write_outer_package_index(registry(), external, data)
    index_path = external / source_gate.PACKAGE_INDEX_NAME
    canonical = index_path.read_bytes()
    expected = source_gate.expected_outer_package_index(registry(), external, data)
    assert canonical == source_gate.canonical_json_bytes(expected)

    variants: list[bytes] = []
    value = json.loads(canonical)
    changed = deepcopy(value)
    changed["active_files"][0]["bytes"] = str(changed["active_files"][0]["bytes"])
    variants.append(source_gate.canonical_json_bytes(changed))
    changed = deepcopy(value)
    changed["unexpected"] = "forbidden"
    variants.append(source_gate.canonical_json_bytes(changed))
    changed = deepcopy(value)
    changed["active_files"][0]["unexpected"] = True
    variants.append(source_gate.canonical_json_bytes(changed))
    changed = deepcopy(value)
    changed["repository_artifacts"].append(
        {"bytes": 0, "path": "fabricated.json", "sha256": "0" * 64}
    )
    variants.append(source_gate.canonical_json_bytes(changed))
    changed = deepcopy(value)
    changed["active_files"] = list(reversed(changed["active_files"]))
    variants.append(source_gate.canonical_json_bytes(changed))
    variants.append(json.dumps(value, separators=(",", ":")).encode("utf-8"))

    for payload in variants:
        index_path.write_bytes(payload)
        problems: list[str] = []
        source_gate.validate_outer_package_index(registry(), external, data, problems)
        assert problems, payload[:80]
    index_path.write_bytes(canonical)
    problems = []
    source_gate.validate_outer_package_index(registry(), external, data, problems)
    assert problems == []

    ledger.write_text('{"id":"tampered"}\n', encoding="utf-8")
    problems = []
    source_gate.validate_outer_package_index(registry(), external, data, problems)
    assert any("hash" in problem or "referenced" in problem for problem in problems)


def test_outer_package_index_revalidates_rebuild_derivative_records(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    repo = tmp_path / "repo"
    data = repo / "docs/data/traffic_v2"
    external = tmp_path / "external"
    rebuild = external / "rebuild_check"
    data.mkdir(parents=True)
    (rebuild / "data").mkdir(parents=True)
    (rebuild / "evidence").mkdir()
    (rebuild / "work/intermediates").mkdir(parents=True)
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    monkeypatch.setattr(
        source_gate, "_validate_staged_compile_set", lambda *args, **kwargs: None
    )
    monkeypatch.setattr(
        source_gate,
        "validate_frozen_naip_authority_closure",
        lambda *_args, **_kwargs: None,
    )

    derivative = rebuild / "work/intermediates/reference_ledger.jsonl"
    derivative.write_text('{"reference_observation_id":"r1"}\n', encoding="utf-8")
    rebuilt_coverage = {
        "reference_ledger": source_gate.file_record(derivative, rebuild / "work")
    }
    source_gate.atomic_write_json(
        rebuild / "data/traffic_source_coverage.json", rebuilt_coverage
    )
    for name in source_gate.EXPECTED_TEXT_ARTIFACTS[1:]:
        (rebuild / "data" / name).write_text(name, encoding="utf-8")
    (rebuild / f"evidence/{source_gate.EXPECTED_EXTERNAL_ARTIFACT}").write_bytes(b"png")
    source_gate.atomic_write_json(rebuild / source_gate.RUN_MANIFEST_NAME, {"outputs": {}})
    source_gate.atomic_write_json(
        rebuild / "rebuild_check.json",
        {
            "mismatches": [],
            "rebuilt_outputs": {},
            "schema": "questflight.traffic-v2-geospatial-rebuild-check.v1",
            "status": "PASS",
        },
    )

    for name in source_gate.EXPECTED_TEXT_ARTIFACTS:
        if name == "traffic_source_coverage.json":
            write_json(data / name, {})
        else:
            (data / name).write_text(name, encoding="utf-8")
    candidate_root = external / "approval_candidate"
    candidate_root.mkdir()
    artifact_hashes: dict[str, str] = {}
    for name in source_gate.EXPECTED_TEXT_ARTIFACTS:
        (candidate_root / name).write_bytes((data / name).read_bytes())
        artifact_hashes[name] = source_gate.sha256_file(candidate_root / name)
    (candidate_root / source_gate.EXPECTED_EXTERNAL_ARTIFACT).write_bytes(b"png")
    artifact_hashes[source_gate.EXPECTED_EXTERNAL_ARTIFACT] = source_gate.sha256_file(
        candidate_root / source_gate.EXPECTED_EXTERNAL_ARTIFACT
    )
    source_gate.atomic_write_json(
        candidate_root / "candidate_manifest.json",
        {
            "artifact_sha256": artifact_hashes,
            "schema": "questflight.traffic-v2-geospatial-approval-candidate.v1",
        },
    )
    write_json(external / source_gate.FREEZE_NAME, {"schema": "test"})
    write_json(external / source_gate.RUN_MANIFEST_NAME, {"schema": "test"})

    source_gate.write_outer_package_index(registry(), external, data)
    problems: list[str] = []
    source_gate.validate_outer_package_index(registry(), external, data, problems)
    assert problems == []
    derivative.write_text('{"reference_observation_id":"forged"}\n', encoding="utf-8")
    problems = []
    source_gate.validate_outer_package_index(registry(), external, data, problems)
    assert any("Staged file record bytes/hash mismatch" in problem for problem in problems)


def test_canonical_json_and_coverage_render_are_deterministic(tmp_path: Path) -> None:
    assert source_gate.canonical_json_bytes({"b": 2, "a": 1}) == b'{\n  "a": 1,\n  "b": 2\n}\n'
    line = {
        "canonical_segment_id": "canonical:test:1:0:0",
        "confidence": "TEST",
        "eligibility_status_or_exclusion_reason": "ELIGIBLE",
        "geometry": {"coordinates": [[-1000.0, 0.0], [1000.0, 0.0]], "type": "LineString"},
        "lane_count": 2,
        "lane_count_authority": "test:1:0:lanes",
        "license_partition": "TEST",
        "represented_source_disposition": "CANONICAL_GEOMETRY",
        "road_class": "primary",
        "source_id": "test",
        "source_precedence_decision": "SELECTED_OFFICIAL_AUTHORITY",
        "source_record_id": "1",
        "source_record_part": 0,
    }
    canonical = tmp_path / "canonical.geojsonl"
    write_jsonl(canonical, [line])
    first = tmp_path / "first.png"
    second = tmp_path / "second.png"
    source_gate.render_coverage_png(registry(), canonical, first, [], "SOURCE_EVIDENCE_CANDIDATE")
    source_gate.render_coverage_png(registry(), canonical, second, [], "SOURCE_EVIDENCE_CANDIDATE")
    assert first.read_bytes() == second.read_bytes()


def test_freeze_hash_walker_checks_external_and_repository_records(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    external = tmp_path / "external"
    repo = tmp_path / "repo"
    raw = external / "raw/source/payload.json"
    raw.parent.mkdir(parents=True)
    raw.write_bytes(b"{}")
    repository_file = repo / "QuestFlight/not-external.bin"
    repository_file.parent.mkdir(parents=True)
    repository_file.write_bytes(b"repository")
    monkeypatch.setattr(source_gate, "REPO_ROOT", repo)
    value = {
        "external": source_gate.file_record(raw, external),
        "registry": source_gate.repository_file_record(repository_file),
    }
    problems: list[str] = []
    source_gate.verify_freeze_files(external, value, problems)
    assert problems == []
    raw.write_bytes(b"changed")
    source_gate.verify_freeze_files(external, value, problems)
    assert problems == [
        "byte mismatch: raw/source/payload.json",
        "SHA-256 mismatch: raw/source/payload.json",
    ]

    escaped = {"bytes": 0, "path": "../outside.json", "sha256": "0" * 64}
    escaped_problems: list[str] = []
    source_gate.verify_freeze_files(external, escaped, escaped_problems)
    assert escaped_problems == [
        "Frozen file record path is not a canonical external relative POSIX path"
    ]

    confused_external = external / "QuestFlight/not-external.bin"
    confused_external.parent.mkdir(parents=True)
    confused_external.write_bytes(b"different-unverified-external-bytes")
    confused = {
        "sources": [
            {
                "raw": source_gate.repository_file_record(repository_file),
            }
        ]
    }
    confused_problems: list[str] = []
    source_gate.verify_freeze_files(external, confused, confused_problems)
    assert confused_problems == [
        "Frozen file record is not under an approved external record root"
    ]


def test_document_authorities_and_payload_checks_are_explicit() -> None:
    value = registry()
    assert {source["id"] for source in value["document_sources"]} == {
        "city_traffic_count_documents",
        "usda_naip_current_colorado_orthophoto_metadata",
        "usgs_3dep_bridge_metadata",
    }
    source_gate.validate_document_payload(
        {"id": "sample", "magic_ascii": "%PDF", "minimum_bytes": 8, "required_contains": ["table"]},
        b"%PDF table",
    )
    with pytest.raises(source_gate.GateError, match="signature"):
        source_gate.validate_document_payload(
            {"id": "sample", "magic_ascii": "%PDF", "minimum_bytes": 1}, b"not-pdf"
        )


def naip_service_fixture(
    *, colorado_year: int = 2021, padding_bytes: int = 0
) -> bytes:
    category_domain = {
        "codedValues": [
            {"code": 1, "name": "Primary"},
            {"code": 2, "name": "Overview"},
        ]
    }
    value = {
        "advancedQueryCapabilities": {
            "supportsOrderBy": True,
            "supportsPagination": True,
            "supportsSqlExpression": True,
        },
        "capabilities": "Image,Metadata,Catalog,Mensuration",
        "copyrightText": (
            "These datasets were provided by the USDA's Farm Production and Conservation "
            "office (FPAC) and are served by the U.S. Forest Service in cooperation with "
            "the Geospatial Platform (GeoPlatform)."
        ),
        "currentVersion": 12,
        "description": (
            "This data set contains imagery from the National Agriculture Imagery Program "
            "(NAIP), provided by the USDA's Farm Production and Conservation office (FPAC). "
            f"CO and UT were acquired in {colorado_year}."
        ),
        "fields": [
            {"name": "objectid", "type": "esriFieldTypeOID"},
            {"name": "name", "type": "esriFieldTypeString"},
            {"domain": category_domain, "name": "category", "type": "esriFieldTypeInteger"},
            {"name": "year", "type": "esriFieldTypeSmallInteger"},
            {"name": "state", "type": "esriFieldTypeString"},
        ],
        "maxRecordCount": 1000,
        "name": "NAIP/NAIP_plus",
        "objectIdField": "objectid",
    }
    if padding_bytes:
        value["padding"] = "x" * padding_bytes
    return source_gate.canonical_json_bytes(value)


def naip_latest_coverage_fixture(*, colorado_year: int = 2021) -> bytes:
    suffix = str(colorado_year)[-2:]
    return (
        "<html><body><p>Quarter quad tile names of the most recent year available.</p>"
        "<h2>NAIP CONUS 2021-2022</h2><table>"
        "<tr><td><strong>Colorado</strong></td>"
        f'<td><a href="/sites/default/files/2024-10/co_naip{suffix}qq-1.zip">'
        f"{source_gate.NAIP_EXPECTED_ANCHOR_LABEL}</a></td>"
        f"<td><a href='co_naip{suffix}seam.zip'>Colorado seams</a></td></tr>"
        "</table></body></html>"
    ).encode("utf-8")


def naip_footprint_fixture(
    bounds: list[float],
    *,
    category: int = 1,
    compact: bool = False,
    partial: bool = False,
    year: int = 2021,
) -> bytes:
    xmin, ymin, xmax, ymax = map(float, bounds)
    covered_xmax = (xmin + xmax) / 2.0 if partial else xmax + 0.01
    ring = [
        [xmin - 0.01, ymin - 0.01],
        [covered_xmax, ymin - 0.01],
        [covered_xmax, ymax + 0.01],
        [xmin - 0.01, ymax + 0.01],
        [xmin - 0.01, ymin - 0.01],
    ]
    primary_ids = list(source_gate.NAIP_EXPECTED_OBJECT_IDS[:-1])
    primaries = [
        {
            "attributes": {
                "category": category,
                "name": f"fixture_primary_{object_id}",
                "objectid": object_id,
                "state": "CO",
                "year": year,
            },
            "geometry": {
                "rings": [deepcopy(ring)],
                "spatialReference": {"latestWkid": 4326, "wkid": 4326},
            },
        }
        for object_id in primary_ids
    ]
    overview = {
        "attributes": {
            "category": 2,
            "name": f"CRF_CO_{year}",
            "objectid": source_gate.NAIP_EXPECTED_OVERVIEW_ID,
            "state": "CO",
            "year": year,
        },
        "geometry": {
            "rings": [ring],
            "spatialReference": {"latestWkid": 4326, "wkid": 4326},
        },
    }
    value = {
            "features": [*primaries, overview],
            "fields": [
                {"name": "objectid", "type": "esriFieldTypeOID"},
                {"name": "name", "type": "esriFieldTypeString"},
                {"name": "category", "type": "esriFieldTypeInteger"},
                {"name": "year", "type": "esriFieldTypeSmallInteger"},
                {"name": "state", "type": "esriFieldTypeString"},
                {"name": "shape", "type": "esriFieldTypeGeometry"},
            ],
            "geometryType": "esriGeometryPolygon",
            "objectIdFieldName": "objectid",
            "spatialReference": {"latestWkid": 4326, "wkid": 4326},
        }
    if compact:
        return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode(
            "utf-8"
        )
    return source_gate.canonical_json_bytes(value)


def naip_ids_fixture(object_ids: list[int] | None = None) -> bytes:
    return source_gate.canonical_json_bytes(
        {
            "objectIdFieldName": "objectid",
            "objectIds": (
                list(source_gate.NAIP_EXPECTED_OBJECT_IDS)
                if object_ids is None
                else object_ids
            ),
        }
    )


def bind_naip_test_contract(contract: dict, footprint_payload: bytes) -> dict:
    """Bind deterministic synthetic geometry while retaining the official ID inventory."""

    bound = deepcopy(contract)
    value = json.loads(footprint_payload)
    features = value["features"]
    primary_features = [
        feature for feature in features if feature["attributes"]["category"] == 1
    ]
    primary_ids = [feature["attributes"]["objectid"] for feature in primary_features]
    overview_feature = next(
        feature for feature in features if feature["attributes"]["category"] == 2
    )
    dispositions = [
        {
            "category": feature["attributes"]["category"],
            "disposition": (
                "ACCEPTED_CURRENT_PRIMARY_FOOTPRINT"
                if feature["attributes"]["category"] == 1
                else "EXCLUDED_SERVICE_OVERVIEW"
            ),
            "name": feature["attributes"]["name"],
            "objectid": feature["attributes"]["objectid"],
            "state": feature["attributes"]["state"],
            "year": feature["attributes"]["year"],
        }
        for feature in features
    ]
    coordinates = [
        point
        for feature in primary_features
        for ring in feature["geometry"]["rings"]
        for point in ring
    ]
    bound.update(
        {
            "coverage_union_bounds_wgs84_xyxy": [
                min(point[0] for point in coordinates),
                min(point[1] for point in coordinates),
                max(point[0] for point in coordinates),
                max(point[1] for point in coordinates),
            ],
            "disposition_inventory_sha256": source_gate.sha256_bytes(
                source_gate.canonical_json_bytes(dispositions)
            ),
            "geometry_inventory_sha256": source_gate.naip_geometry_inventory_sha256(
                features
            ),
            "expected_object_ids": list(source_gate.NAIP_EXPECTED_OBJECT_IDS),
            "expected_overview_id": overview_feature["attributes"]["objectid"],
            "expected_overview_name": overview_feature["attributes"]["name"],
            "expected_query_feature_count": len(features),
            "primary_object_id_inventory_sha256": source_gate.sha256_bytes(
                source_gate.canonical_json_bytes(primary_ids)
            ),
        }
    )
    return bound


def naip_bound_test_payload() -> tuple[dict, bytes]:
    value = registry()
    base = next(
        source
        for source in value["document_sources"]
        if source["id"] == source_gate.NAIP_AUTHORITY_SOURCE_ID
    )["orthophoto_authority_contract"]
    payload = naip_footprint_fixture(base["coverage_bbox_wgs84_xyxy"])
    return bind_naip_test_contract(base, payload), payload


def frozen_get_receipt(
    external: Path,
    request_relative: str,
    response_relative: str,
    url: str,
    payload: bytes,
    *,
    timeout_seconds: int = 180,
) -> dict:
    request_path = external / request_relative
    response_path = external / response_relative
    request_path.parent.mkdir(parents=True, exist_ok=True)
    response_path.parent.mkdir(parents=True, exist_ok=True)
    write_json(
        request_path,
        source_gate.request_record(
            url=url,
            method="GET",
            body=None,
            headers={
                "Accept": "application/json,text/html;q=0.9,*/*;q=0.1",
                "User-Agent": source_gate.USER_AGENT,
            },
            timeout_seconds=timeout_seconds,
        ),
    )
    response_path.write_bytes(payload)
    return {
        "request": source_gate.file_record(request_path, external),
        "response": source_gate.file_record(response_path, external),
        "transport": {"final_url": url, "status": 200},
        "url": url,
    }


def naip_authority_fixture(
    tmp_path: Path,
    *,
    document_payloads: dict[str, bytes] | None = None,
    finalize: bool = True,
    license_payload: bytes | None = None,
    production_document_contract: bool = False,
    query_payload: bytes | None = None,
) -> tuple[dict, dict, dict, dict]:
    value = deepcopy(registry())
    source_index = next(
        index
        for index, source in enumerate(value["document_sources"])
        if source["id"] == source_gate.NAIP_AUTHORITY_SOURCE_ID
    )
    configured = deepcopy(value["document_sources"][source_index])
    if not production_document_contract:
        for document in configured["documents"]:
            document["minimum_bytes"] = 1
            document["required_contains"] = []
    base_contract = configured["orthophoto_authority_contract"]
    good_footprints = naip_footprint_fixture(
        base_contract["coverage_bbox_wgs84_xyxy"],
        compact=production_document_contract,
    )
    if production_document_contract and len(good_footprints) < 20001:
        good_footprints += b" " * (20001 - len(good_footprints))
    configured["orthophoto_authority_contract"] = bind_naip_test_contract(
        base_contract, good_footprints
    )
    archive_bytes = (
        base_contract["archive_bytes"] if production_document_contract else 64
    )
    archive_payload = b"PK\x03\x04" + (b"x" * (archive_bytes - 4))
    configured["orthophoto_authority_contract"].update(
        {
            "archive_bytes": len(archive_payload),
            "archive_sha256": source_gate.sha256_bytes(archive_payload),
        }
    )
    value["document_sources"][source_index] = configured
    latest_payload = naip_latest_coverage_fixture()
    if production_document_contract:
        latest_payload += b"<!--" + (b"x" * 41000) + b"-->"
    payload_by_id = {
        source_gate.NAIP_ARCHIVE_DOCUMENT_ID: archive_payload,
        source_gate.NAIP_LATEST_COVERAGE_DOCUMENT_ID: latest_payload,
        source_gate.NAIP_BOUNDED_IDS_DOCUMENT_ID: naip_ids_fixture(),
        source_gate.NAIP_BOUNDED_FOOTPRINTS_DOCUMENT_ID: query_payload or good_footprints,
    }
    payload_by_id.update(document_payloads or {})
    frozen_documents: list[dict] = []
    for document in configured["documents"]:
        expected_url = source_gate.encoded_request_url(
            document["url"], document.get("params")
        )
        receipt = frozen_get_receipt(
            tmp_path,
            (
                f"requests/{source_gate.NAIP_AUTHORITY_SOURCE_ID}/"
                f"document_{len(frozen_documents):03d}_{document['id']}.json"
            ),
            f"metadata/{source_gate.NAIP_AUTHORITY_SOURCE_ID}/{document['path']}",
            expected_url,
            payload_by_id[document["id"]],
            timeout_seconds=document["timeout_seconds"],
        )
        frozen_documents.append(
            {
                "id": document["id"],
                "media_type": document["media_type"],
                "receipt": receipt,
                "url": expected_url,
            }
        )
    if license_payload is None:
        license_payload = (
            "<html><body>Aerial photography and digital orthophotography acquired by the USDA "
            "are in the public domain.</body></html>"
        ).encode("utf-8")
    license_receipt = frozen_get_receipt(
        tmp_path,
        f"requests/licenses/{source_gate.NAIP_LICENSE_SNAPSHOT_ID}.json",
        f"metadata/licenses/{source_gate.NAIP_LICENSE_SNAPSHOT_ID}.html",
        source_gate.NAIP_LICENSE_URL,
        license_payload,
        timeout_seconds=90,
    )
    licenses = {
        source_gate.NAIP_LICENSE_SNAPSHOT_ID: {
            "receipt": license_receipt,
            "required_text_present": True,
        }
    }
    frozen_source = {
        "documents": frozen_documents,
        "endpoint": source_gate.encoded_request_url(
            configured["documents"][0]["url"], configured["documents"][0].get("params")
        ),
        "feature_count": None,
        "id": source_gate.NAIP_AUTHORITY_SOURCE_ID,
        "license_partition": "US-Government-Work-Public-Domain",
        "metadata_only": True,
        "required": True,
        "role": "orthophoto_ambiguity_resolution_only",
        "source_kind": "document_authority",
        "status": "FROZEN_METADATA",
    }
    if not finalize:
        return value, configured, frozen_source, licenses
    frozen_source["authority_validation"] = source_gate.validate_naip_authority_payloads(
        value, configured, frozen_source, tmp_path, licenses
    )
    source_receipt_path = (
        tmp_path / "receipts" / f"{source_gate.NAIP_AUTHORITY_SOURCE_ID}.json"
    )
    source_receipt_path.parent.mkdir(parents=True, exist_ok=True)
    source_receipt_path.write_bytes(source_gate.canonical_json_bytes(frozen_source))
    frozen_source["receipt_file"] = source_gate.file_record(
        source_receipt_path, tmp_path
    )
    return value, configured, frozen_source, licenses


def test_naip_registry_contract_is_exact_metadata_only_and_non_geometric() -> None:
    value = registry()
    source = next(
        item
        for item in value["document_sources"]
        if item["id"] == source_gate.NAIP_AUTHORITY_SOURCE_ID
    )
    source_gate.validate_naip_registry_contract(value)
    assert source["metadata_only"] is True
    assert source["role"] == "orthophoto_ambiguity_resolution_only"
    assert source["license_snapshot_ids"] == [source_gate.NAIP_LICENSE_SNAPSHOT_ID]
    assert source["orthophoto_authority_contract"]["allowed_use"] == (
        "VISUAL_AMBIGUITY_RESOLUTION_ONLY"
    )
    assert source["orthophoto_authority_contract"]["forbidden_uses"] == [
        "LANE_COUNT_AUTHORITY",
        "LANE_DIRECTION_AUTHORITY",
        "ROAD_CENTERLINE_OR_GEOMETRY_CREATION",
    ]
    contract = source["orthophoto_authority_contract"]
    assert contract["archive_document_id"] == source_gate.NAIP_ARCHIVE_DOCUMENT_ID
    assert contract["archive_bytes"] == source_gate.NAIP_ARCHIVE_BYTES
    assert contract["archive_sha256"] == source_gate.NAIP_ARCHIVE_SHA256
    assert contract["expected_primary_footprint_count"] == 25
    assert contract["expected_statewide_footprint_count"] == 7423
    assert contract["expected_selected_record_numbers"] == list(
        source_gate.NAIP_EXPECTED_SELECTED_RECORDS
    )
    assert contract["expected_raw_href"] == source_gate.NAIP_EXPECTED_RAW_HREF
    assert contract["expected_resolved_href"] == (
        source_gate.NAIP_EXPECTED_RESOLVED_HREF
    )
    assert contract["expected_anchor_label"] == (
        source_gate.NAIP_EXPECTED_ANCHOR_LABEL
    )
    assert contract["selected_record_inventory_sha256"] == (
        source_gate.NAIP_SELECTED_RECORD_INVENTORY_SHA256
    )
    assert contract["coverage_union_bounds_wgs84_xyxy"] == list(
        source_gate.NAIP_EXPECTED_PRIMARY_UNION_BOUNDS_WGS84_XYXY
    )
    authority_ids = {
        item["source_id"]
        for item in value["authority_model"]["canonical_centerline_precedence"]
    } | {
        item["source_id"] for item in value["authority_model"]["lane_evidence"]
    } | {
        item["source_id"] for item in value["authority_model"]["reference_sources"]
    }
    assert source_gate.NAIP_AUTHORITY_SOURCE_ID not in authority_ids
    assert source_gate.NAIP_AUTHORITY_SOURCE_ID not in value["authority_model"]["class_evidence"]
    assert source_gate.NAIP_AUTHORITY_SOURCE_ID not in value["authority_model"]["road_kind_evidence"]
    rejected = {item["id"] for item in value["rejected_sources"]}
    assert {
        "city_2023_naip_full_bounds_authority",
        "city_2024_orthophoto",
        "drcog_current_orthophoto_purchase",
    }.issubset(rejected)


def test_naip_registry_urls_and_query_are_exact_official_primary_metadata() -> None:
    value = registry()
    source = next(
        item
        for item in value["document_sources"]
        if item["id"] == source_gate.NAIP_AUTHORITY_SOURCE_ID
    )
    assert source["documents"] == source_gate.expected_naip_documents(value)
    documents = {document["id"]: document for document in source["documents"]}
    assert set(documents) == {
        source_gate.NAIP_ARCHIVE_DOCUMENT_ID,
        source_gate.NAIP_LATEST_COVERAGE_DOCUMENT_ID,
    }
    assert documents[source_gate.NAIP_ARCHIVE_DOCUMENT_ID]["url"] == (
        source_gate.NAIP_ARCHIVE_URL
    )
    assert documents[source_gate.NAIP_ARCHIVE_DOCUMENT_ID]["media_type"] == (
        "application/zip"
    )
    assert documents[source_gate.NAIP_ARCHIVE_DOCUMENT_ID]["params"] is None
    assert documents[source_gate.NAIP_LATEST_COVERAGE_DOCUMENT_ID]["url"] == (
        source_gate.NAIP_COVERAGE_PAGE_URL
    )
    assert source_gate.encoded_request_url(
        documents[source_gate.NAIP_ARCHIVE_DOCUMENT_ID]["url"],
        documents[source_gate.NAIP_ARCHIVE_DOCUMENT_ID]["params"],
    ) == source_gate.NAIP_ARCHIVE_URL
    assert all(
        document["media_type"] in ("application/zip", "text/html")
        for document in source["documents"]
    )
    assert all(
        token not in source_gate.encoded_request_url(document["url"], document.get("params")).lower()
        for document in source["documents"]
        for token in ("exportimage", "download_url", ".jp2", ".tif")
    )


def test_naip_registry_contract_mutations_fail_closed() -> None:
    mutations: list[dict] = []
    wrong_role = deepcopy(registry())
    next(
        source
        for source in wrong_role["document_sources"]
        if source["id"] == source_gate.NAIP_AUTHORITY_SOURCE_ID
    )["role"] = "road_authority"
    mutations.append(wrong_role)
    wrong_query = deepcopy(registry())
    next(
        source
        for source in wrong_query["document_sources"]
        if source["id"] == source_gate.NAIP_AUTHORITY_SOURCE_ID
    )["documents"][0]["params"] = {"geometry": "-105,39,-104,40"}
    mutations.append(wrong_query)
    wrong_license = deepcopy(registry())
    next(
        snapshot
        for snapshot in wrong_license["license_snapshots"]
        if snapshot["id"] == source_gate.NAIP_LICENSE_SNAPSHOT_ID
    )["url"] = "https://example.test/license"
    mutations.append(wrong_license)
    wrong_bounds = deepcopy(registry())
    wrong_bounds["arcgis_query"]["bbox_wgs84_xyxy"][0] += 0.01
    next(
        source
        for source in wrong_bounds["document_sources"]
        if source["id"] == source_gate.NAIP_AUTHORITY_SOURCE_ID
    )["orthophoto_authority_contract"]["coverage_bbox_wgs84_xyxy"][0] += 0.01
    mutations.append(wrong_bounds)
    geometric_authority = deepcopy(registry())
    geometric_authority["authority_model"]["canonical_centerline_precedence"].append(
        {"priority": 999, "source_id": source_gate.NAIP_AUTHORITY_SOURCE_ID}
    )
    mutations.append(geometric_authority)
    reordered = deepcopy(registry())
    reordered_source = next(
        source
        for source in reordered["document_sources"]
        if source["id"] == source_gate.NAIP_AUTHORITY_SOURCE_ID
    )
    reordered_source["documents"][0], reordered_source["documents"][1] = (
        reordered_source["documents"][1],
        reordered_source["documents"][0],
    )
    mutations.append(reordered)
    for field, changed in (
        ("id", "changed_document_id"),
        ("media_type", "application/octet-stream"),
        ("minimum_bytes", 1),
        ("params", {"f": "json"}),
        ("path", "renamed.json"),
        ("required_contains", []),
        ("timeout_seconds", 181),
        ("url", "https://example.test/query"),
    ):
        changed_document = deepcopy(registry())
        target = next(
            source
            for source in changed_document["document_sources"]
            if source["id"] == source_gate.NAIP_AUTHORITY_SOURCE_ID
        )["documents"][1]
        target[field] = changed
        mutations.append(changed_document)
    extra_declaration_field = deepcopy(registry())
    next(
        source
        for source in extra_declaration_field["document_sources"]
        if source["id"] == source_gate.NAIP_AUTHORITY_SOURCE_ID
    )["documents"][0]["unexpected"] = True
    mutations.append(extra_declaration_field)
    changed_pin = deepcopy(registry())
    next(
        source
        for source in changed_pin["document_sources"]
        if source["id"] == source_gate.NAIP_AUTHORITY_SOURCE_ID
    )["orthophoto_authority_contract"]["geometry_inventory_sha256"] = "0" * 64
    mutations.append(changed_pin)
    for field, changed in (
        ("expected_anchor_label", "Shapefile (zip, 443KB)"),
        (
            "expected_raw_href",
            "/sites/default/files/2024-10/co_naip21qq-1.zip?changed=true",
        ),
        (
            "expected_resolved_href",
            "https://www.fpacbc.usda.gov/sites/default/files/2024-10/"
            "co_naip21qq-1.zip?changed=true",
        ),
    ):
        changed_anchor_contract = deepcopy(registry())
        next(
            source
            for source in changed_anchor_contract["document_sources"]
            if source["id"] == source_gate.NAIP_AUTHORITY_SOURCE_ID
        )["orthophoto_authority_contract"][field] = changed
        mutations.append(changed_anchor_contract)
    for mutated in mutations:
        with pytest.raises(source_gate.GateError):
            source_gate.validate_naip_registry_contract(mutated)


def test_naip_service_and_latest_year_metadata_fail_closed_when_old() -> None:
    with pytest.raises(source_gate.GateError, match="current Colorado"):
        source_gate.validate_naip_service_metadata(
            naip_service_fixture(colorado_year=2020), 2021
        )
    with pytest.raises(source_gate.GateError, match="Colorado quarter-quad anchor"):
        source_gate.validate_naip_latest_coverage_page(
            naip_latest_coverage_fixture(colorado_year=2020), 2021
        )


def test_naip_latest_coverage_ignores_comments_and_rejects_stale_or_conflicting_hrefs() -> None:
    stale_actual_current_commented = (
        "<html><body><p>most recent year available</p><h2>NAIP CONUS 2021-2022</h2>"
        "<!-- <tr><td><strong>Colorado</strong></td>"
        "<td><a href='co_naip21qq-1.zip'>current</a></td></tr> -->"
        "<table><tr><td><strong>Colorado</strong></td>"
        "<td><a href='co_naip19qq-1.zip'>stale</a></td></tr></table></body></html>"
    ).encode("utf-8")
    conflicting = naip_latest_coverage_fixture().replace(
        b"co_naip21seam.zip",
        b"co_naip19qq-1.zip",
    )
    changed_case = naip_latest_coverage_fixture().replace(
        b"co_naip21qq-1.zip", b"CO_NAIP21QQ-1.ZIP"
    )
    for payload in (stale_actual_current_commented, conflicting, changed_case):
        with pytest.raises(source_gate.GateError, match="quarter-quad"):
            source_gate.validate_naip_latest_coverage_page(payload, 2021)
    active_current_commented_stale = naip_latest_coverage_fixture().replace(
        b"<table>",
        b"<!-- <tr><td><strong>Colorado</strong></td><td>"
        b"<a href='co_naip19qq-1.zip'>stale</a></td></tr> --><table>",
    )
    assert source_gate.validate_naip_latest_coverage_page(
        active_current_commented_stale, 2021
    )["colorado_quarter_quad_raw_href"] == source_gate.NAIP_EXPECTED_RAW_HREF


def test_naip_latest_coverage_normalizes_only_substantive_label_whitespace() -> None:
    payload = naip_latest_coverage_fixture().replace(
        source_gate.NAIP_EXPECTED_ANCHOR_LABEL.encode("utf-8"),
        b"  Shapefile \r\n  (zip,\t442KB)  ",
    )
    summary = source_gate.validate_naip_latest_coverage_page(payload, 2021)
    assert summary["colorado_quarter_quad_anchor_label"] == (
        source_gate.NAIP_EXPECTED_ANCHOR_LABEL
    )


def assert_production_naip_document_sizes(
    configured: dict, frozen_source: dict, external: Path
) -> None:
    configured_by_id = {
        document["id"]: document for document in configured["documents"]
    }
    for frozen_document in frozen_source["documents"]:
        document = configured_by_id[frozen_document["id"]]
        response = external / frozen_document["receipt"]["response"]["path"]
        assert response.stat().st_size >= document["minimum_bytes"]


NAIP_TEST_LICENSE_STATEMENT = (
    "Aerial photography and digital orthophotography acquired by the USDA "
    "are in the public domain."
)
NAIP_TEST_COVERAGE_LABELS = (
    "<p>Quarter quad tile names of the most recent year available.</p>"
    "<h2>NAIP CONUS 2021-2022</h2>"
)


def padded_production_html(markup: str) -> bytes:
    payload = (markup + "<!--" + ("x" * 41000) + "-->").encode("utf-8")
    assert len(payload) > 40000
    return payload


def naip_test_current_anchor(*, attributes: str = "") -> str:
    separator = " " if attributes else ""
    return (
        f'<a href="{source_gate.NAIP_EXPECTED_RAW_HREF}"'
        f"{separator}{attributes}>{source_gate.NAIP_EXPECTED_ANCHOR_LABEL}</a>"
    )


def naip_test_current_row(*, anchor_attributes: str = "") -> str:
    return (
        "<tr><td><strong>Colorado</strong></td><td>"
        f"{naip_test_current_anchor(attributes=anchor_attributes)}"
        "<a href='co_naip21seam.zip'>Colorado seams</a></td></tr>"
    )


def naip_coverage_page_with_hidden_authority(
    element: str, attribute: str
) -> bytes:
    row = naip_test_current_row()
    if element == "body":
        markup = (
            f"<html><body {attribute}>{NAIP_TEST_COVERAGE_LABELS}"
            f"<table>{row}</table></body></html>"
        )
    elif element == "div":
        markup = (
            f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<div {attribute}>"
            f"<table>{row}</table></div></body></html>"
        )
    elif element == "table":
        markup = (
            f"<html><body>{NAIP_TEST_COVERAGE_LABELS}"
            f"<table {attribute}>{row}</table></body></html>"
        )
    elif element == "tr":
        markup = (
            f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table>"
            f"<tr {attribute}><td><strong>Colorado</strong></td><td>"
            f"{naip_test_current_anchor()}</td></tr></table></body></html>"
        )
    elif element == "a":
        markup = (
            f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table>"
            f"{naip_test_current_row(anchor_attributes=attribute)}"
            "</table></body></html>"
        )
    else:
        raise AssertionError(f"unsupported coverage fixture element: {element}")
    return padded_production_html(markup)


def naip_license_page_with_hidden_statement(
    element: str, attribute: str
) -> bytes:
    if element == "body":
        markup = f"<html><body {attribute}>{NAIP_TEST_LICENSE_STATEMENT}</body></html>"
    elif element == "div":
        markup = (
            f"<html><body><div {attribute}>{NAIP_TEST_LICENSE_STATEMENT}"
            "</div><p>Visible customer services.</p></body></html>"
        )
    elif element == "table":
        markup = (
            f"<html><body><table {attribute}><tr><td>"
            f"{NAIP_TEST_LICENSE_STATEMENT}</td></tr></table>"
            "<p>Visible customer services.</p></body></html>"
        )
    elif element == "tr":
        markup = (
            f"<html><body><table><tr {attribute}><td>"
            f"{NAIP_TEST_LICENSE_STATEMENT}</td></tr></table>"
            "<p>Visible customer services.</p></body></html>"
        )
    elif element == "a":
        markup = (
            f"<html><body><a href='https://www.fpacbc.usda.gov/' {attribute}>"
            f"{NAIP_TEST_LICENSE_STATEMENT}</a>"
            "<p>Visible customer services.</p></body></html>"
        )
    else:
        raise AssertionError(f"unsupported license fixture element: {element}")
    return padded_production_html(markup)


def assert_production_naip_coverage_rejected(
    tmp_path: Path, payload: bytes, *, match: str | None = None
) -> None:
    value, configured, frozen_source, licenses = naip_authority_fixture(
        tmp_path,
        document_payloads={source_gate.NAIP_LATEST_COVERAGE_DOCUMENT_ID: payload},
        finalize=False,
        production_document_contract=True,
    )
    assert configured["documents"] == source_gate.expected_naip_documents(value)
    assert_production_naip_document_sizes(configured, frozen_source, tmp_path)
    with pytest.raises(source_gate.GateError, match=match):
        source_gate.validate_naip_authority_payloads(
            value, configured, frozen_source, tmp_path, licenses
        )


def assert_production_naip_license_rejected(
    tmp_path: Path, payload: bytes, *, match: str | None = None
) -> None:
    value, configured, frozen_source, licenses = naip_authority_fixture(
        tmp_path,
        finalize=False,
        license_payload=payload,
        production_document_contract=True,
    )
    assert len(payload) > 40000
    assert configured["documents"] == source_gate.expected_naip_documents(value)
    assert_production_naip_document_sizes(configured, frozen_source, tmp_path)
    with pytest.raises(source_gate.GateError, match=match):
        source_gate.validate_naip_authority_payloads(
            value, configured, frozen_source, tmp_path, licenses
        )


def test_naip_structural_visible_body_authorities_pass_production_path(
    tmp_path: Path,
) -> None:
    coverage_payload = padded_production_html(
        naip_latest_coverage_fixture().decode("utf-8")
    )
    license_payload = padded_production_html(
        f"<html><body><main>{NAIP_TEST_LICENSE_STATEMENT}</main></body></html>"
    )
    assert source_gate.structural_visible_html_report(coverage_payload)[
        "structural_errors"
    ] == []
    assert source_gate.structural_visible_html_report(license_payload)[
        "structural_errors"
    ] == []
    value, configured, frozen_source, licenses = naip_authority_fixture(
        tmp_path,
        document_payloads={
            source_gate.NAIP_LATEST_COVERAGE_DOCUMENT_ID: coverage_payload
        },
        finalize=False,
        license_payload=license_payload,
        production_document_contract=True,
    )
    assert_production_naip_document_sizes(configured, frozen_source, tmp_path)
    summary = source_gate.validate_naip_authority_payloads(
        value, configured, frozen_source, tmp_path, licenses
    )
    assert summary["latest_inventory"]["colorado_quarter_quad_raw_href"] == (
        source_gate.NAIP_EXPECTED_RAW_HREF
    )
    assert summary["latest_inventory"]["colorado_quarter_quad_resolved_href"] == (
        source_gate.NAIP_EXPECTED_RESOLVED_HREF
    )
    assert summary["latest_inventory"]["colorado_quarter_quad_anchor_label"] == (
        source_gate.NAIP_EXPECTED_ANCHOR_LABEL
    )
    assert summary["latest_inventory"]["structural_errors"] == []
    assert summary["status"] == "PASS_CURRENT_CLEAR_LICENSE_FULL_BOUNDS"


@pytest.mark.parametrize(
    "base_markup",
    [
        "<head><base href='https://off-authority.example/'></head>",
        "<head><base href='https://www.fpacbc.usda.gov/'/></head>",
        "<template><base href='/alternate/'></template>",
        "<head><base href='https:&#47;&#47;off-authority.example/'></head>",
        "<head><base h&#114;ef='/attribute-alias/'></head>",
        "<head><base href='/first/' href='/second/'></head>",
        "<div hidden><base></div>",
    ],
)
def test_naip_coverage_rejects_every_base_element_in_production_path(
    tmp_path: Path, base_markup: str
) -> None:
    payload = padded_production_html(
        naip_latest_coverage_fixture()
        .decode("utf-8")
        .replace("<html>", f"<html>{base_markup}", 1)
    )
    report = source_gate.structural_visible_html_report(payload)
    assert "FORBIDDEN_BASE_ELEMENT" in report["structural_errors"]
    assert_production_naip_coverage_rejected(
        tmp_path, payload, match="FORBIDDEN_BASE_ELEMENT"
    )


def test_naip_license_rejects_base_even_outside_body_in_production_path(
    tmp_path: Path,
) -> None:
    payload = padded_production_html(
        "<html><head><base href='/alternate/'></head><body>"
        f"{NAIP_TEST_LICENSE_STATEMENT}</body></html>"
    )
    assert_production_naip_license_rejected(
        tmp_path, payload, match="FORBIDDEN_BASE_ELEMENT"
    )


@pytest.mark.parametrize(
    ("scope", "attributes"),
    [
        *[(scope, "") for scope in sorted(source_gate._HTML_NONAUTHORITY_SUBTREE_ELEMENTS)],
        ("details", " open"),
        ("dialog", " open"),
    ],
)
def test_naip_coverage_non_authority_scopes_never_supply_evidence(
    tmp_path: Path, scope: str, attributes: str
) -> None:
    payload = padded_production_html(
        f"<html><body>{NAIP_TEST_COVERAGE_LABELS}"
        f"<{scope}{attributes}><table>{naip_test_current_row()}</table>"
        f"</{scope}></body></html>"
    )
    assert_production_naip_coverage_rejected(tmp_path, payload)


@pytest.mark.parametrize(
    ("scope", "attributes"),
    [
        *[(scope, "") for scope in sorted(source_gate._HTML_NONAUTHORITY_SUBTREE_ELEMENTS)],
        ("details", " open"),
        ("dialog", " open"),
    ],
)
def test_naip_license_non_authority_scopes_never_supply_evidence(
    tmp_path: Path, scope: str, attributes: str
) -> None:
    payload = padded_production_html(
        f"<html><body><{scope}{attributes}>{NAIP_TEST_LICENSE_STATEMENT}"
        f"</{scope}><p>Visible customer services.</p></body></html>"
    )
    assert_production_naip_license_rejected(tmp_path, payload)


@pytest.mark.parametrize("scope", ["meter", "progress"])
def test_naip_coverage_self_closing_nonvoid_fallback_scope_never_supplies_evidence(
    tmp_path: Path, scope: str
) -> None:
    payload = padded_production_html(
        f"<html><body>{NAIP_TEST_COVERAGE_LABELS}"
        f"<{scope}/><table>{naip_test_current_row()}</table>"
        f"</{scope}></body></html>"
    )
    assert_production_naip_coverage_rejected(tmp_path, payload)


@pytest.mark.parametrize("scope", ["meter", "progress"])
def test_naip_license_self_closing_nonvoid_fallback_scope_never_supplies_evidence(
    tmp_path: Path, scope: str
) -> None:
    payload = padded_production_html(
        f"<html><body><{scope}/>{NAIP_TEST_LICENSE_STATEMENT}</{scope}>"
        "<p>Visible customer services.</p></body></html>"
    )
    assert_production_naip_license_rejected(tmp_path, payload)


def test_naip_optional_tbody_and_td_end_tags_preserve_valid_production_path(
    tmp_path: Path,
) -> None:
    payload = padded_production_html(
        f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table><tbody><tr>"
        "<td><strong>Colorado</strong><td>"
        f"{naip_test_current_anchor()}</tr></table></body></html>"
    )
    value, configured, frozen_source, licenses = naip_authority_fixture(
        tmp_path,
        document_payloads={source_gate.NAIP_LATEST_COVERAGE_DOCUMENT_ID: payload},
        finalize=False,
        production_document_contract=True,
    )
    assert_production_naip_document_sizes(configured, frozen_source, tmp_path)
    summary = source_gate.validate_naip_authority_payloads(
        value, configured, frozen_source, tmp_path, licenses
    )
    assert summary["latest_inventory"]["colorado_quarter_quad_raw_href"] == (
        source_gate.NAIP_EXPECTED_RAW_HREF
    )


@pytest.mark.parametrize(
    ("variant", "markup", "error"),
    [
        (
            "nested_body",
            (
                f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table>"
                f"{naip_test_current_row()}</table><body>duplicate</body>"
                "</body></html>"
            ),
            "NESTED_BODY_ELEMENT",
        ),
        (
            "multiple_body",
            (
                f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table>"
                f"{naip_test_current_row()}</table></body>"
                "<body>duplicate</body></html>"
            ),
            "MULTIPLE_BODY_ELEMENTS",
        ),
        (
            "nested_tr",
            (
                f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table><tr>"
                "<td>Colorado</td><td><tr><td>"
                f"{naip_test_current_anchor()}</td></tr></td></tr>"
                "</table></body></html>"
            ),
            "NESTED_TABLE_ROW",
        ),
        (
            "table_close_with_row",
            (
                f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table><tr>"
                f"<td>Colorado</td><td>{naip_test_current_anchor()}</td>"
                "</table></tr></body></html>"
            ),
            "TABLE_CLOSED_WITH_ACTIVE_ROW",
        ),
        (
            "body_close_with_table",
            (
                f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table>"
                f"{naip_test_current_row()}</body></table></html>"
            ),
            "BODY_CLOSED_WITH_ACTIVE_TABLE_OR_ROW",
        ),
        (
            "tr_close_with_anchor",
            (
                f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table><tr>"
                "<td>Colorado</td><td>"
                f"<a href='{source_gate.NAIP_EXPECTED_RAW_HREF}'>"
                f"{source_gate.NAIP_EXPECTED_ANCHOR_LABEL}</tr></a>"
                "</td></table></body></html>"
            ),
            "MISNESTED_END_TAG_TR",
        ),
        (
            "unmatched_tr",
            (
                f"<html><body>{NAIP_TEST_COVERAGE_LABELS}</tr><table>"
                f"{naip_test_current_row()}</table></body></html>"
            ),
            "UNMATCHED_END_TAG_TR",
        ),
        (
            "unclosed_body",
            (
                f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table>"
                f"{naip_test_current_row()}</table>"
            ),
            "UNCLOSED_CRITICAL_BODY",
        ),
        (
            "unclosed_table_row",
            (
                f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table><tr>"
                f"<td>Colorado</td><td>{naip_test_current_anchor()}</td>"
            ),
            "UNCLOSED_CRITICAL_TABLE",
        ),
        (
            "unclosed_anchor",
            (
                f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table><tr>"
                "<td>Colorado</td><td>"
                f"<a href='{source_gate.NAIP_EXPECTED_RAW_HREF}'>"
                f"{source_gate.NAIP_EXPECTED_ANCHOR_LABEL}"
            ),
            "UNCLOSED_CRITICAL_A",
        ),
        (
            "unclosed_hidden",
            (
                f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table>"
                f"{naip_test_current_row()}</table><div hidden>"
            ),
            "UNCLOSED_CRITICAL_DIV",
        ),
        (
            "unclosed_non_authority",
            (
                f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table>"
                f"{naip_test_current_row()}</table><details>"
            ),
            "UNCLOSED_CRITICAL_DETAILS",
        ),
    ],
)
def test_naip_coverage_ambiguous_topology_fails_production_path(
    tmp_path: Path, variant: str, markup: str, error: str
) -> None:
    del variant
    payload = padded_production_html(markup)
    report = source_gate.structural_visible_html_report(payload)
    assert error in report["structural_errors"]
    assert_production_naip_coverage_rejected(tmp_path, payload, match=error)


@pytest.mark.parametrize("hidden_tag", ["script", "style", "template", "noscript"])
def test_naip_coverage_hidden_current_anchor_cannot_satisfy_production_path(
    tmp_path: Path, hidden_tag: str
) -> None:
    payload = (
        "<html><body><p>Quarter quad tile names of the most recent year available.</p>"
        "<h2>NAIP CONUS 2021-2022</h2><table>"
        "<tr><td><strong>Colorado</strong></td>"
        "<td><a href='co_naip21seam.zip'>visible seam only</a></td></tr></table>"
        f"<{hidden_tag}><table><tr><td><strong>Colorado</strong></td>"
        f"<td><a href='{source_gate.NAIP_EXPECTED_RAW_HREF}'>"
        "hidden current quarter quads</a></td>"
        f"</tr></table>{'x' * 41000}</{hidden_tag}></body></html>"
    ).encode("utf-8")
    value, configured, frozen_source, licenses = naip_authority_fixture(
        tmp_path,
        document_payloads={source_gate.NAIP_LATEST_COVERAGE_DOCUMENT_ID: payload},
        finalize=False,
        production_document_contract=True,
    )
    assert configured["documents"] == source_gate.expected_naip_documents(value)
    assert_production_naip_document_sizes(configured, frozen_source, tmp_path)
    with pytest.raises(source_gate.GateError, match="quarter-quad"):
        source_gate.validate_naip_authority_payloads(
            value, configured, frozen_source, tmp_path, licenses
        )


@pytest.mark.parametrize("element", ["body", "table", "tr", "a", "div"])
def test_naip_coverage_boolean_hidden_is_inherited_in_production_path(
    tmp_path: Path, element: str
) -> None:
    assert_production_naip_coverage_rejected(
        tmp_path, naip_coverage_page_with_hidden_authority(element, "hidden")
    )


@pytest.mark.parametrize(
    ("element", "style"),
    [
        ("body", "display:none"),
        ("table", " DISPLAY : NONE ! IMPORTANT "),
        ("tr", "visibility : hidden!important"),
        ("a", "ViSiBiLiTy : HIDDEN ! important"),
        ("div", "color: navy; display : none ! important"),
        ("body", "opacity:0"),
        ("a", "opacity:0"),
        ("table", "filter:opacity(0)"),
        ("tr", "clip-path:inset(100%)"),
        ("a", "position:absolute;left:-999999px"),
        ("div", "color:transparent"),
        ("a", "font-size:0"),
        ("div", "background:navy"),
        ("table", " "),
    ],
)
def test_naip_coverage_nonempty_inline_style_suppresses_subtree_in_production_path(
    tmp_path: Path, element: str, style: str
) -> None:
    assert_production_naip_coverage_rejected(
        tmp_path,
        naip_coverage_page_with_hidden_authority(
            element, f'style="{style}"'
        ),
    )


@pytest.mark.parametrize("hidden_tag", ["script", "style", "template", "noscript"])
def test_naip_coverage_self_closing_nonvoid_tag_suppresses_descendants(
    tmp_path: Path, hidden_tag: str
) -> None:
    payload = padded_production_html(
        f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table><tr>"
        f"<td><strong>Colorado</strong></td><td><{hidden_tag}/>"
        f"{naip_test_current_anchor()}</{hidden_tag}></td></tr></table>"
        "</body></html>"
    )
    assert_production_naip_coverage_rejected(tmp_path, payload)


@pytest.mark.parametrize("location", ["head", "title", "before_body", "after_body"])
def test_naip_coverage_authority_outside_visible_body_fails_production_path(
    tmp_path: Path, location: str
) -> None:
    proof = (
        f"{NAIP_TEST_COVERAGE_LABELS}<table>{naip_test_current_row()}</table>"
    )
    visible_body = "<body><p>Visible USDA coverage page without authority.</p></body>"
    if location == "head":
        markup = f"<html><head>{proof}</head>{visible_body}</html>"
    elif location == "title":
        markup = (
            f"<html><head><title>{proof}</title></head>{visible_body}</html>"
        )
    elif location == "before_body":
        markup = f"<html>{proof}{visible_body}</html>"
    else:
        markup = f"<html>{visible_body}{proof}</html>"
    assert_production_naip_coverage_rejected(
        tmp_path, padded_production_html(markup)
    )


@pytest.mark.parametrize(
    "href",
    [
        "javascript:co_naip21qq-1.zip",
        "mailto:co_naip21qq-1.zip",
        "https://www.fpacbc.usda.gov/sites/default/files/2024-10/co_naip21qq-1.zip",
        "http://www.fpacbc.usda.gov/sites/default/files/2024-10/co_naip21qq-1.zip",
        "https://off-authority.example/sites/default/files/2024-10/co_naip21qq-1.zip",
        "/wrong/path/co_naip21qq-1.zip",
        "/sites/default/files/2024-10/co_naip21qq-1.zip?download=1",
        "/sites/default/files/2024-10/co_naip21qq-1.zip#inventory",
        "/sites/default/files/2024-10/co_naip%ZZqq-1.zip",
        "//www.fpacbc.usda.gov/sites/default/files/2024-10/co_naip21qq-1.zip",
        "sites/default/files/2024-10/co_naip21qq-1.zip",
        "co_naip21qq-1.zip",
        "/sites/default/files/2024-10/CO_NAIP21QQ-1.ZIP",
        "/sites/default/files/2024-10/co_naip21qq-2.zip",
        "&#47;sites/default/files/2024-10/co_naip21qq-1.zip",
        "/sites/default/files/2024-10/co_naip2&#49;qq-1.zip",
        "/sites/default/files/2024-10/co_naip21qq-1&#46;zip",
    ],
)
def test_naip_coverage_anchor_must_have_exact_raw_relative_href_in_production_path(
    tmp_path: Path, href: str
) -> None:
    payload = padded_production_html(
        f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table><tr>"
        "<td><strong>Colorado</strong></td>"
        f"<td><a href='{href}'>{source_gate.NAIP_EXPECTED_ANCHOR_LABEL}</a>"
        "</td></tr></table>"
        "<!-- required declaration token: co_naip21qq-1.zip -->"
        "</body></html>"
    )
    assert_production_naip_coverage_rejected(tmp_path, payload)


@pytest.mark.parametrize(
    "label",
    [
        "Shapefile (zip, 443KB)",
        "Shapefile (ZIP, 442KB)",
        "Colorado quarter quads",
        "",
        "\t\r\n",
        "\u200b",
        "\ufeff",
        "Shapefile\u200b (zip, 442KB)",
        "\x01Shapefile (zip, 442KB)",
        "Shapefile (zip, 442KB)\x7f",
    ],
)
def test_naip_coverage_anchor_label_is_exact_substantive_visible_text(
    tmp_path: Path, label: str
) -> None:
    payload = padded_production_html(
        f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table><tr>"
        "<td><strong>Colorado</strong></td>"
        f"<td><a href='{source_gate.NAIP_EXPECTED_RAW_HREF}'>{label}</a>"
        "</td></tr></table></body></html>"
    )
    assert_production_naip_coverage_rejected(tmp_path, payload)


@pytest.mark.parametrize(
    "anchor_markup",
    [
        (
            f"<a href='{source_gate.NAIP_EXPECTED_RAW_HREF}' "
            f"HREF='{source_gate.NAIP_EXPECTED_RAW_HREF}'>"
            f"{source_gate.NAIP_EXPECTED_ANCHOR_LABEL}</a>"
        ),
        f"<a href>{source_gate.NAIP_EXPECTED_ANCHOR_LABEL}</a>",
        f"<a href=>{source_gate.NAIP_EXPECTED_ANCHOR_LABEL}</a>",
        (
            f"<a href='{source_gate.NAIP_EXPECTED_RAW_HREF}' broken==value>"
            f"{source_gate.NAIP_EXPECTED_ANCHOR_LABEL}</a>"
        ),
    ],
)
def test_naip_coverage_anchor_requires_one_syntactically_valid_raw_href(
    tmp_path: Path, anchor_markup: str
) -> None:
    payload = padded_production_html(
        f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table><tr>"
        f"<td><strong>Colorado</strong></td><td>{anchor_markup}</td>"
        "</tr></table><!-- co_naip21qq-1.zip --></body></html>"
    )
    assert_production_naip_coverage_rejected(tmp_path, payload)


@pytest.mark.parametrize(
    "malformed_authority",
    [
        (
            "<div style='display'><table>"
            f"{naip_test_current_row()}</table></div>"
        ),
        (
            "<div style='visibility:/*unterminated'><table>"
            f"{naip_test_current_row()}</table></div>"
        ),
        (
            "<table><tr><td><strong>Colorado</strong></td><td>"
            f"<a href='{source_gate.NAIP_EXPECTED_RAW_HREF}' "
            "href='javascript:co_naip21qq-1.zip'>duplicate href</a>"
            "</td></tr></table>"
        ),
        (
            "<div hidden><table>"
            f"{naip_test_current_row()}</div></table>"
        ),
    ],
)
def test_naip_coverage_malformed_visibility_or_nesting_fails_production_path(
    tmp_path: Path, malformed_authority: str
) -> None:
    payload = padded_production_html(
        f"<html><body>{NAIP_TEST_COVERAGE_LABELS}{malformed_authority}"
        "</body></html>"
    )
    assert_production_naip_coverage_rejected(tmp_path, payload)


def test_naip_coverage_duplicate_visible_rows_and_anchors_fail_production_path(
    tmp_path: Path,
) -> None:
    duplicate_rows = padded_production_html(
        f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table>"
        f"{naip_test_current_row()}"
        "<tr><td>Colorado</td><td>"
        f"{naip_test_current_anchor()}</td></tr></table></body></html>"
    )
    duplicate_anchors = padded_production_html(
        f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table><tr>"
        "<td>Colorado</td><td>"
        f"{naip_test_current_anchor()}{naip_test_current_anchor()}"
        "</td></tr></table></body></html>"
    )
    assert_production_naip_coverage_rejected(tmp_path / "rows", duplicate_rows)
    assert_production_naip_coverage_rejected(
        tmp_path / "anchors", duplicate_anchors
    )


def test_naip_coverage_anchor_requires_visible_content_in_production_path(
    tmp_path: Path,
) -> None:
    payload = padded_production_html(
        f"<html><body>{NAIP_TEST_COVERAGE_LABELS}<table><tr>"
        "<td>Colorado</td><td>"
        f"<a href='{source_gate.NAIP_EXPECTED_RAW_HREF}'>"
        "<span hidden>hidden quarter-quad label</span></a>"
        "</td></tr></table></body></html>"
    )
    assert_production_naip_coverage_rejected(tmp_path, payload)


@pytest.mark.parametrize("hidden_tag", ["script", "style", "template", "noscript"])
def test_naip_license_hidden_statement_cannot_satisfy_production_path(
    tmp_path: Path, hidden_tag: str
) -> None:
    statement = (
        "Aerial photography and digital orthophotography acquired by the USDA "
        "are in the public domain."
    )
    license_payload = (
        f"<html><body><{hidden_tag}>{statement}{'x' * 41000}</{hidden_tag}>"
        "<p>Visible USDA customer services page.</p></body></html>"
    ).encode("utf-8")
    value, configured, frozen_source, licenses = naip_authority_fixture(
        tmp_path,
        finalize=False,
        license_payload=license_payload,
        production_document_contract=True,
    )
    assert len(license_payload) > 40000
    assert configured["documents"] == source_gate.expected_naip_documents(value)
    assert_production_naip_document_sizes(configured, frozen_source, tmp_path)
    with pytest.raises(source_gate.GateError, match="public-domain statement"):
        source_gate.validate_naip_authority_payloads(
            value, configured, frozen_source, tmp_path, licenses
        )


@pytest.mark.parametrize("element", ["body", "table", "tr", "a", "div"])
def test_naip_license_boolean_hidden_is_inherited_in_production_path(
    tmp_path: Path, element: str
) -> None:
    assert_production_naip_license_rejected(
        tmp_path, naip_license_page_with_hidden_statement(element, "hidden")
    )


@pytest.mark.parametrize(
    ("element", "style"),
    [
        ("body", "display:none"),
        ("table", " DISPLAY : NONE ! IMPORTANT "),
        ("tr", "visibility : hidden!important"),
        ("a", "ViSiBiLiTy : HIDDEN ! important"),
        ("div", "color: navy; display : none ! important"),
        ("body", "opacity:0"),
        ("a", "opacity:0"),
        ("table", "filter:opacity(0)"),
        ("tr", "clip-path:inset(100%)"),
        ("a", "position:absolute;left:-999999px"),
        ("div", "color:transparent"),
        ("a", "font-size:0"),
        ("div", "background:navy"),
        ("table", " "),
    ],
)
def test_naip_license_nonempty_inline_style_suppresses_subtree_in_production_path(
    tmp_path: Path, element: str, style: str
) -> None:
    assert_production_naip_license_rejected(
        tmp_path,
        naip_license_page_with_hidden_statement(
            element, f'style="{style}"'
        ),
    )


@pytest.mark.parametrize("hidden_tag", ["script", "style", "template", "noscript"])
def test_naip_license_self_closing_nonvoid_tag_suppresses_descendants(
    tmp_path: Path, hidden_tag: str
) -> None:
    payload = padded_production_html(
        f"<html><body><{hidden_tag}/>{NAIP_TEST_LICENSE_STATEMENT}"
        f"</{hidden_tag}><p>Visible customer services.</p></body></html>"
    )
    assert_production_naip_license_rejected(tmp_path, payload)


@pytest.mark.parametrize("location", ["head", "title", "before_body", "after_body"])
def test_naip_license_authority_outside_visible_body_fails_production_path(
    tmp_path: Path, location: str
) -> None:
    visible_body = "<body><p>Visible USDA customer services.</p></body>"
    if location == "head":
        markup = (
            f"<html><head><p>{NAIP_TEST_LICENSE_STATEMENT}</p></head>"
            f"{visible_body}</html>"
        )
    elif location == "title":
        markup = (
            f"<html><head><title>{NAIP_TEST_LICENSE_STATEMENT}</title></head>"
            f"{visible_body}</html>"
        )
    elif location == "before_body":
        markup = f"<html>{NAIP_TEST_LICENSE_STATEMENT}{visible_body}</html>"
    else:
        markup = f"<html>{visible_body}{NAIP_TEST_LICENSE_STATEMENT}</html>"
    assert_production_naip_license_rejected(
        tmp_path, padded_production_html(markup)
    )


@pytest.mark.parametrize(
    "malformed_authority",
    [
        f"<div style='display'>{NAIP_TEST_LICENSE_STATEMENT}</div>",
        (
            "<div style='visibility:/*unterminated'>"
            f"{NAIP_TEST_LICENSE_STATEMENT}</div>"
        ),
        (
            "<div hidden><table><tr><td>"
            f"{NAIP_TEST_LICENSE_STATEMENT}</div></td></tr></table>"
        ),
        (
            "<div style='display:block' style='visibility:visible'>"
            f"{NAIP_TEST_LICENSE_STATEMENT}</div>"
        ),
    ],
)
def test_naip_license_malformed_visibility_or_nesting_fails_production_path(
    tmp_path: Path, malformed_authority: str
) -> None:
    assert_production_naip_license_rejected(
        tmp_path,
        padded_production_html(
            f"<html><body>{malformed_authority}"
            "<p>Visible customer services.</p></body></html>"
        ),
    )


@pytest.mark.parametrize(
    "variant",
    [
        "stale_visible",
        "stale_visible_uppercase",
        "stale_visible_percent_encoded",
        "duplicate_current",
    ],
)
def test_naip_visible_stale_or_duplicate_quarter_quad_anchors_fail_production_path(
    tmp_path: Path, variant: str
) -> None:
    if variant == "stale_visible":
        anchors = (
            "<a href='co_naip19qq-1.zip'>stale visible quarter quads</a>"
            "<!-- co_naip21qq-1.zip -->"
        )
    elif variant == "stale_visible_uppercase":
        anchors = (
            f"{naip_test_current_anchor()}"
            "<a href='CO_NAIP19QQ-1.ZIP'>uppercase stale quarter quads</a>"
        )
    elif variant == "stale_visible_percent_encoded":
        anchors = (
            f"{naip_test_current_anchor()}"
            "<a href='co_naip%31%39qq-1.zip'>encoded stale quarter quads</a>"
        )
    else:
        anchors = (
            f"<a href='{source_gate.NAIP_EXPECTED_RAW_HREF}'>"
            f"{source_gate.NAIP_EXPECTED_ANCHOR_LABEL}</a>"
            f"<a href='{source_gate.NAIP_EXPECTED_RAW_HREF}'>"
            f"{source_gate.NAIP_EXPECTED_ANCHOR_LABEL}</a>"
        )
    payload = (
        "<html><body><p>Quarter quad tile names of the most recent year available.</p>"
        "<h2>NAIP CONUS 2021-2022</h2><table>"
        f"<tr><td><strong>Colorado</strong></td><td>{anchors}</td></tr>"
        f"</table><!--{'x' * 41000}--></body></html>"
    ).encode("utf-8")
    value, configured, frozen_source, licenses = naip_authority_fixture(
        tmp_path,
        document_payloads={source_gate.NAIP_LATEST_COVERAGE_DOCUMENT_ID: payload},
        finalize=False,
        production_document_contract=True,
    )
    assert_production_naip_document_sizes(configured, frozen_source, tmp_path)
    with pytest.raises(source_gate.GateError, match="quarter-quad"):
        source_gate.validate_naip_authority_payloads(
            value, configured, frozen_source, tmp_path, licenses
        )


def test_naip_primary_footprint_union_covers_complete_exact_bounds() -> None:
    value = registry()
    contract = next(
        source
        for source in value["document_sources"]
        if source["id"] == source_gate.NAIP_AUTHORITY_SOURCE_ID
    )["orthophoto_authority_contract"]
    payload = naip_footprint_fixture(contract["coverage_bbox_wgs84_xyxy"])
    contract = bind_naip_test_contract(contract, payload)
    catalog = source_gate.validate_naip_bounded_ids(naip_ids_fixture(), contract)
    summary = source_gate.validate_naip_bounded_footprints(
        payload, contract, catalog["ordered_object_ids"]
    )
    assert summary["accepted_primary_footprint_count"] == 25
    assert summary["coverage_gap_area_wgs84_degrees_squared"] == 0.0
    assert summary["disposition_counts"] == {
        "ACCEPTED_CURRENT_PRIMARY_FOOTPRINT": 25,
        "EXCLUDED_SERVICE_OVERVIEW": 1,
    }
    assert catalog["object_id_count"] == 26


@pytest.mark.parametrize(
    ("kwargs", "message"),
    [
        ({"partial": True}, "complete KBDU bounds"),
        ({"year": 2020}, "current Colorado-year"),
        ({"category": 3}, "unadjudicated category"),
    ],
)
def test_naip_partial_old_or_unmapped_footprints_fail_closed(
    kwargs: dict, message: str
) -> None:
    value = registry()
    contract = next(
        source
        for source in value["document_sources"]
        if source["id"] == source_gate.NAIP_AUTHORITY_SOURCE_ID
    )["orthophoto_authority_contract"]
    good_payload = naip_footprint_fixture(contract["coverage_bbox_wgs84_xyxy"])
    contract = bind_naip_test_contract(contract, good_payload)
    with pytest.raises(source_gate.GateError):
        source_gate.validate_naip_bounded_footprints(
            naip_footprint_fixture(contract["coverage_bbox_wgs84_xyxy"], **kwargs),
            contract,
            list(source_gate.NAIP_EXPECTED_OBJECT_IDS),
        )


@pytest.mark.parametrize("transfer_value", [False, True, None, 0, "false", {}, []])
def test_naip_feature_response_rejects_every_transfer_marker_and_extra_schema(
    transfer_value: object,
) -> None:
    contract, payload = naip_bound_test_payload()
    value = json.loads(payload)
    value["exceededTransferLimit"] = transfer_value
    with pytest.raises(source_gate.GateError, match="top-level response schema"):
        source_gate.validate_naip_bounded_footprints(
            source_gate.canonical_json_bytes(value),
            contract,
            list(source_gate.NAIP_EXPECTED_OBJECT_IDS),
        )
    id_value = json.loads(naip_ids_fixture())
    id_value["exceededTransferLimit"] = transfer_value
    with pytest.raises(source_gate.GateError, match="object-ID response schema"):
        source_gate.validate_naip_bounded_ids(
            source_gate.canonical_json_bytes(id_value), contract
        )


def test_naip_feature_and_id_response_reject_error_extra_and_inventory_mutations() -> None:
    contract, payload = naip_bound_test_payload()
    feature_value = json.loads(payload)
    feature_variants = [
        {"error": {"code": 500}},
        {**feature_value, "unexpected": "field"},
    ]
    malformed_feature = deepcopy(feature_value)
    malformed_feature["features"][0]["attributes"] = []
    feature_variants.append(malformed_feature)
    for variant in feature_variants:
        with pytest.raises(source_gate.GateError):
            source_gate.validate_naip_bounded_footprints(
                source_gate.canonical_json_bytes(variant),
                contract,
                list(source_gate.NAIP_EXPECTED_OBJECT_IDS),
            )

    exact_ids = list(source_gate.NAIP_EXPECTED_OBJECT_IDS)
    id_variants = [
        {"error": {"code": 500}},
        {"objectIdFieldName": "objectid", "objectIds": exact_ids, "extra": False},
        {
            "objectIdFieldName": "objectid",
            "objectIds": list(reversed(exact_ids)),
        },
        {"objectIdFieldName": "objectid", "objectIds": exact_ids[:-1]},
    ]
    for variant in id_variants:
        with pytest.raises(source_gate.GateError):
            source_gate.validate_naip_bounded_ids(
                source_gate.canonical_json_bytes(variant), contract
            )


@pytest.mark.parametrize(
    ("attribute", "replacement"),
    [
        ("category", True),
        ("category", 1.0),
        ("year", True),
        ("year", 2021.0),
        ("objectid", True),
        ("objectid", 245982.0),
    ],
)
def test_naip_feature_attributes_require_exact_json_integer_types(
    attribute: str, replacement: object
) -> None:
    contract, payload = naip_bound_test_payload()
    value = json.loads(payload)
    value["features"][0]["attributes"][attribute] = replacement
    if attribute == "objectid":
        contract["expected_object_ids"][0] = replacement
        bounded_ids = deepcopy(contract["expected_object_ids"])
    else:
        bounded_ids = list(source_gate.NAIP_EXPECTED_OBJECT_IDS)
    contract["geometry_inventory_sha256"] = source_gate.naip_geometry_inventory_sha256(
        value["features"]
    )
    with pytest.raises(source_gate.GateError, match="types|object IDs"):
        source_gate.validate_naip_bounded_footprints(
            source_gate.canonical_json_bytes(value), contract, bounded_ids
        )


@pytest.mark.parametrize("replacement", [True, 245982.0])
def test_naip_id_inventory_requires_exact_json_integer_types(replacement: object) -> None:
    contract, _payload = naip_bound_test_payload()
    object_ids = list(source_gate.NAIP_EXPECTED_OBJECT_IDS)
    object_ids[0] = replacement
    contract["expected_object_ids"] = deepcopy(object_ids)
    with pytest.raises(source_gate.GateError, match="inventory/order"):
        source_gate.validate_naip_bounded_ids(naip_ids_fixture(object_ids), contract)


def test_naip_overview_only_and_unpinned_replacement_geometry_fail_closed() -> None:
    contract, payload = naip_bound_test_payload()
    value = json.loads(payload)
    overview_only = {**value, "features": [value["features"][-1]]}
    with pytest.raises(source_gate.GateError):
        source_gate.validate_naip_bounded_footprints(
            source_gate.canonical_json_bytes(overview_only),
            contract,
            list(source_gate.NAIP_EXPECTED_OBJECT_IDS),
        )
    world_replacement = deepcopy(value)
    world_ring = [
        [-180.0, -90.0],
        [180.0, -90.0],
        [180.0, 90.0],
        [-180.0, 90.0],
        [-180.0, -90.0],
    ]
    for feature in world_replacement["features"][:-1]:
        feature["geometry"]["rings"] = [deepcopy(world_ring)]
    with pytest.raises(source_gate.GateError, match="geometry inventory"):
        source_gate.validate_naip_bounded_footprints(
            source_gate.canonical_json_bytes(world_replacement),
            contract,
            list(source_gate.NAIP_EXPECTED_OBJECT_IDS),
        )


@pytest.mark.parametrize(
    "mutation",
    ["open", "self_intersecting", "non_wgs84", "world", "tiny_sliver"],
)
def test_naip_geometry_semantics_fail_closed_even_with_matching_inventory_hash(
    mutation: str,
) -> None:
    contract, payload = naip_bound_test_payload()
    value = json.loads(payload)
    xmin, ymin, xmax, ymax = map(float, contract["coverage_bbox_wgs84_xyxy"])
    if mutation == "open":
        value["features"][0]["geometry"]["rings"][0].pop()
    elif mutation == "self_intersecting":
        value["features"][0]["geometry"]["rings"] = [[
            [xmin, ymin],
            [xmax, ymax],
            [xmin, ymax],
            [xmax, ymin],
            [xmin, ymin],
        ]]
    elif mutation == "non_wgs84":
        ring = value["features"][0]["geometry"]["rings"][0]
        ring[0] = [181.0, ymin]
        ring[-1] = [181.0, ymin]
    elif mutation == "world":
        replacement = [
            [-180.0, -90.0],
            [180.0, -90.0],
            [180.0, 90.0],
            [-180.0, 90.0],
            [-180.0, -90.0],
        ]
        for feature in value["features"][:-1]:
            feature["geometry"]["rings"] = [deepcopy(replacement)]
    else:
        replacement = [
            [xmin, ymin],
            [xmin + 0.00001, ymin],
            [xmin + 0.00001, ymin + 0.00001],
            [xmin, ymin + 0.00001],
            [xmin, ymin],
        ]
        for feature in value["features"][:-1]:
            feature["geometry"]["rings"] = [deepcopy(replacement)]
    contract["geometry_inventory_sha256"] = source_gate.naip_geometry_inventory_sha256(
        value["features"]
    )
    with pytest.raises(source_gate.GateError):
        source_gate.validate_naip_bounded_footprints(
            source_gate.canonical_json_bytes(value),
            contract,
            list(source_gate.NAIP_EXPECTED_OBJECT_IDS),
        )


def test_naip_frozen_payloads_are_recomputed_and_summary_bound(tmp_path: Path) -> None:
    value, configured, frozen_source, licenses = naip_authority_fixture(tmp_path)
    expected = source_gate.validate_naip_authority_payloads(
        value, configured, frozen_source, tmp_path, licenses
    )
    assert expected == frozen_source["authority_validation"]
    freeze = {"license_snapshots": licenses, "sources": [frozen_source]}
    verified = source_gate.validate_frozen_naip_authority_closure(
        value, tmp_path, freeze
    )
    assert isinstance(verified, source_gate.VerifiedNaipAuthority)
    assert verified.summary == expected
    forged = deepcopy(freeze)
    forged["sources"][0]["authority_validation"]["coverage"]["year"] = 2020
    with pytest.raises(source_gate.GateError, match="summary differs"):
        source_gate.validate_frozen_naip_authority_closure(value, tmp_path, forged)


def test_naip_frozen_document_order_and_exact_record_schema_fail_closed(
    tmp_path: Path,
) -> None:
    value, configured, frozen_source, licenses = naip_authority_fixture(tmp_path)
    reordered = deepcopy(frozen_source)
    reordered["documents"][0], reordered["documents"][1] = (
        reordered["documents"][1],
        reordered["documents"][0],
    )
    with pytest.raises(source_gate.GateError, match="ordered document inventory"):
        source_gate.validate_naip_authority_payloads(
            value, configured, reordered, tmp_path, licenses
        )
    expanded = deepcopy(frozen_source)
    expanded["documents"][0]["unexpected"] = True
    with pytest.raises(source_gate.GateError, match="document contract changed"):
        source_gate.validate_naip_authority_payloads(
            value, configured, expanded, tmp_path, licenses
        )


def test_naip_missing_or_tampered_license_snapshot_fails_closed(tmp_path: Path) -> None:
    value, configured, frozen_source, licenses = naip_authority_fixture(tmp_path)
    with pytest.raises(source_gate.GateError, match="license snapshot is missing"):
        source_gate.validate_naip_authority_payloads(
            value, configured, frozen_source, tmp_path, {}
        )
    response_path = tmp_path / licenses[source_gate.NAIP_LICENSE_SNAPSHOT_ID]["receipt"][
        "response"
    ]["path"]
    response_path.write_bytes(response_path.read_bytes() + b" tampered")
    with pytest.raises(source_gate.GateError, match="response bytes differ"):
        source_gate.validate_naip_authority_payloads(
            value, configured, frozen_source, tmp_path, licenses
        )


@pytest.mark.parametrize(
    "target_id",
    [
        source_gate.NAIP_LICENSE_SNAPSHOT_ID,
        source_gate.NAIP_ARCHIVE_DOCUMENT_ID,
        source_gate.NAIP_LATEST_COVERAGE_DOCUMENT_ID,
    ],
)
@pytest.mark.parametrize("mutation", ["missing", "tampered"])
def test_naip_every_evidence_receipt_missing_or_tampered_fails_closed(
    tmp_path: Path, target_id: str, mutation: str
) -> None:
    value, configured, frozen_source, licenses = naip_authority_fixture(tmp_path)
    if target_id == source_gate.NAIP_LICENSE_SNAPSHOT_ID:
        receipt = licenses[target_id]["receipt"]
    else:
        receipt = next(
            document["receipt"]
            for document in frozen_source["documents"]
            if document["id"] == target_id
        )
    response_path = tmp_path / receipt["response"]["path"]
    if mutation == "missing":
        response_path.unlink()
    else:
        response_path.write_bytes(response_path.read_bytes() + b"tampered")
    with pytest.raises(source_gate.GateError):
        source_gate.validate_naip_authority_payloads(
            value, configured, frozen_source, tmp_path, licenses
        )


@pytest.mark.parametrize(
    "target_id",
    [
        source_gate.NAIP_LICENSE_SNAPSHOT_ID,
        source_gate.NAIP_ARCHIVE_DOCUMENT_ID,
        source_gate.NAIP_LATEST_COVERAGE_DOCUMENT_ID,
    ],
)
@pytest.mark.parametrize("mutation", ["missing_receipt", "tampered_receipt"])
def test_naip_every_receipt_record_missing_or_tampered_fails_closed(
    tmp_path: Path, target_id: str, mutation: str
) -> None:
    value, configured, frozen_source, licenses = naip_authority_fixture(tmp_path)
    if target_id == source_gate.NAIP_LICENSE_SNAPSHOT_ID:
        record = licenses[target_id]
    else:
        record = next(
            document
            for document in frozen_source["documents"]
            if document["id"] == target_id
        )
    if mutation == "missing_receipt":
        record.pop("receipt")
    else:
        record["receipt"]["url"] += "?tampered=true"
    with pytest.raises(source_gate.GateError):
        source_gate.validate_naip_authority_payloads(
            value, configured, frozen_source, tmp_path, licenses
        )


@pytest.mark.parametrize("mutation", ["missing", "tampered"])
def test_naip_source_receipt_closure_missing_or_tampered_fails_closed(
    tmp_path: Path, mutation: str
) -> None:
    value, _configured, frozen_source, licenses = naip_authority_fixture(tmp_path)
    receipt_path = tmp_path / frozen_source["receipt_file"]["path"]
    if mutation == "missing":
        receipt_path.unlink()
    else:
        receipt_path.write_bytes(receipt_path.read_bytes() + b"tampered")
    with pytest.raises(source_gate.GateError):
        source_gate.validate_frozen_naip_authority_closure(
            value,
            tmp_path,
            {"license_snapshots": licenses, "sources": [frozen_source]},
        )


@pytest.mark.parametrize(
    "mutation",
    [
        "header",
        "timeout",
        "timeout_type",
        "request_url",
        "final_url",
        "request_path",
        "response_path",
        "status",
        "status_type",
        "transport_extra",
        "record_bytes_type",
    ],
)
def test_naip_receipt_request_and_transport_contract_mutations_fail_closed(
    tmp_path: Path, mutation: str
) -> None:
    value, configured, frozen_source, licenses = naip_authority_fixture(tmp_path)
    receipt = next(
        document["receipt"]
        for document in frozen_source["documents"]
        if document["id"] == source_gate.NAIP_SERVICE_DOCUMENT_ID
    )
    request_path = tmp_path / receipt["request"]["path"]
    if mutation in {"header", "timeout", "timeout_type", "request_url"}:
        request = json.loads(request_path.read_text(encoding="utf-8"))
        if mutation == "header":
            request["headers"]["Accept"] = "*/*"
        elif mutation == "timeout":
            request["timeout_seconds"] = 179
        elif mutation == "timeout_type":
            request["timeout_seconds"] = 180.0
        else:
            request["url"] = request["url"] + "&changed=true"
        request_path.write_bytes(source_gate.canonical_json_bytes(request))
    elif mutation == "final_url":
        receipt["transport"]["final_url"] += "&changed=true"
    elif mutation == "request_path":
        receipt["request"]["path"] = "requests/renamed.json"
    elif mutation == "response_path":
        receipt["response"]["path"] = "metadata/renamed.json"
    elif mutation == "status":
        receipt["transport"]["status"] = 206
    elif mutation == "status_type":
        receipt["transport"]["status"] = 200.0
    elif mutation == "transport_extra":
        receipt["transport"]["unexpected"] = False
    else:
        receipt["response"]["bytes"] = float(receipt["response"]["bytes"])
    with pytest.raises(source_gate.GateError):
        source_gate.validate_naip_authority_payloads(
            value, configured, frozen_source, tmp_path, licenses
        )


def test_orthophoto_temporal_gap_clears_only_for_exact_naip_summary(
    tmp_path: Path,
) -> None:
    value, _configured, frozen_source, licenses = naip_authority_fixture(tmp_path)
    verified = source_gate.validate_frozen_naip_authority_closure(
        value,
        tmp_path,
        {"license_snapshots": licenses, "sources": [frozen_source]},
    )
    base_sources = [
        {"has_z": True, "id": "drcog_pavement_lines_2024"},
        frozen_source,
    ]
    unverified_conflicts = source_gate.source_conflicts(
        value,
        {"product_binding_warnings": [], "sources": base_sources},
        [],
        {"status": "LEAD_D_APPROVED"},
    )
    assert any(
        conflict["id"] == "orthophoto_temporal_gap"
        for conflict in unverified_conflicts
    )
    fabricated_source = {
        "authority_validation": verified.summary,
        "id": source_gate.NAIP_AUTHORITY_SOURCE_ID,
        "license_partition": "US-Government-Work-Public-Domain",
        "metadata_only": True,
        "role": "orthophoto_ambiguity_resolution_only",
        "source_kind": "document_authority",
    }
    fabricated_conflicts = source_gate.source_conflicts(
        value,
        {
            "product_binding_warnings": [],
            "sources": [
                {"has_z": True, "id": "drcog_pavement_lines_2024"},
                fabricated_source,
            ],
        },
        [],
        {"status": "LEAD_D_APPROVED"},
    )
    assert any(
        conflict["id"] == "orthophoto_temporal_gap"
        for conflict in fabricated_conflicts
    )
    conflicts = source_gate.source_conflicts(
        value,
        {"product_binding_warnings": [], "sources": base_sources},
        [],
        {"status": "LEAD_D_APPROVED"},
        verified,
    )
    assert not any(conflict["id"] == "orthophoto_temporal_gap" for conflict in conflicts)
    for mutation in ("missing", "partial", "old", "forged_hash"):
        mutated_source = deepcopy(frozen_source)
        if mutation == "missing":
            mutated_source.pop("authority_validation")
        elif mutation == "partial":
            mutated_source["authority_validation"]["coverage"][
                "coverage_gap_area_wgs84_degrees_squared"
            ] = 0.001
        elif mutation == "old":
            mutated_source["authority_validation"]["coverage"]["year"] = 2020
        else:
            mutated_source["authority_validation"]["evidence_sha256"][
                source_gate.NAIP_LICENSE_SNAPSHOT_ID
            ] = "not-a-sha"
        conflicts = source_gate.source_conflicts(
            value,
            {
                "product_binding_warnings": [],
                "sources": [
                    {"has_z": True, "id": "drcog_pavement_lines_2024"},
                    mutated_source,
                ],
            },
            [],
            {"status": "LEAD_D_APPROVED"},
        )
        gap = next(item for item in conflicts if item["id"] == "orthophoto_temporal_gap")
        assert gap["severity"] == "BLOCKING"
        assert gap["status"] == "UNRESOLVED_FAIL_CLOSED"


def test_city_2023_partial_corroboration_cannot_clear_full_bounds_gap() -> None:
    value = registry()
    fake_city = {
        "authority_validation": {
            "coverage": {
                "accepted_primary_footprint_count": 1,
                "coverage_bbox_wgs84_xyxy": value["arcgis_query"]["bbox_wgs84_xyxy"],
                "coverage_gap_area_wgs84_degrees_squared": 0.0,
                "state": "CO",
                "year": 2023,
            },
            "status": "PASS_CURRENT_CLEAR_LICENSE_FULL_BOUNDS",
        },
        "id": "city_2023_naip_central_corroboration",
        "metadata_only": True,
    }
    conflicts = source_gate.source_conflicts(
        value,
        {
            "product_binding_warnings": [],
            "sources": [
                {"has_z": True, "id": "drcog_pavement_lines_2024"},
                fake_city,
            ],
        },
        [],
        {"status": "LEAD_D_APPROVED"},
    )
    assert any(item["id"] == "orthophoto_temporal_gap" for item in conflicts)


def test_geospatial_gate_directory_is_exactly_six_small_text_files() -> None:
    expected = {
        "README.md",
        "compile_walking_skeleton_lanes.py",
        "requirements-lock.txt",
        "source_gate.py",
        "source_registry.json",
        "test_source_gate.py",
    }
    actual = {
        path.relative_to(source_gate.SCRIPT_DIR).as_posix()
        for path in source_gate.SCRIPT_DIR.rglob("*")
        if path.is_file()
    }
    assert actual == expected
    assert all((source_gate.SCRIPT_DIR / name).stat().st_size < 1_000_000 for name in expected)


def test_toolchain_launcher_requires_isolated_no_bytecode_flags() -> None:
    from types import SimpleNamespace

    values = {
        name: getattr(source_gate.sys.flags, name)
        for name in source_gate.TOOLCHAIN_FLAG_NAMES
    }
    values["isolated"] = 0
    with pytest.raises(source_gate.ToolchainPreflightError, match=r"python -I -B"):
        source_gate.launcher_runtime_identity(
            flags=SimpleNamespace(**values),
            path_entries=(),
            environ={},
            enable_user_site=False,
        )
    values["isolated"] = 1
    values["dont_write_bytecode"] = 0
    with pytest.raises(source_gate.ToolchainPreflightError, match=r"python -I -B"):
        source_gate.launcher_runtime_identity(
            flags=SimpleNamespace(**values),
            path_entries=(),
            environ={},
            enable_user_site=False,
        )


def test_toolchain_semantic_environment_is_exact_and_closed() -> None:
    absent = source_gate.semantic_environment_identity({})
    assert tuple(absent) == source_gate.TOOLCHAIN_SEMANTIC_ENVIRONMENT_KEYS
    assert set(absent.values()) == {None}
    exact = source_gate.semantic_environment_identity({"PROJ_NETWORK": "OFF"})
    assert exact["PROJ_NETWORK"] == "OFF"
    with pytest.raises(source_gate.ToolchainPreflightError, match="unrecognized startup input"):
        source_gate.semantic_environment_identity({"PYTHON_UNBOUND_STARTUP_HOOK": "1"})


def test_toolchain_site_packages_is_record_closed_and_binds_executable_pth(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    site_root = tmp_path / "environment/Lib/site-packages"
    package = site_root / "fixture/__init__.py"
    record = site_root / "fixture-1.0.dist-info/RECORD"
    executable_pth = site_root / "fixture-1.0.dist-info/fixture.pth"
    for path, payload in (
        (package, b"VALUE = 1\n"),
        (record, b"fixture/__init__.py,,\n"),
        (executable_pth, b"import fixture\n"),
    ):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)

    def label(path: Path) -> str:
        relative = Path(path).resolve().relative_to(tmp_path.resolve())
        return relative.as_posix()

    monkeypatch.setattr(source_gate, "_payload_location", label)
    owned = {label(path) for path in (package, record, executable_pth)}
    identity = source_gate.site_packages_identity(owned, roots=[site_root])
    assert identity["exceptions"] == []
    assert identity["executable_pth_files"] == [
        {
            "bytes": len(b"import fixture\n"),
            "path": label(executable_pth),
            "sha256": source_gate.sha256_file(executable_pth),
        }
    ]

    stray = site_root / "unowned_module.py"
    stray.write_bytes(b"raise RuntimeError\n")
    with pytest.raises(source_gate.ToolchainPreflightError, match="stray module"):
        source_gate.site_packages_identity(owned, roots=[site_root])
    stray.unlink()

    unowned_pth = site_root / "unowned.pth"
    unowned_pth.write_bytes(b"import attacker\n")
    with pytest.raises(source_gate.ToolchainPreflightError, match=r"unowned \.pth"):
        source_gate.site_packages_identity(owned, roots=[site_root])


def test_toolchain_site_packages_freezes_only_noncode_exceptions(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    site_root = tmp_path / "environment/Lib/site-packages"
    owned_file = site_root / "fixture/data.json"
    exception = site_root / "NOTICE.txt"
    for path, payload in ((owned_file, b"{}\n"), (exception, b"notice\n")):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)

    monkeypatch.setattr(
        source_gate,
        "_payload_location",
        lambda path: Path(path).resolve().relative_to(tmp_path.resolve()).as_posix(),
    )
    identity = source_gate.site_packages_identity(
        {source_gate._payload_location(owned_file)}, roots=[site_root]
    )
    assert identity["exceptions"] == [
        {
            "bytes": exception.stat().st_size,
            "path": source_gate._payload_location(exception),
            "sha256": source_gate.sha256_file(exception),
        }
    ]


def test_toolchain_proj_runtime_is_offline_and_inside_owned_payload(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    site_root = tmp_path / "environment/Lib/site-packages"
    data_root = site_root / "pyproj/proj_dir/share/proj"
    database = data_root / "proj.db"
    database.parent.mkdir(parents=True)
    database.write_bytes(b"database")

    def label(path: Path) -> str:
        return Path(path).resolve().relative_to(tmp_path.resolve()).as_posix()

    monkeypatch.setattr(source_gate, "_payload_location", label)
    monkeypatch.setattr(source_gate.pyproj.network, "is_network_enabled", lambda: False)
    monkeypatch.setattr(source_gate.pyproj.datadir, "get_data_dir", lambda: str(data_root))
    payload = {
        "site_packages": {
            "exceptions": [],
            "roots": [label(site_root)],
        }
    }
    identity = source_gate.proj_runtime_identity(payload)
    assert identity["network_enabled"] is False
    assert identity["data_directory"] == label(data_root)
    assert identity["data_payload"]["file_count"] == 1

    payload["site_packages"]["exceptions"] = [
        {"bytes": 8, "path": label(database), "sha256": source_gate.sha256_file(database)}
    ]
    with pytest.raises(source_gate.ToolchainPreflightError, match="non-RECORD-owned"):
        source_gate.proj_runtime_identity(payload)
    monkeypatch.setattr(source_gate.pyproj.network, "is_network_enabled", lambda: True)
    with pytest.raises(source_gate.ToolchainPreflightError, match="network access"):
        source_gate.proj_runtime_identity(payload)


def test_registry_binds_toolchain_startup_site_and_proj_closure() -> None:
    contract = registry()["toolchain"]
    launcher = contract["runtime_identity"]["launcher"]
    assert launcher["flags"]["isolated"] == 1
    assert launcher["flags"]["dont_write_bytecode"] == 1
    assert launcher["flags"]["safe_path"] is True
    assert launcher["site_enable_user_site"] is False
    assert set(launcher["semantic_environment"].values()) == {None}
    assert contract["native_libraries"]["PROJ_runtime"]["network_enabled"] is False
    assert contract["payload_identity"]["site_packages"]["exceptions"] == []


@pytest.mark.parametrize("path_probe_behavior", ["false", "raise"])
def test_path_kind_uses_one_lstat_when_path_probes_are_unreliable(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    path_probe_behavior: str,
) -> None:
    ordinary_file = tmp_path / "ordinary-file.bin"
    ordinary_file.write_bytes(b"ordinary")
    ordinary_directory = tmp_path / "ordinary-directory"
    ordinary_directory.mkdir()
    real_lstat = source_gate.os.lstat
    lstat_calls: list[str] = []

    def recording_lstat(path: object) -> object:
        lstat_calls.append(source_gate.os.fspath(path))
        return real_lstat(path)

    def forbidden_lexists(_path: object) -> bool:
        raise AssertionError("_path_kind must not call os.path.lexists")

    def unreliable_path_probe(_path: Path) -> bool:
        if path_probe_behavior == "raise":
            raise AssertionError("_path_kind must not call Path.is_* probes")
        return False

    monkeypatch.setattr(source_gate.os, "lstat", recording_lstat)
    monkeypatch.setattr(source_gate.os.path, "lexists", forbidden_lexists)
    monkeypatch.setattr(Path, "is_file", unreliable_path_probe)
    monkeypatch.setattr(Path, "is_dir", unreliable_path_probe)
    monkeypatch.setattr(Path, "is_symlink", unreliable_path_probe)

    assert source_gate._path_kind(ordinary_file) == "file"
    assert source_gate._path_kind(ordinary_directory) == "dir"
    assert lstat_calls == [
        source_gate._native_filesystem_path(ordinary_file),
        source_gate._native_filesystem_path(ordinary_directory),
    ]


@pytest.mark.skipif(os.name != "nt", reason="Windows extended-path regression")
def test_long_windows_archive_paths_are_topology_checked_hashed_and_walked(
    tmp_path: Path,
) -> None:
    root = tmp_path / "archive"
    root.mkdir()
    deep_file = root / ("source_" + "a" * 80) / ("request_" + "b" * 80) / (
        "document_" + "c" * 80 + ".json"
    )
    native_parent = source_gate._native_filesystem_path(deep_file.parent)
    source_gate.os.makedirs(native_parent)
    with open(source_gate._native_filesystem_path(deep_file), "wb") as handle:
        handle.write(b"long-path-receipt\n")
    assert len(str(deep_file)) > 260
    assert source_gate._path_kind(deep_file) == "file"
    source_gate.validate_tree_no_reparse(root, "Long archive fixture")
    assert list(source_gate._walk_tree_files(root, "Long archive fixture")) == [
        deep_file
    ]
    assert source_gate.file_record(deep_file, root) == {
        "bytes": 18,
        "path": deep_file.relative_to(root).as_posix(),
        "sha256": source_gate.sha256_bytes(b"long-path-receipt\n"),
    }


def test_path_kind_mode_bits_preserve_reparse_symlink_and_absent_semantics(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    class SyntheticMetadata:
        def __init__(self, mode: int, attributes: int = 0) -> None:
            self.st_mode = mode
            self.st_file_attributes = attributes

    metadata_by_name = {
        "ordinary-file": SyntheticMetadata(0o100644),
        "ordinary-dir": SyntheticMetadata(0o040755),
        "reparse-file": SyntheticMetadata(0o100644, 0x400),
        "symlink": SyntheticMetadata(0o120777),
        "other": SyntheticMetadata(0o060600),
    }
    calls: list[str] = []

    def synthetic_lstat(path: object) -> object:
        name = Path(source_gate.os.fspath(path)).name
        calls.append(name)
        if name == "absent":
            raise FileNotFoundError(name)
        return metadata_by_name[name]

    def forbidden_path_probe(_path: Path) -> bool:
        raise AssertionError("_path_kind must classify only its lstat result")

    monkeypatch.setattr(source_gate.os, "lstat", synthetic_lstat)
    monkeypatch.setattr(Path, "is_file", forbidden_path_probe)
    monkeypatch.setattr(Path, "is_dir", forbidden_path_probe)
    monkeypatch.setattr(Path, "is_symlink", forbidden_path_probe)

    expected = {
        "ordinary-file": "file",
        "ordinary-dir": "dir",
        "reparse-file": "reparse",
        "symlink": "reparse",
        "other": "other",
        "absent": "absent",
    }
    assert {
        name: source_gate._path_kind(tmp_path / name) for name in expected
    } == expected
    assert calls == list(expected)


def test_reserved_path_topology_rejects_wrong_types_before_mutation(
    tmp_path: Path,
) -> None:
    value = registry()
    external_file = tmp_path / "external-file"
    external_file.write_bytes(b"sentinel")
    with pytest.raises(source_gate.GateError, match="External run root.*wrong filesystem kind"):
        source_gate.validate_command_path_topology(
            value, external_file, command="inventory"
        )
    assert external_file.read_bytes() == b"sentinel"

    external = tmp_path / "external"
    external.mkdir()
    (external / source_gate.FREEZE_NAME).mkdir()
    with pytest.raises(source_gate.GateError, match="Source freeze.*wrong filesystem kind"):
        source_gate.validate_command_path_topology(
            value,
            external,
            command="compile",
            data_dir=tmp_path / "data",
            evidence_dir=tmp_path / "evidence",
        )
    assert not (external / source_gate.OPERATION_LOCK_NAME).exists()
    (external / source_gate.FREEZE_NAME).rmdir()
    write_json(
        external / source_gate.FREEZE_NAME,
        {"acquisition_completed_utc": "2026-08-21T00:00:00Z"},
    )
    (external / "toolchain").write_bytes(b"parent-file")
    with pytest.raises(source_gate.GateError, match="wrong filesystem kind"):
        source_gate.validate_command_path_topology(
            value, external, command="inventory"
        )
    (external / "toolchain").unlink()
    data_file = tmp_path / "data-file"
    data_file.write_bytes(b"data-sentinel")
    with pytest.raises(source_gate.GateError, match="Normative data root.*wrong filesystem kind"):
        source_gate.validate_command_path_topology(
            value,
            external,
            command="compile",
            data_dir=data_file,
            evidence_dir=tmp_path / "evidence",
        )
    assert data_file.read_bytes() == b"data-sentinel"


def test_cli_wrong_type_external_root_is_no_mutation(tmp_path: Path) -> None:
    external_file = tmp_path / "external-file"
    external_file.write_bytes(b"sentinel")
    before = {
        path.name: path.read_bytes() for path in tmp_path.iterdir() if path.is_file()
    }
    result = source_gate.subprocess.run(
        [
            source_gate.sys.executable,
            "-I",
            "-B",
            str(Path(source_gate.__file__).resolve()),
            "inventory",
            "--external-run",
            str(external_file),
        ],
        cwd=source_gate.REPO_ROOT,
        capture_output=True,
        text=True,
        timeout=30,
    )
    assert result.returncode == 2
    assert "wrong filesystem kind" in result.stderr
    assert before == {
        path.name: path.read_bytes() for path in tmp_path.iterdir() if path.is_file()
    }


def test_reserved_tree_rejects_symlink_or_reparse_escape(tmp_path: Path) -> None:
    external = tmp_path / "external"
    outside = tmp_path / "outside"
    external.mkdir()
    outside.mkdir()
    link = external / "raw"
    try:
        link.symlink_to(outside, target_is_directory=True)
    except OSError as error:
        if getattr(error, "winerror", None) == 1314:
            pytest.skip(f"OS privilege does not permit a directory symlink: {error}")
        raise
    with pytest.raises(source_gate.GateError, match="symlink/junction/reparse"):
        source_gate.validate_command_path_topology(
            registry(), external, command="inventory"
        )
    assert list(outside.iterdir()) == []


def test_target_lock_identity_canonicalizes_lexical_filesystem_aliases(
    tmp_path: Path,
) -> None:
    canonical = tmp_path / "target"
    alias = tmp_path / "unused-parent" / ".." / "target"
    assert source_gate.target_set_lock_plan([canonical]) == source_gate.target_set_lock_plan(
        [alias]
    )


def _synthetic_promotion_tree(tmp_path: Path) -> dict[str, Path]:
    external = tmp_path / "external"
    data = tmp_path / "data"
    evidence = tmp_path / "evidence"
    stage = tmp_path / "stage"
    stage_work = stage / "work"
    stage_data = stage / "data"
    stage_evidence = stage / "evidence"
    candidate = stage / "approval_candidate"
    for directory in (
        external,
        data,
        evidence,
        stage_work / "normalized",
        stage_work / "intermediates",
        stage_data,
        stage_evidence,
        candidate,
    ):
        directory.mkdir(parents=True, exist_ok=True)
    (external / "sentinel.bin").write_bytes(b"external-sentinel")
    (stage_work / "normalized/new.bin").write_bytes(b"normalized")
    (stage_work / "intermediates/new.bin").write_bytes(b"intermediate")
    for name in source_gate.EXPECTED_TEXT_ARTIFACTS:
        (stage_data / name).write_bytes(b"new-" + name.encode())
        (candidate / name).write_bytes((stage_data / name).read_bytes())
    (stage_evidence / source_gate.EXPECTED_EXTERNAL_ARTIFACT).write_bytes(b"png")
    (candidate / source_gate.EXPECTED_EXTERNAL_ARTIFACT).write_bytes(b"png")
    (candidate / "candidate_manifest.json").write_bytes(b"candidate")
    stage_manifest = stage / source_gate.RUN_MANIFEST_NAME
    stage_manifest.write_bytes(b"manifest")
    return {
        "candidate": candidate,
        "data": data,
        "evidence": evidence,
        "external": external,
        "stage": stage,
        "stage_data": stage_data,
        "stage_evidence": stage_evidence,
        "stage_manifest": stage_manifest,
        "stage_work": stage_work,
    }


def test_promotion_postcommit_failure_restores_exact_root_topology(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    paths = _synthetic_promotion_tree(tmp_path)
    before_external = {
        path.relative_to(paths["external"]).as_posix(): path.read_bytes()
        for path in paths["external"].rglob("*")
        if path.is_file()
    }

    def write_index(_registry: dict, external: Path, _data: Path) -> None:
        (external / source_gate.PACKAGE_INDEX_NAME).write_bytes(b"index")

    monkeypatch.setattr(source_gate, "write_outer_package_index", write_index)
    with pytest.raises(OSError, match="postcommit drift"):
        source_gate._promote_compile_stage(
            {},
            paths["external"],
            paths["stage"],
            paths["stage_work"],
            paths["stage_data"],
            paths["stage_evidence"],
            paths["stage_manifest"],
            paths["data"],
            paths["evidence"],
            paths["candidate"],
            commit_validator=lambda: (_ for _ in ()).throw(
                OSError("postcommit drift")
            ),
        )
    assert before_external == {
        path.relative_to(paths["external"]).as_posix(): path.read_bytes()
        for path in paths["external"].rglob("*")
        if path.is_file()
    }
    assert paths["data"].is_dir() and list(paths["data"].iterdir()) == []
    assert paths["evidence"].is_dir() and list(paths["evidence"].iterdir()) == []
    assert not (paths["stage"] / "rollback").exists()


def test_promotion_wrong_type_refuses_before_any_target_move(tmp_path: Path) -> None:
    paths = _synthetic_promotion_tree(tmp_path)
    wrong = paths["data"] / source_gate.EXPECTED_TEXT_ARTIFACTS[0]
    wrong.mkdir()
    before_external = {
        path.relative_to(paths["external"]).as_posix(): path.read_bytes()
        for path in paths["external"].rglob("*")
        if path.is_file()
    }
    with pytest.raises(source_gate.GateError, match="wrong filesystem kind"):
        source_gate._promote_compile_stage(
            {},
            paths["external"],
            paths["stage"],
            paths["stage_work"],
            paths["stage_data"],
            paths["stage_evidence"],
            paths["stage_manifest"],
            paths["data"],
            paths["evidence"],
            paths["candidate"],
        )
    assert wrong.is_dir()
    assert before_external == {
        path.relative_to(paths["external"]).as_posix(): path.read_bytes()
        for path in paths["external"].rglob("*")
        if path.is_file()
    }
    assert not (paths["stage"] / "rollback").exists()


def test_authority_snapshot_allows_only_exact_first_candidate_transition() -> None:
    base = {"candidate": {"state": "absent"}, "freeze": {"sha256": "a" * 64}}
    created = {
        "candidate": {"state": "directory", "files": {"manifest": {"sha256": "b" * 64}}},
        "freeze": {"sha256": "a" * 64},
    }
    source_gate.require_same_authority_snapshot(
        base, created, allow_candidate_creation=True
    )
    with pytest.raises(source_gate.GateError, match="approval-candidate"):
        source_gate.require_same_authority_snapshot(
            base, created, allow_candidate_creation=False
        )
    drifted = deepcopy(created)
    drifted["freeze"]["sha256"] = "c" * 64
    with pytest.raises(source_gate.GateError, match="authority closure"):
        source_gate.require_same_authority_snapshot(
            base, drifted, allow_candidate_creation=True
        )


def test_target_set_lock_contends_across_real_processes(tmp_path: Path) -> None:
    target = tmp_path / "shared-target"
    ready = tmp_path / "ready"
    release = tmp_path / "release"
    code = "\n".join(
        (
            "import pathlib, sys, time",
            "sys.path.insert(0, sys.argv[1])",
            "import source_gate",
            "target, ready, release = map(pathlib.Path, sys.argv[2:5])",
            "with source_gate.exclusive_target_set_lock([target], 'child'):",
            "    ready.write_text('ready', encoding='utf-8')",
            "    while not release.exists(): time.sleep(0.02)",
        )
    )
    temp_a = tmp_path / "temp-a"
    temp_b = tmp_path / "temp-b"
    temp_a.mkdir()
    temp_b.mkdir()
    environment_a = dict(source_gate.os.environ)
    environment_b = dict(source_gate.os.environ)
    environment_a.update({"TEMP": str(temp_a), "TMP": str(temp_a)})
    environment_b.update({"TEMP": str(temp_b), "TMP": str(temp_b)})
    process = source_gate.subprocess.Popen(
        [
            source_gate.sys.executable,
            "-I",
            "-B",
            "-c",
            code,
            str(Path(source_gate.__file__).resolve().parent),
            str(target),
            str(ready),
            str(release),
        ],
        cwd=source_gate.REPO_ROOT,
        env=environment_a,
        stderr=source_gate.subprocess.PIPE,
        stdout=source_gate.subprocess.PIPE,
        text=True,
    )
    try:
        deadline = source_gate.time.monotonic() + 10.0
        while not ready.is_file() and process.poll() is None:
            if source_gate.time.monotonic() > deadline:
                pytest.fail("child target lock did not become ready")
            source_gate.time.sleep(0.02)
        assert process.poll() is None
        contender_code = "\n".join(
            (
                "import pathlib, sys",
                "sys.path.insert(0, sys.argv[1])",
                "import source_gate",
                "target = pathlib.Path(sys.argv[2])",
                "try:",
                "    with source_gate.exclusive_target_set_lock([target], 'contender'):",
                "        print('ACQUIRED')",
                "except source_gate.ConcurrentAcquisitionError:",
                "    print('BLOCKED')",
            )
        )
        contender = source_gate.subprocess.run(
            [
                source_gate.sys.executable,
                "-I",
                "-B",
                "-c",
                contender_code,
                str(Path(source_gate.__file__).resolve().parent),
                str(target),
            ],
            cwd=source_gate.REPO_ROOT,
            env=environment_b,
            capture_output=True,
            text=True,
            timeout=10,
        )
        assert contender.returncode == 0, contender.stderr
        assert contender.stdout.strip() == "BLOCKED"
    finally:
        release.write_text("release", encoding="utf-8")
        try:
            process.wait(timeout=10)
        except source_gate.subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)
            pytest.fail("child target-lock process did not terminate")
    child_stdout, child_stderr = process.communicate()
    assert process.returncode == 0, child_stderr
    assert child_stdout == ""
    after_release = source_gate.subprocess.run(
        [
            source_gate.sys.executable,
            "-I",
            "-B",
            "-c",
            contender_code,
            str(Path(source_gate.__file__).resolve().parent),
            str(target),
        ],
        cwd=source_gate.REPO_ROOT,
        env=environment_b,
        capture_output=True,
        text=True,
        timeout=10,
    )
    assert after_release.returncode == 0, after_release.stderr
    assert after_release.stdout.strip() == "ACQUIRED"


def test_forged_public_compile_command_fails_before_any_mutation(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    external = tmp_path / "external"
    external.mkdir()
    freeze = external / source_gate.FREEZE_NAME
    write_json(freeze, {"acquisition_completed_utc": "2026-08-21T00:00:00Z"})
    data = tmp_path / "data"
    evidence = tmp_path / "evidence"
    monkeypatch.setattr(
        source_gate,
        "validate_frozen_run_closure",
        lambda *_args, **_kwargs: {
            "external_run": external,
            "freeze_path": freeze,
            "registry": registry(),
        },
    )
    forged = source_gate.compile_command_context(
        source_gate.DEFAULT_REGISTRY, external, data, evidence
    )
    forged["argv"][forged["argv"].index("--data-dir") + 1] = str(
        (tmp_path / "forged-data").resolve()
    )
    before = freeze.read_bytes()
    with pytest.raises(source_gate.GateError, match="arguments do not match"):
        source_gate.compile_artifacts(
            source_gate.DEFAULT_REGISTRY,
            external,
            data,
            evidence,
            producer_command=forged,
        )
    assert freeze.read_bytes() == before
    assert not data.exists() and not evidence.exists()
    assert not (external / source_gate.OPERATION_LOCK_NAME).exists()
    assert not list(tmp_path.glob("traffic_v2_compile_*"))


def test_candidate_event_is_freeze_derived_and_all_future_times_fail(
    tmp_path: Path,
) -> None:
    event = "2026-08-21T02:02:47.000000Z"
    write_json(
        tmp_path / source_gate.FREEZE_NAME,
        {"acquisition_completed_utc": event},
    )
    assert source_gate.frozen_candidate_event_utc(tmp_path) == event
    assert source_gate.frozen_candidate_event_utc(tmp_path) == event
    future = (
        source_gate.dt.datetime.now(source_gate.dt.timezone.utc)
        + source_gate.dt.timedelta(days=1)
    ).isoformat(timespec="microseconds").replace("+00:00", "Z")
    write_json(
        tmp_path / "approval_candidate/candidate_manifest.json",
        {"candidate_event_utc": future},
    )
    with pytest.raises(source_gate.GateError, match="future-dated"):
        source_gate.frozen_candidate_event_utc(tmp_path)


def test_external_record_collector_rejects_conflicting_exact_claims() -> None:
    first = {"bytes": 1, "path": "raw/source.bin", "sha256": "a" * 64}
    second = {"bytes": 1, "path": "raw/source.bin", "sha256": "b" * 64}
    with pytest.raises(source_gate.GateError, match="Conflicting external file records"):
        source_gate.collect_external_file_records({"first": first, "second": second})
