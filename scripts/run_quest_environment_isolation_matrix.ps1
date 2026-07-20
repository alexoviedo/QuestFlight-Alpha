[CmdletBinding()]
param(
  [string]$AdbExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe',
  [string]$ProjectPath = 'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight',
  [string]$ApkPath = '',
  [string]$ArtifactDir = '',
  [string]$DeviceSerial = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ApkPath)) {
  $ApkPath = Join-Path $ProjectPath 'Builds\Android\QuestFlight-production.apk'
}
if (-not (Test-Path -LiteralPath $ApkPath)) {
  throw "Quest APK not found: $ApkPath"
}
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
  $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
  $ArtifactDir = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\quest_environment_isolation_$stamp"
}
New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
$ArtifactDir = (Resolve-Path -LiteralPath $ArtifactDir).Path
$temporalGate = Join-Path $PSScriptRoot 'run_quest_temporal_visual_gate.ps1'
$profiles = @('production', 'sky_only', 'water_off', 'buildings_vegetation_off', 'reduced_texture_tier')
$apkSha = (Get-FileHash -LiteralPath $ApkPath -Algorithm SHA256).Hash.ToLowerInvariant()
$rows = @()

for ($index = 0; $index -lt $profiles.Count; $index++) {
  $profile = $profiles[$index]
  $runDir = Join-Path $ArtifactDir ("{0:D2}_{1}" -f ($index + 1), $profile)
  $arguments = @{
    AdbExe = $AdbExe
    ProjectPath = $ProjectPath
    ApkPath = $ApkPath
    OutputDir = $runDir
    IsolationProfile = $profile
    SuppressOperatorPrompt = $true
    NoHumanWitness = $true
    SkipScreenRecording = $true
  }
  if (-not [string]::IsNullOrWhiteSpace($DeviceSerial)) { $arguments.DeviceSerial = $DeviceSerial }
  if ($index -gt 0) { $arguments.SkipInstall = $true }

  Write-Host "Running Quest environment isolation profile '$profile' ($($index + 1)/$($profiles.Count))"
  & $temporalGate @arguments

  $summaryPath = Join-Path $runDir 'quest_temporal_visual_gate_summary.json'
  if (-not (Test-Path -LiteralPath $summaryPath)) {
    throw "Isolation profile '$profile' did not produce a temporal summary."
  }
  $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
  $runtime = $summary.runtime
  $vrApi = $summary.vrApiAppPidWindow
  $rows += [pscustomobject]@{
    profile = $profile
    appliedProfile = [string]$runtime.diagnosticIsolationProfile
    profileEchoPassed = [string]$runtime.diagnosticIsolationProfile -eq $profile
    classification = [string]$summary.classification
    summaryPath = $summaryPath
    appPid = [string]$summary.appPid
    disabledRenderers = [int]$runtime.diagnosticDisabledRendererCount
    textureMipmapLimit = [int]$runtime.diagnosticTextureMipmapLimit
    allocationMeasurementAvailable = [bool]$runtime.allocationMeasurementAvailable
    allocationMeasurementValidated = [bool]$runtime.allocationMeasurementValidated
    allocationMeasurementSource = [string]$runtime.allocationMeasurementSource
    maxGcAllocatedBytesPerFrame = [long]$runtime.maxGcAllocatedBytesPerFrame
    totalManagedAllocationCount = [long]$runtime.totalManagedAllocationCount
    maxManagedAllocationCountPerFrame = [long]$runtime.maxManagedAllocationCountPerFrame
    drawCalls = [long]$runtime.drawCalls
    visibleTriangles = [long]$runtime.visibleTriangles
    sceneMaterials = [int]$runtime.sceneMaterialCount
    averageCpuAndGpuMs = [double]$vrApi.averageCpuAndGpuMs
    p95CpuAndGpuMs = [double]$vrApi.p95CpuAndGpuMs
    samplesOverBudgetRatio = [double]$vrApi.cpuAndGpuSamplesOverBudgetRatio
    staleTotal = [int]$vrApi.staleTotal
    tearTotal = [int]$vrApi.tearTotal
    averageGpuUtilization = [double]$vrApi.averageGpuUtilization
    averageCpuUtilization = [double]$vrApi.averageCpuUtilization
    maximumTemperatureC = [double]$vrApi.maximumTemperatureC
    physicalTimingPassed = [bool]$summary.physicalQuestTimingGate.passed
    exactMeasurementWindow = [bool]$vrApi.exactMeasurementWindow
    vrApiSampleCount = [int]$vrApi.sampleCount
    vrApiCoverageSeconds = [double]$vrApi.observedCoverageSeconds
    faultFree = -not [bool]$summary.faultChecks.crash -and
      -not [bool]$summary.faultChecks.anr -and
      -not [bool]$summary.faultChecks.oom -and
      [int]$summary.faultChecks.unityExceptionCount -eq 0
    thermalPassed = -not [bool]$summary.thermal.warning
  }
}

