[CmdletBinding()]
param(
    [string]$ArtifactRoot,
    [string]$ReplayRoot,
    [string[]]$SessionId = @(),
    [switch]$AuditOnly,
    [switch]$RebuildPlayer,
    [switch]$AllowMissingRecordings,
    [switch]$LiveSmokeWhenRecordingsMissing,
    [string]$RecordSession,
    [string]$DevelopmentScratchRoot,
    [int]$SessionTimeoutSeconds = 240,
    [int]$CaptureWidth = 1440,
    [int]$CaptureHeight = 1440
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$unityProject = Join-Path $repoRoot "QuestFlight"
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $stamp = [DateTime]::UtcNow.ToString("yyyyMMdd_HHmmss")
    $ArtifactRoot = Join-Path (Split-Path -Parent $repoRoot) "T2-QuestFlight-product-artifacts\standalone_recovery_$stamp"
}
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$DevelopmentScratchRoot = if ([string]::IsNullOrWhiteSpace($DevelopmentScratchRoot)) {
    Join-Path ([IO.Path]::GetTempPath()) "QuestFlight\MetaXrSimulator"
} else {
    $DevelopmentScratchRoot
}
$DevelopmentScratchRoot = [IO.Path]::GetFullPath($DevelopmentScratchRoot)
$simulatorRoot = Join-Path $ArtifactRoot "meta_xr_simulator"
$outputDirectory = Join-Path $simulatorRoot "qa_run"
$sessionOutputRoot = Join-Path $outputDirectory "sessions"
$logRoot = Join-Path $outputDirectory "logs"
$replayEvidenceRoot = Join-Path $outputDirectory "replays"
$playerPath = Join-Path $simulatorRoot "player\QuestFlightMetaXrSimulatorQA.exe"
$buildReportPath = Join-Path $simulatorRoot "player\simulator_dev_build.json"
$runReportPath = Join-Path $outputDirectory "simulator_qa_run_report.json"
if ([string]::IsNullOrWhiteSpace($ReplayRoot)) {
    $ReplayRoot = Join-Path $simulatorRoot "recordings"
}
$ReplayRoot = [IO.Path]::GetFullPath($ReplayRoot)
New-Item -ItemType Directory -Path $outputDirectory,$sessionOutputRoot,$logRoot,$replayEvidenceRoot,$ReplayRoot -Force | Out-Null
New-Item -ItemType Directory -Path $DevelopmentScratchRoot -Force | Out-Null
$developmentCopyPath = $null
$developmentCopyRemoved = $false
$developmentCleanupError = $null

$sessionCatalog = @(
    [ordered]@{ id = "ambient_airport"; description = "Ordinary runway start: early taxi and airborne traffic"; delaySeconds = 22; startDemo = $false },
    [ordered]@{ id = "ambient_departure"; description = "Ordinary authored departure with ambient traffic"; delaySeconds = 42; startDemo = $true },
    [ordered]@{ id = "ambient_foothill"; description = "Supplemental native foothill start, not normal-route coverage"; delaySeconds = 4; startDemo = $false },
    [ordered]@{ id = "cockpit_neutral"; description = "Cockpit neutral forward view"; delaySeconds = 4; startDemo = $false },
    [ordered]@{ id = "head_pan"; description = "Slow head pan left/right/up/down"; delaySeconds = 8; startDemo = $false },
    [ordered]@{ id = "rear_view"; description = "180-degree rearward view"; delaySeconds = 6; startDemo = $false },
    [ordered]@{ id = "viewpoint_calibration"; description = "Viewpoint menu open/adjust/save/reset"; delaySeconds = 10; startDemo = $false },
    [ordered]@{ id = "control_surface_sweep"; description = "Control-surface sweep"; delaySeconds = 8; startDemo = $false },
    [ordered]@{ id = "taxi"; description = "Taxi"; delaySeconds = 7; startDemo = $true },
    [ordered]@{ id = "takeoff_roll"; description = "Takeoff roll"; delaySeconds = 20; startDemo = $true },
    [ordered]@{ id = "rotation_climb"; description = "Rotation and climb"; delaySeconds = 34; startDemo = $true },
    [ordered]@{ id = "shallow_turns"; description = "Shallow left/right turns"; delaySeconds = 55; startDemo = $false },
    [ordered]@{ id = "final_approach"; description = "Final approach"; delaySeconds = 120; startDemo = $false }
)
if ($SessionId.Count -gt 0) {
    $unknown = $SessionId | Where-Object { $_ -notin $sessionCatalog.id }
    if (@($unknown).Count -gt 0) {
        throw "Unknown simulator session id(s): $($unknown -join ', ')"
    }
    $sessionCatalog = @($sessionCatalog | Where-Object { $_.id -in $SessionId })
}
if (-not [string]::IsNullOrWhiteSpace($RecordSession)) {
    if ($RecordSession -notin $sessionCatalog.id -and $RecordSession -notin @(
        "cockpit_neutral", "head_pan", "rear_view", "viewpoint_calibration", "control_surface_sweep",
        "taxi", "takeoff_roll", "rotation_climb", "shallow_turns", "final_approach")) {
        throw "Unknown simulator recording id: $RecordSession"
    }
    $sessionCatalog = @($sessionCatalog | Where-Object { $_.id -eq $RecordSession })
}

