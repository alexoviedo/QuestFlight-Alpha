$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$parser=Join-Path (Split-Path $PSScriptRoot -Parent) 'run_quest_temporal_visual_gate.ps1'
$tokens=$null;$parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($parser,[ref]$tokens,[ref]$parseErrors)
if($parseErrors.Count){throw 'Production parser has syntax errors.'}
foreach($name in @('Get-ObjectProperty','Get-BottleneckDiagnosis')) {
  $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
  if($null -eq $function){throw "Missing production function $name"}
  Invoke-Expression $function.Extent.Text
}
$QuestFrameBudgetMs=1000/72
# Actual Quest capture shape: direct App/GPU timing exists while utilization
# identifies the likely resource. Retain wall-time and CPU-execution limits.
foreach($case in @(
 @{gpu=.95;cpu=.5;combined=25;available=$true;prefix='Likely GPU-limited'},
 @{gpu=.7;cpu=.5;combined=25;available=$true;prefix='Mixed GPU/compositor'},
 @{gpu=.7;cpu=.5;combined=10;available=$true;prefix='No single-resource'},
 @{gpu=.95;cpu=.5;combined=25;available=$false;prefix='Likely GPU-limited'}
)) {
  $value=[pscustomobject]@{available=$true;p95GpuUtilization=$case.gpu;p95CpuUtilization=$case.cpu;p95CpuAndGpuMs=$case.combined;staleTotal=0;directGpuTimingAvailable=$case.available;p95DirectGpuMs=23.6955}
  $result=Get-BottleneckDiagnosis -Runtime $null -VrApi $value
  if(-not $result.StartsWith($case.prefix)){throw "Resource attribution changed: $result"}
  if($case.available){
    if($result -notmatch 'application GPU p95=23\.696 ms' -or $result -match 'Direct GPU milliseconds remain unavailable'){throw "Measured GPU timing falsely absent: $result"}
    if($result -notmatch 'CPU execution milliseconds remain unmeasured'){throw 'Wall timing was presented as CPU execution.'}
  } elseif($result -notmatch 'Direct GPU milliseconds remain unavailable'){throw 'Unavailable GPU timing claimed present.'}
}
Write-Output 'PASS4 scoped production diagnosis cases; no adb, Editor, artifact mutation or acceptance changes.'
