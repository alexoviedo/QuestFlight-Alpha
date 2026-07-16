[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$parser = Join-Path $repoRoot 'scripts\run_quest_temporal_visual_gate.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("questflight_vrapi_parser_" + [Guid]::NewGuid().ToString('N'))
$logcat = Join-Path $testRoot 'logcat.txt'
$evidence = Join-Path $testRoot 'evidence'
$output = Join-Path $testRoot 'output'

try {
  New-Item -ItemType Directory -Force -Path $evidence | Out-Null
  @'
07-16 15:00:00.000  4242  4243 I Unity   : [QuestFlight][TemporalVisualGate] Measurement started
07-16 15:00:00.100  9999 10000 I VrApi   : FPS=72/72,Tear=9,Stale=9,Fov=0,CPU4/GPU=1/1,1000/400MHz,PLS=0,Temp=30.0C/0.0C,App=20.00ms,GD=0.00ms,CPU&GPU=20.00ms,GPU%=0.99,CPU%=0.99,CFL=20.00/20.00,ICFLp95=20.00,SF=0.60
07-16 15:00:01.000  4242  4244 I VrApi   : FPS=72/72,Tear=0,Stale=0,Fov=0,CPU4/GPU=2/2,1382/456MHz,PLS=0,Temp=31.0C/0.0C,App=8.00ms,GD=0.00ms,CPU&GPU=11.00ms,GPU%=0.60,CPU%=0.20,CFL=13.80/18.00,ICFLp95=17.00,SF=1.00
07-16 15:00:02.000  4242  4244 I VrApi   : FPS=72/72,Tear=0,Stale=0,Fov=2,CPU4/GPU=2/2,1382/456MHz,PLS=0,Temp=31.0C/0.0C,App=9.00ms,GD=0.00ms,CPU&GPU=12.00ms,GPU%=0.70,CPU%=0.30,CFL=13.90/18.10,ICFLp95=17.10,SF=1.00
07-16 15:00:03.000  4242  4243 I Unity   : [QuestFlight][TemporalVisualGate] Evidence written: /sdcard/evidence.json
'@ | Set-Content -LiteralPath $logcat -Encoding UTF8

  & powershell -NoProfile -ExecutionPolicy Bypass -File $parser `
    -ParseOnlyLogcat $logcat -ParseOnlyEvidenceDir $evidence -OutputDir $output
  if ($LASTEXITCODE -ne 0) { throw "Parser-only process failed with exit code $LASTEXITCODE" }

  $summary = Get-Content -LiteralPath (Join-Path $output 'quest_temporal_visual_gate_summary.json') -Raw | ConvertFrom-Json
  $vrApi = $summary.vrApiAppPidWindow
  if (-not $vrApi.available) { throw 'Expected VrApi evidence to be available.' }
  if ([int]$vrApi.appPid -ne 4242) { throw "Expected app PID 4242; got $($vrApi.appPid)." }
  if ([int]$vrApi.sampleCount -ne 2) { throw "Expected exactly two app samples; got $($vrApi.sampleCount)." }
  if (@($vrApi.observedFovFieldValues).Count -ne 2 -or
      @($vrApi.observedFovFieldValues) -notcontains 0 -or
      @($vrApi.observedFovFieldValues) -notcontains 2) {
    throw "Expected Fov telemetry values 0 and 2; got $(@($vrApi.observedFovFieldValues) -join ',')."
  }
  if ([int]$vrApi.staleTotal -ne 0 -or [int]$vrApi.tearTotal -ne 0) {
    throw 'A compositor row from a different PID leaked into the app summary.'
  }
  Write-Host 'Quest VrApi parser regression tests passed: current Fov=0 and legacy Fov=2 app rows accepted; other-PID compositor row rejected.'
}
finally {
  Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
