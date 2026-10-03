[CmdletBinding()]
param(
    [string]$Destination = (Join-Path (Split-Path $PSScriptRoot -Parent) '..\T2-QuestFlight-product-artifacts\tester-feedback'),
    [string]$Adb = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
)
$ErrorActionPreference='Stop'
$destinationPath=[IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
& $Adb pull '/sdcard/Android/data/com.alexoviedo.t2.questflight/files/Feedback' $destinationPath
if($LASTEXITCODE -ne 0){throw 'No feedback exported. Check the connected headset and mark an issue from Flight tools first.'}
& $Adb pull '/sdcard/Android/data/com.alexoviedo.t2.questflight/files/flight-history.json' $destinationPath
Write-Output "Local feedback exported to $destinationPath. Nothing uploaded or removed from the headset."
