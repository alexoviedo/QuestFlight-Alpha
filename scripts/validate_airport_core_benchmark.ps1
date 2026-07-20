[CmdletBinding()]
param(
  [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe',
  [string]$ProjectPath = 'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight',
  [string]$ArtifactDir = '',
  [ValidateRange(30, 1800)]
  [int]$TimeoutSeconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $UnityExe)) { throw "Unity not found: $UnityExe" }
if (-not (Test-Path -LiteralPath $ProjectPath)) { throw "Project not found: $ProjectPath" }
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
  $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
  $ArtifactDir = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\airport_core_v4_validation_$stamp"
}

New-Item -ItemType Directory -Path $ArtifactDir -Force | Out-Null
$ArtifactDir = (Resolve-Path -LiteralPath $ArtifactDir).Path
$lock = Join-Path $ProjectPath 'Temp\UnityLockfile'
if (Test-Path -LiteralPath $lock) {
  if (Get-Process Unity -ErrorAction SilentlyContinue) {
    throw "Unity is already running; refusing to collide with the product project: $lock"
  }
  Remove-Item -LiteralPath $lock -Force
}

$previousOutput = $env:QFL_AIRPORT_CORE_V4_VALIDATION_DIR
$log = Join-Path $ArtifactDir 'unity_airport_core_v4_validation.log'
try {
  $env:QFL_AIRPORT_CORE_V4_VALIDATION_DIR = $ArtifactDir
  $arguments = @(
    '-batchmode', '-projectPath', $ProjectPath,
    '-executeMethod', 'QuestFlight.Editor.AirportCoreBenchmarkValidator.ValidateBatch',
    '-logFile', $log
  )
  $process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -PassThru -WindowStyle Hidden
  if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw "Airport-core V4 validation exceeded ${TimeoutSeconds}s. Log: $log"
  }
} finally {
  $env:QFL_AIRPORT_CORE_V4_VALIDATION_DIR = $previousOutput
}

$exitCode = [int]$process.ExitCode
$logText = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { '' }
if ($logText -match 'Application will terminate with return code ([1-9][0-9]*)') {
  $exitCode = [int]$Matches[1]
}

$reportPath = Join-Path $ArtifactDir 'airport_core_benchmark_validation.json'
if (-not (Test-Path -LiteralPath $reportPath)) {
  throw "Airport-core validator produced no report. Log: $log"
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($exitCode -ne 0 -or -not $report.passed) {
  throw "Airport-core V4 structural validation failed: $reportPath"
}

Write-Host "Airport-core V4 classification: $($report.classification)"
Write-Host "Airport-core V4 validation artifacts: $ArtifactDir"
