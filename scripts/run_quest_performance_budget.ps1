[CmdletBinding()]
param(
  [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe',
  [string]$ProjectPath = 'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight',
  [string]$ArtifactDir = '',
  [string]$VisualQaReport = '',
  [ValidateRange(60, 1800)]
  [int]$TimeoutSeconds = 900,
  [switch]$SkipVisualQa
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path -LiteralPath $UnityExe)) { throw "Unity not found: $UnityExe" }
if (-not (Test-Path -LiteralPath $ProjectPath)) { throw "Project not found: $ProjectPath" }
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
  $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
  $ArtifactDir = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\quest_performance_budget_$stamp"
}
New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
$ArtifactDir = (Resolve-Path -LiteralPath $ArtifactDir).Path

if (-not $SkipVisualQa) {
  $visualDir = Join-Path $ArtifactDir 'visual_qa'
  & (Join-Path $PSScriptRoot 'run_production_visual_qa.ps1') -UnityExe $UnityExe -ProjectPath $ProjectPath `
    -ArtifactDir $visualDir -TimeoutSeconds $TimeoutSeconds
  $VisualQaReport = Join-Path $visualDir 'visual_qa_report.json'
}
if (-not [string]::IsNullOrWhiteSpace($VisualQaReport) -and -not (Test-Path -LiteralPath $VisualQaReport)) {
  throw "Visual QA report not found: $VisualQaReport"
}

$lock = Join-Path $ProjectPath 'Temp\UnityLockfile'
if (Test-Path -LiteralPath $lock) {
  if (Get-Process Unity -ErrorAction SilentlyContinue) {
    throw "Unity is already running; refusing to collide with the product project: $lock"
  }
  Remove-Item -LiteralPath $lock -Force
}

$previousOutput = $env:QFL_PERFORMANCE_BUDGET_DIR
$previousVisual = $env:QFL_VISUAL_QA_REPORT
$log = Join-Path $ArtifactDir 'unity_performance_budget.log'
try {
  $env:QFL_PERFORMANCE_BUDGET_DIR = $ArtifactDir
  $env:QFL_VISUAL_QA_REPORT = $VisualQaReport
  $arguments = @(
    '-batchmode', '-projectPath', $ProjectPath,
    '-executeMethod', 'QuestFlight.Editor.QuestPerformanceBudgetBatchRunner.RunBatch',
    '-logFile', $log
  )
  $process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -PassThru -WindowStyle Hidden
  if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw "Quest performance audit exceeded ${TimeoutSeconds}s. Log: $log"
  }
} finally {
  $env:QFL_PERFORMANCE_BUDGET_DIR = $previousOutput
  $env:QFL_VISUAL_QA_REPORT = $previousVisual
}

$exitCode = [int]$process.ExitCode
$logText = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { '' }
if ($logText -match 'Application will terminate with return code ([1-9][0-9]*)') { $exitCode = [int]$Matches[1] }
if ($exitCode -ne 0) { throw "Quest performance audit failed with exit code $exitCode. Log: $log" }

$reportPath = Join-Path $ArtifactDir 'performance_budget_report.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw "Performance audit did not produce $reportPath" }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if (-not $report.passed) { throw "Static Quest performance budget failed: $reportPath" }
Write-Host "Quest static performance classification: $($report.classification)"
Write-Host "Quest static performance artifacts: $ArtifactDir"
