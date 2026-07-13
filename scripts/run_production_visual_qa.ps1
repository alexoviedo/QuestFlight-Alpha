[CmdletBinding()]
param(
  [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe',
  [string]$ProjectPath = 'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight',
  [string]$ArtifactDir = '',
  [string]$LegacyBaselineDir = '',
  [ValidateRange(1, 1800)]
  [int]$TimeoutSeconds = 600,
  [ValidateRange(320, 4096)]
  [int]$Width = 1280,
  [ValidateRange(180, 2160)]
  [int]$Height = 720,
  [switch]$SkipPythonAnalysis
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $UnityExe)) { throw "Unity not found: $UnityExe" }
if (-not (Test-Path -LiteralPath $ProjectPath)) { throw "Project not found: $ProjectPath" }
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
  $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
  $ArtifactDir = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-extraction-artifacts\product_visual_qa_$stamp"
}

New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
$ArtifactDir = (Resolve-Path -LiteralPath $ArtifactDir).Path
$env:QFL_PRODUCTION_VISUAL_QA_DIR = $ArtifactDir
$env:QFL_PRODUCTION_VISUAL_QA_WIDTH = "$Width"
$env:QFL_PRODUCTION_VISUAL_QA_HEIGHT = "$Height"

$lock = Join-Path $ProjectPath 'Temp\UnityLockfile'
if (Test-Path -LiteralPath $lock) {
  if (Get-Process Unity -ErrorAction SilentlyContinue) {
    throw "Unity is already running; production Visual QA will not collide with the active editor: $lock"
  }
  Remove-Item -LiteralPath $lock -Force
}

$log = Join-Path $ArtifactDir 'unity_production_visual_qa.log'
$arguments = @(
  '-batchmode',
  '-quit',
  '-projectPath', $ProjectPath,
  '-executeMethod', 'QuestFlightLab.Editor.ProductionVisualQaBatchRunner.RunProductionVisualQa',
  '-logFile', $log
)
$process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
  Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
  throw "Production Visual QA exceeded ${TimeoutSeconds}s and was stopped. Log: $log"
}

$exitCode = [int]$process.ExitCode
if (Test-Path -LiteralPath $log) {
  $logText = Get-Content -LiteralPath $log -Raw
  if ($logText -match 'Application will terminate with return code ([1-9][0-9]*)') {
    $exitCode = [int]$Matches[1]
  }
}
if ($exitCode -ne 0) { throw "Production Visual QA failed with exit code $exitCode. Log: $log" }

$reportPath = Join-Path $ArtifactDir 'visual_qa_report.json'
if (-not (Test-Path -LiteralPath $reportPath)) {
  throw "Unity exited without a production visual report: $reportPath"
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$shotIds = @($report.shots | ForEach-Object { [string]$_.id })
$required = @(
  '01_default_cockpit',
  '02_calibrated_cockpit',
  '03_instrument_yoke',
  '04_runway_from_cockpit',
  '05_runway_surface_markings',
  '06_apron_hangars',
  '07_boulder_reservoir',
  '08_near_terrain',
  '09_pattern_altitude_macro',
  '10_stable_mountain_horizon',
  '11_external_aircraft_static',
  '12_external_control_sweep',
  '13_takeoff_roll',
  '14_climb',
  '15_shallow_turn',
  '16_approach'
)
$missing = @($required | Where-Object { $_ -notin $shotIds })
if ($missing.Count -gt 0) { throw "Production Visual QA omitted required view(s): $($missing -join ', ')" }

if (-not $SkipPythonAnalysis) {
  $analyzer = Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\visual_qa_analyze.py'
  if (Test-Path -LiteralPath $analyzer) {
    $analyzerArguments = @($analyzer, '--input', $ArtifactDir, '--minimum-screenshots', '16', '--fail-on-errors')
    if (-not [string]::IsNullOrWhiteSpace($LegacyBaselineDir)) {
      $analyzerArguments += @('--baseline', $LegacyBaselineDir)
    }
    python @analyzerArguments
    if ($LASTEXITCODE -ne 0) {
      throw "Production Visual QA image analysis failed with exit code $LASTEXITCODE. ArtifactDir: $ArtifactDir"
    }
  }
}

Write-Host "Production Visual QA classification: $($report.classification)"
Write-Host "Production Visual QA artifacts: $ArtifactDir"
Write-Host "Production contact sheet: $(Join-Path $ArtifactDir 'visual_qa_contact_sheet.png')"