function Get-RegistryOpenXrRuntime {
    param([Microsoft.Win32.RegistryView]$View)
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine,
        $View)
    try {
        $key = $baseKey.OpenSubKey("SOFTWARE\Khronos\OpenXR\1", $false)
        if ($null -eq $key) { return $null }
        try { return [string]$key.GetValue("ActiveRuntime", $null) }
        finally { $key.Dispose() }
    }
    finally { $baseKey.Dispose() }
}

function Get-OpenXrRegistrySnapshot {
    [ordered]@{
        runtime64 = Get-RegistryOpenXrRuntime -View ([Microsoft.Win32.RegistryView]::Registry64)
        runtime32 = Get-RegistryOpenXrRuntime -View ([Microsoft.Win32.RegistryView]::Registry32)
    }
}

function Get-Sha256File {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)
    $stream = [IO.File]::OpenRead($LiteralPath)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
        $stream.Dispose()
    }
}

function Set-RegistryOpenXrRuntime {
    param(
        [Microsoft.Win32.RegistryView]$View,
        [AllowNull()][string]$Value
    )
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine,
        $View)
    try {
        $key = $baseKey.OpenSubKey("SOFTWARE\Khronos\OpenXR\1", $true)
        if ($null -eq $key) { throw "The machine OpenXR registry key is unavailable or not writable." }
        try {
            if ($null -eq $Value) { $key.DeleteValue("ActiveRuntime", $false) }
            else { $key.SetValue("ActiveRuntime", $Value, [Microsoft.Win32.RegistryValueKind]::String) }
        }
        finally { $key.Dispose() }
    }
    finally { $baseKey.Dispose() }
}

function Restore-OpenXrRegistrySnapshot {
    param([System.Collections.IDictionary]$Snapshot)
    $beforeRestore = Get-OpenXrRegistrySnapshot
    $errors = @()
    $attempted = $false
    foreach ($entry in @(
        [ordered]@{ name = "runtime64"; view = [Microsoft.Win32.RegistryView]::Registry64 },
        [ordered]@{ name = "runtime32"; view = [Microsoft.Win32.RegistryView]::Registry32 }
    )) {
        $name = [string]$entry.name
        if ($beforeRestore[$name] -eq $Snapshot[$name]) { continue }
        $attempted = $true
        try { Set-RegistryOpenXrRuntime -View $entry.view -Value $Snapshot[$name] }
        catch { $errors += ("{0}: {1}" -f $name, $_.Exception.Message) }
    }
    $afterRestore = Get-OpenXrRegistrySnapshot
    [pscustomobject][ordered]@{
        attempted = $attempted
        succeeded = $afterRestore.runtime64 -eq $Snapshot.runtime64 -and
            $afterRestore.runtime32 -eq $Snapshot.runtime32
        errors = @($errors)
        beforeRestore = $beforeRestore
        afterRestore = $afterRestore
    }
}

