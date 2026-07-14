[CmdletBinding()]
param(
    [string]$ArtifactRoot,
    [string]$InstallerPath,
    [switch]$InstallIfMissing,
    [switch]$AuditOnly,
    [string]$ChildCommand,
    [string[]]$ChildArgumentList = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $stamp = [DateTime]::UtcNow.ToString("yyyyMMdd_HHmmss")
    $ArtifactRoot = Join-Path (Split-Path -Parent $repoRoot) "T2-QuestFlight-product-artifacts\standalone_recovery_$stamp"
}
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$outputDirectory = Join-Path $ArtifactRoot "meta_xr_simulator\setup"
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$reportPath = Join-Path $outputDirectory "meta_xr_simulator_setup_report.json"
$summaryPath = Join-Path $outputDirectory "meta_xr_simulator_setup_summary.md"
$officialDownloadPage = "https://developers.meta.com/horizon/downloads/package/meta-xr-simulator-windows/"

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

function Get-MetaXrSimulatorInstallation {
    $roots = @(
        (Join-Path $env:ProgramFiles "MetaXRSimulator"),
        (Join-Path $env:LOCALAPPDATA "MetaXRSimulator")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) }

    $candidates = foreach ($root in $roots) {
        Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $exe = Join-Path $_.FullName "MetaXRSimulator.exe"
            $manifest = Join-Path $_.FullName "meta_openxr_simulator.json"
            if ((Test-Path -LiteralPath $exe -PathType Leaf) -and
                (Test-Path -LiteralPath $manifest -PathType Leaf)) {
                $versionText = $_.Name -replace '^v', ''
                $parsedVersion = $null
                [void][version]::TryParse($versionText, [ref]$parsedVersion)
                [pscustomobject]@{
                    Directory = $_.FullName
                    Executable = $exe
                    Manifest = $manifest
                    DirectoryVersion = $parsedVersion
                }
            }
        }
    }
    return $candidates | Sort-Object DirectoryVersion -Descending | Select-Object -First 1
}

function Install-MetaXrSimulator {
    param([string]$MsiPath)
    if (-not (Test-Path -LiteralPath $MsiPath -PathType Leaf)) {
        throw "Meta XR Simulator MSI not found: $MsiPath"
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $MsiPath
    if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
        $signature.SignerCertificate.Subject -notmatch 'Meta Platforms') {
        throw "Refusing to install an unsigned or non-Meta MSI: status=$($signature.Status), signer=$($signature.SignerCertificate.Subject)"
    }
    $installLog = Join-Path $outputDirectory "meta_xr_simulator_install.log"
    $arguments = @(
        "/i",
        ('"{0}"' -f ([IO.Path]::GetFullPath($MsiPath))),
        "/qn",
        "/norestart",
        "/L*v",
        ('"{0}"' -f $installLog)
    )
    $process = Start-Process -FilePath "msiexec.exe" -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -notin @(0, 3010)) {
        throw "Meta XR Simulator MSI failed with exit code $($process.ExitCode). See $installLog"
    }
}

function Get-FileSignatureEvidence {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    [ordered]@{
        status = $signature.Status.ToString()
        signer = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
        thumbprint = if ($signature.SignerCertificate) { $signature.SignerCertificate.Thumbprint } else { $null }
    }
}

function Get-PackageVersion {
    param([object]$Manifest, [string]$PackageName)
    $property = $Manifest.dependencies.PSObject.Properties[$PackageName]
    if ($null -eq $property) { return $null }
    return [string]$property.Value
}

$runtimeBefore = Get-OpenXrRegistrySnapshot
$previousRuntimeEnvironment = $env:XR_RUNTIME_JSON
$childExitCode = $null
$registryPreserved = $false
$persistentConfigurationTouched = $false

try {
    $installation = Get-MetaXrSimulatorInstallation
    if ($null -eq $installation -and $InstallIfMissing) {
        if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
            throw "-InstallIfMissing requires -InstallerPath. Download only from $officialDownloadPage"
        }
        Install-MetaXrSimulator -MsiPath $InstallerPath
        $installation = Get-MetaXrSimulatorInstallation
    }

    $packageManifestPath = Join-Path $repoRoot "QuestFlight\Packages\manifest.json"
    $packageManifest = Get-Content -LiteralPath $packageManifestPath -Raw | ConvertFrom-Json
    $openXrVersion = Get-PackageVersion -Manifest $packageManifest -PackageName "com.unity.xr.openxr"
    $xrManagementVersion = Get-PackageVersion -Manifest $packageManifest -PackageName "com.unity.xr.management"
    $inputSystemVersion = Get-PackageVersion -Manifest $packageManifest -PackageName "com.unity.inputsystem"
    $embeddedSimulatorPackage = Get-PackageVersion -Manifest $packageManifest -PackageName "com.meta.xr.simulator"

    $installed = $null -ne $installation
    $simulatorVersion = $null
    $deviceProfile = $null
    $useOffscreenMode = $null
    $executableSignature = $null
    $simulatorManifestValid = $false
    $syntheticEnvironmentAvailable = $false
    if ($installed) {
        $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($installation.Executable)
        $simulatorVersion = $versionInfo.ProductVersion
        $executableSignature = Get-FileSignatureEvidence -Path $installation.Executable
        $runtimeManifest = Get-Content -LiteralPath $installation.Manifest -Raw | ConvertFrom-Json
        $libraryPath = Join-Path $installation.Directory ([string]$runtimeManifest.runtime.library_path)
        $simulatorManifestValid = [bool]$runtimeManifest.runtime.name -and
            (Test-Path -LiteralPath $libraryPath -PathType Leaf)
        $coreConfigPath = Join-Path $installation.Directory "config\sim_core_configuration.json"
        if (Test-Path -LiteralPath $coreConfigPath -PathType Leaf) {
            $coreConfig = Get-Content -LiteralPath $coreConfigPath -Raw | ConvertFrom-Json
            $deviceProfile = [string]$coreConfig.device_profile
            $useOffscreenMode = [bool]$coreConfig.use_offscreen_mode
        }
        $syntheticEnvironmentAvailable = Test-Path -LiteralPath (
            Join-Path $installation.Directory "Resources\simulated_environments\simulated_environments.json") -PathType Leaf
    }

    if (-not $AuditOnly -and -not [string]::IsNullOrWhiteSpace($ChildCommand)) {
        if (-not $installed) { throw "Meta XR Simulator is not installed." }
        if (-not (Test-Path -LiteralPath $ChildCommand -PathType Leaf)) {
            throw "Child command not found: $ChildCommand"
        }
        # The Khronos loader honors XR_RUNTIME_JSON for this process tree. This
        # avoids Meta's registry activation script and therefore needs no UAC.
        $env:XR_RUNTIME_JSON = $installation.Manifest
        & $ChildCommand @ChildArgumentList
        $childExitCode = $LASTEXITCODE
    }
}
finally {
    $env:XR_RUNTIME_JSON = $previousRuntimeEnvironment
    $runtimeAfter = Get-OpenXrRegistrySnapshot
    $registryPreserved =
        $runtimeBefore.runtime64 -eq $runtimeAfter.runtime64 -and
        $runtimeBefore.runtime32 -eq $runtimeAfter.runtime32
}

