"""Exercise the real runner orchestration with mocked build/runtime boundaries only.

No Unity, simulator, device, real registry, or actual persistent configuration access.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest


SCRIPTS = Path(__file__).resolve().parents[1]


class BuildOnlyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.pwsh = shutil.which("pwsh")
        if cls.pwsh is None:
            raise RuntimeError("PowerShell 7 is required for the real runner orchestration test.")
        source = (SCRIPTS / "run_meta_xr_simulator_qa.ps1").read_text(encoding="utf-8-sig")
        replacements = {
            "Get-MetaXrSimulatorInstallation": """
    if ($env:QFL_TEST_NO_INSTALL) { return $null }
    [pscustomobject]@{Version=[version]'201.0';Manifest='mock-manifest';Executable='mock-frontend';Directory='mock-install'}
""",
            "Get-OpenXrRegistrySnapshot": """
    $script:snapshotCount++
    $runtime = if ($env:QFL_TEST_REGISTRY_MUTATION -and $script:snapshotCount -gt 1) {'changed'} else {'original-runtime'}
    [ordered]@{runtime64=$runtime;runtime32=$null}
""",
            "Get-PreviousOpenXrRuntime64": """
    $script:previousCount++
    if ($env:QFL_TEST_PREVIOUS_MUTATION -and $script:previousCount -gt 1) { return 'changed-previous' }
    return $null
""",
            "Build-SimulatorDevelopmentPlayer": """
    if ($env:QFL_TEST_BUILD_FAIL) { throw 'mock build failed' }
    New-Item -ItemType Directory -Path (Split-Path $playerPath -Parent) -Force | Out-Null
    Set-Content -LiteralPath $playerPath -Value 'not-an-executable: mock build boundary' -NoNewline
    @{sourceIdentitySha256=$currentSourceIdentitySha256;playerSha256=(Get-FileHash -LiteralPath $playerPath).Hash.ToLowerInvariant()} |
        ConvertTo-Json | Set-Content -LiteralPath $playerIdentityPath
    if ($env:QFL_TEST_PERSISTENT_MUTATION) {
        Set-Content -LiteralPath $persistentPath -Value '{"external_change":true}' -NoNewline
    }
""",
            "Restore-MetaOpenXrRuntimeSnapshot": "throw 'UNSAFE: runtime restoration invoked by build-only'",
            "Invoke-SimulatorSession": "throw 'UNSAFE: runtime session invoked by build-only'",
            "Set-RegistryOpenXrRuntime": "throw 'UNSAFE: registry write invoked by build-only'",
            "Set-SessionCaptureConfiguration": "throw 'UNSAFE: persistent configuration write invoked by build-only'",
        }
        for name, body in replacements.items():
            pattern = rf"(?ms)^function {re.escape(name)} \{{.*?^\}}"
            source, count = re.subn(pattern, f"function {name} {{\n{body}\n}}", source)
            if count != 1:
                raise AssertionError(f"Expected exactly one mocked boundary: {name}")
        # Keep parameter validation and all real orchestration branches intact.
        injected = """
