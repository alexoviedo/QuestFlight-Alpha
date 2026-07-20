[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string]$InputDir,
  [string]$ArtifactDir = '',
  [string]$SurfaceReport = '',
  [string]$PlacementReport = '',
  [string]$BuildingReport = '',
  [string]$VegetationReport = '',
  [string]$WaterReport = '',
  [string]$HlodReport = '',
  [string]$BudgetReport = '',
  [string]$PythonExe = 'python'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$validator = Join-Path $repoRoot 'tools\environment_reconstruction_qa\validate_environment_reconstruction.py'
if (-not (Test-Path -LiteralPath $validator -PathType Leaf)) {
  throw "Environment reconstruction validator is missing: $validator"
}
if (-not (Test-Path -LiteralPath $InputDir -PathType Container)) {
  throw "Environment reconstruction input directory is missing: $InputDir"
}
$InputDir = (Resolve-Path -LiteralPath $InputDir).Path

if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
  $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
  $ArtifactDir = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\environment_reconstruction_qa_$stamp"
}
New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
$ArtifactDir = (Resolve-Path -LiteralPath $ArtifactDir).Path

function Resolve-ReportPath {
  param([string]$Requested, [string]$DefaultName)
  if (-not [string]::IsNullOrWhiteSpace($Requested)) {
    return [IO.Path]::GetFullPath($Requested)
  }
  return Join-Path $InputDir $DefaultName
}

$resolved = [ordered]@{
  surface = Resolve-ReportPath $SurfaceReport 'production_terrain_surface_report.json'
  placement = Resolve-ReportPath $PlacementReport 'placement_validation.json'
  building = Resolve-ReportPath $BuildingReport 'building_coverage_report.json'
  vegetation = Resolve-ReportPath $VegetationReport 'vegetation_coverage_report.json'
  water = Resolve-ReportPath $WaterReport 'water_coverage_report.json'
  hlod = Resolve-ReportPath $HlodReport 'hlod_validation_report.json'
  budgets = Resolve-ReportPath $BudgetReport 'performance_budget_report.json'
}

$arguments = @($validator)
foreach ($entry in $resolved.GetEnumerator()) {
  $arguments += @("--$($entry.Key)", [string]$entry.Value)
}
$arguments += @('--output-dir', $ArtifactDir)

& $PythonExe @arguments
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
  throw "Environment reconstruction QA failed closed with exit code $exitCode. Artifacts: $ArtifactDir"
}

Write-Host "Environment reconstruction QA passed: $ArtifactDir"
