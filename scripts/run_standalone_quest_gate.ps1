[CmdletBinding()]
param(
  [string]$AdbExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe',
  [string]$ProjectPath = 'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight',
  [string]$ApkPath = '',
  [string]$ArtifactDir = '',
  [string]$DeviceSerial = '',
  [ValidateRange(3, 10)]
  [int]$RunCount = 3,
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
$operatorAction = 'Put on the Quest 3, dismiss the Headset Power panel with Cancel (do not select Sleep), wake both Touch controllers, complete the controller-required/Guardian dialog once, confirm the ProductionVerticalSlice cockpit is visible, then rerun scripts\run_standalone_quest_gate.ps1.'

function Get-OptionalProperty {
  param($InputObject, [string]$Name, $Default = $null)
  if ($null -eq $InputObject) { return $Default }
  $property = $InputObject.PSObject.Properties[$Name]
  if ($null -eq $property -or $null -eq $property.Value) { return $Default }
  return $property.Value
}

function Write-GateResult {
  param(
    [string]$Classification,
    [string]$Reason,
    [string]$DialogStatus,
    [object[]]$Runs = @(),
    [string]$ResolvedApk = '',
    [string]$InitialApkSha256 = '',
    [string]$FinalApkSha256 = ''
  )
  $apkExists = -not [string]::IsNullOrWhiteSpace($ResolvedApk) -and (Test-Path -LiteralPath $ResolvedApk)
  $disposition = Get-StandaloneQuestTemporalClassificationDisposition -Classification $Classification
  $summary = [ordered]@{
    schemaVersion = 4
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
    initialApkSha256 = $InitialApkSha256
    finalApkSha256 = $FinalApkSha256
    apkImmutableAcrossRuns = -not [string]::IsNullOrWhiteSpace($InitialApkSha256) -and $InitialApkSha256 -eq $FinalApkSha256
    controllerGuardianDialogStatus = $DialogStatus
    reason = $Reason
    exactHumanActionIfBlocked = $operatorAction
    warmupSeconds = 90
    measurementSeconds = 60
    requiredRunCount = $RunCount
    completedRunCount = @($Runs).Count
    allRunsUnrecorded = @($Runs).Count -eq $RunCount -and @($Runs | Where-Object { -not $_.unrecorded }).Count -eq 0
    allRunsPassed = @($Runs).Count -eq $RunCount -and @($Runs | Where-Object { -not $_.passed }).Count -eq 0
    temporalRuns = @($Runs)
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
    "- Required/completed unrecorded runs: $RunCount / $(@($Runs).Count)",
    "- All runs unrecorded/passed: $($summary.allRunsUnrecorded) / $($summary.allRunsPassed)",
    "- APK immutable across runs: $($summary.apkImmutableAcrossRuns)",
    '',
    '## Temporal runs',
    '',
    '| Run | Classification | Unrecorded | Passed | Summary |',
    '|---:|---|---:|---:|---|',
    $(if (@($Runs).Count -gt 0) {
        @($Runs | ForEach-Object { "| $($_.runNumber) | ``$($_.classification)`` | $($_.unrecorded) | $($_.passed) | ``$($_.summaryPath)`` |" }) -join [Environment]::NewLine
      } else { '| — | `NOT_RUN` | False | False | — |' }),
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
$runResults = @()
$initialApkSha256 = ''
$finalApkSha256 = ''

try {
  if ($SkipInstall) {
    throw '-SkipInstall is not permitted by the three-run acceptance gate because the exact APK must be installed and verified.'
  }
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
  $initialApkSha256 = (Get-FileHash -LiteralPath $ApkPath -Algorithm SHA256).Hash.ToLowerInvariant()
  $finalApkSha256 = $initialApkSha256
  $temporalGuardianDialog = $false
  $apkChanged = $false
  Write-Host "Running $RunCount distinct unrecorded Quest measurements against APK SHA256 $initialApkSha256"
  for ($runNumber = 1; $runNumber -le $RunCount; $runNumber++) {
    $temporalDir = Join-Path $ArtifactDir ("temporal_gate_run_{0:D2}" -f $runNumber)
    if (Test-Path -LiteralPath $temporalDir) {
      throw "Refusing to reuse an existing temporal run directory: $temporalDir"
    }
    $temporalArgs = @{
      AdbExe = $AdbExe
      ProjectPath = $ProjectPath
      ApkPath = $ApkPath
      DeviceSerial = $script:resolvedSerial
      OutputDir = $temporalDir
      SuppressOperatorPrompt = [bool]$Unattended
      NoHumanWitness = [bool]$Unattended
      SkipScreenRecording = $true
      StaticEvidenceOnly = [bool]$StaticEvidenceOnly
      SkipInstall = $runNumber -gt 1
    }
    Write-Host "Quest unrecorded temporal run $runNumber/$RunCount"
    & (Join-Path $PSScriptRoot 'run_quest_temporal_visual_gate.ps1') @temporalArgs
    $temporalSummary = Join-Path $temporalDir 'quest_temporal_visual_gate_summary.json'
    if (-not (Test-Path -LiteralPath $temporalSummary)) {
      throw "Temporal run $runNumber exited without its JSON summary: $temporalSummary"
    }
    $temporal = Get-Content -LiteralPath $temporalSummary -Raw | ConvertFrom-Json
    $runClassification = [string]$temporal.classification
    $runGuardianDialog = $false
    if ($null -ne $temporal.faultChecks) {
      if ($null -ne $temporal.faultChecks.PSObject.Properties['blockingSystemDialogDetected']) {
        $runGuardianDialog = [bool]$temporal.faultChecks.blockingSystemDialogDetected
      } elseif ($null -ne $temporal.faultChecks.PSObject.Properties['guardianDialogDetected']) {
        $runGuardianDialog = [bool]$temporal.faultChecks.guardianDialogDetected
      }
    }
    $temporalGuardianDialog = $temporalGuardianDialog -or $runGuardianDialog
    if ($runGuardianDialog -and $runClassification -like 'FAIL_INCOMPLETE_*') {
      $runClassification = 'BLOCKED_HUMAN_ACTION_REQUIRED'
    }

    $screenRecordingStarted = [bool](Get-OptionalProperty $temporal.screenRecording 'started' $false)
    $unrecorded = -not $screenRecordingStarted
    $machinePassed = [bool]$temporal.machineGate.passed
    $runPassed = $runClassification -eq 'MACHINE_GATE_PASS_HUMAN_WITNESS_REQUIRED' -and
      $machinePassed -and $unrecorded
    $runResults += [pscustomobject]@{
      runNumber = $runNumber
      artifactDirectory = $temporalDir
      summaryPath = $temporalSummary
      classification = $runClassification
      disposition = Get-StandaloneQuestTemporalClassificationDisposition -Classification $runClassification
      apkSha256 = $initialApkSha256
      unrecorded = $unrecorded
      passed = $runPassed
      averageCpuAndGpuMs = [double](Get-OptionalProperty $temporal.vrApiAppPidWindow 'averageCpuAndGpuMs' 0)
      p95CpuAndGpuMs = [double](Get-OptionalProperty $temporal.vrApiAppPidWindow 'p95CpuAndGpuMs' 0)
      cpuAndGpuSamplesOverBudgetRatio = [double](Get-OptionalProperty $temporal.vrApiAppPidWindow 'cpuAndGpuSamplesOverBudgetRatio' 0)
      staleFrames = [int](Get-OptionalProperty $temporal.vrApiAppPidWindow 'staleTotal' 0)
      tears = [int](Get-OptionalProperty $temporal.vrApiAppPidWindow 'tearTotal' 0)
      maximumTemperatureC = [double](Get-OptionalProperty $temporal.vrApiAppPidWindow 'maximumTemperatureC' 0)
      drawCalls = [long](Get-OptionalProperty $temporal.runtime 'drawCalls' 0)
      visibleTriangles = [long](Get-OptionalProperty $temporal.runtime 'visibleTriangles' 0)
      sceneMaterials = [int](Get-OptionalProperty $temporal.runtime 'sceneMaterialCount' 0)
    }

    $finalApkSha256 = (Get-FileHash -LiteralPath $ApkPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($finalApkSha256 -ne $initialApkSha256) {
      $apkChanged = $true
      break
    }
    if ($runClassification -eq 'BLOCKED_HUMAN_ACTION_REQUIRED' -or $runClassification -like 'NOT_RUN_*') { break }
  }

  if ($apkChanged) {
    $classification = 'FAIL_APK_CHANGED_DURING_GATE'
    $reason = "APK SHA256 changed during the physical gate: $initialApkSha256 -> $finalApkSha256."
  } elseif (@($runResults | Where-Object { $_.classification -eq 'BLOCKED_HUMAN_ACTION_REQUIRED' }).Count -gt 0) {
    $classification = 'BLOCKED_HUMAN_ACTION_REQUIRED'
    $reason = 'A controller-required and/or Guardian system dialog blocked product readiness. Complete the exact one-time in-headset action recorded by this gate, then rerun; no performance inference is permitted from this attempt.'
  } elseif (@($runResults | Where-Object { $_.classification -like 'NOT_RUN_*' }).Count -gt 0) {
    $classification = [string]($runResults | Where-Object { $_.classification -like 'NOT_RUN_*' } | Select-Object -First 1).classification
    $reason = 'Physical temporal acceptance was intentionally not run; preserved static evidence cannot satisfy the three-run headset gate.'
  } elseif ($runResults.Count -eq $RunCount -and @($runResults | Where-Object { -not $_.passed }).Count -eq 0) {
    $classification = 'MACHINE_GATE_PASS_HUMAN_WITNESS_REQUIRED'
    $reason = "All $RunCount distinct unrecorded physical Quest runs passed against immutable APK SHA256 $initialApkSha256."
  } else {
    $classification = 'FAIL_THREE_RUN_QUEST_GATE'
    $reason = "$($runResults.Count)/$RunCount physical Quest runs completed and every run did not pass; inspect each preserved temporal summary."
  }

  $postflightActivity = (& $AdbExe @adbPrefix shell dumpsys activity activities | Out-String)
  $postflightActivity | Set-Content -LiteralPath (Join-Path $ArtifactDir 'system_ui_postflight.txt') -Encoding UTF8
  if ($temporalGuardianDialog) {
    $dialogStatus = 'CONTROLLER_OR_GUARDIAN_DIALOG_OBSERVED_IN_GATE_LOGCAT_REQUIRES_HUMAN'
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
  Write-GateResult -Classification 'BLOCKED_OR_FAILED' -Reason $reason -DialogStatus $dialogStatus -Runs $runResults `
    -ResolvedApk $ApkPath -InitialApkSha256 $initialApkSha256 -FinalApkSha256 $finalApkSha256
  throw
}

Write-GateResult -Classification $classification -Reason $reason -DialogStatus $dialogStatus -Runs $runResults `
  -ResolvedApk $ApkPath -InitialApkSha256 $initialApkSha256 -FinalApkSha256 $finalApkSha256
$disposition = Get-StandaloneQuestTemporalClassificationDisposition -Classification $classification
if ($disposition -eq 'FAIL_ATTEMPTED') {
  $temporalPaths = @($runResults | ForEach-Object { $_.summaryPath }) -join ', '
  throw "Standalone Quest temporal gate failed with preserved classification '$classification'. Summaries: $temporalPaths and $(Join-Path $ArtifactDir 'standalone_quest_gate_summary.json')"
}