$finalApkSha = (Get-FileHash -LiteralPath $ApkPath -Algorithm SHA256).Hash.ToLowerInvariant()
$evidenceComplete = @($rows | Where-Object {
  -not $_.profileEchoPassed -or
  -not $_.exactMeasurementWindow -or
  $_.vrApiSampleCount -lt 55 -or
  $_.vrApiCoverageSeconds -lt 54.0 -or
  -not $_.faultFree -or
  -not $_.thermalPassed
}).Count -eq 0
$apkImmutable = $apkSha -eq $finalApkSha
$production = @($rows | Where-Object profile -eq 'production')[0]
$comparisons = @($rows | ForEach-Object {
  [pscustomobject]@{
    profile = $_.profile
    averageCpuAndGpuMsDeltaVsProduction = $_.averageCpuAndGpuMs - $production.averageCpuAndGpuMs
    p95CpuAndGpuMsDeltaVsProduction = $_.p95CpuAndGpuMs - $production.p95CpuAndGpuMs
    samplesOverBudgetRatioDeltaVsProduction = $_.samplesOverBudgetRatio - $production.samplesOverBudgetRatio
    drawCallsDeltaVsProduction = $_.drawCalls - $production.drawCalls
    visibleTrianglesDeltaVsProduction = $_.visibleTriangles - $production.visibleTriangles
  }
})
$bestDiagnostic = if ($evidenceComplete -and $apkImmutable) {
  @($rows | Where-Object profile -ne 'production' | Sort-Object averageCpuAndGpuMs, p95CpuAndGpuMs | Select-Object -First 1)[0]
} else { $null }
$aggregate = [ordered]@{
  schemaVersion = 1
  generatedUtc = [DateTime]::UtcNow.ToString('O')
  shippingTarget = 'standalone Meta Quest 3 only'
  diagnosticOnly = $true
  apkPath = (Resolve-Path -LiteralPath $ApkPath).Path
  apkSha256 = $apkSha
  finalApkSha256 = $finalApkSha
  apkImmutable = $apkImmutable
  evidenceComplete = $evidenceComplete
  classification = $(if ($evidenceComplete -and $apkImmutable) { 'COMPLETE_DIAGNOSTIC_MATRIX' } else { 'INCONCLUSIVE_DIAGNOSTIC_MATRIX' })
  profiles = $rows
  comparisons = $comparisons
  lowestAverageDiagnosticProfile = $(if ($null -ne $bestDiagnostic) { $bestDiagnostic.profile } else { '' })
  interpretationBoundary = 'These one-run profiles isolate rendering features; only the unmodified production profile and the exact three-run hardware gate can establish product acceptance.'
}
$jsonPath = Join-Path $ArtifactDir 'quest_environment_isolation_matrix.json'
$aggregate | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$markdown = New-Object System.Text.StringBuilder
[void]$markdown.AppendLine('# Quest Environment Feature-Isolation Matrix')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- APK SHA256: ``$apkSha``")
[void]$markdown.AppendLine('- Diagnostic only: true')
[void]$markdown.AppendLine("- Classification: ``$($aggregate.classification)``")
[void]$markdown.AppendLine("- Evidence complete / APK immutable: $evidenceComplete / $apkImmutable")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('| Profile | Avg CPU+GPU ms | p95 ms | Over budget | Draws | Triangles | Disabled renderers | Mip limit | Allocation max (bytes/objects) | Timing pass |')
[void]$markdown.AppendLine('|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
foreach ($row in $rows) {
  [void]$markdown.AppendLine((
    '| {0} | {1:0.000} | {2:0.000} | {3:0.00}% | {4} | {5} | {6} | {7} | {8} | {9} |' -f
      $row.profile,
      $row.averageCpuAndGpuMs,
      $row.p95CpuAndGpuMs,
      ($row.samplesOverBudgetRatio * 100.0),
      $row.drawCalls,
      $row.visibleTriangles,
      $row.disabledRenderers,
      $row.textureMipmapLimit,
      $(if ($row.allocationMeasurementSource -match 'IL2CPP') { $row.maxManagedAllocationCountPerFrame } else { $row.maxGcAllocatedBytesPerFrame }),
      $row.physicalTimingPassed))
}
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- Lowest-average diagnostic profile: ``$(if ($null -ne $bestDiagnostic) { $bestDiagnostic.profile } else { 'not selected; matrix inconclusive' })``")
[void]$markdown.AppendLine("- Boundary: $($aggregate.interpretationBoundary)")
$markdownPath = Join-Path $ArtifactDir 'quest_environment_isolation_matrix.md'
$markdown.ToString() | Set-Content -LiteralPath $markdownPath -Encoding UTF8

Write-Host "Quest isolation matrix: $markdownPath"
