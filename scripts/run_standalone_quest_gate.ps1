[CmdletBinding()]
param(
  [string]$AdbExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe',
  [string]$ProjectPath = 'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight',
  [string]$ApkPath = '',
  [string]$ArtifactDir = '',
  [string]$DeviceSerial = '',
  [switch]$Rebuild,
  [switch]$SkipInstall,
  [switch]$Unattended,
  [switch]$SkipScreenRecording,
  [switch]$StaticEvidenceOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'standalone_quest_gate_policy.ps1')
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
  $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
  $ArtifactDir = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\standalone_quest_gate_$stamp"
}
New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
$ArtifactDir = (Resolve-Path -LiteralPath $ArtifactDir).Path
$temporalDir = Join-Path $ArtifactDir 'temporal_gate'
$operatorAction = 'Put on the Quest 3, wake both Touch controllers, complete the Horizon/Guardian or controller dialog once, confirm the ProductionVerticalSlice cockpit is visible, then rerun scripts\run_standalone_quest_gate.ps1.'

function Write-GateResult {
  param(
    [string]$Classification,
    [string]$Reason,
    [string]$DialogStatus,
    [string]$TemporalSummary = '',
    [string]$ResolvedApk = ''
  )
  $apkExists = -not [string]::IsNullOrWhiteSpace($ResolvedApk) -and (Test-Path -LiteralPath $ResolvedApk)
  $disposition = Get-StandaloneQuestTemporalClassificationDisposition -Classification $Classification
  $summary = [ordered]@{
    schemaVersion = 2
    generatedUtc = [DateTime]::UtcNow.ToString('O')
    classification = $Classification
    classificationDisposition = $disposition
    shippingTarget = 'standalone Meta Quest 3 only'
    packageId = 'com.alexoviedo.t2.questflight'
    scene = 'Assets/Scenes/ProductionVerticalSlice.unity'
    questDetected = $script:questDetected
    questSerial = $script:resolvedSerial
    apkPath = $ResolvedApk
    apkBytes = $(if ($apkExists) { (Get-Item -LiteralPath $ResolvedApk).Length } else { 0 })
    apkSha256 = $(if ($apkExists) { (Get-FileHash -LiteralPath $ResolvedApk -Algorithm SHA256).Hash.ToLowerInvariant() } else { '' })
    controllerGuardianDialogStatus = $DialogStatus
    reason = $Reason
    exactHumanActionIfBlocked = $operatorAction
    warmupSeconds = 90
    measurementSeconds = 60
    temporalSummaryPath = $TemporalSummary
    evidenceBoundary = 'Only a completed physical Quest run can establish frame, stale/tear, thermal, controller, Guardian, comfort, and worn-display acceptance.'
  }
  $json = Join-Path $ArtifactDir 'standalone_quest_gate_summary.json'
  $md = Join-Path $ArtifactDir 'standalone_quest_gate_summary.md'
  $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $json -Encoding UTF8
  @(
    '# Standalone Quest Hardware Gate',
    '',
    "- Classification: ``$Classification``",
    "- Classification disposition: ``$disposition``",
    "- Quest detected / serial: $($script:questDetected) / ``$($script:resolvedSerial)``",
    "- APK: ``$ResolvedApk``",
    "- APK bytes / SHA256: $($summary.apkBytes) / ``$($summary.apkSha256)``",
    "- Controller/Guardian dialog: ``$DialogStatus``",
    "- Reason: $Reason",
    "- Temporal evidence: ``$TemporalSummary``",
    '',
    '## Exact one-time action if blocked',
    '',
    $operatorAction,
    '',
    '## Evidence boundary',
    '',
    $summary.evidenceBoundary
  ) | Set-Content -LiteralPath $md -Encoding UTF8
  Write-Host "Standalone Quest gate summary: $md"
}

$script:questDetected = $false
$script:resolvedSerial = ''
$classification = 'BLOCKED_OR_FAILED'
$dialogStatus = 'NOT_CHECKED'
$reason = ''
$temporalSummary = ''

