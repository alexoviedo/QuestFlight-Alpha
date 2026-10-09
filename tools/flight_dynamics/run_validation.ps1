param(
  [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe',
  [string]$ProjectPath = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'QuestFlight'),
  [string]$ArtifactDir = '',
  [int]$TimeoutSeconds = 600,
  [switch]$RequirePass
)

$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'scripts/process_arguments.ps1')

if (!(Test-Path -LiteralPath $UnityExe)) { throw "Unity not found: $UnityExe" }
if (!(Test-Path -LiteralPath $ProjectPath)) { throw "Unity project not found: $ProjectPath" }
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
  $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
  $ArtifactDir = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\flight_dynamics_validation_$stamp"
}

New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
$ArtifactDir = (Resolve-Path -LiteralPath $ArtifactDir).Path
$log = Join-Path $ArtifactDir 'unity_flight_dynamics_validation.log'
$lock = Join-Path $ProjectPath 'Temp\UnityLockfile'
if (Test-Path -LiteralPath $lock) {
  if (Get-Process Unity -ErrorAction SilentlyContinue) {
    throw "Unity is already running; close it before running the deterministic evidence harness. Lock: $lock"
  }
  Remove-Item -LiteralPath $lock -Force
}

$previousArtifactDir = $env:QUESTFLIGHT_FLIGHT_DYNAMICS_ARTIFACT_DIR
try {
  $env:QUESTFLIGHT_FLIGHT_DYNAMICS_ARTIFACT_DIR = $ArtifactDir
  $arguments = @(
    '-batchmode',
    '-quit',
    '-projectPath', $ProjectPath,
    '-executeMethod', 'QuestFlight.Editor.FlightDynamicsValidation.FlightDynamicsEvidenceBatch.RunBatch',
    '-logFile', $log
  )
  $process = Start-Process -FilePath $UnityExe -ArgumentList (ConvertTo-ProcessArgumentString $arguments) -PassThru -WindowStyle Hidden
  if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw "Flight-dynamics validation exceeded ${TimeoutSeconds}s and was stopped. Log: $log"
  }
  if ([int]$process.ExitCode -ne 0) {
    throw "Unity flight-dynamics validation failed with exit code $($process.ExitCode). Log: $log"
  }
}
finally {
  $env:QUESTFLIGHT_FLIGHT_DYNAMICS_ARTIFACT_DIR = $previousArtifactDir
}

$validator = Join-Path $PSScriptRoot 'validate_evidence.py'
$arguments = @($validator, '--input', (Join-Path $ArtifactDir 'flight_dynamics_evidence.json'))
if ($RequirePass) { $arguments += '--require-pass' }
& python @arguments
if ($LASTEXITCODE -ne 0) { throw "Evidence validation failed with exit code $LASTEXITCODE." }

Write-Host "Flight-dynamics validation artifacts: $ArtifactDir"
