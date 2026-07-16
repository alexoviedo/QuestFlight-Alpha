[CmdletBinding()]
param(
  [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe',
  [string]$ProjectPath = 'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight',
  [string]$SourceRepo = 'C:\Users\ovied\Dev\T2\T2-QuestFlightLab',
  [string]$SourceRef = 'cd4131fa7732a6db2d7387921dfef33e2c206970',
  [string]$ArtifactDir = '',
  [switch]$RequireCleanGit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
  $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
  $ArtifactDir = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-extraction-artifacts\validate_product_$stamp"
}
New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
$ArtifactDir = (Resolve-Path -LiteralPath $ArtifactDir).Path

function Invoke-Checked {
  param([string]$Label, [scriptblock]$Command)
  Write-Host "[validate_product] $Label"
  & $Command
  if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE" }
}

function Invoke-UnityMethod {
  param([string]$Method, [string]$LogName)
  $log = Join-Path $ArtifactDir $LogName
  $arguments = @('-batchmode', '-projectPath', $ProjectPath, '-executeMethod', $Method, '-logFile', $log)
  $process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -PassThru -WindowStyle Hidden
  if (-not $process.WaitForExit(900000)) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw "$Method exceeded 900 seconds; log: $log"
  }
  if ([int]$process.ExitCode -ne 0) { throw "$Method failed with exit code $($process.ExitCode); log: $log" }
}

if (-not (Test-Path -LiteralPath $UnityExe)) { throw "Unity not found: $UnityExe" }
if (-not (Test-Path -LiteralPath $ProjectPath)) { throw "Unity project not found: $ProjectPath" }
if ((git -C $SourceRepo rev-parse $SourceRef).Trim() -ne $SourceRef) { throw 'Frozen R&D source ref mismatch' }
if ((git -C $SourceRepo rev-list -n 1 product-extraction-source-2026-07-12).Trim() -ne $SourceRef) {
  throw 'Frozen R&D tag does not peel to the source ref'
}
if (git -C $SourceRepo status --porcelain) { throw 'R&D source working tree is not clean' }

