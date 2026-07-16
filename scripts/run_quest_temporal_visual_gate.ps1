[CmdletBinding()]
param(
  [string]$AdbExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe',
  [string]$ProjectPath = 'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight',
  [string]$ApkPath = '',
  [string]$PackageId = 'com.alexoviedo.t2.questflight',
  [string]$Activity = 'com.unity3d.player.UnityPlayerGameActivity',
  [string]$DeviceSerial = '',
  [string]$OutputDir = '',
  [string]$BaselineEvidenceDir = '',
  [string]$ParseOnlyLogcat = '',
  [string]$ParseOnlyEvidenceDir = '',
  [ValidateRange(30, 240)]
  [int]$ReadinessTimeoutSeconds = 120,
  [ValidateRange(60, 180)]
  [int]$MeasurementTimeoutSeconds = 90,
  [switch]$SkipInstall,
  [switch]$SkipScreenRecording,
  [switch]$StaticEvidenceOnly,
  [switch]$SuppressOperatorPrompt,
  [switch]$NoHumanWitness
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$QuestFrameBudgetMs = 1000.0 / 72.0
$RequiredP95Ms = $QuestFrameBudgetMs
$RequiredOverBudgetRatio = 0.05
$RequiredDrawCalls = 180
$RequiredVisibleTriangles = 700000
$RequiredSceneMaterials = 40
$LaunchSceneryMode = 'production_vertical_slice_v2'
$OperatorRequest = 'Please put on the Quest 3, wake both Touch controllers, complete any Guardian/controller dialog, and remain normally seated. Do not physically step forward or backward. Tell me when the production cockpit is visible.'
$script:ResolvedSerial = $DeviceSerial

function New-ArtifactDirectory {
  param([string]$RequestedPath, [string]$Prefix)
  if ([string]::IsNullOrWhiteSpace($RequestedPath)) {
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $RequestedPath = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-extraction-artifacts\${Prefix}_$stamp"
  }
  New-Item -ItemType Directory -Force -Path $RequestedPath | Out-Null
  return (Resolve-Path -LiteralPath $RequestedPath).Path
}

function Get-ObjectProperty {
  param($InputObject, [string]$Name, $Default = $null)
  if ($null -eq $InputObject) { return $Default }
  $property = $InputObject.PSObject.Properties[$Name]
  if ($null -eq $property -or $null -eq $property.Value) { return $Default }
  return $property.Value
}

function Get-Percentile {
  param([object[]]$Values, [double]$Percentile)
  $numbers = @($Values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ } | Sort-Object)
  if ($numbers.Count -eq 0) { return 0.0 }
  if ($numbers.Count -eq 1) { return $numbers[0] }
  $position = [Math]::Max(0.0, [Math]::Min(1.0, $Percentile)) * ($numbers.Count - 1)
  $lower = [Math]::Floor($position)
  $upper = [Math]::Ceiling($position)
  if ($lower -eq $upper) { return $numbers[[int]$lower] }
  $blend = $position - $lower
  return $numbers[[int]$lower] + (($numbers[[int]$upper] - $numbers[[int]$lower]) * $blend)
}

function Get-Average {
  param([object[]]$Values)
  $numbers = @($Values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ })
  if ($numbers.Count -eq 0) { return 0.0 }
  return [double](($numbers | Measure-Object -Average).Average)
}

function Get-Maximum {
  param([object[]]$Values)
  $numbers = @($Values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ })
  if ($numbers.Count -eq 0) { return 0.0 }
  return [double](($numbers | Measure-Object -Maximum).Maximum)
}

function Get-Sum {
  param([object[]]$Values)
  $numbers = @($Values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ })
  if ($numbers.Count -eq 0) { return 0.0 }
  return [double](($numbers | Measure-Object -Sum).Sum)
}