$script:snapshotCount = 0
$script:previousCount = 0
function python {
    $identityOutput = $args[-1]
    New-Item -ItemType Directory -Path $identityOutput -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $identityOutput 'source_identity.json') -Value '{"mock_source":true}' -NoNewline
    $global:LASTEXITCODE = 0
}
"""
        cls.source = source.replace('Set-StrictMode -Version Latest', injected + '\nSet-StrictMode -Version Latest', 1)

    def run_case(self, extra=(), flags=None):
        with tempfile.TemporaryDirectory(prefix="QuestFlight-build-only-test-") as directory:
            root = Path(directory)
            scripts = root / "scripts"
            scripts.mkdir()
            runner = scripts / "run_meta_xr_simulator_qa.ps1"
            runner.write_text(self.source, encoding="utf-8")
            shutil.copyfile(SCRIPTS / "simulator_evidence_policy.ps1", scripts / "simulator_evidence_policy.ps1")
            (scripts / "setup_meta_xr_simulator.ps1").write_text("throw 'UNSAFE: setup called by build-only'", encoding="utf-8")
            appdata = root / "appdata"
            persistent = appdata / "MetaXR/MetaXrSimulator/persistent_data.json"
            persistent.parent.mkdir(parents=True)
            persistent.write_bytes(b'{"tracking":{"fixture":true}}')
            initial_hash = hashlib.sha256(persistent.read_bytes()).hexdigest()
            env = dict(os.environ)
            for name in list(env):
                if name.startswith("QFL_TEST_"):
                    del env[name]
            env.update(flags or {})
            env["APPDATA"] = str(appdata)
            artifacts = root / "artifacts"
            process = subprocess.run(
                [self.pwsh, "-NoProfile", "-File", str(runner), "-BuildOnly", "-RebuildPlayer",
                 "-ArtifactRoot", str(artifacts), "-DevelopmentScratchRoot", str(root / "scratch"), *extra],
                env=env, capture_output=True, text=True, timeout=30,
            )
            report_path = artifacts / "meta_xr_simulator/qa_run/simulator_qa_run_report.json"
            report = json.loads(report_path.read_text(encoding="utf-8-sig")) if report_path.exists() else None
            if report:
                self.assertTrue(report["buildOnly"])
                self.assertFalse(report["runtimeExecuted"])
                self.assertEqual(report["sessionRuns"], [])
                self.assertEqual(report["activationMethod"], "NotActivatedBuildOnly")
                self.assertFalse(report["activeRuntimeMutationDetected"])
                self.assertFalse(report["activeRuntimeRestorationRequiredUac"])
                self.assertEqual(report["activeRuntimeRestorationStrategy"], "ReadOnlyBuildOnlyStateCheck")
                self.assertNotIn("UNSAFE:", report["orchestrationError"] or "")
            current_hash = hashlib.sha256(persistent.read_bytes()).hexdigest()
            if not (flags or {}).get("QFL_TEST_PERSISTENT_MUTATION"):
                self.assertEqual(current_hash, initial_hash)
            return process, report

    def test_build_only_success_is_not_runtime_pass(self):
        process, report = self.run_case()
        self.assertEqual(process.returncode, 0, process.stdout + process.stderr)
        self.assertEqual(report["classification"], "BUILD_ONLY_RUNTIME_NOT_EXECUTED")
        self.assertTrue(report["playerBuilt"])
        self.assertTrue(report["activeRuntimePreserved"])
        self.assertTrue(report["persistentConfigurationRestored"])
        self.assertEqual(len(report["sourceIdentitySha256"]), 64)
        self.assertIn("no runtime, stereo", report["timingEvidenceScope"])

    def test_independent_build_without_simulator_installation(self):
        process, report = self.run_case(flags={"QFL_TEST_NO_INSTALL": "1"})
        self.assertEqual(process.returncode, 0, process.stdout + process.stderr)
        self.assertIsNone(report["simulatorVersion"])
        self.assertEqual(report["classification"], "BUILD_ONLY_RUNTIME_NOT_EXECUTED")

    def test_failed_build_is_not_success(self):
        process, report = self.run_case(flags={"QFL_TEST_BUILD_FAIL": "1"})
        self.assertEqual(process.returncode, 2)
        self.assertEqual(report["classification"], "FAIL_SIMULATOR_ORCHESTRATION")
        self.assertIn("mock build failed", report["orchestrationError"])

    def test_external_registry_change_is_detected_without_repair(self):
        process, report = self.run_case(flags={"QFL_TEST_REGISTRY_MUTATION": "1"})
        self.assertEqual(process.returncode, 2)
        self.assertFalse(report["activeRuntimePreserved"])
        self.assertEqual(report["classification"], "FAIL_SIMULATOR_STATE_RESTORATION")

    def test_external_previous_runtime_change_is_detected_without_repair(self):
        process, report = self.run_case(flags={"QFL_TEST_PREVIOUS_MUTATION": "1"})
        self.assertEqual(process.returncode, 2)
        self.assertFalse(report["activeRuntimePreserved"])
        self.assertEqual(report["classification"], "FAIL_SIMULATOR_STATE_RESTORATION")

    def test_external_persistent_change_is_detected_without_repair(self):
        process, report = self.run_case(flags={"QFL_TEST_PERSISTENT_MUTATION": "1"})
        self.assertEqual(process.returncode, 2)
        self.assertFalse(report["persistentConfigurationRestored"])
        self.assertEqual(report["classification"], "FAIL_SIMULATOR_STATE_RESTORATION")

    def test_incompatible_modes_rejected_before_work(self):
        for mode in [("-AuditOnly",), ("-LiveSmokeWhenRecordingsMissing",), ("-RecordSession", "cockpit_neutral")]:
            with self.subTest(mode=mode):
                process, report = self.run_case(extra=mode)
                self.assertNotEqual(process.returncode, 0)
                self.assertIsNone(report)
                self.assertIn("BuildOnly cannot request", process.stderr)


if __name__ == "__main__":
    unittest.main()