function Get-PreviousOpenXrRuntime64 {
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine,
        [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key = $baseKey.OpenSubKey("SOFTWARE\Khronos\OpenXR\1", $false)
        if ($null -eq $key) { return $null }
        try { return [string]$key.GetValue("PreviousActiveRuntime", $null) }
        finally { $key.Dispose() }
    }
    finally { $baseKey.Dispose() }
}

function Restore-MetaOpenXrRuntimeSnapshot {
    param(
        [System.Collections.IDictionary]$Snapshot,
        [AllowNull()][object]$Installation
    )
    $beforeRestore = Get-OpenXrRegistrySnapshot
    if ($beforeRestore.runtime64 -eq $Snapshot.runtime64 -and
        $beforeRestore.runtime32 -eq $Snapshot.runtime32) {
        return [pscustomobject][ordered]@{
            attempted = $false
            succeeded = $true
            strategy = "AlreadyPreserved"
            uacRequired = $false
            errors = @()
            beforeRestore = $beforeRestore
            afterRestore = $beforeRestore
        }
    }

    $previousRuntime64 = Get-PreviousOpenXrRuntime64
    $deactivationScript = if ($Installation) {
        Join-Path ([string]$Installation.Directory) "deactivate_simulator.ps1"
    } else { $null }
    $pairedMetaDeactivationSafe = $Installation -and
        $beforeRestore.runtime64 -eq [string]$Installation.Manifest -and
        $beforeRestore.runtime32 -eq $Snapshot.runtime32 -and
        $previousRuntime64 -eq $Snapshot.runtime64 -and
        (Test-Path -LiteralPath $deactivationScript -PathType Leaf)

    if (-not $pairedMetaDeactivationSafe) {
        $direct = Restore-OpenXrRegistrySnapshot -Snapshot $Snapshot
        return [pscustomobject][ordered]@{
            attempted = [bool]$direct.attempted
            succeeded = [bool]$direct.succeeded
            strategy = "DirectExactSnapshot"
            uacRequired = $false
            errors = @($direct.errors)
            beforeRestore = $direct.beforeRestore
            afterRestore = $direct.afterRestore
        }
    }

    $errors = @()
    try {
        $arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -MessageTime 0' -f $deactivationScript
        $process = Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
        if ($process.ExitCode -ne 0) { $errors += "Meta deactivation exited with code $($process.ExitCode)." }
    }
    catch { $errors += $_.Exception.Message }
    $afterRestore = Get-OpenXrRegistrySnapshot
    $previousAfter = Get-PreviousOpenXrRuntime64
    $succeeded = $afterRestore.runtime64 -eq $Snapshot.runtime64 -and
        $afterRestore.runtime32 -eq $Snapshot.runtime32 -and
        [string]::IsNullOrWhiteSpace($previousAfter)
    if (-not $succeeded -and $errors.Count -eq 0) {
        $errors += "Meta's deactivation script did not restore the exact pre-run runtime snapshot."
    }
    [pscustomobject][ordered]@{
        attempted = $true
        succeeded = $succeeded
        strategy = "MetaPairedDeactivationScript"
        uacRequired = $true
        errors = @($errors)
        beforeRestore = $beforeRestore
        afterRestore = $afterRestore
    }
}

function Close-SimulatorProcess {
    param([Diagnostics.Process]$Process)
    if ($null -eq $Process) { return }
    try { $Process.Refresh() } catch { return }
    if ($Process.HasExited) { return }
    $closeRequested = $false
    try { $closeRequested = $Process.CloseMainWindow() } catch { $closeRequested = $false }
    if ($closeRequested) {
        try {
            if ($Process.WaitForExit(15000)) { return }
        }
        catch { }
    }
    Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    try { [void]$Process.WaitForExit(5000) } catch { }
}

function Get-MetaXrSimulatorInstallation {
    $root = Join-Path $env:ProgramFiles "MetaXRSimulator"
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { return $null }
    return Get-ChildItem -LiteralPath $root -Directory | ForEach-Object {
        $exe = Join-Path $_.FullName "MetaXRSimulator.exe"
        $manifest = Join-Path $_.FullName "meta_openxr_simulator.json"
        if ((Test-Path -LiteralPath $exe -PathType Leaf) -and (Test-Path -LiteralPath $manifest -PathType Leaf)) {
            $version = $null
            [void][version]::TryParse(($_.Name -replace '^v', ''), [ref]$version)
            [pscustomobject]@{ Directory = $_.FullName; Executable = $exe; Manifest = $manifest; Version = $version }
        }
    } | Sort-Object Version -Descending | Select-Object -First 1
}

