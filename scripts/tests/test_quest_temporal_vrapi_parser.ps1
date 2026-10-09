[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$parser = Join-Path $repoRoot 'scripts\run_quest_temporal_visual_gate.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("questflight_vrapi_parser_" + [Guid]::NewGuid().ToString('N'))
$invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture

function Write-RuntimeEvidenceFixture {
  param(
    [string]$Directory,
    [int]$TransparentMaterialSlots,
    [long]$ManagedAllocationCount = 0,
    [bool]$AllocationValidated = $true,
    [bool]$CalibrationPassed = $true,
    [int]$AllocationSamples = 4320
  )

  New-Item -ItemType Directory -Force -Path $Directory | Out-Null
  [ordered]@{
    schemaVersion = 5
    readinessPrerequisitesMet = $true
    readinessStatus = 'PASS'
    warmupSeconds = 90.0
    measuredSeconds = 60.0
    sampleCount = 4320
    requestedQualityMode = 'Native72'
    native72ModeObserved = $true
    spaceWarpRequestedSamples = 0
    spaceWarpActiveSamples = 0
    spaceWarpSubmissionSamples = 0
    averageFrameMs = 13.8
    p95FrameMs = 13.888
    p99FrameMs = 13.888
    framesOver72HzBudget = 216
    framesOver72HzBudgetRatio = 0.05
    averageWorkloadMs = 11.5
    p95WorkloadMs = 11.95
    workloadSamplesOver72HzBudgetRatio = 0.0
    allocationRecorderAvailable = $false
    allocationMeasurementAvailable = $true
    allocationMeasurementValidated = $AllocationValidated
    allocationMeasurementSource = 'Unity IL2CPP public runtime stat: IL2CPP_STAT_NEW_OBJECT_COUNT'
    allocationMeasurementScope = 'test fixture'
    allocationMeasurementSamples = $AllocationSamples
    allocationByteMeasurementAvailable = $false
    managedAllocationCountMeasurementAvailable = $true
    allocationCounterCalibrationPassed = $CalibrationPassed
    allocationCounterCalibrationDelta = $(if ($CalibrationPassed) { 1 } else { 0 })
    totalManagedAllocationCount = $ManagedAllocationCount
    maxManagedAllocationCountPerFrame = $ManagedAllocationCount
    allocationMeasurementInvalidReason = $(if ($AllocationValidated) { '' } else { 'fixture invalidation' })
    averageGcAllocatedBytesPerFrame = -1
    maxGcAllocatedBytesPerFrame = -1
    diagnosticIsolationProfile = 'production'
    renderBudget = [ordered]@{
      estimatedInstancedDrawCalls = 120
      estimatedFrustumTriangles = 500000
      uniqueMaterialCount = 30
      transparentMaterialSlots = $TransparentMaterialSlots
    }
    sceneMaterialCount = 30
    transparentMaterialSlots = $TransparentMaterialSlots
  } | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $Directory 'quest_temporal_visual_gate_test.json') -Encoding UTF8
}