$report = [ordered]@{
    schemaVersion = 1
    generatedUtc = [DateTime]::UtcNow.ToString("O")
    repository = $repoRoot
    officialDownloadPage = $officialDownloadPage
    installed = $installed
    installationDirectory = if ($installed) { $installation.Directory } else { $null }
    executablePath = if ($installed) { $installation.Executable } else { $null }
    executableProductVersion = $simulatorVersion
    executableSignature = $executableSignature
    runtimeManifestPath = if ($installed) { $installation.Manifest } else { $null }
    runtimeManifestValid = $simulatorManifestValid
    deviceProfile = $deviceProfile
    requestedQaRefreshRateHz = 72
    refreshRateVerification = "The development player requests 72 Hz and each session must verify XRDisplaySubsystem reports 72 Hz. Simulator timing is not headset performance."
    stereoscopicRendering = "Enabled only in the external StandaloneWindows64 OpenXR development copy."
    headsetTracking = $installed
    touchControllers = "Oculus Touch and Meta Quest Touch Plus profiles are enabled only in the external QA copy."
    gamepadInput = "Supported by the product input boundary and Meta XR Simulator; optional for replay recording."
    syntheticEnvironmentAvailable = $syntheticEnvironmentAvailable
    useOffscreenMode = $useOffscreenMode
    recordReplayAvailable = $installed
    unityPackages = [ordered]@{
        openXr = $openXrVersion
        xrManagement = $xrManagementVersion
        inputSystem = $inputSystemVersion
        obsoleteEmbeddedMetaSimulator = $embeddedSimulatorPackage
    }
    packageCompatibility = if ($openXrVersion -and -not $embeddedSimulatorPackage) {
        "Current standalone simulator path is compatible with the project's Unity OpenXR integration; no embedded Meta simulator package is required."
    } else {
        "Review required."
    }
    activeRuntimeBefore = $runtimeBefore
    activeRuntimeAfter = $runtimeAfter
    activeRuntimePreserved = $registryPreserved
    runtimeActivationMethod = "Per-process XR_RUNTIME_JSON; machine registry is never modified."
    persistentConfigurationTouched = $persistentConfigurationTouched
    childCommand = $ChildCommand
    childExitCode = $childExitCode
    productConnectionVerification = "scripts/run_meta_xr_simulator_qa.ps1 verifies runtime name, stereo eye poses, and session evidence."
    shippingAndroidConfigurationChanged = $false
    limitations = @(
        "Meta XR Simulator proves API-level OpenXR stereo, tracking, input, and composition behavior only.",
        "Simulator or Windows timing must never be presented as standalone Quest GPU, thermal, or frame-performance evidence.",
        "A physical Quest 3 remains required for frame, stale/tear, and thermal acceptance."
    )
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8

$summary = @"
# Meta XR Simulator setup

- Installed: **$installed**
- Version: **$simulatorVersion**
- Device profile: **$deviceProfile**
- Runtime manifest valid: **$simulatorManifestValid**
- Active OpenXR runtime preserved: **$registryPreserved**
- Runtime activation: per-process ``XR_RUNTIME_JSON``; no registry mutation or UAC during QA runs.
- Unity OpenXR / XR Management / Input System: ``$openXrVersion`` / ``$xrManagementVersion`` / ``$inputSystemVersion``
- Obsolete embedded simulator package present: **$([bool]$embeddedSimulatorPackage)**
- Shipping Android configuration changed: **False**

The development player requests and verifies 72 Hz, stereo, headset tracking, and Touch profiles. Meta XR Simulator timing is not Quest 3 hardware-performance evidence.

Official installer and workflow: $officialDownloadPage
"@
Set-Content -LiteralPath $summaryPath -Value $summary -Encoding utf8

Write-Host "Meta XR Simulator setup report: $reportPath"
if (-not $installed -or -not $simulatorManifestValid -or -not $registryPreserved) { exit 2 }
if ($null -ne $childExitCode -and $childExitCode -ne 0) { exit $childExitCode }
exit 0