Push-Location $repoRoot
try {
  Invoke-Checked 'standalone Quest gate classification policy tests' {
    & .\scripts\tests\test_standalone_quest_gate_policy.ps1
  }
  Invoke-Checked 'Quest VrApi parser regression tests' {
    & .\scripts\tests\test_quest_temporal_vrapi_parser.ps1
  }

  Invoke-Checked 'extractor unit tests' { python -B -m unittest discover -s tools/tests -v }

  Invoke-Checked 'KBDU data pipeline unit tests' {
    python -B -m unittest discover -s tools/kbdu_environment/tests -v
  }
  Invoke-Checked 'KBDU source, license, geometry, and budget validation' {
    python -B .\tools\kbdu_environment\validate_kbdu_assets.py `
      --repo-root . `
      --report (Join-Path $ArtifactDir 'kbdu_validation.json')
  }

  $dryRun = Join-Path $ArtifactDir 'source_dry_run'
  # Compare the frozen R&D source against an empty staging destination. The
  # product intentionally replaces several extracted source files, so using the
  # live product tree here would misclassify those reviewed replacements as
  # extraction conflicts.
  $sourceStage = Join-Path $ArtifactDir 'source_staging\QuestFlight'
  New-Item -ItemType Directory -Force -Path $sourceStage | Out-Null
  & python -B .\tools\extract_from_rd.py --source-repo $SourceRepo --source-ref $SourceRef `
    --destination-project $sourceStage --manifest .\tools\extraction_manifest.json --report-dir $dryRun --dry-run
  $extractorExit = $LASTEXITCODE
  if ($extractorExit -ne 3) { throw "Expected frozen-source closure exit 3; got $extractorExit" }
  $comparison = Get-Content -LiteralPath (Join-Path $dryRun 'allowlist_comparison.json') -Raw | ConvertFrom-Json
  $inventory = Get-Content -LiteralPath (Join-Path $dryRun 'extraction_inventory.json') -Raw | ConvertFrom-Json
  if ($comparison.rejected_count -ne 1 -or $comparison.unknown_count -ne 2 -or
      @($inventory.missing_allowlist_entries).Count -ne 0) {
    throw 'Frozen-source extraction blockers differ from the reviewed 1 rejected / 2 unknown / 0 missing contract'
  }

  Invoke-Checked 'aircraft source/license/hash verification' {
    & .\scripts\sync_local_aircraft_asset.ps1 -VerifyOnly
  }

  Invoke-UnityMethod 'QuestFlight.Editor.ProductProjectConfigurator.ConfigureProjectBatch' 'unity_configure.log'
  Invoke-UnityMethod 'QuestFlight.Editor.ProductIntegrityValidator.ValidateBatch' 'unity_integrity.log'
  Invoke-UnityMethod 'T2.QuestFlight.Editor.ProductionDependencyClosureExporter.ExportBatch' 'unity_dependency_closure.log'

  & .\scripts\run_unity_tests.ps1 -TestPlatform EditMode -ArtifactDir (Join-Path $ArtifactDir 'editmode') -TimeoutSeconds 900
  & .\scripts\run_unity_tests.ps1 -TestPlatform PlayMode -ArtifactDir (Join-Path $ArtifactDir 'playmode') -TimeoutSeconds 900
  $visualQaDir = Join-Path $ArtifactDir 'visual_qa'
  & .\scripts\run_production_visual_qa.ps1 -ArtifactDir $visualQaDir -TimeoutSeconds 900
  & .\scripts\run_quest_performance_budget.ps1 -ArtifactDir (Join-Path $ArtifactDir 'performance_budget') `
    -SkipVisualQa -VisualQaReport (Join-Path $visualQaDir 'visual_qa_report.json') -TimeoutSeconds 900
  & .\scripts\build_production_quest.ps1 -ArtifactDir (Join-Path $ArtifactDir 'build') -TimeoutSeconds 1800

  $tracked = @(git ls-files)
  $forbidden = @($tracked | Where-Object {
    $_ -match '(^|/)(Library|Temp|Obj|Logs|Builds|PackageCache)(/|$)|\.(apk|aab|mp4|mov|log|zip|7z)$'
  })
  if ($forbidden.Count -gt 0) { throw "Forbidden tracked paths: $($forbidden -join ', ')" }
  $approvedJsbsimPromotion = @(
    '^QuestFlight/Packages/com\.alexoviedo\.questflight\.jsbsim/',
    '^QuestFlight/Assets/Scripts/Flight/Backends/JSBSimNativeDevelopmentBackend\.cs(\.meta)?$',
    '^QuestFlight/Assets/Editor/Tests/JSBSimPromotionEditModeTests\.cs(\.meta)?$',
    '^docs/NATIVE_JSBSIM_PROMOTION_GATE\.md$'
  ) -join '|'
  $legacy = @($tracked | Where-Object {
    $_ -match 'InputLab|QuestFirstViewRuntimeRepair|Usb2Ble|HOTAS|GaussianSplat|SplatSceneryProvider|FlightDynamicsRuntimeBootstrap|ShortPlaytestDemoPilot|FlightTelemetry' -or
    ($_ -match 'JSBSim' -and
      $_ -notmatch $approvedJsbsimPromotion)
  })
  if ($legacy.Count -gt 0) { throw "Legacy product files leaked into Git: $($legacy -join ', ')" }
  if ($RequireCleanGit -and (git status --porcelain)) { throw 'Product Git working tree is not clean' }

  [ordered]@{
    generatedUtc = [DateTime]::UtcNow.ToString('O')
    sourceRef = $SourceRef
    extractorTests = 'PASS'
    kbduPipelineTests = 'PASS'
    kbduAssetValidation = 'PASS'
    frozenSourceExpectedBlockers = 'PASS_1_REJECTED_2_UNKNOWN'
    productIntegrity = 'PASS'
    productDependencyClosure = 'PASS'
    editMode = 'PASS'
    playMode = 'PASS'
    visualQa = 'PASS_MACHINE_HUMAN_REVIEW_REQUIRED'
    staticQuestPerformanceBudget = 'PASS_PLANNING_ONLY_HEADSET_MEASUREMENT_REQUIRED'
    androidBuild = 'PASS'
    cleanGitRequired = [bool]$RequireCleanGit
  } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactDir 'validation_summary.json') -Encoding UTF8
  Write-Host "Product validation passed. Artifacts: $ArtifactDir"
} finally {
  Pop-Location
}