function Write-VrApiFixture {
  param(
    [string]$Path,
    [int]$SampleCount,
    [int]$RefreshRateHz = 72,
    [int]$AppFps = 72,
    [double]$CpuAndGpuMs = 12.0,
    [int]$Stale = 0,
    [int]$Tear = 0,
    [double]$BoundaryGpuMs = 0,
    [switch]$IncludeOtherPidRow,
    [switch]$IncludeControllerDialog
  )

  $start = [DateTime]::new(2000, 7, 16, 15, 0, 0, [DateTimeKind]::Unspecified)
  $lines = @(
    "$(($start).ToString('MM-dd HH:mm:ss.fff', $invariantCulture))  4242  4243 I Unity   : [QuestFlight][TemporalVisualGate] Measurement started"
  )
  if ($IncludeOtherPidRow) {
    $otherPidTimestamp = $start.AddMilliseconds(100).ToString('MM-dd HH:mm:ss.fff', $invariantCulture)
    $lines += "$otherPidTimestamp  9999 10000 I VrApi   : FPS=72/72,Tear=9,Stale=9,Fov=0,CPU4/GPU=1/1,1000/400MHz,PLS=0,Temp=30.0C/0.0C,App=20.00ms,GD=0.00ms,CPU&GPU=20.00ms,GPU%=0.99,CPU%=0.99,CFL=20.00/20.00,ICFLp95=20.00,SF=0.60"
  }
  if ($IncludeControllerDialog) {
    $dialogTimestamp = $start.AddMilliseconds(200).ToString('MM-dd HH:mm:ss.fff', $invariantCulture)
    $lines += "$dialogTimestamp  1505  2370 I ActivityLaunchInterceptorController: RequiresControllersLaunchInterceptor: com.alexoviedo.t2.questflight/com.unity3d.player.UnityPlayerGameActivity com.oculus.vrshell/com.oculus.vrshell.systemdialog.launchcheck.LaunchCheckControllerRequiredDialogActivity"
  }

  for ($index = 0; $index -lt $SampleCount; $index++) {
    $timestamp = $start.AddSeconds($index + 1).ToString('MM-dd HH:mm:ss.fff', $invariantCulture)
    $fov = $(if (($index % 2) -eq 0) { 0 } else { 2 })
    $cpuAndGpu = $CpuAndGpuMs.ToString('0.00', $invariantCulture)
    $boundaryGpu = $BoundaryGpuMs.ToString('0.00', $invariantCulture)
    $lines += "$timestamp  4242  4244 I VrApi   : FPS=$AppFps/$RefreshRateHz,Tear=$Tear,Stale=$Stale,Fov=$fov,CPU4/GPU=2/2,1382/456MHz,PLS=0,Temp=31.0C/0.0C,App=9.00ms,GD=${boundaryGpu}ms,CPU&GPU=${cpuAndGpu}ms,GPU%=0.70,CPU%=0.30,CFL=13.80/18.00,ICFLp95=17.00,SF=1.00"
  }

  $endTimestamp = $start.AddSeconds($SampleCount + 1).ToString('MM-dd HH:mm:ss.fff', $invariantCulture)
  $lines += "$endTimestamp  4242  4243 I Unity   : [QuestFlight][TemporalVisualGate] Evidence written: /sdcard/evidence.json"
  $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Invoke-ParserFixture {
  param(
    [string]$Logcat,
    [string]$Evidence,
    [string]$Output,
    [string]$RequestedQualityMode = 'Native72'
  )

  $processOutput = & powershell -NoProfile -ExecutionPolicy Bypass -File $parser `
    -ParseOnlyLogcat $Logcat -ParseOnlyEvidenceDir $Evidence -OutputDir $Output -RequestedQualityMode $RequestedQualityMode
  if ($LASTEXITCODE -ne 0) {
    throw "Parser-only process failed with exit code $LASTEXITCODE for '$Logcat': $($processOutput -join [Environment]::NewLine)"
  }
  return Get-Content -LiteralPath (Join-Path $Output 'quest_temporal_visual_gate_summary.json') -Raw |
    ConvertFrom-Json
}

try {
  New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
  $passingEvidence = Join-Path $testRoot 'evidence_passing'
  $transparentFailEvidence = Join-Path $testRoot 'evidence_transparent_fail'
  $allocationCountFailEvidence = Join-Path $testRoot 'evidence_allocation_count_fail'
  $allocationValidationFailEvidence = Join-Path $testRoot 'evidence_allocation_validation_fail'
  $allocationCoverageFailEvidence = Join-Path $testRoot 'evidence_allocation_coverage_fail'
  Write-RuntimeEvidenceFixture -Directory $passingEvidence -TransparentMaterialSlots 2
  Write-RuntimeEvidenceFixture -Directory $transparentFailEvidence -TransparentMaterialSlots 3
  Write-RuntimeEvidenceFixture -Directory $allocationCountFailEvidence -TransparentMaterialSlots 2 -ManagedAllocationCount 1
  Write-RuntimeEvidenceFixture -Directory $allocationValidationFailEvidence -TransparentMaterialSlots 2 `
    -AllocationValidated $false -CalibrationPassed $false
  Write-RuntimeEvidenceFixture -Directory $allocationCoverageFailEvidence -TransparentMaterialSlots 2 -AllocationSamples 4319

  $shortLogcat = Join-Path $testRoot 'logcat_two_rows.txt'
  Write-VrApiFixture -Path $shortLogcat -SampleCount 2 -IncludeOtherPidRow
  $short = Invoke-ParserFixture -Logcat $shortLogcat -Evidence $passingEvidence -Output (Join-Path $testRoot 'output_two_rows')
  $shortVrApi = $short.vrApiAppPidWindow
  if (-not $shortVrApi.available) { throw 'Expected two-row VrApi evidence to remain parseable.' }
  if ([int]$shortVrApi.appPid -ne 4242) { throw "Expected app PID 4242; got $($shortVrApi.appPid)." }
  if ([int]$shortVrApi.sampleCount -ne 2) { throw "Expected exactly two app samples; got $($shortVrApi.sampleCount)." }
  if (@($shortVrApi.observedFovFieldValues).Count -ne 2 -or
      @($shortVrApi.observedFovFieldValues) -notcontains 0 -or
      @($shortVrApi.observedFovFieldValues) -notcontains 2) {
    throw "Expected Fov telemetry values 0 and 2; got $(@($shortVrApi.observedFovFieldValues) -join ',')."
  }
  if ([int]$shortVrApi.staleTotal -ne 0 -or [int]$shortVrApi.tearTotal -ne 0) {
    throw 'A compositor row from a different PID leaked into the app summary.'
  }
  if ([bool]$short.physicalQuestTimingGate.minimumSampleCountPassed -or
      [bool]$short.physicalQuestTimingGate.timestampCoveragePassed -or
      [bool]$short.physicalQuestTimingGate.passed -or
      [bool]$short.machineGate.passed) {
    throw 'Two app-PID rows incorrectly satisfied the 60-second VrApi coverage gate.'
  }
  if ($short.classification -ne 'NOT_RUN_BLOCKED_HEADSET_TEMPORAL_ACCEPTANCE') {
    throw "Parser-only mode should retain its non-hardware classification; got '$($short.classification)'."
  }

  $passingLogcat = Join-Path $testRoot 'logcat_passing_55_rows.txt'
  Write-VrApiFixture -Path $passingLogcat -SampleCount 55 -IncludeOtherPidRow
  $passing = Invoke-ParserFixture -Logcat $passingLogcat -Evidence $passingEvidence -Output (Join-Path $testRoot 'output_passing')
  $passingVrApi = $passing.vrApiAppPidWindow
  if ([int]$passingVrApi.sampleCount -ne 55 -or [int]$passingVrApi.timestampedSampleCount -ne 55) {
    throw 'Expected exactly 55 timestamped app-PID VrApi samples at the acceptance boundary.'
  }
  if ([double]$passingVrApi.observedCoverageSeconds -lt 54.0 -or
      -not [bool]$passing.physicalQuestTimingGate.minimumSampleCountPassed -or
      -not [bool]$passing.physicalQuestTimingGate.timestampCoveragePassed) {
    throw "Expected sufficient VrApi coverage; observed $($passingVrApi.observedCoverageSeconds) seconds."
  }
  if (-not [bool]$passing.physicalQuestTimingGate.passed -or -not [bool]$passing.machineGate.passed) {
    throw 'Expected the sufficient VrApi and exact-boundary runtime fixtures to satisfy the machine gate.'
  }
  if (-not [bool]$passing.runtime.allocationMeasurementValidated -or
      -not [bool]$passing.runtime.managedAllocationCountMeasurementAvailable -or
      [long]$passing.runtime.totalManagedAllocationCount -ne 0 -or
      [long]$passing.runtime.maxManagedAllocationCountPerFrame -ne 0) {
    throw 'Expected calibrated count-only IL2CPP zero-allocation evidence to satisfy the allocation branch.'
  }

  $allocationCountFail = Invoke-ParserFixture -Logcat $passingLogcat -Evidence $allocationCountFailEvidence `
    -Output (Join-Path $testRoot 'output_allocation_count_fail')
  if ([bool]$allocationCountFail.machineGate.passed -or
      [bool]$allocationCountFail.machineGate.zeroSteadyStateGcAllocationsPassed) {
    throw 'One managed allocation incorrectly passed the exact zero-allocation gate.'
  }
  $allocationValidationFail = Invoke-ParserFixture -Logcat $passingLogcat -Evidence $allocationValidationFailEvidence `
    -Output (Join-Path $testRoot 'output_allocation_validation_fail')
  if ([bool]$allocationValidationFail.machineGate.zeroSteadyStateGcAllocationsPassed) {
    throw 'An unvalidated/calibration-failed allocation counter incorrectly passed.'
  }
  $allocationCoverageFail = Invoke-ParserFixture -Logcat $passingLogcat -Evidence $allocationCoverageFailEvidence `
    -Output (Join-Path $testRoot 'output_allocation_coverage_fail')
  if ([bool]$allocationCoverageFail.machineGate.zeroSteadyStateGcAllocationsPassed) {
    throw 'Incomplete allocation sample coverage incorrectly passed.'
  }
  if (-not [bool]$passing.machineGate.transparentMaterialSlotsPassed -or
      [int]$passing.runtime.transparentMaterialSlots -ne 2) {
    throw 'Expected two transparent material slots to pass and be projected into the report.'
  }
  if ([double]$passing.requirements.averageCpuAndGpuMsMax -ne 12.8 -or
      [int]$passing.requirements.targetRefreshRateHz -ne 72 -or
      [int]$passing.requirements.vrApiSampleCountMin -ne 55 -or
      [double]$passing.requirements.vrApiObservedCoverageSecondsMin -ne 54.0 -or
      [int]$passing.requirements.staleFramesMax -ne 2 -or
      [int]$passing.requirements.tearsMax -ne 0 -or
      [int]$passing.requirements.drawCallsMax -ne 120 -or
      [int]$passing.requirements.visibleTrianglesMax -ne 500000 -or
      [int]$passing.requirements.sceneMaterialsMax -ne 30 -or
      [int]$passing.requirements.transparentMaterialSlotsMax -ne 2) {
    throw 'Physical Quest hard-gate requirements differ from the requested contract.'
  }

  $controllerDialogLogcat = Join-Path $testRoot 'logcat_controller_dialog.txt'
  Write-VrApiFixture -Path $controllerDialogLogcat -SampleCount 2 -IncludeControllerDialog
  $controllerDialog = Invoke-ParserFixture -Logcat $controllerDialogLogcat -Evidence $passingEvidence `
    -Output (Join-Path $testRoot 'output_controller_dialog')
  if (-not [bool]$controllerDialog.faultChecks.controllerDialogDetected -or
      -not [bool]$controllerDialog.faultChecks.blockingSystemDialogDetected -or
      [bool]$controllerDialog.faultChecks.guardianDialogDetected) {
    throw 'Controller-required launch interception was not reported distinctly as a blocking system dialog.'
  }

  $noProductPidLogcat = Join-Path $testRoot 'logcat_compositor_only.txt'
  @(
    '07-16 15:00:00.200  3094  3375 I VrApi   : FPS=72/72,Tear=0,Stale=0,Fov=0,CPU4/GPU=2/2,1382/456MHz,PLS=0,Temp=31.0C/0.0C,App=1.00ms,GD=0.00ms,CPU&GPU=5.00ms,GPU%=0.20,CPU%=0.30,CFL=13.80/18.00,ICFLp95=17.00,SF=1.00',
    '07-16 15:00:00.300  1505  2370 I ActivityLaunchInterceptorController: RequiresControllersLaunchInterceptor: com.alexoviedo.t2.questflight/com.unity3d.player.UnityPlayerGameActivity com.oculus.vrshell/com.oculus.vrshell.systemdialog.launchcheck.LaunchCheckControllerRequiredDialogActivity'
  ) | Set-Content -LiteralPath $noProductPidLogcat -Encoding UTF8
  $noProductPid = Invoke-ParserFixture -Logcat $noProductPidLogcat -Evidence $passingEvidence `
    -Output (Join-Path $testRoot 'output_compositor_only')
  if ([bool]$noProductPid.vrApiAppPidWindow.available -or
      [bool]$noProductPid.vrApiAppPidWindow.identityVerifiedByProductPid -or
      [int]$noProductPid.vrApiAppPidWindow.sampleCount -ne 0 -or
      -not [string]::IsNullOrWhiteSpace([string]$noProductPid.vrApiAppPidWindow.appPid)) {
    throw 'A compositor-only SF=1.00 row was incorrectly treated as product-PID performance evidence.'
  }

  $failingThresholdLogcat = Join-Path $testRoot 'logcat_failing_thresholds_60_rows.txt'
  Write-VrApiFixture -Path $failingThresholdLogcat -SampleCount 60 -RefreshRateHz 90 `
    -CpuAndGpuMs 14.0 -Stale 3 -Tear 1
  $failingThresholds = Invoke-ParserFixture -Logcat $failingThresholdLogcat -Evidence $passingEvidence `
    -Output (Join-Path $testRoot 'output_failing_thresholds')
  if (-not [bool]$failingThresholds.physicalQuestTimingGate.minimumSampleCountPassed -or
      -not [bool]$failingThresholds.physicalQuestTimingGate.timestampCoveragePassed) {
    throw 'Failing-threshold fixture did not isolate thresholds with sufficient VrApi coverage.'
  }
  if ([bool]$failingThresholds.physicalQuestTimingGate.passed -or [bool]$failingThresholds.machineGate.passed) {
    throw 'Refresh/timing/stale/tear threshold violations incorrectly passed the physical machine gate.'
  }
  foreach ($flag in @('refreshRate72HzPassed', 'averageCpuAndGpuPassed', 'p95CpuAndGpuPassed',
      'samplesOverBudgetRatioPassed', 'staleFramesPassed', 'tearsPassed')) {
    if ([bool]$failingThresholds.physicalQuestTimingGate.$flag) {
      throw "Expected failing physical threshold flag '$flag' to be false."
    }
  }

  $transparentFail = Invoke-ParserFixture -Logcat $passingLogcat -Evidence $transparentFailEvidence `
    -Output (Join-Path $testRoot 'output_transparent_fail')
  if (-not [bool]$transparentFail.physicalQuestTimingGate.passed) {
    throw 'Transparent-slot fixture unexpectedly failed otherwise sufficient physical timing evidence.'
  }
  if ([bool]$transparentFail.machineGate.transparentMaterialSlotsPassed -or
      [bool]$transparentFail.machineGate.staticRuntimeBudgetsPassed -or
      [bool]$transparentFail.machineGate.passed -or
      [int]$transparentFail.runtime.transparentMaterialSlots -ne 3) {
    throw 'Three transparent material slots incorrectly satisfied the <=2 machine budget.'
  }

  foreach ($thermalCase in @(
    @{ name='telemetry_config'; warning=$false; text='E SettingsToPropertiesMapper: key=persist.device_config.oculus_shared_os_services.oculus_mobile_telemetry_statsd_event_verification_list value=oculus_mobile_thermal_throttling_events;os_update_process_started;os_update_shutdown_triggered; exceeds system property max length.' },
    @{ name='severe'; warning=$true; text='W ThermalService: temperature severe' },
    @{ name='active_throttling'; warning=$true; text='W ThermalService: thermal throttling active' },
    @{ name='shutdown'; warning=$true; text='E ThermalService: thermal shutdown' }
  )) {
    $thermalLogcat = Join-Path $testRoot ($thermalCase.name + '.txt')
    ((Get-Content -LiteralPath $passingLogcat -Raw) + "`n07-16 15:00:30.000 1505 7934 " + $thermalCase.text) |
      Set-Content -LiteralPath $thermalLogcat -Encoding UTF8
    $thermalResult = Invoke-ParserFixture -Logcat $thermalLogcat -Evidence $passingEvidence `
      -Output (Join-Path $testRoot ('output_' + $thermalCase.name))
    if ([bool]$thermalResult.faultChecks.thermalWarningInLog -ne $thermalCase.warning) {
      throw "Thermal event classification failed: $($thermalCase.name)"
    }
  }

  $boundaryLog = Join-Path $testRoot 'boundary_gpu.txt'
  Write-VrApiFixture -Path $boundaryLog -SampleCount 60 -BoundaryGpuMs 3.5
  $boundary = Invoke-ParserFixture -Logcat $boundaryLog -Evidence $passingEvidence -Output (Join-Path $testRoot 'output_boundary_gpu')
  if ($boundary.vrApiAppPidWindow.averageDirectGpuMs -ne 9 -or
      $boundary.vrApiAppPidWindow.averageBoundaryGpuMs -ne 3.5 -or
      $boundary.vrApiAppPidWindow.cpuExecutionTimingAvailable) {
    throw 'Application GPU, boundary GPU, and CPU execution semantics are conflated.'
  }
  $halfRateLog=Join-Path $testRoot 'half_rate.txt'
  Write-VrApiFixture -Path $halfRateLog -SampleCount 60 -AppFps 36
  $halfRate=Invoke-ParserFixture -Logcat $halfRateLog -Evidence $passingEvidence -Output (Join-Path $testRoot 'output_half_rate')
  if($halfRate.machineGate.passed -or $halfRate.machineGate.nativeApplicationCadencePassed){throw '36/72 application cadence accepted as Native72'}
  $wrongRequestedMode=Invoke-ParserFixture -Logcat $passingLogcat -Evidence $passingEvidence -Output (Join-Path $testRoot 'output_request_echo_mismatch') -RequestedQualityMode 'SpaceWarp36_72'
  if($wrongRequestedMode.machineGate.passed -or $wrongRequestedMode.machineGate.native72ModePassed -or $wrongRequestedMode.machineGate.requestedQualityMode -ne 'SpaceWarp36_72'){
    throw 'Explicit SpaceWarp request accepted mismatched Native72 evidence or lost request echo'
  }
  foreach($modeCase in @(
    @{name='active_spacewarp';field='spaceWarpActiveSamples';value=1},
    @{name='requested_spacewarp';field='spaceWarpRequestedSamples';value=1},
    @{name='submitted_spacewarp';field='spaceWarpSubmissionSamples';value=1},
    @{name='unknown_mode';field='requestedQualityMode';value='Unknown'},
    @{name='missing_mode';field='native72ModeObserved';value=$null},
    @{name='slow_delivered_cadence';field='p95FrameMs';value=27.777},
    @{name='unknown_delivered_cadence';field='p95FrameMs';value=$null}
  )) {
    $modeEvidence=Join-Path $testRoot ('evidence_' + $modeCase.name)
    New-Item -ItemType Directory -Path $modeEvidence | Out-Null
    $modeJson=Get-Content -LiteralPath (Join-Path $passingEvidence 'quest_temporal_visual_gate_test.json') -Raw | ConvertFrom-Json
    if($null -eq $modeCase.value){$modeJson.PSObject.Properties.Remove($modeCase.field)}else{$modeJson.($modeCase.field)=$modeCase.value}
    $modeJson | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $modeEvidence 'quest_temporal_visual_gate_test.json') -Encoding UTF8
    $modeResult=Invoke-ParserFixture -Logcat $passingLogcat -Evidence $modeEvidence -Output (Join-Path $testRoot ('output_' + $modeCase.name))
    if($modeResult.machineGate.passed){throw "Invalid Native72/cadence evidence accepted: $($modeCase.name)"}
  }
  Write-Host 'Quest VrApi parser regression tests passed: product-PID budgets, Native72 versus 36/72/SpaceWarp/unknown/transition controls, delivered cadence, App/GD semantics and real thermal events.'
}
finally {
  Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