function Get-UnityExecutable {
    $projectVersionPath = Join-Path $unityProject "ProjectSettings\ProjectVersion.txt"
    $versionLine = Get-Content -LiteralPath $projectVersionPath | Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
    $unityVersion = ($versionLine -split ':', 2)[1].Trim()
    $candidates = @(@(
        (Join-Path $env:ProgramFiles "Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Unity\Hub\Editor\$unityVersion\Editor\Unity.exe")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) })
    if ($candidates.Count -eq 0) { throw "Unity $unityVersion was not found in a Unity Hub editor directory." }
    return $candidates[0]
}

function Assert-UnitySlotAvailable {
    $lockPath = Join-Path $unityProject "Temp\UnityLockfile"
    $unityProcesses = @(Get-Process -Name Unity -ErrorAction SilentlyContinue)
    if ((Test-Path -LiteralPath $lockPath) -or $unityProcesses.Count -gt 0) {
        throw "Unity is already running or the product lockfile exists. Coordinate the shared Editor slot before launching simulator QA."
    }
}

function New-DevelopmentProjectCopy {
    $copyStamp = [DateTime]::UtcNow.ToString("yyyyMMdd_HHmmss")
    $copyRoot = Join-Path $DevelopmentScratchRoot ("dev_project_{0}_{1}" -f $copyStamp, $PID)
    $scratchPrefix = $DevelopmentScratchRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $copyRoot.StartsWith($scratchPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Simulator development copy escaped its scratch root: $copyRoot"
    }
    $script:developmentCopyPath = $copyRoot
    New-Item -ItemType Directory -Path $copyRoot -Force | Out-Null
    foreach ($name in @("Assets", "Packages", "ProjectSettings")) {
        $source = Join-Path $unityProject $name
        Copy-Item -LiteralPath $source -Destination $copyRoot -Recurse -Force
    }
    return $copyRoot
}

function Remove-DevelopmentProjectCopy {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Container)) { return }
    $scratch = [IO.Path]::GetFullPath($DevelopmentScratchRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $candidate = [IO.Path]::GetFullPath($Path)
    $prefix = $scratch + [IO.Path]::DirectorySeparatorChar
    $leaf = [IO.Path]::GetFileName($candidate)
    if (-not $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        $leaf -notmatch '^dev_project_\d{8}_\d{6}_\d+$') {
        throw "Refusing to remove unverified simulator development copy: $candidate"
    }
    Remove-Item -LiteralPath $candidate -Recurse -Force
    $script:developmentCopyRemoved = -not (Test-Path -LiteralPath $candidate)
}