function Format-Number {
  param($Value, [string]$Format)
  return ([double]$Value).ToString($Format, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-RegexDouble {
  param([string]$Text, [string]$Pattern, [double]$Default = 0.0)
  $match = [regex]::Match($Text, $Pattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
  if (-not $match.Success) { return $Default }
  $value = 0.0
  if ([double]::TryParse($match.Groups[1].Value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$value)) {
    return $value
  }
  return $Default
}

function Get-RegexInt {
  param([string]$Text, [string]$Pattern, [int]$Default = 0)
  $match = [regex]::Match($Text, $Pattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
  if (-not $match.Success) { return $Default }
  $value = 0
  if ([int]::TryParse($match.Groups[1].Value, [ref]$value)) { return $value }
  return $Default
}

function Find-AppPidInLog {
  param([string[]]$Lines, [string]$ExplicitPid = '')
  if (-not [string]::IsNullOrWhiteSpace($ExplicitPid)) { return $ExplicitPid.Trim() }

  $candidates = @()
  foreach ($line in $Lines) {
    if ($line -notmatch '\[QuestFlight\]') { continue }
    $match = [regex]::Match($line, '^\S+\s+\S+\s+(\d+)\s+\d+\s+[A-Z]\s+Unity\s*:')
    if ($match.Success) { $candidates += $match.Groups[1].Value }
  }
  if ($candidates.Count -eq 0) {
    foreach ($line in $Lines) {
      if ($line -notmatch 'VrApi\s*:.*FPS=.*SF=1\.00') { continue }
      $match = [regex]::Match($line, '^\S+\s+\S+\s+(\d+)\s+\d+\s+[A-Z]\s+VrApi\s*:')
      if ($match.Success) { $candidates += $match.Groups[1].Value }
    }
  }
  if ($candidates.Count -eq 0) { return '' }
  return ($candidates | Group-Object | Sort-Object Count -Descending | Select-Object -First 1).Name
}

function Get-MeasurementLineWindow {
  param([string[]]$Lines)
  $start = -1
  $end = -1
  for ($index = 0; $index -lt $Lines.Count; $index++) {
    if ($Lines[$index] -match '\[QuestFlight\]\[TemporalVisualGate\] Measurement started') {
      $start = $index
    }
    if ($start -ge 0 -and $index -ge $start -and $Lines[$index] -match '\[QuestFlight\]\[TemporalVisualGate\] Evidence written:') {
      $end = $index
      break
    }
  }
  if ($start -ge 0) {
    if ($end -lt $start) { $end = $Lines.Count - 1 }
    return [pscustomobject]@{ exact = $true; startIndex = $start; endIndex = $end; lines = @($Lines[$start..$end]) }
  }
  return [pscustomobject]@{ exact = $false; startIndex = 0; endIndex = [Math]::Max(0, $Lines.Count - 1); lines = @($Lines) }
}

function Get-VrApiEvidence {
  param(
    [string]$LogcatPath,
    [string]$ExplicitAppPid = '',
    [string]$CsvPath = ''
  )
  if (-not (Test-Path -LiteralPath $LogcatPath)) {
    return [pscustomobject]@{ summary = [pscustomobject]@{ available = $false; reason = "logcat not found: $LogcatPath" }; samples = @() }
  }

  $allLines = @(Get-Content -LiteralPath $LogcatPath)
  $window = Get-MeasurementLineWindow -Lines $allLines
  $appPid = Find-AppPidInLog -Lines $allLines -ExplicitPid $ExplicitAppPid
  $samples = @()
  foreach ($line in $window.lines) {
    if ($line -notmatch 'VrApi\s*:.*FPS=') { continue }
    $prefix = [regex]::Match($line, '^\S+\s+\S+\s+(\d+)\s+\d+\s+[A-Z]\s+VrApi\s*:\s*(.*)$')
    if (-not $prefix.Success) { continue }
    $processIdValue = $prefix.Groups[1].Value
    $metrics = $prefix.Groups[2].Value
    if (-not [string]::IsNullOrWhiteSpace($appPid) -and $processIdValue -ne $appPid) { continue }
    # The app PID is the identity boundary. Quest firmware has emitted the
    # product row with both Fov=2 and Fov=0, so Fov is telemetry rather than an
    # identity discriminator. SF=1.00 remains a fallback guard against vrshell
    # compositor rows when PID inference must use VrApi output.
    if ($metrics -notmatch 'SF=1\.00') { continue }

    $fps = Get-RegexDouble $metrics 'FPS=([0-9.]+)/'
    $refresh = Get-RegexDouble $metrics 'FPS=[0-9.]+/([0-9.]+)'
    $cfl = [regex]::Match($metrics, 'CFL=([0-9.]+)/([0-9.]+)')
    $levels = [regex]::Match($metrics, 'CPU4/GPU=(\d+)/(\d+)')
    $clocks = [regex]::Match($metrics, ',(\d+)/(\d+)MHz')
    $samples += [pscustomobject]@{
      pid = [int]$processIdValue
      fovFieldValue = Get-RegexInt $metrics 'Fov=(\d+)' -1
      fps = $fps
      refreshHz = $refresh
      deliveredIntervalFromFpsMs = $(if ($fps -gt 0) { 1000.0 / $fps } else { 0.0 })
      tear = Get-RegexInt $metrics 'Tear=(\d+)'
      stale = Get-RegexInt $metrics 'Stale=(\d+)'
      stale2 = Get-RegexInt $metrics 'Stale2/5/10/max=(\d+)/'
      stale5 = Get-RegexInt $metrics 'Stale2/5/10/max=\d+/(\d+)/'
      stale10 = Get-RegexInt $metrics 'Stale2/5/10/max=\d+/\d+/(\d+)/'
      staleMax = Get-RegexInt $metrics 'Stale2/5/10/max=\d+/\d+/\d+/(\d+)'
      appMs = Get-RegexDouble $metrics 'App=([0-9.]+)ms'
      gpuDurationMs = Get-RegexDouble $metrics 'GD=([0-9.]+)ms'
      cpuAndGpuMs = Get-RegexDouble $metrics 'CPU&GPU=([0-9.]+)ms'
      gpuUtilization = Get-RegexDouble $metrics 'GPU%=([0-9.]+)'
      cpuUtilization = Get-RegexDouble $metrics 'CPU%=([0-9.]+)'
      cpuLevel = $(if ($levels.Success) { [int]$levels.Groups[1].Value } else { 0 })
      gpuLevel = $(if ($levels.Success) { [int]$levels.Groups[2].Value } else { 0 })
      cpuMHz = $(if ($clocks.Success) { [int]$clocks.Groups[1].Value } else { 0 })
      gpuMHz = $(if ($clocks.Success) { [int]$clocks.Groups[2].Value } else { 0 })
      temperatureC = Get-RegexDouble $metrics 'Temp=([0-9.]+)C/'
      powerLevelState = Get-RegexInt $metrics 'PLS=(\d+)'
      cflPrimaryMs = $(if ($cfl.Success) { [double]::Parse($cfl.Groups[1].Value, [System.Globalization.CultureInfo]::InvariantCulture) } else { 0.0 })
      cflSecondaryMs = $(if ($cfl.Success) { [double]::Parse($cfl.Groups[2].Value, [System.Globalization.CultureInfo]::InvariantCulture) } else { 0.0 })
      icflP95Ms = Get-RegexDouble $metrics 'ICFLp95=([0-9.]+)'
      raw = $metrics
    }
  }

  if (-not $window.exact -and $samples.Count -gt 60) {
    $samples = @($samples | Select-Object -Last 60)
  }
  if (-not [string]::IsNullOrWhiteSpace($CsvPath) -and $samples.Count -gt 0) {
    $samples | Select-Object * -ExcludeProperty raw | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $CsvPath
  }

  $directGpu = @($samples | Where-Object { $_.gpuDurationMs -gt 0.01 } | ForEach-Object { $_.gpuDurationMs })
  $summary = [pscustomobject]@{
    available = $samples.Count -gt 0
    appPid = $appPid
    exactMeasurementWindow = [bool]$window.exact
    sampleCount = $samples.Count
    observedFovFieldValues = @($samples | ForEach-Object { $_.fovFieldValue } | Sort-Object -Unique)
    averageFps = Get-Average @($samples | ForEach-Object { $_.fps })
    p05Fps = Get-Percentile @($samples | ForEach-Object { $_.fps }) 0.05
    averageDeliveredIntervalFromFpsMs = Get-Average @($samples | ForEach-Object { $_.deliveredIntervalFromFpsMs })
    p95DeliveredIntervalFromFpsMs = Get-Percentile @($samples | ForEach-Object { $_.deliveredIntervalFromFpsMs }) 0.95
    averageAppMs = Get-Average @($samples | ForEach-Object { $_.appMs })
    p95AppMs = Get-Percentile @($samples | ForEach-Object { $_.appMs }) 0.95
    averageCpuAndGpuMs = Get-Average @($samples | ForEach-Object { $_.cpuAndGpuMs })
    p95CpuAndGpuMs = Get-Percentile @($samples | ForEach-Object { $_.cpuAndGpuMs }) 0.95
    cpuAndGpuSamplesOverBudget = @($samples | Where-Object { $_.cpuAndGpuMs -gt $QuestFrameBudgetMs }).Count
    cpuAndGpuSamplesOverBudgetRatio = $(if ($samples.Count -gt 0) { @($samples | Where-Object { $_.cpuAndGpuMs -gt $QuestFrameBudgetMs }).Count / [double]$samples.Count } else { 0.0 })
    directGpuTimingAvailable = $directGpu.Count -gt 0
    averageDirectGpuMs = Get-Average $directGpu
    p95DirectGpuMs = Get-Percentile $directGpu 0.95
    averageGpuUtilization = Get-Average @($samples | ForEach-Object { $_.gpuUtilization })
    p95GpuUtilization = Get-Percentile @($samples | ForEach-Object { $_.gpuUtilization }) 0.95
    averageCpuUtilization = Get-Average @($samples | ForEach-Object { $_.cpuUtilization })
    p95CpuUtilization = Get-Percentile @($samples | ForEach-Object { $_.cpuUtilization }) 0.95
    staleTotal = [int](Get-Sum @($samples | ForEach-Object { $_.stale }))
    stalePeakPerSample = [int](Get-Maximum @($samples | ForEach-Object { $_.stale }))
    samplesWithStale = @($samples | Where-Object { $_.stale -gt 0 }).Count
    tearTotal = [int](Get-Sum @($samples | ForEach-Object { $_.tear }))
    maximumCpuLevel = [int](Get-Maximum @($samples | ForEach-Object { $_.cpuLevel }))
    maximumGpuLevel = [int](Get-Maximum @($samples | ForEach-Object { $_.gpuLevel }))
    maximumCpuMHz = [int](Get-Maximum @($samples | ForEach-Object { $_.cpuMHz }))
    maximumGpuMHz = [int](Get-Maximum @($samples | ForEach-Object { $_.gpuMHz }))
    maximumTemperatureC = Get-Maximum @($samples | ForEach-Object { $_.temperatureC })
    maximumPowerLevelState = [int](Get-Maximum @($samples | ForEach-Object { $_.powerLevelState }))
    averageCflPrimaryMs = Get-Average @($samples | ForEach-Object { $_.cflPrimaryMs })
    p95CflSecondaryMs = Get-Percentile @($samples | ForEach-Object { $_.cflSecondaryMs }) 0.95
    p95IcflP95Ms = Get-Percentile @($samples | ForEach-Object { $_.icflP95Ms }) 0.95
    semantics = 'App, CPU&GPU, GPU%, CPU%, stale, tear, clocks, levels, and thermal fields are VrApi rows filtered to the app PID and SF=1.00. Fov is recorded as telemetry because supported Quest firmware has emitted product rows with both Fov=0 and Fov=2. GD is direct GPU duration only when nonzero.'
  }
  return [pscustomobject]@{ summary = $summary; samples = @($samples) }
}

function Get-LatestEvidenceJson {
  param([string]$Root, [string]$Prefix)
  if ([string]::IsNullOrWhiteSpace($Root) -or -not (Test-Path -LiteralPath $Root)) { return $null }
  return Get-ChildItem -LiteralPath $Root -Recurse -File -Filter "$Prefix*.json" |
    Sort-Object Name -Descending |
    Select-Object -First 1
}

function Get-RuntimeEvidenceSummary {
  param([string]$EvidenceRoot)
  $temporalFile = Get-LatestEvidenceJson -Root $EvidenceRoot -Prefix 'quest_temporal_visual_gate_'
  if ($null -ne $temporalFile) {
    $report = Get-Content -LiteralPath $temporalFile.FullName -Raw | ConvertFrom-Json
    $budget = Get-ObjectProperty $report 'renderBudget'
    $runtimeDraw = [long](Get-ObjectProperty $report 'maximumRuntimeDrawCalls' 0)
    $auditDraw = [long](Get-ObjectProperty $budget 'estimatedInstancedDrawCalls' 0)
    $runtimeTriangles = [long](Get-ObjectProperty $report 'maximumRuntimeTriangles' 0)
    $auditTriangles = [long](Get-ObjectProperty $budget 'estimatedFrustumTriangles' 0)
    return [pscustomobject]@{
      available = $true
      source = 'QuestTemporalVisualGateRecorder'
      path = $temporalFile.FullName
      readinessMet = [bool](Get-ObjectProperty $report 'readinessPrerequisitesMet' $false)
      readinessStatus = [string](Get-ObjectProperty $report 'readinessStatus' '')
      warmupSeconds = [double](Get-ObjectProperty $report 'warmupSeconds' 0)
      measuredSeconds = [double](Get-ObjectProperty $report 'measuredSeconds' 0)
      sampleCount = [int](Get-ObjectProperty $report 'sampleCount' 0)
      averageFrameMs = [double](Get-ObjectProperty $report 'averageFrameMs' 0)
      p95FrameMs = [double](Get-ObjectProperty $report 'p95FrameMs' 0)
      p99FrameMs = [double](Get-ObjectProperty $report 'p99FrameMs' 0)
      framesOverBudget = [int](Get-ObjectProperty $report 'framesOver72HzBudget' 0)
      framesOverBudgetRatio = [double](Get-ObjectProperty $report 'framesOver72HzBudgetRatio' 0)
      deliveredIntervalSemantics = [string](Get-ObjectProperty $report 'deliveredIntervalSemantics' 'Time.unscaledDeltaTime delivered-frame interval')
      averageWorkloadMs = [double](Get-ObjectProperty $report 'averageWorkloadMs' 0)
      p95WorkloadMs = [double](Get-ObjectProperty $report 'p95WorkloadMs' 0)
      workloadSamplesOverBudgetRatio = [double](Get-ObjectProperty $report 'workloadSamplesOver72HzBudgetRatio' 0)
      workloadTimingSemantics = [string](Get-ObjectProperty $report 'workloadTimingSemantics' 'Unity FrameTimingManager max(CPU,GPU) when available')
      averageCpuFrameMs = [double](Get-ObjectProperty $report 'averageCpuFrameMs' 0)
      p95CpuFrameMs = [double](Get-ObjectProperty $report 'p95CpuFrameMs' 0)
      averageGpuFrameMs = [double](Get-ObjectProperty $report 'averageGpuFrameMs' 0)
      p95GpuFrameMs = [double](Get-ObjectProperty $report 'p95GpuFrameMs' 0)
      gpuTimingSamples = [int](Get-ObjectProperty $report 'gpuTimingSamples' 0)
      allocationRecorderAvailable = [bool](Get-ObjectProperty $report 'allocationRecorderAvailable' $false)
      averageGcAllocatedBytesPerFrame = [long](Get-ObjectProperty $report 'averageGcAllocatedBytesPerFrame' ([long]::MaxValue))
      maxGcAllocatedBytesPerFrame = [long](Get-ObjectProperty $report 'maxGcAllocatedBytesPerFrame' ([long]::MaxValue))
      drawCalls = $(if ($runtimeDraw -gt 0) { $runtimeDraw } else { $auditDraw })
      drawCallSource = $(if ($runtimeDraw -gt 0) { 'Unity runtime profiler counter' } else { 'QuestRenderBudgetAudit effective visible material-submesh slots before batching/instancing' })
      visibleTriangles = $(if ($runtimeTriangles -gt 0) { $runtimeTriangles } else { $auditTriangles })
      triangleSource = $(if ($runtimeTriangles -gt 0) { 'Unity runtime profiler counter' } else { 'QuestRenderBudgetAudit effective visible material-submesh triangle indices' })
      activeRenderers = [int](Get-ObjectProperty $report 'activeRendererCount' 0)
      activeLodGroups = [int](Get-ObjectProperty $report 'activeLodGroupCount' 0)
      crossFadeLodGroups = [int](Get-ObjectProperty $report 'crossFadeLodGroupCount' 0)
      waterRenderers = [int](Get-ObjectProperty $report 'waterRendererCount' 0)
      transparentWaterRenderers = [int](Get-ObjectProperty $report 'transparentWaterRendererCount' 0)
      waterMaterials = [int](Get-ObjectProperty $report 'uniqueWaterMaterialCount' 0)
      waterTriangles = [long](Get-ObjectProperty $report 'waterTriangles' 0)
      productionVerticalSliceActive = [bool](Get-ObjectProperty $report 'productionVerticalSliceActive' $false)
      productionArchitectureVersion = [string](Get-ObjectProperty $report 'productionArchitectureVersion' '')
      authoredProductionHierarchyValid = [bool](Get-ObjectProperty $report 'authoredProductionHierarchyValid' $false)
      productionEnvironmentContractValid = [bool](Get-ObjectProperty $report 'productionEnvironmentContractValid' $false)
      legacyRuntimeRepairAbsent = [bool](Get-ObjectProperty $report 'legacyRuntimeRepairAbsent' $false)
      authoritativePhysicsBackend = [string](Get-ObjectProperty $report 'authoritativePhysicsBackend' '')
      sceneMaterialCount = [int](Get-ObjectProperty $report 'sceneMaterialCount' 0)
      performanceDrawCallGate = [int](Get-ObjectProperty $report 'performanceDrawCallGate' 0)
      performanceVisibleTriangleGate = [long](Get-ObjectProperty $report 'performanceVisibleTriangleGate' 0)
      realDataEnvironmentActive = [bool](Get-ObjectProperty $report 'realDataEnvironmentActive' $false)
      proceduralFallbackActive = [bool](Get-ObjectProperty $report 'proceduralFallbackActive' $false)
      runtimeEnvironmentRoot = [string](Get-ObjectProperty $report 'runtimeEnvironmentRoot' '')
      seatAlignmentCompleted = [bool](Get-ObjectProperty $report 'startupSeatAlignmentCompleted' $false)
      seatRecenterCount = [int](Get-ObjectProperty $report 'startupSeatRecenterCount' 0)
      seatPositionErrorMeters = [double](Get-ObjectProperty $report 'startupSeatPositionErrorMeters' -1)
      seatYawErrorDegrees = [double](Get-ObjectProperty $report 'startupSeatYawErrorDegrees' -1)
      defaultPilotEyeAftMeters = [double](Get-ObjectProperty $report 'defaultPilotEyeAftMeters' 0)
      eyeToPanelDistanceMeters = [double](Get-ObjectProperty $report 'eyeToPanelDistanceMeters' -1)
      productionDefaultSeatAuthored = [bool](Get-ObjectProperty $report 'productionDefaultSeatAuthored' $false)
      cockpitLightingStrategy = [string](Get-ObjectProperty $report 'cockpitLightingStrategy' '')
      cockpitStaticDepthStrength = [double](Get-ObjectProperty $report 'cockpitStaticDepthStrength' 0)
      cockpitRealtimeShadowCasters = [int](Get-ObjectProperty $report 'cockpitRealtimeShadowCasters' -1)
      cockpitRealtimeShadowReceivers = [int](Get-ObjectProperty $report 'cockpitRealtimeShadowReceivers' -1)
    }
  }

  $renderFile = Get-LatestEvidenceJson -Root $EvidenceRoot -Prefix 'render_performance_'
  if ($null -eq $renderFile) {
    return [pscustomobject]@{ available = $false; source = 'none'; path = ''; readinessMet = $false }
  }
  $report = Get-Content -LiteralPath $renderFile.FullName -Raw | ConvertFrom-Json
  $timing = Get-ObjectProperty $report 'steadyStateFrameTiming'
  $budget = Get-ObjectProperty $report 'renderBudget'
  return [pscustomobject]@{
    available = $true
    source = 'QuestRenderBudgetReporter'
    path = $renderFile.FullName
    readinessMet = [bool](Get-ObjectProperty $report 'readinessPrerequisitesMet' $true)
    readinessStatus = [string](Get-ObjectProperty $report 'readinessStatus' 'legacy evidence did not record readiness')
    warmupSeconds = 0.0
    measuredSeconds = 0.0
    sampleCount = [int](Get-ObjectProperty $timing 'sampleCount' 0)
    averageFrameMs = [double](Get-ObjectProperty $timing 'averageFrameMs' 0)
    p95FrameMs = [double](Get-ObjectProperty $timing 'p95FrameMs' 0)
    p99FrameMs = [double](Get-ObjectProperty $timing 'p99FrameMs' 0)
    framesOverBudget = [int](Get-ObjectProperty $timing 'framesOver72HzBudget' 0)
    framesOverBudgetRatio = [double](Get-ObjectProperty $timing 'framesOver72HzBudgetRatio' 0)
    deliveredIntervalSemantics = 'Legacy Unity Time.unscaledDeltaTime delivered-frame interval.'
    averageWorkloadMs = [double](Get-ObjectProperty $report 'averageCpuFrameMs' 0)
    p95WorkloadMs = [double](Get-ObjectProperty $report 'p95CpuFrameMs' 0)
    workloadSamplesOverBudgetRatio = [double](Get-ObjectProperty $timing 'framesOver72HzBudgetRatio' 0)
    workloadTimingSemantics = 'Legacy Unity FrameTimingManager CPU duration; may include XR synchronization wait.'
    averageCpuFrameMs = [double](Get-ObjectProperty $report 'averageCpuFrameMs' 0)
    p95CpuFrameMs = [double](Get-ObjectProperty $report 'p95CpuFrameMs' 0)
    averageGpuFrameMs = [double](Get-ObjectProperty $report 'averageGpuFrameMs' 0)
    p95GpuFrameMs = [double](Get-ObjectProperty $report 'p95GpuFrameMs' 0)
    gpuTimingSamples = $(if ([double](Get-ObjectProperty $report 'averageGpuFrameMs' 0) -gt 0.01) { [int](Get-ObjectProperty $report 'frameTimingManagerSamples' 0) } else { 0 })
    allocationRecorderAvailable = $false
    averageGcAllocatedBytesPerFrame = [long]::MaxValue
    maxGcAllocatedBytesPerFrame = [long]::MaxValue
    drawCalls = [long](Get-ObjectProperty $budget 'estimatedInstancedDrawCalls' 0)
    drawCallSource = 'QuestRenderBudgetAudit instancing estimate'
    visibleTriangles = [long](Get-ObjectProperty $budget 'estimatedFrustumTriangles' 0)
    triangleSource = 'QuestRenderBudgetAudit frustum estimate'
    activeRenderers = [int](Get-ObjectProperty $budget 'enabledRendererCount' 0)
    activeLodGroups = [int](Get-ObjectProperty $budget 'lodGroupCount' 0)
    crossFadeLodGroups = -1
    waterRenderers = -1
    transparentWaterRenderers = -1
    waterMaterials = -1
    waterTriangles = -1
    realDataEnvironmentActive = $true
    proceduralFallbackActive = $false
    runtimeEnvironmentRoot = 'KBDU_RealData_World_NotForNavigation (from baseline runtime log)'
    seatAlignmentCompleted = $true
    seatRecenterCount = 1
    seatPositionErrorMeters = 0.0
    seatYawErrorDegrees = 0.0
    defaultPilotEyeAftMeters = 0.0
    eyeToPanelDistanceMeters = -1
    cockpitLightingStrategy = 'legacy no-cockpit-shadow-map policy'
    cockpitStaticDepthStrength = 0.0
    cockpitRealtimeShadowCasters = 0
    cockpitRealtimeShadowReceivers = 0
  }
}

function Get-MountainSummary {
  param([string]$EvidenceRoot)
  $file = Get-LatestEvidenceJson -Root $EvidenceRoot -Prefix 'mountain_temporal_stability_'
  if ($null -eq $file) { return [pscustomobject]@{ available = $false; path = ''; classification = 'NOT_CAPTURED' } }
  $report = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
  return [pscustomobject]@{
    available = $true
    path = $file.FullName
    classification = [string](Get-ObjectProperty $report 'classification' '')
    passed = [bool](Get-ObjectProperty $report 'passed' $false)
    frames = [int](Get-ObjectProperty $report 'sampledFrameCount' 0)
    rendererSamples = [int](Get-ObjectProperty $report 'rendererSampleCount' 0)
    immutableTransforms = [bool](Get-ObjectProperty $report 'immutableTransforms' $false)
    immutableMeshes = [bool](Get-ObjectProperty $report 'immutableMeshes' $false)
    immutableRendererSet = [bool](Get-ObjectProperty $report 'immutableRendererSet' $false)
    noLodOrDither = [bool](Get-ObjectProperty $report 'noTerrainLodOrDither' $false)
    oneMountainSource = [bool](Get-ObjectProperty $report 'oneMountainSource' $false)
    violations = @((Get-ObjectProperty $report 'violations' @()))
  }
}

function Get-FaultSummary {
  param([string]$LogText, [string]$AppPid, [string]$PackageName)
  $packageRegex = [regex]::Escape($PackageName)
  $pidRegex = if ([string]::IsNullOrWhiteSpace($AppPid)) { '\d+' } else { [regex]::Escape($AppPid) }
  $crash = $LogText -match "(?is)(FATAL EXCEPTION.{0,2000}$packageRegex|Fatal signal \d+.{0,500}(?:$packageRegex|pid\s+$pidRegex)|Force finishing activity.{0,300}$packageRegex)"
  $anr = $LogText -match "(?is)(ANR in\s+$packageRegex|am_anr.{0,500}$packageRegex)"
  $lines = @($LogText -split "`r?`n")
  # A system-wide logcat commonly contains low-memory kills for unrelated
  # background packages. Require the OOM signal and this app's package/PID on
  # the same log line so nearby system traffic cannot fail the product gate.
  $oomLines = @($lines | Where-Object {
    $_ -match '(?i)(OutOfMemoryError|lowmemorykiller|oom_reaper)' -and
    ($_ -match $packageRegex -or $_ -match "\b$pidRegex\b")
  })
  $unityExceptions = @($lines | Where-Object {
    $_ -match "\s+$pidRegex\s+\d+\s+[EAWID]\s+Unity\s*:.*(?:Exception|Error):" -and
    $_ -notmatch 'Evidence write failed'
  })
  # Treat dumpsys status and VrApi PLS as the primary thermal signals. Logcat
  # only contributes explicit critical/severe/emergency/shutdown events or an
  # affirmative active/enabled/started throttling event; class names and empty
  # `*Throttling {}` configuration messages are not thermal failures.
  $thermalWarningLines = @($lines | Where-Object {
    $_ -match '(?i)(thermal|temperature)' -and
    ($_ -match '(?i)(critical|severe|emergency|shutdown|status\s*[:=]\s*[1-6])' -or
      $_ -match '(?i)(throttl(?:e|ing)[^\r\n]*(?:active|enabled|started)|(?:active|enabled|started)[^\r\n]*throttl(?:e|ing))')
  })
  $guardianDialogLines = @($lines | Where-Object {
    $_ -match '(?i)(GuardianDialogActivity|GuardianDialogMgr[^\r\n]*requestDialog|GUARDIAN_TRACKING_LOST)'
  })
  return [pscustomobject]@{
    crash = [bool]$crash
    anr = [bool]$anr
    oom = $oomLines.Count -gt 0
    oomLines = @($oomLines | Select-Object -Unique)
    unityExceptionCount = $unityExceptions.Count
    unityExceptionLines = @($unityExceptions | Select-Object -Unique)
    thermalWarningInLog = $thermalWarningLines.Count -gt 0
    thermalWarningLines = @($thermalWarningLines | Select-Object -Unique)
    guardianDialogDetected = $guardianDialogLines.Count -gt 0
    guardianDialogLines = @($guardianDialogLines | Select-Object -Unique -First 20)
  }
}

function Get-BottleneckDiagnosis {
  param($Runtime, $VrApi)
  if ($null -ne $Runtime -and [int](Get-ObjectProperty $Runtime 'gpuTimingSamples' 0) -gt 0) {
    $cpu = [double](Get-ObjectProperty $Runtime 'p95CpuFrameMs' 0)
    $gpu = [double](Get-ObjectProperty $Runtime 'p95GpuFrameMs' 0)
    if ($gpu -gt $cpu * 1.10) { return 'GPU-limited by nonzero Unity FrameTimingManager p95 GPU duration.' }
    if ($cpu -gt $gpu * 1.10) { return 'CPU-limited by Unity FrameTimingManager p95 CPU duration, subject to XR-wait semantics.' }
    return 'Mixed CPU/GPU workload by nonzero Unity FrameTimingManager durations.'
  }
  if ($null -eq $VrApi -or -not [bool](Get-ObjectProperty $VrApi 'available' $false)) {
    return 'UNPROVEN: no direct GPU timing or app-PID VrApi samples were available.'
  }
  $gpu95 = [double](Get-ObjectProperty $VrApi 'p95GpuUtilization' 0)
  $cpu95 = [double](Get-ObjectProperty $VrApi 'p95CpuUtilization' 0)
  $combined95 = [double](Get-ObjectProperty $VrApi 'p95CpuAndGpuMs' 0)
  if ($gpu95 -ge 0.85 -and $cpu95 -lt 0.75) { return 'Likely GPU-limited: VrApi GPU utilization p95 is high while CPU utilization is lower; direct GPU milliseconds remain unavailable.' }
  if ($cpu95 -ge 0.85 -and $gpu95 -lt 0.75) { return 'Likely CPU-limited: VrApi CPU utilization p95 is high while GPU utilization is lower; Unity CPU timing may include XR wait.' }
  if ($combined95 -gt $QuestFrameBudgetMs -or [int](Get-ObjectProperty $VrApi 'staleTotal' 0) -gt 0) {
    return 'Mixed GPU/compositor pacing: direct GPU milliseconds are unavailable; VrApi combined timing/stale frames reach the cadence budget without single-resource saturation.'
  }
  return 'No single-resource saturation proven: direct GPU milliseconds are unavailable and app-PID VrApi utilization remains below a clear CPU/GPU bound.'
}

function Invoke-Adb {
  param([string[]]$Arguments, [switch]$AllowFailure)
  $all = @()
  if (-not [string]::IsNullOrWhiteSpace($script:ResolvedSerial)) {
    $all += @('-s', $script:ResolvedSerial)
  }
  $all += $Arguments
  $output = (& $AdbExe @all 2>&1 | Out-String)
  $exitCode = $LASTEXITCODE
  if ($exitCode -ne 0 -and -not $AllowFailure) {
    throw "adb failed (exit $exitCode): $($Arguments -join ' ')`n$output"
  }
  return $output.TrimEnd()
}

function Resolve-QuestDevice {
  if (-not (Test-Path -LiteralPath $AdbExe)) { throw "ADB not found: $AdbExe" }
  & $AdbExe start-server | Out-Null
  $devicesText = (& $AdbExe devices -l | Out-String)
  $deviceLines = @($devicesText -split "`r?`n" | Where-Object { $_ -match '^\S+\s+device\b' })
  $unauthorized = @($devicesText -split "`r?`n" | Where-Object { $_ -match '^\S+\s+unauthorized\b' })
  if ($unauthorized.Count -gt 0) { throw 'Quest ADB is unauthorized. Approve USB debugging in-headset before the final gate.' }
  if (-not [string]::IsNullOrWhiteSpace($script:ResolvedSerial)) {
    if (-not ($deviceLines | Where-Object { $_ -match ('^' + [regex]::Escape($script:ResolvedSerial) + '\s+device\b') })) {
      throw "Requested Quest serial is not connected: $($script:ResolvedSerial)"
    }
  } elseif ($deviceLines.Count -eq 1) {
    $script:ResolvedSerial = ($deviceLines[0] -split '\s+')[0]
  } elseif ($deviceLines.Count -eq 0) {
    throw 'No Quest ADB device is connected.'
  } else {
    throw 'Multiple ADB devices are connected. Pass -DeviceSerial to select the Quest explicitly.'
  }
  return $devicesText
}

function Start-AdbBackgroundProcess {
  param([string[]]$Arguments, [string]$StdoutPath, [string]$StderrPath)
  $all = @()
  if (-not [string]::IsNullOrWhiteSpace($script:ResolvedSerial)) {
    $all += @('-s', $script:ResolvedSerial)
  }
  $all += $Arguments
  return Start-Process -FilePath $AdbExe -ArgumentList $all -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $StdoutPath -RedirectStandardError $StderrPath
}

function Wait-ForLogPattern {
  param(
    [string]$Path,
    [string]$Pattern,
    [int]$TimeoutSeconds,
    [string]$Stage
  )
  $started = Get-Date
  $nextUpdate = 0
  while (((Get-Date) - $started).TotalSeconds -lt $TimeoutSeconds) {
    $elapsed = [int]((Get-Date) - $started).TotalSeconds
    if ($elapsed -ge $nextUpdate) {
      Write-Host "${Stage}: ${elapsed}s / ${TimeoutSeconds}s"
      $nextUpdate += 10
    }
    if (Test-Path -LiteralPath $Path) {
      $text = Get-Content -LiteralPath $Path -Raw -ErrorAction SilentlyContinue
      if ($text -match $Pattern) { return $true }
    }
    Start-Sleep -Milliseconds 1000
  }
  return $false
}

function Capture-AdbScreenshot {
  param([string]$Path, [string]$ErrorPath)
  $arguments = @()
  if (-not [string]::IsNullOrWhiteSpace($script:ResolvedSerial)) {
    $arguments += @('-s', $script:ResolvedSerial)
  }
  $arguments += @('exec-out', 'screencap', '-p')
  $startInfo = New-Object System.Diagnostics.ProcessStartInfo
  $startInfo.FileName = $AdbExe
  $startInfo.Arguments = ($arguments -join ' ')
  $startInfo.UseShellExecute = $false
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $process = [System.Diagnostics.Process]::Start($startInfo)
  $stream = [System.IO.File]::Create($Path)
  try { $process.StandardOutput.BaseStream.CopyTo($stream) } finally { $stream.Dispose() }
  $errorText = $process.StandardError.ReadToEnd()
  $process.WaitForExit()
  if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $Path) -or (Get-Item -LiteralPath $Path).Length -eq 0) {
    Set-Content -LiteralPath $ErrorPath -Value $errorText -Encoding UTF8
    return $false
  }
  return $true
}

function Pull-CurrentEvidencePaths {
  param([string]$LogText, [string]$DestinationRoot)
  $escapedPackage = [regex]::Escape($PackageId)
  $pattern = "/storage/emulated/0/Android/data/$escapedPackage/files/QuestFlight/[^\s\]\[\)\(,;]+\.(?:json|md|png)"
  $paths = @([regex]::Matches($LogText, $pattern) | ForEach-Object { $_.Value.TrimEnd('.', ':') } | Sort-Object -Unique)
  $derived = @()
  foreach ($path in $paths) {
    $derived += $path
    if ($path.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase)) {
      $derived += $path.Substring(0, $path.Length - 5) + '.md'
    }
  }
  $results = @()
  foreach ($devicePath in @($derived | Sort-Object -Unique)) {
    $exists = Invoke-Adb -Arguments @('shell', 'test', '-f', $devicePath) -AllowFailure
    if ($LASTEXITCODE -ne 0) { continue }
    $relative = $devicePath.Substring($devicePath.IndexOf('/QuestFlight/', [StringComparison]::Ordinal) + '/QuestFlight/'.Length)
    $relativeWindows = $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $localPath = Join-Path $DestinationRoot $relativeWindows
    $parent = Split-Path -Parent $localPath
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    $pullOutput = Invoke-Adb -Arguments @('pull', $devicePath, $localPath) -AllowFailure
    $results += [pscustomobject]@{
      devicePath = $devicePath
      localPath = $localPath
      pulled = (Test-Path -LiteralPath $localPath)
      bytes = $(if (Test-Path -LiteralPath $localPath) { (Get-Item -LiteralPath $localPath).Length } else { 0 })
      adb = $pullOutput
    }
  }
  return $results
}

function Write-GateSummary {
  param(
    [string]$ArtifactDir,
    [string]$LogcatPath,
    [string]$AppPid,
    $Runtime,
    $VrApi,
    $Mountain,
    $Baseline,
    $Faults,
    [bool]$ThermalWarning,
    [string]$ThermalStatus,
    $ScreenRecording,
    [string]$HumanStatus,
    [bool]$StaticOnly,
    [object[]]$PulledEvidence
  )
  $runtimeAvailable = [bool](Get-ObjectProperty $Runtime 'available' $false)
  $p95Pass = $runtimeAvailable -and [double](Get-ObjectProperty $Runtime 'p95FrameMs' 999) -le $RequiredP95Ms
  $overPass = $runtimeAvailable -and [double](Get-ObjectProperty $Runtime 'framesOverBudgetRatio' 1) -le $RequiredOverBudgetRatio
  $drawPass = $runtimeAvailable -and [long](Get-ObjectProperty $Runtime 'drawCalls' ([long]::MaxValue)) -le $RequiredDrawCalls
  $trianglePass = $runtimeAvailable -and [long](Get-ObjectProperty $Runtime 'visibleTriangles' ([long]::MaxValue)) -le $RequiredVisibleTriangles
  $materialPass = $runtimeAvailable -and [int](Get-ObjectProperty $Runtime 'sceneMaterialCount' 0) -le $RequiredSceneMaterials
  $allocationPass = $runtimeAvailable -and
    [bool](Get-ObjectProperty $Runtime 'allocationRecorderAvailable' $false) -and
    [long](Get-ObjectProperty $Runtime 'maxGcAllocatedBytesPerFrame' ([long]::MaxValue)) -eq 0
  $durationPass = $runtimeAvailable -and [double](Get-ObjectProperty $Runtime 'warmupSeconds' 0) -ge 90.0 -and [double](Get-ObjectProperty $Runtime 'measuredSeconds' 0) -ge 60.0
  $faultFree = -not $Faults.crash -and -not $Faults.anr -and -not $Faults.oom -and $Faults.unityExceptionCount -eq 0
  $machinePass = $p95Pass -and $overPass -and $drawPass -and $trianglePass -and $materialPass -and $allocationPass -and $durationPass -and $faultFree -and -not $ThermalWarning

  $workloadSource = 'unavailable'
  $workloadAverage = 0.0
  $workloadP95 = 0.0
  $workloadOverRatio = 1.0
  $workloadSampleCount = 0
  if ([bool](Get-ObjectProperty $VrApi 'available' $false)) {
    $workloadSource = 'VrApi app-PID CPU&GPU one-second samples'
    $workloadAverage = [double](Get-ObjectProperty $VrApi 'averageCpuAndGpuMs' 0)
    $workloadP95 = [double](Get-ObjectProperty $VrApi 'p95CpuAndGpuMs' 0)
    $workloadOverRatio = [double](Get-ObjectProperty $VrApi 'cpuAndGpuSamplesOverBudgetRatio' 1)
    $workloadSampleCount = [int](Get-ObjectProperty $VrApi 'sampleCount' 0)
  } elseif ($runtimeAvailable -and [double](Get-ObjectProperty $Runtime 'p95WorkloadMs' 0) -gt 0) {
    $workloadSource = 'Unity FrameTimingManager max(CPU,GPU); CPU may include XR wait'
    $workloadAverage = [double](Get-ObjectProperty $Runtime 'averageWorkloadMs' 0)
    $workloadP95 = [double](Get-ObjectProperty $Runtime 'p95WorkloadMs' 0)
    $workloadOverRatio = [double](Get-ObjectProperty $Runtime 'workloadSamplesOverBudgetRatio' 1)
    $workloadSampleCount = [int](Get-ObjectProperty $Runtime 'sampleCount' 0)
  }
  $workloadTimingPass = $workloadSampleCount -gt 0 -and $workloadP95 -le $RequiredP95Ms -and $workloadOverRatio -le $RequiredOverBudgetRatio
  $workloadGatePass = $workloadTimingPass -and $drawPass -and $trianglePass -and $faultFree -and -not $ThermalWarning

  $classification = if ($StaticOnly) {
    'NOT_RUN_BLOCKED_HEADSET_TEMPORAL_ACCEPTANCE'
  } elseif (-not $runtimeAvailable) {
    'FAIL_INCOMPLETE_RUNTIME_EVIDENCE'
  } elseif ($machinePass) {
    'MACHINE_GATE_PASS_HUMAN_WITNESS_REQUIRED'
  } else {
    'FAIL_MACHINE_GATE'
  }
  $bottleneck = Get-BottleneckDiagnosis -Runtime $Runtime -VrApi $VrApi
  $baselineComparison = if ($null -ne $Baseline -and [bool](Get-ObjectProperty $Baseline 'available' $false) -and $runtimeAvailable) {
    [pscustomobject]@{
      averageFrameMsDelta = [double](Get-ObjectProperty $Runtime 'averageFrameMs' 0) - [double](Get-ObjectProperty $Baseline 'averageFrameMs' 0)
      p95FrameMsDelta = [double](Get-ObjectProperty $Runtime 'p95FrameMs' 0) - [double](Get-ObjectProperty $Baseline 'p95FrameMs' 0)
      framesOverBudgetRatioDelta = [double](Get-ObjectProperty $Runtime 'framesOverBudgetRatio' 0) - [double](Get-ObjectProperty $Baseline 'framesOverBudgetRatio' 0)
      drawCallsDelta = [long](Get-ObjectProperty $Runtime 'drawCalls' 0) - [long](Get-ObjectProperty $Baseline 'drawCalls' 0)
      visibleTrianglesDelta = [long](Get-ObjectProperty $Runtime 'visibleTriangles' 0) - [long](Get-ObjectProperty $Baseline 'visibleTriangles' 0)
    }
  } else { $null }

  $summary = [ordered]@{
    schemaVersion = 2
    generatedUtc = [DateTime]::UtcNow.ToString('O')
    classification = $classification
    humanAcceptanceStatus = $HumanStatus
    staticEvidenceOnly = $StaticOnly
    packageId = $PackageId
    appPid = $AppPid
    requirements = [ordered]@{
      warmupSeconds = 90
      measurementSeconds = 60
      averageFrameMs = 'reported, not a hard gate in Clean Product Extraction V1'
      p95FrameMsMax = $RequiredP95Ms
      framesOverBudgetRatioMax = $RequiredOverBudgetRatio
      drawCallsMax = $RequiredDrawCalls
      visibleTrianglesMax = $RequiredVisibleTriangles
      sceneMaterialsMax = $RequiredSceneMaterials
      steadyStateGcAllocatedBytesPerFrameMax = 0
    }
    deliveredCadenceGate = [ordered]@{
      passed = $machinePass
      semantics = 'Strict requested frame-series thresholds evaluated on the Unity delivered-frame intervals; at locked 72 Hz the average naturally approaches 13.889 ms.'
      durationPassed = $durationPass
      p95Passed = $p95Pass
      overBudgetRatioPassed = $overPass
      drawCallsPassed = $drawPass
      visibleTrianglesPassed = $trianglePass
      sceneMaterialsPassed = $materialPass
      zeroSteadyStateGcAllocationsPassed = $allocationPass
      faultFree = $faultFree
      thermalPassed = -not $ThermalWarning
    }
    workloadGate = [ordered]@{
      passed = $workloadGatePass
      source = $workloadSource
      semantics = 'Supplemental execution-workload gate. VrApi CPU&GPU values are one-second app-row samples, not a substitute for the delivered per-frame distribution.'
      sampleCount = $workloadSampleCount
      averageMs = $workloadAverage
      p95Ms = $workloadP95
      samplesOverBudgetRatio = $workloadOverRatio
      timingPassed = $workloadTimingPass
      drawCallsPassed = $drawPass
      visibleTrianglesPassed = $trianglePass
      sceneMaterialsPassed = $materialPass
      faultFree = $faultFree
      thermalPassed = -not $ThermalWarning
    }
    machineGate = [ordered]@{
      passed = $machinePass
      compatibilityNote = 'Alias of deliveredCadenceGate for existing evidence readers; workloadGate is intentionally separate.'
    }
    runtime = $Runtime
    vrApiAppPidWindow = $VrApi
    mountainProbe = $Mountain
    faultChecks = $Faults
    thermal = [ordered]@{ warning = $ThermalWarning; status = $ThermalStatus }
    screenRecording = $ScreenRecording
    bottleneckDiagnosis = $bottleneck
    baseline = $Baseline
    baselineComparison = $baselineComparison
    captureOverhead = [ordered]@{
      screenRecordingDuringMeasurement = [bool](Get-ObjectProperty $ScreenRecording 'started' $false)
      disclosure = 'adb screenrecord can add compositor/encoder overhead. Runtime per-frame and VrApi values are retained with this flag; rerun with -SkipScreenRecording for an unrecorded performance control if a hard gate is marginal.'
      recorderDisclosure = 'The Unity recorder preallocates struct samples and reuses its yield instruction; it performs no intentional per-frame managed allocation.'
    }
    pulledEvidence = @($PulledEvidence | Where-Object { $null -ne $_ } | ForEach-Object {
      [pscustomobject]@{
        devicePath = Get-ObjectProperty $_ 'devicePath' ''
        localPath = Get-ObjectProperty $_ 'localPath' ''
        pulled = [bool](Get-ObjectProperty $_ 'pulled' $false)
        bytes = [long](Get-ObjectProperty $_ 'bytes' 0)
      }
    })
    logcatPath = $LogcatPath
    metricSemantics = [ordered]@{
      deliveredInterval = 'Unity Time.unscaledDeltaTime is the delivered-frame cadence. At synchronized 72 Hz it naturally centers near 13.889 ms, so it is reported separately from execution workload.'
      workload = 'Unity FrameTimingManager CPU/GPU values are workload evidence, but CPU may include XR wait and Quest GPU values may be zero.'
      vrApi = 'VrApi is filtered to the app PID plus SF=1.00 and sliced between the measurement-start and evidence-written markers. Fov is recorded but is not used as an app-identity filter.'
      acceptance = 'The requested average/p95/over-budget thresholds are evaluated against the delivered per-frame series without silently substituting another metric.'
    }
  }

  $jsonPath = Join-Path $ArtifactDir 'quest_temporal_visual_gate_summary.json'
  $summary | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

  $md = New-Object System.Text.StringBuilder
  [void]$md.AppendLine('# Quest Temporal Visual Gate Summary')
  [void]$md.AppendLine()
  [void]$md.AppendLine("- Classification: **$classification**")
  [void]$md.AppendLine("- Human acceptance: **$HumanStatus**")
  [void]$md.AppendLine("- Machine gate: **$(if ($machinePass) { 'PASS' } else { 'FAIL/NOT RUN' })**")
  [void]$md.AppendLine("- Supplemental workload gate: **$(if ($workloadGatePass) { 'PASS' } else { 'FAIL/NOT RUN' })** ($workloadSource)")
  [void]$md.AppendLine("- Bottleneck diagnosis: $bottleneck")
  [void]$md.AppendLine()
  [void]$md.AppendLine('## Exact 90 s + 60 s timing gate')
  [void]$md.AppendLine()
  if ($runtimeAvailable) {
    [void]$md.AppendLine("- Warmup / measurement: $(Format-Number $Runtime.warmupSeconds '0.00') s / $(Format-Number $Runtime.measuredSeconds '0.00') s")
    [void]$md.AppendLine("- Delivered interval average / p95 / p99: $(Format-Number $Runtime.averageFrameMs '0.000') / $(Format-Number $Runtime.p95FrameMs '0.000') / $(Format-Number $Runtime.p99FrameMs '0.000') ms")
    [void]$md.AppendLine("- Frames over 13.889 ms: $($Runtime.framesOverBudget) / $($Runtime.sampleCount) ($(Format-Number ([double]$Runtime.framesOverBudgetRatio * 100) '0.00')%)")
    [void]$md.AppendLine("- Workload average / p95: $(Format-Number $Runtime.averageWorkloadMs '0.000') / $(Format-Number $Runtime.p95WorkloadMs '0.000') ms")
    [void]$md.AppendLine("- Draw calls: $($Runtime.drawCalls) ($($Runtime.drawCallSource)); visible triangles: $($Runtime.visibleTriangles) ($($Runtime.triangleSource)); scene materials: $(Get-ObjectProperty $Runtime 'sceneMaterialCount' 0)")
    [void]$md.AppendLine("- GC allocation recorder / average / maximum: $($Runtime.allocationRecorderAvailable) / $($Runtime.averageGcAllocatedBytesPerFrame) / $($Runtime.maxGcAllocatedBytesPerFrame) bytes per frame")
    [void]$md.AppendLine("- Production scene / architecture / backend: $(Get-ObjectProperty $Runtime 'productionVerticalSliceActive' $false) / $(Get-ObjectProperty $Runtime 'productionArchitectureVersion' '') / $(Get-ObjectProperty $Runtime 'authoritativePhysicsBackend' '')")
    [void]$md.AppendLine("- Active renderers / LOD groups / crossfade LOD groups: $($Runtime.activeRenderers) / $($Runtime.activeLodGroups) / $($Runtime.crossFadeLodGroups)")
    [void]$md.AppendLine("- Water surfaces / transparent water slots / materials / triangles: $($Runtime.waterRenderers) / $($Runtime.transparentWaterRenderers) / $($Runtime.waterMaterials) / $($Runtime.waterTriangles)")
    [void]$md.AppendLine("- Seat: completed=$($Runtime.seatAlignmentCompleted), recenter=$($Runtime.seatRecenterCount), error=$(Format-Number $Runtime.seatPositionErrorMeters '0.0000') m / $(Format-Number $Runtime.seatYawErrorDegrees '0.00') deg")
  } else {
    [void]$md.AppendLine('- Runtime measurement evidence was not produced.')
  }
  [void]$md.AppendLine()
  [void]$md.AppendLine('The delivered interval is presentation cadence, not pure app execution time. The requested thresholds are still evaluated against that series; workload and VrApi values are reported separately rather than substituted silently.')
  [void]$md.AppendLine()
  [void]$md.AppendLine('## App-PID VrApi evidence')
  [void]$md.AppendLine()
  if ([bool](Get-ObjectProperty $VrApi 'available' $false)) {
    [void]$md.AppendLine("- PID / exact window / samples: $($VrApi.appPid) / $($VrApi.exactMeasurementWindow) / $($VrApi.sampleCount)")
    [void]$md.AppendLine("- App average / p95: $(Format-Number $VrApi.averageAppMs '0.000') / $(Format-Number $VrApi.p95AppMs '0.000') ms")
    [void]$md.AppendLine("- CPU&GPU average / p95: $(Format-Number $VrApi.averageCpuAndGpuMs '0.000') / $(Format-Number $VrApi.p95CpuAndGpuMs '0.000') ms")
    [void]$md.AppendLine("- GPU / CPU utilization average: $(Format-Number ([double]$VrApi.averageGpuUtilization * 100) '0.0')% / $(Format-Number ([double]$VrApi.averageCpuUtilization * 100) '0.0')%")
    [void]$md.AppendLine("- Stale / tear total: $($VrApi.staleTotal) / $($VrApi.tearTotal); stale peak per one-second sample: $($VrApi.stalePeakPerSample)")
    [void]$md.AppendLine("- Direct GPU ms available: $($VrApi.directGpuTimingAvailable); maximum CPU/GPU level: $($VrApi.maximumCpuLevel)/$($VrApi.maximumGpuLevel)")
  } else {
    [void]$md.AppendLine('- VrApi app-row evidence unavailable.')
  }
  [void]$md.AppendLine()
  [void]$md.AppendLine('## Stability, faults, and thermal')
  [void]$md.AppendLine()
  [void]$md.AppendLine("- Mountain probe: $(Get-ObjectProperty $Mountain 'classification' 'NOT_CAPTURED'); immutable transform/mesh/renderer set=$(Get-ObjectProperty $Mountain 'immutableTransforms' $false)/$(Get-ObjectProperty $Mountain 'immutableMeshes' $false)/$(Get-ObjectProperty $Mountain 'immutableRendererSet' $false)")
  [void]$md.AppendLine("- Crash / ANR / OOM / Unity exceptions: $($Faults.crash) / $($Faults.anr) / $($Faults.oom) / $($Faults.unityExceptionCount)")
  [void]$md.AppendLine("- Thermal warning: $ThermalWarning; $ThermalStatus")
  [void]$md.AppendLine("- Screen recording: started=$($ScreenRecording.started), pulled=$($ScreenRecording.pulled), bytes=$($ScreenRecording.bytes), error=$($ScreenRecording.error)")
  [void]$md.AppendLine()
  [void]$md.AppendLine('## Baseline comparison')
  [void]$md.AppendLine()
  if ($null -ne $Baseline -and [bool](Get-ObjectProperty $Baseline 'available' $false)) {
    [void]$md.AppendLine("- Baseline average / p95 / over-budget: $(Format-Number $Baseline.averageFrameMs '0.000') ms / $(Format-Number $Baseline.p95FrameMs '0.000') ms / $(Format-Number ([double]$Baseline.framesOverBudgetRatio * 100) '0.0')%")
    [void]$md.AppendLine("- Baseline draw calls / visible triangles: $($Baseline.drawCalls) / $($Baseline.visibleTriangles)")
    if ($null -ne $baselineComparison) {
      [void]$md.AppendLine("- Final deltas (average / p95 / over-budget / draws / triangles): $(Format-Number $baselineComparison.averageFrameMsDelta '+0.000;-0.000;0.000') ms / $(Format-Number $baselineComparison.p95FrameMsDelta '+0.000;-0.000;0.000') ms / $(Format-Number ([double]$baselineComparison.framesOverBudgetRatioDelta * 100) '+0.0;-0.0;0.0') pp / $($baselineComparison.drawCallsDelta) / $($baselineComparison.visibleTrianglesDelta)")
    }
  } else {
    [void]$md.AppendLine('- Baseline evidence unavailable.')
  }
  [void]$md.AppendLine()
  [void]$md.AppendLine('## Capture limitations')
  [void]$md.AppendLine()
  [void]$md.AppendLine('- `adb screenrecord` can add encoder/compositor overhead; use `-SkipScreenRecording` for a control run if timing is marginal.')
  [void]$md.AppendLine('- Machine invariants and recordings do not replace the required human stereo-headset witness.')
  [void]$md.AppendLine('- Static/off-head mode never promotes seat alignment or temporal visual acceptance to pass.')
  $markdownPath = Join-Path $ArtifactDir 'quest_temporal_visual_gate_summary.md'
  Set-Content -LiteralPath $markdownPath -Value $md.ToString() -Encoding UTF8
  Write-Host "Quest temporal summary: $markdownPath"
  return [pscustomobject]@{ jsonPath = $jsonPath; markdownPath = $markdownPath; classification = $classification }
}

$parseOnly = -not [string]::IsNullOrWhiteSpace($ParseOnlyLogcat)
$OutputDir = New-ArtifactDirectory -RequestedPath $OutputDir -Prefix $(if ($parseOnly) { 'quest_temporal_parser' } else { 'quest_temporal_visual_gate' })

if ($parseOnly) {
  Write-Host "Parser-only validation: $ParseOnlyLogcat"
  $evidenceRoot = if ([string]::IsNullOrWhiteSpace($ParseOnlyEvidenceDir)) { Split-Path -Parent $ParseOnlyLogcat } else { $ParseOnlyEvidenceDir }
  $vrApiResult = Get-VrApiEvidence -LogcatPath $ParseOnlyLogcat -CsvPath (Join-Path $OutputDir 'vrapi_app_samples.csv')
  $runtime = Get-RuntimeEvidenceSummary -EvidenceRoot $evidenceRoot
  $mountain = Get-MountainSummary -EvidenceRoot $evidenceRoot
  $baseline = Get-RuntimeEvidenceSummary -EvidenceRoot $BaselineEvidenceDir
  $logText = Get-Content -LiteralPath $ParseOnlyLogcat -Raw
  $faults = Get-FaultSummary -LogText $logText -AppPid $vrApiResult.summary.appPid -PackageName $PackageId
  $thermalWarning = $faults.thermalWarningInLog -or ([int](Get-ObjectProperty $vrApiResult.summary 'maximumPowerLevelState' 0) -gt 0)
  $screen = [pscustomobject]@{ started = $false; pulled = $false; bytes = 0; error = 'parser-only mode' }
  Write-GateSummary -ArtifactDir $OutputDir -LogcatPath $ParseOnlyLogcat -AppPid $vrApiResult.summary.appPid `
    -Runtime $runtime -VrApi $vrApiResult.summary -Mountain $mountain -Baseline $baseline -Faults $faults `
    -ThermalWarning $thermalWarning -ThermalStatus 'Derived from recorded VrApi/logcat only.' -ScreenRecording $screen `
    -HumanStatus 'NOT_RUN_PARSER_ONLY' -StaticOnly $true -PulledEvidence @() | Out-Null
  exit 0
}

$logcatProcess = $null
$screenRecordProcess = $null
$screenRecordDevicePath = "/sdcard/qfl_temporal_$((Get-Date).ToUniversalTime().ToString('yyyyMMdd_HHmmss')).mp4"
$screenRecordLocalPath = Join-Path $OutputDir 'quest_temporal_screenrecord.mp4'
$screenRecordErrorPath = Join-Path $OutputDir 'screenrecord_stderr.txt'
$logcatPath = Join-Path $OutputDir 'logcat_full.txt'
$logcatErrorPath = Join-Path $OutputDir 'logcat_stderr.txt'
$pulledEvidence = @()
$appPid = ''
$humanStatus = if ($StaticEvidenceOnly) {
  'NOT_RUN_BLOCKED_HEADSET_OFF_OR_UNAVAILABLE'
} elseif ($NoHumanWitness) {
  'NOT_RUN_NO_USER_AVAILABLE'
} else {
  'PENDING_HUMAN_HEADSET_WITNESS'
}
$screen = [pscustomobject]@{ started = $false; pulled = $false; bytes = 0; error = '' }
$originalStayAwakeSetting = ''
$stayAwakeSettingChanged = $false
$questPowerAutomationChanged = $false

try {
  $devices = Resolve-QuestDevice
  Set-Content -LiteralPath (Join-Path $OutputDir 'adb_devices.txt') -Value $devices -Encoding UTF8
  if ([string]::IsNullOrWhiteSpace($ApkPath)) {
    $apkName = 'QuestFlight-production.apk'
    $ApkPath = Join-Path $ProjectPath "Builds\Android\$apkName"
  }
  if (-not $SkipInstall) {
    if (-not (Test-Path -LiteralPath $ApkPath)) { throw "APK not found: $ApkPath. Run scripts\build_production_quest.ps1 first." }
    $install = Invoke-Adb -Arguments @('install', '-r', $ApkPath)
    Set-Content -LiteralPath (Join-Path $OutputDir 'adb_install.txt') -Value $install -Encoding UTF8
  }

  $originalStayAwakeSetting = (Invoke-Adb -Arguments @('shell', 'settings', 'get', 'global', 'stay_on_while_plugged_in') -AllowFailure).Trim()
  Set-Content -LiteralPath (Join-Path $OutputDir 'stay_awake_setting_before.txt') -Value $originalStayAwakeSetting -Encoding UTF8
  Invoke-Adb -Arguments @('shell', 'svc', 'power', 'stayon', 'usb') -AllowFailure | Out-Null
  $stayAwakeSettingChanged = $true
  if ($NoHumanWitness) {
    Invoke-Adb -Arguments @('shell', 'am', 'broadcast', '-a', 'com.oculus.vrpowermanager.automation_disable') -AllowFailure | Out-Null
    Invoke-Adb -Arguments @('shell', 'am', 'broadcast', '-a', 'com.oculus.vrpowermanager.prox_close') -AllowFailure | Out-Null
    $questPowerAutomationChanged = $true
  }
  Invoke-Adb -Arguments @('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP') -AllowFailure | Out-Null
  Start-Sleep -Seconds 2
  $powerAfterWake = Invoke-Adb -Arguments @('shell', 'dumpsys', 'power') -AllowFailure
  Set-Content -LiteralPath (Join-Path $OutputDir 'power_after_unattended_wake.txt') -Value $powerAfterWake -Encoding UTF8
  if ($NoHumanWitness -and $powerAfterWake -notmatch 'mWakefulness=Awake') {
    Invoke-Adb -Arguments @('shell', 'am', 'broadcast', '-a', 'com.oculus.vrpowermanager.prox_close') -AllowFailure | Out-Null
    Invoke-Adb -Arguments @('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP') -AllowFailure | Out-Null
    Start-Sleep -Seconds 2
    $powerAfterWake = Invoke-Adb -Arguments @('shell', 'dumpsys', 'power') -AllowFailure
    Set-Content -LiteralPath (Join-Path $OutputDir 'power_after_unattended_wake_retry.txt') -Value $powerAfterWake -Encoding UTF8
  }
  if ($NoHumanWitness -and $powerAfterWake -notmatch 'mWakefulness=Awake') {
    throw 'Quest remained asleep after unattended wake/proximity automation; no app measurement was started.'
  }
  Invoke-Adb -Arguments @('shell', 'dumpsys', 'thermalservice') -AllowFailure |
    Set-Content -LiteralPath (Join-Path $OutputDir 'thermal_before.txt') -Encoding UTF8
  Invoke-Adb -Arguments @('shell', 'dumpsys', 'battery') -AllowFailure |
    Set-Content -LiteralPath (Join-Path $OutputDir 'battery_before.txt') -Encoding UTF8
  Invoke-Adb -Arguments @('shell', 'getprop') -AllowFailure |
    Set-Content -LiteralPath (Join-Path $OutputDir 'device_getprop.txt') -Encoding UTF8
  Invoke-Adb -Arguments @('shell', 'dumpsys', 'package', $PackageId) -AllowFailure |
    Set-Content -LiteralPath (Join-Path $OutputDir 'package.txt') -Encoding UTF8

  $launchOptionsPath = Join-Path $OutputDir 'launch_options.json'
  [ordered]@{ options = @(
    [ordered]@{ key = 'qfl_scenery_mode'; value = $LaunchSceneryMode }
  ) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $launchOptionsPath -Encoding UTF8
  # Do not create the application evidence directory through adb. A directory
  # created by the shell user is readable but not writable by the release app
  # on scoped storage, which prevents the recorder from writing its report.
  # The option is supplied below as an intent extra; retain the local JSON as
  # evidence of the exact value. Clean up only the obsolete shell-owned file
  # from older gate versions, then remove its parent only if it is empty so the
  # application can create and own the directory itself.
  $remoteEvidenceRoot = "/sdcard/Android/data/$PackageId/files/QuestFlight"
  Invoke-Adb -Arguments @('shell', 'rm', '-f', "$remoteEvidenceRoot/launch_options.json") -AllowFailure | Out-Null
  Invoke-Adb -Arguments @('shell', 'rmdir', $remoteEvidenceRoot) -AllowFailure | Out-Null
  Set-Content -LiteralPath (Join-Path $OutputDir 'launch_options_transport.txt') `
    -Value 'qfl_scenery_mode is supplied through the Activity intent extra; no shell-owned app evidence directory was created.' -Encoding UTF8

  if (-not $StaticEvidenceOnly -and -not $SuppressOperatorPrompt -and -not $NoHumanWitness) { Write-Host $OperatorRequest }
  Invoke-Adb -Arguments @('logcat', '-c') | Out-Null
  $logcatProcess = Start-AdbBackgroundProcess -Arguments @('logcat', '-v', 'threadtime') -StdoutPath $logcatPath -StderrPath $logcatErrorPath
  $component = "$PackageId/$Activity"
  $launch = Invoke-Adb -Arguments @(
    'shell', 'am', 'start', '-S', '-n', $component,
    '--es', 'qfl_scenery_mode', $LaunchSceneryMode
  )
  Set-Content -LiteralPath (Join-Path $OutputDir 'app_launch.txt') -Value $launch -Encoding UTF8

  Start-Sleep -Seconds 2
  for ($pidAttempt = 0; $pidAttempt -lt 20; $pidAttempt++) {
    $appPid = (Invoke-Adb -Arguments @('shell', 'pidof', $PackageId) -AllowFailure).Trim()
    if (-not [string]::IsNullOrWhiteSpace($appPid)) { break }
    Start-Sleep -Seconds 1
  }
  Set-Content -LiteralPath (Join-Path $OutputDir 'app_pid.txt') -Value $appPid -Encoding UTF8
  if ([string]::IsNullOrWhiteSpace($appPid)) {
    throw 'Production app did not acquire a PID after launch; see power/app_launch/logcat artifacts.'
  }

  if ($StaticEvidenceOnly) {
    [void](Wait-ForLogPattern -Path $logcatPath -Pattern '\[QuestFlight\]\[ProductionEnvironment\] Authored prefab validated:' -TimeoutSeconds ([Math]::Min(45, $ReadinessTimeoutSeconds)) -Stage 'Waiting for static product environment evidence')
    [void](Wait-ForLogPattern -Path $logcatPath -Pattern '\[QuestFlight\]\[AircraftLighting\]' -TimeoutSeconds 30 -Stage 'Waiting for static product aircraft evidence')
    Start-Sleep -Seconds 5
  } else {
    $ready = Wait-ForLogPattern -Path $logcatPath -Pattern '\[QuestFlight\]\[TemporalVisualGate\] Readiness PASS' -TimeoutSeconds $ReadinessTimeoutSeconds -Stage 'Waiting for environment/cockpit/seat/evidence readiness'
    if (-not $ready) { Write-Warning 'Readiness PASS marker was not observed; the runtime report will force the gate to fail.' }
    $measurementStarted = Wait-ForLogPattern -Path $logcatPath -Pattern '\[QuestFlight\]\[TemporalVisualGate\] Measurement started' -TimeoutSeconds ($ReadinessTimeoutSeconds + 30) -Stage 'Waiting through the exact 90-second warmup'
    if (-not $measurementStarted) { Write-Warning 'Measurement-start marker was not observed.' }

    if ($measurementStarted -and -not $SkipScreenRecording) {
      try {
        $screenRecordProcess = Start-AdbBackgroundProcess -Arguments @('shell', 'screenrecord', '--time-limit', '60', '--bit-rate', '6000000', $screenRecordDevicePath) `
          -StdoutPath (Join-Path $OutputDir 'screenrecord_stdout.txt') -StderrPath $screenRecordErrorPath
        $screen.started = $true
      } catch {
        $screen.error = $_.Exception.Message
      }
    } elseif ($SkipScreenRecording) {
      $screen.error = 'disabled by -SkipScreenRecording'
    }

    $measurementComplete = Wait-ForLogPattern -Path $logcatPath -Pattern '\[QuestFlight\]\[TemporalVisualGate\] Evidence written:' -TimeoutSeconds $MeasurementTimeoutSeconds -Stage 'Recording exact 60-second measurement'
    if (-not $measurementComplete) { Write-Warning 'Temporal gate evidence marker was not observed.' }
  }

  Capture-AdbScreenshot -Path (Join-Path $OutputDir 'adb_screenshot_final.png') -ErrorPath (Join-Path $OutputDir 'adb_screenshot_final_error.txt') | Out-Null
  Invoke-Adb -Arguments @('shell', 'dumpsys', 'thermalservice') -AllowFailure |
    Set-Content -LiteralPath (Join-Path $OutputDir 'thermal_after.txt') -Encoding UTF8
  Invoke-Adb -Arguments @('shell', 'dumpsys', 'battery') -AllowFailure |
    Set-Content -LiteralPath (Join-Path $OutputDir 'battery_after.txt') -Encoding UTF8
  Invoke-Adb -Arguments @('shell', 'dumpsys', 'activity', 'activities') -AllowFailure |
    Set-Content -LiteralPath (Join-Path $OutputDir 'activity.txt') -Encoding UTF8
  Invoke-Adb -Arguments @('shell', 'dumpsys', 'power') -AllowFailure |
    Set-Content -LiteralPath (Join-Path $OutputDir 'power.txt') -Encoding UTF8
} finally {
  if ($null -ne $screenRecordProcess) {
    if (-not $screenRecordProcess.HasExited) {
      [void]$screenRecordProcess.WaitForExit(15000)
    }
    if (-not $screenRecordProcess.HasExited) { Stop-Process -Id $screenRecordProcess.Id -Force -ErrorAction SilentlyContinue }
  }
  if ($null -ne $logcatProcess -and -not $logcatProcess.HasExited) {
    Stop-Process -Id $logcatProcess.Id -Force -ErrorAction SilentlyContinue
  }
  if ($questPowerAutomationChanged) {
    Invoke-Adb -Arguments @('shell', 'am', 'broadcast', '-a', 'com.oculus.vrpowermanager.prox_far') -AllowFailure | Out-Null
    Invoke-Adb -Arguments @('shell', 'am', 'broadcast', '-a', 'com.oculus.vrpowermanager.automation_enable') -AllowFailure | Out-Null
    Set-Content -LiteralPath (Join-Path $OutputDir 'quest_power_automation_restore.txt') `
      -Value 'prox_far and automation_enable broadcasts sent' -Encoding UTF8
  }
  if ($stayAwakeSettingChanged) {
    if ([string]::IsNullOrWhiteSpace($originalStayAwakeSetting) -or $originalStayAwakeSetting -eq 'null') {
      Invoke-Adb -Arguments @('shell', 'settings', 'delete', 'global', 'stay_on_while_plugged_in') -AllowFailure | Out-Null
    } else {
      Invoke-Adb -Arguments @('shell', 'settings', 'put', 'global', 'stay_on_while_plugged_in', $originalStayAwakeSetting) -AllowFailure | Out-Null
    }
    $restored = (Invoke-Adb -Arguments @('shell', 'settings', 'get', 'global', 'stay_on_while_plugged_in') -AllowFailure).Trim()
    Set-Content -LiteralPath (Join-Path $OutputDir 'stay_awake_setting_after_restore.txt') -Value $restored -Encoding UTF8
  }
  Start-Sleep -Milliseconds 500
}

if (Test-Path -LiteralPath $logcatPath) {
  $logText = Get-Content -LiteralPath $logcatPath -Raw
} else {
  $logText = ''
}

if ($screen.started) {
  try {
    $pullVideo = Invoke-Adb -Arguments @('pull', $screenRecordDevicePath, $screenRecordLocalPath) -AllowFailure
    Set-Content -LiteralPath (Join-Path $OutputDir 'adb_pull_screenrecord.txt') -Value $pullVideo -Encoding UTF8
    if (Test-Path -LiteralPath $screenRecordLocalPath) {
      $screen.bytes = (Get-Item -LiteralPath $screenRecordLocalPath).Length
      $screen.pulled = $screen.bytes -gt 0
    }
    if (-not $screen.pulled -and (Test-Path -LiteralPath $screenRecordErrorPath)) {
      $screen.error = Get-Content -LiteralPath $screenRecordErrorPath -Raw
    }
    Invoke-Adb -Arguments @('shell', 'rm', '-f', $screenRecordDevicePath) -AllowFailure | Out-Null
  } catch {
    $screen.error = $_.Exception.Message
  }
}

$pullRoot = Join-Path $OutputDir 'pulled_device_evidence'
New-Item -ItemType Directory -Force -Path $pullRoot | Out-Null
if (-not [string]::IsNullOrWhiteSpace($logText)) {
  $pulledEvidence = Pull-CurrentEvidencePaths -LogText $logText -DestinationRoot $pullRoot
}
ConvertTo-Json -InputObject @($pulledEvidence) -Depth 6 |
  Set-Content -LiteralPath (Join-Path $OutputDir 'pulled_evidence_manifest.json') -Encoding UTF8

$vrApiResult = Get-VrApiEvidence -LogcatPath $logcatPath -ExplicitAppPid $appPid -CsvPath (Join-Path $OutputDir 'vrapi_app_samples.csv')
$runtime = Get-RuntimeEvidenceSummary -EvidenceRoot $pullRoot
$mountain = Get-MountainSummary -EvidenceRoot $pullRoot
$baseline = Get-RuntimeEvidenceSummary -EvidenceRoot $BaselineEvidenceDir
$faults = Get-FaultSummary -LogText $logText -AppPid $appPid -PackageName $PackageId
$thermalAfterPath = Join-Path $OutputDir 'thermal_after.txt'
$thermalText = if (Test-Path -LiteralPath $thermalAfterPath) { Get-Content -LiteralPath $thermalAfterPath -Raw } else { '' }
$thermalStatusCode = Get-RegexInt $thermalText '(?:mStatus|Thermal Status)\s*[:=]\s*(\d+)' 0
$thermalWarning = $faults.thermalWarningInLog -or $thermalStatusCode -gt 0 -or ([int](Get-ObjectProperty $vrApiResult.summary 'maximumPowerLevelState' 0) -gt 0)
$thermalStatus = "thermalservice status=$thermalStatusCode; VrApi max PLS=$([int](Get-ObjectProperty $vrApiResult.summary 'maximumPowerLevelState' 0)); VrApi max temperature=$(Format-Number (Get-ObjectProperty $vrApiResult.summary 'maximumTemperatureC' 0) '0.0') C"

Write-GateSummary -ArtifactDir $OutputDir -LogcatPath $logcatPath -AppPid $appPid `
  -Runtime $runtime -VrApi $vrApiResult.summary -Mountain $mountain -Baseline $baseline -Faults $faults `
  -ThermalWarning $thermalWarning -ThermalStatus $thermalStatus -ScreenRecording $screen `
  -HumanStatus $humanStatus -StaticOnly ([bool]$StaticEvidenceOnly) -PulledEvidence $pulledEvidence | Out-Null

if ($StaticEvidenceOnly) {
  Invoke-Adb -Arguments @('shell', 'am', 'force-stop', $PackageId) -AllowFailure | Out-Null
}

Write-Host "Quest temporal visual gate artifacts: $OutputDir"
