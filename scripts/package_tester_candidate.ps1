[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Destination,
    [Parameter(Mandatory=$true)][string]$BuildManifest,
    [string]$Apk=(Join-Path (Split-Path $PSScriptRoot -Parent) 'QuestFlight/Builds/Android/QuestFlight-production.apk')
)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$source=(Resolve-Path -LiteralPath $Apk).Path
$manifestPath=(Resolve-Path -LiteralPath $BuildManifest).Path
$build=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$hash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
if ($build.sha256 -ne $hash -or $build.bytes -ne (Get-Item -LiteralPath $source).Length) { throw 'Build manifest does not identify the supplied APK bytes.' }
$evidenceRoot=Split-Path $manifestPath -Parent
$evidenceFiles=@($build.sourceIdentity,$build.sourceChanges,$build.sourceChangesArchive,$build.sceneryPayloadInventory)
foreach ($name in $evidenceFiles) {
    if ([string]::IsNullOrWhiteSpace($name) -or [IO.Path]::GetFileName($name) -ne $name) { throw 'Build source evidence must use relative filenames.' }
    if (-not (Test-Path -LiteralPath (Join-Path $evidenceRoot $name) -PathType Leaf)) { throw "Build source evidence missing: $name" }
    $expected=@($build.sourceEvidence | Where-Object file -eq $name)
    if ($expected.Count -ne 1 -or $expected[0].sha256 -ne (Get-FileHash -LiteralPath (Join-Path $evidenceRoot $name) -Algorithm SHA256).Hash.ToLowerInvariant()) { throw "Build source evidence hash missing or mismatched: $name" }
}
$identity=Get-Content -LiteralPath (Join-Path $evidenceRoot $build.sourceIdentity) -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($identity.head) -or $identity.head -ne $build.sourceHead) { throw 'Build manifest and source identity disagree.' }
$destinationPath=[IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
$packageApk=Join-Path $destinationPath 'QuestFlight-production.apk'
if($source -eq $packageApk){throw 'Choose a package directory separate from build output.'}
Copy-Item -LiteralPath $source -Destination $packageApk -Force
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $destinationPath 'product_apk_manifest.json') -Force
foreach ($name in $evidenceFiles) { Copy-Item -LiteralPath (Join-Path $evidenceRoot $name) -Destination (Join-Path $destinationPath $name) -Force }
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/TESTER_GUIDE.md') -Destination $destinationPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'export_headset_feedback.ps1') -Destination $destinationPath -Force
$hash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
if((Get-FileHash -LiteralPath $packageApk -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash){throw 'Package copy hash mismatch'}
[ordered]@{
    generatedUtc=[DateTime]::UtcNow.ToString('O');apk='QuestFlight-production.apk';sha256=$hash
    bytes=(Get-Item -LiteralPath $packageApk).Length
    target='Standalone Quest 3 / Android ARM64 / Vulkan / OpenXR'
    sourceHead=$identity.head
    buildManifest='product_apk_manifest.json';sourceIdentity=$build.sourceIdentity
    sourceEvidence=@($evidenceFiles | ForEach-Object { [ordered]@{file=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $destinationPath $_) -Algorithm SHA256).Hash.ToLowerInvariant()} })
    sourceWorktree=$(if(@($identity.changes).Count){'Build includes recorded local source changes; approval is not asserted.'}else{'Recorded build source is clean.'})
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
