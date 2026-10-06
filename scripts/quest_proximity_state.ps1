# Development tooling only. Command mapping verified against installed MQDH:
# disableProximitySensor -> prox_close; enableProximitySensor -> automation_disable.
# These commands do not disable Guardian or synthesize room/head tracking.
function Get-QuestVirtualProximityState {
  param([Parameter(Mandatory=$true)][string]$Dump)
  if ($Dump -notmatch '(?m)^Virtual proximity state:\s*(CLOSE|FAR|DISABLED)\s*$') {
    throw 'Cannot read Quest proximity override; refusing to change an unknown power state.'
  }
  return $Matches[1]
}

function Get-QuestProximityRestoreAction {
  param(
    [Parameter(Mandatory=$true)][ValidateSet('CLOSE','FAR','DISABLED')][string]$PreviousState,
    [switch]$KeepAwake
  )
  if ($KeepAwake -or $PreviousState -eq 'CLOSE') { return 'com.oculus.vrpowermanager.prox_close' }
  if ($PreviousState -eq 'FAR') { return 'com.oculus.vrpowermanager.prox_far' }
  return 'com.oculus.vrpowermanager.automation_disable'
}
