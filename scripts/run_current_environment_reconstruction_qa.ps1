[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string]$ArtifactDir,
  [string]$ArtifactSourceRoot = '',
  [string]$PerformanceEvidence = '',
  [string]$PythonExe = 'python'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$generator = Join-Path $repoRoot 'tools\environment_reconstruction_qa\generate_current_reports.py'
$gate = Join-Path $PSScriptRoot 'run_environment_reconstruction_qa.ps1'
$inputDir = Join-Path $ArtifactDir 'current_inputs'
$gateDir = Join-Path $ArtifactDir 'gate'
New-Item -ItemType Directory -Force -Path $inputDir, $gateDir | Out-Null

$arguments = @($generator, '--repo-root', $repoRoot, '--output-dir', $inputDir)
if (-not [string]::IsNullOrWhiteSpace($ArtifactSourceRoot)) {
  $arguments += @('--artifact-root', $ArtifactSourceRoot)
}
if (-not [string]::IsNullOrWhiteSpace($PerformanceEvidence)) {
  $arguments += @('--performance-evidence', $PerformanceEvidence)
}

& $PythonExe @arguments
if ($LASTEXITCODE -ne 0) {
  throw "Current reconstruction report adapter failed with exit code $LASTEXITCODE."
}

# The current V2 state is expected to fail closed while integration evidence is
# missing. Preserve the validator's nonzero result for automation and callers.
& $gate -InputDir $inputDir -ArtifactDir $gateDir -PythonExe $PythonExe