function Build-SimulatorDevelopmentPlayer {
    if ([string]::IsNullOrWhiteSpace($env:QFL_META_XR_SIMULATOR_AERIAL_SOURCES) -or
        -not (Test-Path -LiteralPath $env:QFL_META_XR_SIMULATOR_AERIAL_SOURCES -PathType Container)) {
        throw 'Set QFL_META_XR_SIMULATOR_AERIAL_SOURCES to the retained terrain_recovery/pages directory before building. No development copy was created.'
    }
    Assert-UnitySlotAvailable
    $unityExe = Get-UnityExecutable
    $devProject = New-DevelopmentProjectCopy
    $buildLog = Join-Path $logRoot "unity_simulator_dev_build.log"
    $previous = [ordered]@{
        allow = $env:QFL_META_XR_SIMULATOR_ALLOW_DEV_COPY
        expected = $env:QFL_META_XR_SIMULATOR_EXPECTED_PROJECT_ROOT
        player = $env:QFL_META_XR_SIMULATOR_PLAYER_PATH
        report = $env:QFL_META_XR_SIMULATOR_BUILD_REPORT
    }
    try {
        $env:QFL_META_XR_SIMULATOR_ALLOW_DEV_COPY = "1"
        $env:QFL_META_XR_SIMULATOR_EXPECTED_PROJECT_ROOT = $devProject
        $env:QFL_META_XR_SIMULATOR_PLAYER_PATH = $playerPath
        $env:QFL_META_XR_SIMULATOR_BUILD_REPORT = $buildReportPath
        $arguments = @(
            "-batchmode",
            "-quit",
            "-buildTarget", "StandaloneWindows64",
            "-projectPath", ('"{0}"' -f $devProject),
            "-executeMethod", "QuestFlightLab.Editor.MetaXrSimulatorQaBuild.BuildPlayer",
            "-logFile", ('"{0}"' -f $buildLog)
        )
        $process = Start-Process -FilePath $unityExe -ArgumentList $arguments -PassThru -WindowStyle Hidden
        if (-not $process.WaitForExit(900000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "Meta XR Simulator development player build exceeded 900 seconds. See $buildLog"
        }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $playerPath -PathType Leaf)) {
            throw "Meta XR Simulator development player build failed (exit $($process.ExitCode)). See $buildLog"
        }
    }
    finally {
        $env:QFL_META_XR_SIMULATOR_ALLOW_DEV_COPY = $previous.allow
        $env:QFL_META_XR_SIMULATOR_EXPECTED_PROJECT_ROOT = $previous.expected
        $env:QFL_META_XR_SIMULATOR_PLAYER_PATH = $previous.player
        $env:QFL_META_XR_SIMULATOR_BUILD_REPORT = $previous.report
        try { Remove-DevelopmentProjectCopy -Path $devProject }
        catch { $script:developmentCleanupError = $_.Exception.Message }
    }
}

function Set-SessionCaptureConfiguration {
    param(
        [string]$PersistentPath,
        [string]$Mode,
        [string]$VrsPath
    )
    $parent = Split-Path -Parent $PersistentPath
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $data = [ordered]@{}
    if (Test-Path -LiteralPath $PersistentPath -PathType Leaf) {
        try { $data = Get-Content -LiteralPath $PersistentPath -Raw | ConvertFrom-Json -AsHashtable }
        catch { throw "Meta XR Simulator persistent configuration is invalid JSON: $PersistentPath" }
    }
    if ($Mode -eq "live") {
        [void]$data.Remove("session_capture")
        $data | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $PersistentPath -Encoding utf8
        return
    }
    $sessionCapture = [ordered]@{
        delay_start_ms = 1000
        exec_state = $Mode
        quit_buffer_ms = 1500
        quit_when_complete = $true
    }
    if ($Mode -eq "replay") { $sessionCapture.replay_path = $VrsPath }
    if ($Mode -eq "record") { $sessionCapture.record_path = $VrsPath }
    $data.session_capture = $sessionCapture
    $data | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $PersistentPath -Encoding utf8
}

