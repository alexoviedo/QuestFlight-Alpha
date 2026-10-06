[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$ArtifactDir,
  [string]$ProjectPath=(Join-Path (Split-Path $PSScriptRoot -Parent) 'QuestFlight'),
  [string]$UnityExe='C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe',
  [switch]$BuildApk
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$ArtifactDir=[IO.Path]::GetFullPath($ArtifactDir)
$ProjectPath=[IO.Path]::GetFullPath($ProjectPath)
if($ArtifactDir.StartsWith($ProjectPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Validation artifacts must be outside the Unity project'}
New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
$results=[Collections.Generic.List[object]]::new()
function Stage([string]$Name,[scriptblock]$Work) {
  try {& $Work;$results.Add([ordered]@{stage=$Name;status='PASS'})}
  catch {$results.Add([ordered]@{stage=$Name;status='FAIL';reason=$_.Exception.Message});Write-Warning $_.Exception.Message}
}
function UnityMethod([string]$Method,[string]$Log) {
  if(Get-Process Unity -ErrorAction SilentlyContinue){throw 'Another Unity process is running'}
  $process=Start-Process $UnityExe -ArgumentList @('-batchmode','-buildTarget','Android','-projectPath',$ProjectPath,'-executeMethod',$Method,'-logFile',(Join-Path $ArtifactDir $Log)) -PassThru -WindowStyle Hidden
  if(-not $process.WaitForExit(1200000)){Stop-Process -Id $process.Id;throw "$Method timed out"}
  if($process.ExitCode -ne 0){throw "$Method failed: $($process.ExitCode)"}
}
$old=$env:QFL_SPACEWARP_ARTIFACTS
$oldClosure=$env:QFL_DEPENDENCY_CLOSURE_DIR
try {
  $env:QFL_SPACEWARP_ARTIFACTS=$ArtifactDir
  $env:QFL_DEPENDENCY_CLOSURE_DIR=Join-Path $ArtifactDir 'closure'
  Stage 'production integrity' {UnityMethod 'QuestFlight.Editor.ProductIntegrityValidator.ValidateBatch' 'integrity.log'}
  Stage 'dependency closure' {UnityMethod 'T2.QuestFlight.Editor.ProductionDependencyClosureExporter.ExportBatch' 'closure.log'}
  Stage 'SpaceWarp coverage' {UnityMethod 'QuestFlight.Editor.ProductSpaceWarpAudit.RunBatch' 'spacewarp_coverage.log'}
  Stage 'EditMode' {& (Join-Path $PSScriptRoot 'run_unity_tests.ps1') -ProjectPath $ProjectPath -UnityExe $UnityExe -TestPlatform EditMode -TestFilter QuestFlight -ArtifactDir (Join-Path $ArtifactDir 'editmode') -TimeoutSeconds 1800}
  Stage 'PlayMode' {& (Join-Path $PSScriptRoot 'run_unity_tests.ps1') -ProjectPath $ProjectPath -UnityExe $UnityExe -TestPlatform PlayMode -TestFilter QuestFlight -ArtifactDir (Join-Path $ArtifactDir 'playmode') -TimeoutSeconds 1200}
  Stage 'standard Visual QA machine checks' {& (Join-Path $PSScriptRoot 'run_production_visual_qa.ps1') -ProjectPath $ProjectPath -UnityExe $UnityExe -ArtifactDir (Join-Path $ArtifactDir 'visual_qa') -TimeoutSeconds 1800}
  if($BuildApk){Stage 'Android build' {& (Join-Path $PSScriptRoot 'build_production_quest.ps1') -ProjectPath $ProjectPath -UnityExe $UnityExe -ArtifactDir (Join-Path $ArtifactDir 'build') -TimeoutSeconds 3600}}
} finally {
  $env:QFL_SPACEWARP_ARTIFACTS=$old
  $env:QFL_DEPENDENCY_CLOSURE_DIR=$oldClosure
  [ordered]@{stages=@($results.ToArray());stereoInspection='Separate inspected Meta simulator captures required';physicalHeadset='NOT RUN';releaseAccepted=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ArtifactDir 'current_product_validation.json') -Encoding UTF8
}
if(@($results | Where-Object status -eq 'FAIL').Count){exit 1}
