[CmdletBinding()]
param(
  [string]$AdbExe='C:/Program Files/Meta Quest Developer Hub/resources/bin/adb.exe',
  [string]$DeviceSerial='',
  [ValidateSet('candidate','baseline','source_geometry')][string]$Variant='candidate',
  [ValidateRange(0,8)][int]$Station=0,
  [switch]$VisibilityDiagnostic,
  [string]$OutputDir='',
  [int]$TimeoutSeconds=150
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$package='com.alexoviedo.t2.questflight'
if(-not $OutputDir){$OutputDir=Join-Path (Split-Path $PSScriptRoot -Parent) '../T2-QuestFlight-product-artifacts/connected_grove_pilot_20260909/observations'}
$OutputDir=[IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$deviceArguments=@()
if($DeviceSerial){$deviceArguments=@('-s',$DeviceSerial)}
function Adb([string[]]$Arguments){
  $result=& $AdbExe @deviceArguments @Arguments 2>&1
  if($LASTEXITCODE -ne 0){throw ($result -join "`n")}
  return $result
}
$profile=if($Variant -eq 'baseline'){'connected_grove_off'}elseif($Variant -eq 'source_geometry'){'grove_source_geometry'}else{'production'}
$request=Join-Path $OutputDir "${Variant}_${Station}_request.json"
[ordered]@{schema='questflight.acceptance-request.v1';expiresUtc=[DateTime]::UtcNow.AddMinutes(10).ToString('O');isolationProfile=$profile;startDemo=$false;groveObservation=$true;groveObservationStation=$Station;groveVisibilityDiagnostic=[bool]$VisibilityDiagnostic} |
  ConvertTo-Json | Set-Content -LiteralPath $request -Encoding utf8NoBOM
$observationReady=$false
$log=@()
try {
[void](Adb @('shell','am','force-stop',$package))
[void](Adb @('push',$request,"/sdcard/Android/data/$package/files/qfl_acceptance_request.json"))
[void](Adb @('logcat','-c'))
[void](Adb @('shell','am','start','-n',"$package/com.unity3d.player.UnityPlayerGameActivity",'--es','qfl_scenery_mode','production_vertical_slice_v2','--es','qfl_environment_isolation',$profile))
Write-Output "Waiting for native scene readiness and the $Variant observation, station $Station. This is appearance-only, with flight paused."
$started=[DateTime]::UtcNow
while(([DateTime]::UtcNow-$started).TotalSeconds -lt $TimeoutSeconds){
  Start-Sleep -Seconds 3
  $log=Adb @('logcat','-d','-v','threadtime')
  if($log -match '\[ConnectedGroveObservation\] READY'){
    $log | Set-Content -LiteralPath (Join-Path $OutputDir "${Variant}_${Station}_logcat.txt")
    [void](Adb @('pull',"/sdcard/Android/data/$package/files/connected_grove_observation.json",(Join-Path $OutputDir "${Variant}_${Station}_evidence.json")))
    [void](Adb @('shell','rm','-f',"/sdcard/Android/data/$package/files/qfl_acceptance_request.json"))
    $observationReady=$true
    Write-Output 'READY. Authored map observation loaded. Inspect submitted-cell evidence and the actual pilot view; position readiness alone is not visual acceptance. Restarting without a request restores normal startup.'
    exit 0
  }
  if($log -match '\[ConnectedGroveObservation\].*(failed|Missing)' -or $log -match 'Readiness FAIL'){
    $log | Set-Content -LiteralPath (Join-Path $OutputDir "${Variant}_${Station}_logcat.txt")
    throw 'Observation did not reach its native/readiness gate; inspect the retained log.'
  }
}
throw 'Timed out waiting for the observation. Inspect the retained activity state and screenshot before attributing the failure to tracking or scenery.'
} catch {
  # Capture focus while the app still exists; the finally block stops failed runs.
  $failure=$_
  & $AdbExe @deviceArguments shell dumpsys activity activities 2>&1 |
    Set-Content -LiteralPath (Join-Path $OutputDir "${Variant}_${Station}_failure_activity.txt")
  & $AdbExe @deviceArguments shell screencap -p /sdcard/qfl_observation_failure.png 2>&1 | Out-Null
  & $AdbExe @deviceArguments pull /sdcard/qfl_observation_failure.png (Join-Path $OutputDir "${Variant}_${Station}_failure_screen.png") 2>&1 | Out-Null
  throw $failure
} finally {
  $log | Set-Content -LiteralPath (Join-Path $OutputDir "${Variant}_${Station}_logcat.txt")
  # Never leave a queued observation to surprise a later ordinary launch.
  & $AdbExe @deviceArguments shell rm -f "/sdcard/Android/data/$package/files/qfl_acceptance_request.json" 2>&1 | Out-Null
  if(-not $observationReady){& $AdbExe @deviceArguments shell am force-stop $package 2>&1 | Out-Null}
}
