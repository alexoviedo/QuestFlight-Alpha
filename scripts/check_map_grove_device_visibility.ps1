[CmdletBinding()]
param(
  [string]$AdbExe='C:/Program Files/Meta Quest Developer Hub/resources/bin/adb.exe',
  [string]$DeviceSerial='2G0YC5ZG8907TD',
  [Parameter(Mandatory=$true)][string]$OutputDir,
  [int[]]$Stations=@(0,1,2,3,4,5,6,7,8),
  [switch]$KeepHeadsetAwake
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'quest_proximity_state.ps1')
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
function Adb([string[]]$Arguments) {
  $result=& $AdbExe -s $DeviceSerial @Arguments 2>&1
  if($LASTEXITCODE -ne 0){throw ($result -join "`n")}
  return $result
}
$previous=(Adb @('shell','settings','get','global','stay_on_while_plugged_in') | Out-String).Trim()
$previous | Set-Content (Join-Path $OutputDir 'stay_awake_before.txt')
$results=@()
$originalVirtualProximity=Get-QuestVirtualProximityState -Dump ((Adb @('shell','dumpsys','oculus.internal.power.IVrPowerManager/default')) | Out-String)
$originalVirtualProximity | Set-Content (Join-Path $OutputDir 'virtual_proximity_before.txt')
try {
  [void](Adb @('shell','svc','power','stayon','usb'))
  [void](Adb @('shell','am','broadcast','-a','com.oculus.vrpowermanager.prox_close'))
  [void](Adb @('shell','input','keyevent','KEYCODE_WAKEUP'))
  Start-Sleep -Seconds 2
  foreach($station in $Stations) {
    if($station -lt 0 -or $station -gt 8){throw 'Unsupported authored observation station'}
    $directory=Join-Path $OutputDir ('station_{0:00}' -f $station)
    & (Join-Path $PSScriptRoot 'run_connected_grove_observation.ps1') -AdbExe $AdbExe -DeviceSerial $DeviceSerial -Variant candidate -Station $station -VisibilityDiagnostic -OutputDir $directory -TimeoutSeconds 150
    if($LASTEXITCODE -ne 0){throw "Observation startup failed at $station"}
    $evidence=Get-Content (Join-Path $directory "candidate_${station}_evidence.json") -Raw | ConvertFrom-Json
    if(!$evidence.mapGroveActive -or !$evidence.mapGroveCameraMatches -or $evidence.mapGroveLoadFailures -gt 0){throw "Map grove runtime validation failed at $station"}
    [void](Adb @('shell','screencap','-p','/sdcard/qfl_map_grove_visibility.png'))
    $capture=Join-Path $directory 'device_stereo_visibility.png'
    [void](Adb @('pull','/sdcard/qfl_map_grove_visibility.png',$capture))
    $results+=[pscustomobject]@{station=$station;selected=$evidence.mapGroveSelectedCells;eligible=$evidence.mapGroveEligibleCells;draws=$evidence.mapGroveDraws;pages=$evidence.mapGroveResidentPages;screenshotBytes=(Get-Item $capture).Length;status='UNWORN_RUNTIME_AND_CAPTURE_REQUIRES_PIXEL_INSPECTION'}
    $results | ConvertTo-Json | Set-Content (Join-Path $OutputDir 'runtime_visibility.json')
    Write-Output ("Station {0}: {1} cells, {2} draws, capture {3} bytes" -f $station,$evidence.mapGroveSelectedCells,$evidence.mapGroveDraws,(Get-Item $capture).Length)
    if(!$evidence.mapGroveReady -or $evidence.mapGroveDraws -lt 1){throw "No grove draw was submitted in the tracked view at station $station"}
    # The diagnostic expires on its own. Retain the normal rendering too; a
    # highlighted draw is visibility evidence, not an appearance acceptance.
    Start-Sleep -Seconds 15
    [void](Adb @('shell','screencap','-p','/sdcard/qfl_map_grove_visibility.png'))
    [void](Adb @('pull','/sdcard/qfl_map_grove_visibility.png',(Join-Path $directory 'device_stereo_normal.png')))
  }
} catch {
  $failure=$_
  $focus=& $AdbExe -s $DeviceSerial shell dumpsys activity activities 2>&1
  $focus | Set-Content (Join-Path $OutputDir 'failure_activity_state.txt')
  & $AdbExe -s $DeviceSerial shell screencap -p /sdcard/qfl_map_grove_visibility.png 2>&1 | Out-Null
  & $AdbExe -s $DeviceSerial pull /sdcard/qfl_map_grove_visibility.png (Join-Path $OutputDir 'failure_device_screen.png') 2>&1 | Out-Null
  [ordered]@{status='FAILED_NO_HEADSET_VISIBILITY_ACCEPTANCE';utc=[DateTime]::UtcNow.ToString('O');reason=$failure.Exception.Message;focusedActivity=@($focus | Select-String 'mFocusedApp=' | ForEach-Object {$_.Line.Trim()})} |
    ConvertTo-Json -Depth 3 | Set-Content (Join-Path $OutputDir 'failure.json')
  throw $failure
} finally {
  & $AdbExe -s $DeviceSerial shell am force-stop com.alexoviedo.t2.questflight | Out-Null
  & $AdbExe -s $DeviceSerial shell rm -f /sdcard/Android/data/com.alexoviedo.t2.questflight/files/qfl_acceptance_request.json | Out-Null
  $restoreAction=Get-QuestProximityRestoreAction -PreviousState $originalVirtualProximity -KeepAwake:$KeepHeadsetAwake
  & $AdbExe -s $DeviceSerial shell am broadcast -a $restoreAction | Out-Null
  "Previous=$originalVirtualProximity; KeepAwake=$KeepHeadsetAwake; sent=$restoreAction" | Set-Content (Join-Path $OutputDir 'quest_power_automation_restore.txt')
  if(!$previous -or $previous -eq 'null'){& $AdbExe -s $DeviceSerial shell settings delete global stay_on_while_plugged_in | Out-Null}
  else{& $AdbExe -s $DeviceSerial shell settings put global stay_on_while_plugged_in $previous | Out-Null}
  & $AdbExe -s $DeviceSerial shell settings get global stay_on_while_plugged_in | Set-Content (Join-Path $OutputDir 'stay_awake_restored.txt')
  & $AdbExe -s $DeviceSerial shell dumpsys oculus.internal.power.IVrPowerManager/default | Set-Content (Join-Path $OutputDir 'virtual_proximity_after.txt')
}