try {
  if (-not (Test-Path -LiteralPath $AdbExe)) { throw "ADB not found: $AdbExe" }
  $devices = (& $AdbExe devices -l | Out-String)
  $devices | Set-Content -LiteralPath (Join-Path $ArtifactDir 'adb_devices.txt') -Encoding UTF8
  $unauthorized = @($devices -split "`r?`n" | Where-Object { $_ -match '^\S+\s+unauthorized\b' })
  if ($unauthorized.Count -gt 0) { throw 'Quest is connected but ADB authorization is pending inside the headset.' }
  $connected = @($devices -split "`r?`n" | Where-Object { $_ -match '^\S+\s+device\b' -and $_ -match 'model:Quest_3\b' })
  if (-not [string]::IsNullOrWhiteSpace($DeviceSerial)) {
    $connected = @($connected | Where-Object { $_ -match ('^' + [regex]::Escape($DeviceSerial) + '\s+') })
  }
  if ($connected.Count -ne 1) { throw "Expected exactly one authorized Quest 3; found $($connected.Count)." }
  $script:resolvedSerial = ($connected[0] -split '\s+')[0]
  $script:questDetected = $true

  $adbPrefix = @()
  if (-not [string]::IsNullOrWhiteSpace($script:resolvedSerial)) { $adbPrefix = @('-s', $script:resolvedSerial) }
  $preflightActivity = (& $AdbExe @adbPrefix shell dumpsys activity activities | Out-String)
  $preflightActivity | Set-Content -LiteralPath (Join-Path $ArtifactDir 'system_ui_preflight.txt') -Encoding UTF8
  $dialogStatus = if ($preflightActivity -match '(?i)(guardian|boundary|controller.*(?:dialog|pair)|horizon.*(?:dialog|permission))') {
    'POSSIBLE_SYSTEM_DIALOG_DETECTED_REQUIRES_HUMAN'
  } else {
    'NO_DIALOG_SIGNATURE_DETECTED_HUMAN_WITNESS_STILL_REQUIRED'
  }

  if ([string]::IsNullOrWhiteSpace($ApkPath)) {
    $ApkPath = Join-Path $ProjectPath 'Builds\Android\QuestFlight-production.apk'
  }
  if ($Rebuild -or -not (Test-Path -LiteralPath $ApkPath)) {
    & (Join-Path $PSScriptRoot 'build_production_quest.ps1') -ProjectPath $ProjectPath -ArtifactDir (Join-Path $ArtifactDir 'build')
  }
  if (-not (Test-Path -LiteralPath $ApkPath)) { throw "Quest APK is missing after build: $ApkPath" }

  $temporalArgs = @{
    AdbExe = $AdbExe
    ProjectPath = $ProjectPath
    ApkPath = $ApkPath
    DeviceSerial = $script:resolvedSerial
    OutputDir = $temporalDir
    SuppressOperatorPrompt = [bool]$Unattended
    NoHumanWitness = [bool]$Unattended
    SkipScreenRecording = [bool]$SkipScreenRecording
    StaticEvidenceOnly = [bool]$StaticEvidenceOnly
    SkipInstall = [bool]$SkipInstall
  }
  & (Join-Path $PSScriptRoot 'run_quest_temporal_visual_gate.ps1') @temporalArgs
  $temporalSummary = Join-Path $temporalDir 'quest_temporal_visual_gate_summary.json'
  if (-not (Test-Path -LiteralPath $temporalSummary)) { throw 'Temporal gate exited without its JSON summary.' }
  $temporal = Get-Content -LiteralPath $temporalSummary -Raw | ConvertFrom-Json
  $classification = [string]$temporal.classification
  $reason = "Physical Quest temporal gate completed with preserved classification '$classification'; inspect the machine and human-witness fields in $temporalSummary."
  $temporalGuardianDialog = $false
  if ($null -ne $temporal.faultChecks -and
      $null -ne $temporal.faultChecks.PSObject.Properties['guardianDialogDetected']) {
    $temporalGuardianDialog = [bool]$temporal.faultChecks.guardianDialogDetected
  }
  if ($temporalGuardianDialog -and $classification -eq 'FAIL_INCOMPLETE_RUNTIME_EVIDENCE') {
    $classification = 'BLOCKED_HUMAN_ACTION_REQUIRED'
    $reason = 'Horizon Guardian/tracking UI blocked product readiness. Complete the exact one-time in-headset action recorded by this gate, then rerun; no performance inference is permitted from this attempt.'
  }

  $postflightActivity = (& $AdbExe @adbPrefix shell dumpsys activity activities | Out-String)
  $postflightActivity | Set-Content -LiteralPath (Join-Path $ArtifactDir 'system_ui_postflight.txt') -Encoding UTF8
  if ($temporalGuardianDialog) {
    $dialogStatus = 'GUARDIAN_DIALOG_OBSERVED_IN_GATE_LOGCAT_REQUIRES_HUMAN'
  } elseif ($postflightActivity -match '(?i)(GuardianDialogActivity|guardian.*(?:dialog|tracking)|controller.*(?:dialog|pair))') {
    $dialogStatus = 'SYSTEM_DIALOG_LIKELY_BLOCKING_PRODUCT'
  } elseif ($postflightActivity -match 'com\.alexoviedo\.t2\.questflight') {
    $dialogStatus = 'PRODUCT_ACTIVITY_OBSERVED_ADB_HUMAN_WITNESS_REQUIRED'
  } else {
    $dialogStatus = 'PRODUCT_ACTIVITY_NOT_OBSERVED_EXACT_HUMAN_ACTION_RECORDED'
  }
} catch {
  $reason = $_.Exception.Message
  if ($dialogStatus -eq 'NOT_CHECKED') { $dialogStatus = 'BLOCKED_BEFORE_DIALOG_CHECK' }
  Write-GateResult -Classification 'BLOCKED_OR_FAILED' -Reason $reason -DialogStatus $dialogStatus -TemporalSummary $temporalSummary -ResolvedApk $ApkPath
  throw
}

Write-GateResult -Classification $classification -Reason $reason -DialogStatus $dialogStatus -TemporalSummary $temporalSummary -ResolvedApk $ApkPath
$disposition = Get-StandaloneQuestTemporalClassificationDisposition -Classification $classification
if ($disposition -eq 'FAIL_ATTEMPTED') {
  throw "Standalone Quest temporal gate failed with preserved classification '$classification'. Summaries: $temporalSummary and $(Join-Path $ArtifactDir 'standalone_quest_gate_summary.json')"
}
