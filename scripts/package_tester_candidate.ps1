[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Destination,
    [string]$Apk=(Join-Path (Split-Path $PSScriptRoot -Parent) 'QuestFlight/Builds/Android/QuestFlight-production.apk')
)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$source=(Resolve-Path -LiteralPath $Apk).Path
$destinationPath=[IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
$packageApk=Join-Path $destinationPath 'QuestFlight-production.apk'
if($source -eq $packageApk){throw 'Choose a package directory separate from build output.'}
Copy-Item -LiteralPath $source -Destination $packageApk -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/TESTER_GUIDE.md') -Destination $destinationPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'export_headset_feedback.ps1') -Destination $destinationPath -Force
$hash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
if((Get-FileHash -LiteralPath $packageApk -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash){throw 'Package copy hash mismatch'}
[ordered]@{
    generatedUtc=[DateTime]::UtcNow.ToString('O');apk='QuestFlight-production.apk';sha256=$hash
    bytes=(Get-Item -LiteralPath $packageApk).Length
    target='Standalone Quest 3 / Android ARM64 / Vulkan / OpenXR'
    sourceHead=(& git -C $repoRoot rev-parse HEAD)
    sourceWorktree='Includes local approved changes; see build evidence for dirty source identity.'
    hardwareAcceptance='Pending: no headset installation or test performed for this candidate.'
    guide='TESTER_GUIDE.md';feedbackExport='export_headset_feedback.ps1'
}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $destinationPath 'candidate.json') -Encoding utf8
@'
# QuestFlight test candidate

Read TESTER_GUIDE.md for controls, activities and feedback instructions.
candidate.json identifies this exact APK. This package runs offline on standalone Quest 3.

When a headset is available, install with your existing ADB setup:

    adb install -r QuestFlight-production.apk

Launch Quest Flight from the headset. Installation preserves existing app data.
Do not uninstall first unless deliberately resetting local progress/settings.

To export an issue after a session, run export_headset_feedback.ps1 with your
ADB executable and a chosen Destination. Reports remain local until you share them.
No automatic installation, uploads, microphone recording or PC runtime is required.
'@|Set-Content -LiteralPath (Join-Path $destinationPath 'README.md') -Encoding utf8
Write-Output "Verified tester package: $destinationPath ($hash)"