function Invoke-SimulatorSession {
    param(
        [System.Collections.IDictionary]$Session,
        [string]$Mode,
        [string]$VrsPath,
        [object]$Installation,
        [string]$PersistentPath
    )
    $id = [string]$Session.id
    $sessionDirectory = Join-Path $sessionOutputRoot $id
    New-Item -ItemType Directory -Path $sessionDirectory -Force | Out-Null
    $playerLog = Join-Path $logRoot ("{0}_player.log" -f $id)
    $persistentExisted = Test-Path -LiteralPath $PersistentPath -PathType Leaf
    $persistentBytes = if ($persistentExisted) { [IO.File]::ReadAllBytes($PersistentPath) } else { $null }
    $envNames = @(
        "XR_RUNTIME_JSON", "QFL_META_XR_SIMULATOR_QA_DIR", "QFL_META_XR_SIMULATOR_SESSION",
        "QFL_META_XR_SIMULATOR_REPLAY_PATH", "QFL_META_XR_SIMULATOR_CAPTURE_DELAY_SECONDS",
        "QFL_META_XR_SIMULATOR_CAPTURE_WIDTH", "QFL_META_XR_SIMULATOR_CAPTURE_HEIGHT",
        "QFL_META_XR_SIMULATOR_START_DEMO", "QFL_META_XR_SIMULATOR_EXIT_AFTER_CAPTURE",
        "QFL_META_XR_SIMULATOR_EXPECTED_RUNTIME_VERSION"
    )
    $environmentBefore = @{}
    foreach ($name in $envNames) { $environmentBefore[$name] = [Environment]::GetEnvironmentVariable($name, "Process") }
    $frontend = $null
    $player = $null
    $timedOut = $false
    try {
        Set-SessionCaptureConfiguration -PersistentPath $PersistentPath -Mode $Mode -VrsPath $VrsPath
        $env:XR_RUNTIME_JSON = $Installation.Manifest
        $env:QFL_META_XR_SIMULATOR_EXPECTED_RUNTIME_VERSION = $Installation.Version.ToString()
        $env:QFL_META_XR_SIMULATOR_QA_DIR = $sessionDirectory
        $env:QFL_META_XR_SIMULATOR_SESSION = $id
        $env:QFL_META_XR_SIMULATOR_REPLAY_PATH = if ($Mode -eq "replay") { $VrsPath } else { "" }
        $env:QFL_META_XR_SIMULATOR_CAPTURE_DELAY_SECONDS = [string]$Session.delaySeconds
        $env:QFL_META_XR_SIMULATOR_CAPTURE_WIDTH = [string]$CaptureWidth
        $env:QFL_META_XR_SIMULATOR_CAPTURE_HEIGHT = [string]$CaptureHeight
        $env:QFL_META_XR_SIMULATOR_START_DEMO = if ([bool]$Session.startDemo) { "1" } else { "0" }
        $env:QFL_META_XR_SIMULATOR_EXIT_AFTER_CAPTURE = if ($Mode -eq "record") { "0" } else { "1" }

        $alreadyRunning = @(Get-Process -Name "MetaXRSimulator" -ErrorAction SilentlyContinue).Count -gt 0
        if (-not $alreadyRunning) {
            $windowStyle = if ($Mode -eq "record") { "Normal" } else { "Hidden" }
            $frontend = Start-Process -FilePath $Installation.Executable -PassThru -WindowStyle $windowStyle
            Start-Sleep -Seconds 2
        }
        $arguments = @(
            "-logFile", ('"{0}"' -f $playerLog),
            "-screen-width", "1440",
            "-screen-height", "900",
            "-screen-fullscreen", "0"
        )
        $playerWindowStyle = if ($Mode -eq "record") { "Normal" } else { "Hidden" }
        $player = Start-Process -FilePath $playerPath -ArgumentList $arguments -PassThru -WindowStyle $playerWindowStyle
        if (-not $player.WaitForExit($SessionTimeoutSeconds * 1000)) {
            $timedOut = $true
            Stop-Process -Id $player.Id -Force -ErrorAction SilentlyContinue
        }
        $exitCode = if ($timedOut) { 124 } else { $player.ExitCode }
        $sessionReportPath = Join-Path $sessionDirectory ("{0}_session_report.json" -f $id)
        $sessionReport = if (Test-Path -LiteralPath $sessionReportPath -PathType Leaf) {
            Get-Content -LiteralPath $sessionReportPath -Raw | ConvertFrom-Json
        } else { $null }
        return [ordered]@{
            id = $id
            mode = $Mode
            vrsPath = $VrsPath
            outputDirectory = $sessionDirectory
            playerLog = $playerLog
            playerExitCode = $exitCode
            timedOut = $timedOut
            sessionReportPath = $sessionReportPath
            sessionReportPresent = $null -ne $sessionReport
            productConnected = $null -ne $sessionReport -and [bool]$sessionReport.xrDeviceActive -and [bool]$sessionReport.stereoEnabled
            runtimeName = if ($sessionReport) { [string]$sessionReport.openXrRuntimeName } else { $null }
            sessionPassed = $null -ne $sessionReport -and [bool]$sessionReport.passed
        }
    }
    finally {
        Close-SimulatorProcess -Process $player
        # Meta's frontend can perform machine-runtime bookkeeping during startup.
        # Give its normal shutdown path a chance to complete before any force stop.
        Close-SimulatorProcess -Process $frontend
        foreach ($name in $envNames) {
            [Environment]::SetEnvironmentVariable($name, $environmentBefore[$name], "Process")
        }
        if ($persistentExisted) { [IO.File]::WriteAllBytes($PersistentPath, $persistentBytes) }
        elseif (Test-Path -LiteralPath $PersistentPath -PathType Leaf) { Remove-Item -LiteralPath $PersistentPath -Force }
    }
}

