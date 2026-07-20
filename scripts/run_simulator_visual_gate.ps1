[CmdletBinding()]
param(
    [string]$ArtifactRoot,
    [string]$ReplayRoot,
    [switch]$AuditOnly,
    [switch]$RebuildPlayer,
    [switch]$SkipLiveSmoke,
    [switch]$NoFailOnBlocked,
    [int]$CaptureWidth = 1440,
    [int]$CaptureHeight = 1440
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $stamp = [DateTime]::UtcNow.ToString("yyyyMMdd_HHmmss")
    $ArtifactRoot = Join-Path (Split-Path -Parent $repoRoot) "T2-QuestFlight-product-artifacts\standalone_recovery_$stamp"
}
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$simulatorRoot = Join-Path $ArtifactRoot "meta_xr_simulator"
$qaRunDirectory = Join-Path $simulatorRoot "qa_run"
$qaRunReportPath = Join-Path $qaRunDirectory "simulator_qa_run_report.json"
$gateDirectory = Join-Path $simulatorRoot "visual_gate"
$reportPath = Join-Path $gateDirectory "simulator_visual_report.json"
$summaryPath = Join-Path $gateDirectory "simulator_visual_summary.md"
$contactSheetPath = Join-Path $gateDirectory "simulator_stereo_contact_sheet.png"
New-Item -ItemType Directory -Path $gateDirectory -Force | Out-Null

$requiredSessions = @(
    "cockpit_neutral",
    "head_pan",
    "rear_view",
    "viewpoint_calibration",
    "control_surface_sweep",
    "taxi",
    "takeoff_roll",
    "rotation_climb",
    "shallow_turns",
    "final_approach"
)

function New-StereoContactSheet {
    param([object[]]$SessionReports, [string]$Destination)
    $sources = @($SessionReports | Where-Object {
        $_.stereoImage -and (Test-Path -LiteralPath $_.stereoImage -PathType Leaf)
    })
    if ($sources.Count -eq 0) { return $null }
    try {
        Add-Type -AssemblyName System.Drawing
        $columnWidth = 900
        $imageHeight = 450
        $labelHeight = 44
        $columns = 2
        $rows = [Math]::Ceiling($sources.Count / $columns)
        $bitmap = New-Object System.Drawing.Bitmap ($columnWidth * $columns),([int]($rows * ($imageHeight + $labelHeight)))
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $font = New-Object System.Drawing.Font("Segoe UI", 15, [System.Drawing.FontStyle]::Bold)
        $labelBrush = [System.Drawing.Brushes]::White
        try {
            $graphics.Clear([System.Drawing.Color]::FromArgb(18, 20, 24))
            for ($index = 0; $index -lt $sources.Count; $index++) {
                $row = [Math]::Floor($index / $columns)
                $column = $index % $columns
                $x = $column * $columnWidth
                $y = $row * ($imageHeight + $labelHeight)
                $image = [System.Drawing.Image]::FromFile([string]$sources[$index].stereoImage)
                try {
                    $graphics.DrawImage($image, $x, $y, $columnWidth, $imageHeight)
                    $graphics.DrawString([string]$sources[$index].sessionId, $font, $labelBrush, $x + 10, $y + $imageHeight + 8)
                }
                finally { $image.Dispose() }
            }
            $bitmap.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $font.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
        return $Destination
    }
    catch {
        Write-Warning "Could not create stereo contact sheet: $($_.Exception.Message)"
        return $null
    }
}

$runnerArguments = @{
    ArtifactRoot = $ArtifactRoot
    AuditOnly = $AuditOnly
    RebuildPlayer = $RebuildPlayer
    CaptureWidth = $CaptureWidth
    CaptureHeight = $CaptureHeight
}
if (-not [string]::IsNullOrWhiteSpace($ReplayRoot)) { $runnerArguments.ReplayRoot = $ReplayRoot }
if (-not $AuditOnly -and -not $SkipLiveSmoke) { $runnerArguments.LiveSmokeWhenRecordingsMissing = $true }
& (Join-Path $PSScriptRoot "run_meta_xr_simulator_qa.ps1") @runnerArguments
$runnerExitCode = $LASTEXITCODE

$orchestrationReport = if (Test-Path -LiteralPath $qaRunReportPath -PathType Leaf) {
    Get-Content -LiteralPath $qaRunReportPath -Raw | ConvertFrom-Json
} else { $null }
$sessionReports = @()
$sessionEvidence = @()
$missingRecordings = @($requiredSessions)
$errors = @()
$warnings = @()

if ($null -eq $orchestrationReport) {
    $errors += "Simulator orchestration did not produce $qaRunReportPath"
}
else {
    $missingRecordings = @($orchestrationReport.missingRecordings)
    if ($orchestrationReport.orchestrationError) { $errors += [string]$orchestrationReport.orchestrationError }
    foreach ($run in @($orchestrationReport.sessionRuns)) {
        $sessionReport = $null
        if ($run.sessionReportPath -and (Test-Path -LiteralPath $run.sessionReportPath -PathType Leaf)) {
            $sessionReport = Get-Content -LiteralPath $run.sessionReportPath -Raw | ConvertFrom-Json
            $sessionReports += $sessionReport
        }
        $sessionEvidence += [ordered]@{
            id = [string]$run.id
            mode = [string]$run.mode
            replayPath = [string]$run.vrsPath
            reportPath = [string]$run.sessionReportPath
            leftEye = if ($sessionReport) { [string]$sessionReport.leftEyeImage } else { $null }
            rightEye = if ($sessionReport) { [string]$sessionReport.rightEyeImage } else { $null }
            stereoPair = if ($sessionReport) { [string]$sessionReport.stereoImage } else { $null }
            cameraPose = if ($sessionReport) { $sessionReport.trackedCameraPose } else { $null }
            xrOriginPose = if ($sessionReport) { $sessionReport.xrOriginPose } else { $null }
            aircraftPose = if ($sessionReport) { $sessionReport.aircraftPose } else { $null }
            frameNumber = if ($sessionReport) { $sessionReport.frameNumber } else { $null }
            timestampSeconds = if ($sessionReport) { $sessionReport.unscaledTimestampSeconds } else { $null }
            environmentProfile = if ($sessionReport) { [string]$sessionReport.activeEnvironmentProfile } else { $null }
            renderProfile = if ($sessionReport) { [string]$sessionReport.activeRenderProfile } else { $null }
            productConnected = $null -ne $sessionReport -and [bool]$sessionReport.xrDeviceActive -and [bool]$sessionReport.stereoEnabled
            runtimeName = if ($sessionReport) { [string]$sessionReport.openXrRuntimeName } else { $null }
            hmdInputDevice = if ($sessionReport) { $sessionReport.hmdInputDevice } else { $null }
            leftControllerInputDevice = if ($sessionReport) { $sessionReport.leftControllerInputDevice } else { $null }
            rightControllerInputDevice = if ($sessionReport) { $sessionReport.rightControllerInputDevice } else { $null }
            warnings = if ($sessionReport) { @($sessionReport.warnings) } else { @() }
            errors = if ($sessionReport) { @($sessionReport.errors) } else { @() }
            passed = $null -ne $sessionReport -and [bool]$sessionReport.passed
            classification = if ($sessionReport) { [string]$sessionReport.classification } else { "NO_SESSION_REPORT" }
        }
    }
}

$recordedReplayReports = @($sessionEvidence | Where-Object { $_.mode -eq "replay" })
$recordedIds = @($recordedReplayReports | Where-Object { $_.passed } | ForEach-Object { $_.id })
$missingSessionReports = @($requiredSessions | Where-Object { $_ -notin $recordedIds })
$failedSessionReports = @($recordedReplayReports | Where-Object { -not $_.passed })
$liveStereoConnectionPassed = @($sessionReports | Where-Object {
    $_.xrDeviceActive -and $_.stereoEnabled -and $_.checks.metaSimulatorRuntimeConnected -and
    $_.checks.quest3RefreshRate72Hz -and $_.checks.simulatedHmdAndTouchControllersAvailable -and
    $_.checks.bothEyesNonBlank -and $_.checks.stereoSceneConsistency
}).Count -gt 0
$contactSheet = New-StereoContactSheet -SessionReports $sessionReports -Destination $contactSheetPath

$classification = "PASS_MACHINE_GATE_HUMAN_STEREO_REVIEW_REQUIRED"
$passed = $true
if ($null -eq $orchestrationReport -or $orchestrationReport.classification -eq "FAIL_SIMULATOR_ORCHESTRATION") {
    $classification = "FAIL_SIMULATOR_ORCHESTRATION"
    $passed = $false
}
elseif (-not [bool]$orchestrationReport.activeRuntimePreserved -or
        -not [bool]$orchestrationReport.persistentConfigurationRestored -or
        [bool]$orchestrationReport.shippingAndroidConfigurationChanged) {
    # State safety takes precedence over missing replay coverage. A blocked replay
    # catalog must never conceal a machine-runtime or shipping-config mutation.
    $classification = "FAIL_SIMULATOR_STATE_RESTORATION"
    $passed = $false
}
elseif ($missingRecordings.Count -gt 0 -or $missingSessionReports.Count -gt 0) {
    $classification = "BLOCKED_MISSING_RECORDED_REPLAYS"
    $passed = $false
}
elseif ($failedSessionReports.Count -gt 0) {
    $classification = "FAIL_SIMULATOR_VISUAL_MACHINE_CHECKS"
    $passed = $false
}
elseif (-not $contactSheet) {
    $classification = "FAIL_CONTACT_SHEET_NOT_GENERATED"
    $passed = $false
}
elseif (-not $liveStereoConnectionPassed) {
    $classification = "FAIL_SIMULATOR_STEREO_OR_72HZ_CONNECTION"
    $passed = $false
}

$report = [ordered]@{
    schemaVersion = 3
    generatedUtc = [DateTime]::UtcNow.ToString("O")
    classification = $classification
    passed = $passed
    runnerExitCode = $runnerExitCode
    simulatorSetupVersion = if ($orchestrationReport) { [string]$orchestrationReport.simulatorVersion } else { $null }
    deviceProfile = "Meta Quest 3"
    requestedRefreshRateHz = 72
    requiredSessionIds = $requiredSessions
    recordedReplayPassIds = $recordedIds
    missingRecordingFiles = $missingRecordings
    missingOrFailedReplayReports = $missingSessionReports
    failedReplayReports = $failedSessionReports
    liveStereoConnectionPassed = $liveStereoConnectionPassed
    sessions = $sessionEvidence
    contactSheetPath = $contactSheet
    individualEyeCaptureRoot = (Join-Path $qaRunDirectory "sessions")
    replayEvidenceRoot = Join-Path $qaRunDirectory "replays"
    logsRoot = Join-Path $qaRunDirectory "logs"
    activeRuntimePreserved = if ($orchestrationReport) { [bool]$orchestrationReport.activeRuntimePreserved } else { $false }
    persistentConfigurationRestored = if ($orchestrationReport) { [bool]$orchestrationReport.persistentConfigurationRestored } else { $false }
    shippingAndroidConfigurationChanged = if ($orchestrationReport) { [bool]$orchestrationReport.shippingAndroidConfigurationChanged } else { $false }
    simulatorStateRestorationPassed = $null -ne $orchestrationReport -and
        [bool]$orchestrationReport.activeRuntimePreserved -and
        [bool]$orchestrationReport.persistentConfigurationRestored -and
        -not [bool]$orchestrationReport.shippingAndroidConfigurationChanged
    fullResolutionLeadInspection = "REQUIRED_NOT_AUTOMATICALLY_CLAIMED"
    automatedChecks = @(
        "either eye nonblank",
        "plausible distinct stereo eye poses and cross-eye scene consistency",
        "immutable mountain mesh/transform/renderer identity during sampled frames",
        "aircraft visual root and CG pivot invariants",
        "viewpoint UI protected instrument-panel region heuristic",
        "runway/collider/terrain authored contract",
        "missing references/components and error logs",
        "debug/training overlay presence"
    )
    limitations = @(
        "The viewpoint overlap check is a conservative screen-region heuristic; the full-resolution stereo pair must be inspected.",
        "Meta XR Simulator evidence covers API-level stereo, tracking, input replay, and composition only.",
        "No simulator or Windows timing is Quest GPU, thermal, stale/tear, or frame-performance evidence.",
        "Physical Quest 3 acceptance remains required."
    )
    errors = $errors
    warnings = $warnings
}
$report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $reportPath -Encoding utf8

$sessionTable = if ($sessionEvidence.Count -gt 0) {
    ($sessionEvidence | ForEach-Object {
        "| $($_.id) | $($_.mode) | $($_.productConnected) | $($_.passed) | $($_.classification) |"
    }) -join [Environment]::NewLine
} else {
    "| _none_ | — | False | False | No session evidence |"
}
$missingText = if ($missingRecordings.Count -gt 0) { $missingRecordings -join ", " } else { "none" }
$summary = @"
# Meta XR Simulator visual gate

- Classification: **$classification**
- Meta Quest 3 profile / requested refresh: **Quest 3 / 72 Hz**
- Active OpenXR runtime preserved: **$(if ($orchestrationReport) { $orchestrationReport.activeRuntimePreserved } else { $false })**
- Meta persistent replay configuration restored: **$(if ($orchestrationReport) { $orchestrationReport.persistentConfigurationRestored } else { $false })**
- Shipping Android configuration changed: **$(if ($orchestrationReport) { $orchestrationReport.shippingAndroidConfigurationChanged } else { $false })**
- Live Meta OpenXR stereo/HMD/Touch connection: **$liveStereoConnectionPassed**
- Missing VRS recordings: **$missingText**
- Stereo contact sheet: ``$contactSheet``
- Full-resolution lead inspection: **required; not automatically claimed**

| Session | Mode | Connected | Full gate passed | Classification |
|---|---:|---:|---:|---|
$sessionTable

Meta XR Simulator proves OpenXR API-level stereo, tracking, input replay, and composition behavior. It does **not** prove standalone Quest 3 GPU performance, thermal behavior, stale/tear rate, or frame timing.

When recordings are missing, create each one once with ``scripts/run_meta_xr_simulator_qa.ps1 -RecordSession <id>`` and then rerun this gate. Do not synthesize or rename unrelated VRS files.
"@
Set-Content -LiteralPath $summaryPath -Value $summary -Encoding utf8

Write-Host "Simulator visual report: $reportPath"
Write-Host "Simulator visual summary: $summaryPath"
if ($passed) { exit 0 }
if ($NoFailOnBlocked -and $classification -like "BLOCKED_*") { exit 0 }
exit 2