$installation = Get-MetaXrSimulatorInstallation
$runtimeBefore = Get-OpenXrRegistrySnapshot
$persistentPath = Join-Path $env:APPDATA "MetaXR\MetaXrSimulator\persistent_data.json"
$persistentInitiallyExisted = Test-Path -LiteralPath $persistentPath -PathType Leaf
$persistentInitialHash = if ($persistentInitiallyExisted) { Get-Sha256File -LiteralPath $persistentPath } else { $null }
$sessionRuns = @()
$missingRecordings = @()
$copiedReplays = @()
$classification = "NOT_RUN"
$playerBuilt = Test-Path -LiteralPath $playerPath -PathType Leaf
$caughtError = $null
$runtimeRestoration = $null

try {
    if ($null -eq $installation) { throw "Meta XR Simulator is not installed." }
    & (Join-Path $PSScriptRoot "setup_meta_xr_simulator.ps1") -ArtifactRoot $ArtifactRoot -AuditOnly
    if ($LASTEXITCODE -ne 0) { throw "Meta XR Simulator setup audit failed." }

    foreach ($session in $sessionCatalog) {
        $vrs = Join-Path $ReplayRoot ("{0}.vrs" -f $session.id)
        if (-not (Test-Path -LiteralPath $vrs -PathType Leaf)) { $missingRecordings += [string]$session.id }
    }
    if ($AuditOnly) {
        $classification = if ($missingRecordings.Count -eq 0) { "READY_TO_REPLAY" } else { "BLOCKED_MISSING_RECORDINGS" }
    }
    else {
        if ($RebuildPlayer -or -not $playerBuilt) {
            Build-SimulatorDevelopmentPlayer
            $playerBuilt = Test-Path -LiteralPath $playerPath -PathType Leaf
        }

        if (-not [string]::IsNullOrWhiteSpace($RecordSession)) {
            $session = $sessionCatalog | Where-Object { $_.id -eq $RecordSession } | Select-Object -First 1
            $recordPath = Join-Path $ReplayRoot ("{0}.vrs" -f $RecordSession)
            Write-Host "Interactive one-time recording: $RecordSession"
            Write-Host "Use Meta XR Simulator input controls, stop recording in its Record panel, then close the development player."
            $sessionRuns += Invoke-SimulatorSession -Session $session -Mode "record" -VrsPath $recordPath -Installation $installation -PersistentPath $persistentPath
            $classification = if (Test-Path -LiteralPath $recordPath -PathType Leaf) { "RECORDING_CREATED_REPLAY_NOT_YET_VERIFIED" } else { "BLOCKED_RECORDING_NOT_CREATED" }
        }
        else {
            foreach ($session in $sessionCatalog) {
                $vrs = Join-Path $ReplayRoot ("{0}.vrs" -f $session.id)
                if (-not (Test-Path -LiteralPath $vrs -PathType Leaf)) { continue }
                $evidenceCopy = Join-Path $replayEvidenceRoot ("{0}.vrs" -f $session.id)
                Copy-Item -LiteralPath $vrs -Destination $evidenceCopy -Force
                $copiedReplays += $evidenceCopy
                $sessionRuns += Invoke-SimulatorSession -Session $session -Mode "replay" -VrsPath $vrs -Installation $installation -PersistentPath $persistentPath
            }
            if ($sessionRuns.Count -eq 0 -and $LiveSmokeWhenRecordingsMissing) {
                # A live neutral connection/capture proves the player can create a Meta
                # OpenXR stereo session. It is explicitly not counted as replay coverage.
                $smoke = $sessionCatalog | Where-Object { $_.id -eq "cockpit_neutral" } | Select-Object -First 1
                if ($null -eq $smoke) { $smoke = $sessionCatalog | Select-Object -First 1 }
                $sessionRuns += Invoke-SimulatorSession -Session $smoke -Mode "live" -VrsPath "" -Installation $installation -PersistentPath $persistentPath
            }
            $failedRuns = @($sessionRuns | Where-Object { -not $_.sessionPassed })
            if ($missingRecordings.Count -gt 0) { $classification = "BLOCKED_MISSING_RECORDINGS" }
            elseif ($failedRuns.Count -gt 0) { $classification = "FAIL_SIMULATOR_SESSION_GATE" }
            else { $classification = "PASS_ALL_RECORDED_SIMULATOR_SESSIONS" }
        }
    }
}
catch {
    $caughtError = $_.Exception.ToString()
    $classification = "FAIL_SIMULATOR_ORCHESTRATION"
}
finally {
    # Per-process XR_RUNTIME_JSON remains the intended activation mechanism, but
    # current Meta frontends may still mutate HKLM. Restore the exact snapshot or
    # fail closed with the required UAC/admin error preserved in evidence.
    $runtimeRestoration = Restore-MetaOpenXrRuntimeSnapshot -Snapshot $runtimeBefore -Installation $installation
    $runtimeAfter = $runtimeRestoration.afterRestore
    $persistentAfterExists = Test-Path -LiteralPath $persistentPath -PathType Leaf
    $persistentAfterHash = if ($persistentAfterExists) { Get-Sha256File -LiteralPath $persistentPath } else { $null }
    $runtimePreserved = [bool]$runtimeRestoration.succeeded
    $persistentRestored = $persistentInitiallyExisted -eq $persistentAfterExists -and $persistentInitialHash -eq $persistentAfterHash
    if (-not $runtimePreserved) { $classification = "FAIL_SIMULATOR_STATE_RESTORATION" }

    $report = [ordered]@{
        schemaVersion = 2
        generatedUtc = [DateTime]::UtcNow.ToString("O")
        classification = $classification
        repository = $repoRoot
        artifactRoot = $ArtifactRoot
        simulatorVersion = if ($installation) { $installation.Version.ToString() } else { $null }
        simulatorExecutable = if ($installation) { $installation.Executable } else { $null }
        simulatorRuntimeManifest = if ($installation) { $installation.Manifest } else { $null }
        playerPath = $playerPath
        playerBuilt = $playerBuilt
        developmentScratchRoot = $DevelopmentScratchRoot
        developmentProjectRemoved = $developmentCopyRemoved
        developmentCleanupError = $developmentCleanupError
        shippingAndroidConfigurationChanged = $false
        activationMethod = "Per-process XR_RUNTIME_JSON"
        activeRuntimeBefore = $runtimeBefore
        activeRuntimeAfter = $runtimeAfter
        activeRuntimePreserved = $runtimePreserved
        activeRuntimeMutationDetected = [bool]$runtimeRestoration.attempted
        activeRuntimeRestorationStrategy = [string]$runtimeRestoration.strategy
        activeRuntimeRestorationRequiredUac = [bool]$runtimeRestoration.uacRequired
        activeRuntimeRestorationErrors = @($runtimeRestoration.errors)
        activeRuntimeBeforeRestoreAttempt = $runtimeRestoration.beforeRestore
        persistentConfigurationPath = $persistentPath
        persistentConfigurationRestored = $persistentRestored
        replayRoot = $ReplayRoot
        requiredSessionIds = @($sessionCatalog.id)
        missingRecordings = $missingRecordings
        replayEvidenceCopies = $copiedReplays
        sessionRuns = $sessionRuns
        orchestrationError = $caughtError
        timingEvidenceScope = "Simulator API/stereo/tracking/composition evidence only; never Quest GPU, thermal, or frame-performance evidence."
    }
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $runReportPath -Encoding utf8
}

Write-Host "Meta XR Simulator QA run report: $runReportPath"
if ($classification -eq "PASS_ALL_RECORDED_SIMULATOR_SESSIONS" -or
    $classification -eq "READY_TO_REPLAY" -or
    $classification -eq "RECORDING_CREATED_REPLAY_NOT_YET_VERIFIED") { exit 0 }
if ($AllowMissingRecordings -and $classification -eq "BLOCKED_MISSING_RECORDINGS") { exit 0 }
exit 2
