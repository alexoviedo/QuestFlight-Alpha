[CmdletBinding()]
param(
  [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe',
  [string]$ProjectPath = 'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight',
  [string]$ArtifactDir = '',
  [ValidateRange(60, 3600)]
  [int]$TimeoutSeconds = 1200,
  [switch]$Development,
  [switch]$PreflightOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ProjectPath)) { throw "Project not found: $ProjectPath" }
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path

function Test-ProductProjectPathLength {
  param([Parameter(Mandatory = $true)][string]$UnityProjectPath)

  # Unity 6000.3 still reaches legacy System.IO paths while loading project assets
  # and package assemblies. Fail before Unity when a valid file would cross the
  # Windows MAX_PATH boundary, which otherwise surfaces as a misleading
  # DirectoryNotFoundException. Generated build output is excluded because its
  # path layout is controlled by Unity/Gradle after this input preflight.
  $maximumInputPathLength = 259
  $inputRoots = @(
    'Assets',
    'Packages',
    'ProjectSettings',
    'Library\PackageCache'
  )
  $longestInput = $null
  foreach ($relativeRoot in $inputRoots) {
    $inputRoot = Join-Path $UnityProjectPath $relativeRoot
    if (-not (Test-Path -LiteralPath $inputRoot -PathType Container)) { continue }
    foreach ($inputFile in Get-ChildItem -LiteralPath $inputRoot -Recurse -File -Force) {
      if ($null -eq $longestInput -or $inputFile.FullName.Length -gt $longestInput.FullName.Length) {
        $longestInput = $inputFile
      }
    }
  }

  if ($null -eq $longestInput) {
    throw "Production project-path preflight failed: no Unity input files were found under $UnityProjectPath"
  }
  if ($longestInput.FullName.Length -gt $maximumInputPathLength) {
    throw "Production project-path preflight failed: longest Unity input path is $($longestInput.FullName.Length) characters (maximum $maximumInputPathLength). Use a short drive alias or a shorter preserved stage path. Longest path: $($longestInput.FullName)"
  }

  [ordered]@{
    projectPath = $UnityProjectPath
    longestInputPath = $longestInput.FullName
    longestInputPathLength = $longestInput.FullName.Length
    maximumInputPathLength = $maximumInputPathLength
  }
}

function Test-ProductStreamingAssets {
  param([Parameter(Mandatory = $true)][string]$UnityProjectPath)

  $streamingRoot = Join-Path $UnityProjectPath 'Assets\StreamingAssets'
  if (-not (Test-Path -LiteralPath $streamingRoot -PathType Container)) {
    throw "Production StreamingAssets preflight failed: directory is missing: $streamingRoot"
  }

  # This is the complete persistent StreamingAssets set in the accepted standalone
  # baseline. JSBSim-1.3.1 is allowed only as a correctly marked package-owned
  # recovery artifact; Unity removes that injection before validating and rebuilding.
  $requiredEntries = @(
    'QuestFlightBuildingClipmap',
    'QuestFlightBuildingClipmap.meta'
  )
  $allowedEntries = @($requiredEntries)
  $ownedJsbsimDirectory = Join-Path $streamingRoot 'JSBSim-1.3.1'
  if (Test-Path -LiteralPath $ownedJsbsimDirectory -PathType Container) {
    $ownershipMarker = Join-Path $ownedJsbsimDirectory '.questflight_jsbsim_package_injection'
    $expectedMarker = 'com.alexoviedo.questflight.jsbsim/0.2.0'
    if (-not (Test-Path -LiteralPath $ownershipMarker -PathType Leaf) -or
        -not [string]::Equals(
          (Get-Content -LiteralPath $ownershipMarker -Raw),
          $expectedMarker,
          [StringComparison]::Ordinal)) {
      throw "Production StreamingAssets preflight failed: unowned JSBSim-1.3.1 data is present: $ownedJsbsimDirectory"
    }
    $allowedEntries += 'JSBSim-1.3.1'
    if (Test-Path -LiteralPath ($ownedJsbsimDirectory + '.meta') -PathType Leaf) {
      $allowedEntries += 'JSBSim-1.3.1.meta'
    }
  }

  $entryNames = @(Get-ChildItem -LiteralPath $streamingRoot -Force |
      ForEach-Object { $_.Name } |
      Sort-Object -Unique)
  $missing = @($requiredEntries | Where-Object { $_ -notin $entryNames })
  $unexpected = @($entryNames | Where-Object { $_ -notin $allowedEntries })
  if ($missing.Count -ne 0 -or $unexpected.Count -ne 0) {
    $missingText = if ($missing.Count -eq 0) { '<none>' } else { $missing -join ', ' }
    $unexpectedText = if ($unexpected.Count -eq 0) { '<none>' } else { $unexpected -join ', ' }
    throw "Production StreamingAssets preflight failed. missing=[$missingText]; unexpected=[$unexpectedText]. Build from a clean accepted product stage; the build wrapper will not delete, move, or silently exclude local data. Root: $streamingRoot"
  }

  [ordered]@{
    root = $streamingRoot
    entries = $entryNames
    requiredEntries = $requiredEntries
    packageOwnedJsbsimRecoveryPresent = [bool](Test-Path -LiteralPath $ownedJsbsimDirectory -PathType Container)
  }
}

$projectPathPreflight = Test-ProductProjectPathLength -UnityProjectPath $ProjectPath
Write-Host "Production project-path preflight: PASS ($($projectPathPreflight.longestInputPathLength)/$($projectPathPreflight.maximumInputPathLength) characters)"
$streamingAssetsPreflight = Test-ProductStreamingAssets -UnityProjectPath $ProjectPath
Write-Host "Production StreamingAssets preflight: PASS ($($streamingAssetsPreflight.entries -join ', '))"
if ($PreflightOnly) { return }

if (-not (Test-Path -LiteralPath $UnityExe)) { throw "Unity not found: $UnityExe" }
function Get-Sha256Hex {
  param([Parameter(Mandatory = $true)][string]$LiteralPath)

  $fileHashCommand = Get-Command Get-FileHash -ErrorAction SilentlyContinue
  if ($null -ne $fileHashCommand) {
    return (Get-FileHash -LiteralPath $LiteralPath -Algorithm SHA256).Hash.ToLowerInvariant()
  }

  $stream = [IO.File]::OpenRead($LiteralPath)
  $sha256 = [Security.Cryptography.SHA256]::Create()
  try {
    return ([BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
  } finally {
    $sha256.Dispose()
    $stream.Dispose()
  }
}

if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
  $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
  $ArtifactDir = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-extraction-artifacts\product_build_$stamp"
}
New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
$ArtifactDir = (Resolve-Path -LiteralPath $ArtifactDir).Path

$lock = Join-Path $ProjectPath 'Temp\UnityLockfile'
if (Test-Path -LiteralPath $lock) {
  if (Get-Process Unity -ErrorAction SilentlyContinue) {
    throw "Unity is already running; refusing to collide with the clean product project: $lock"
  }
  Remove-Item -LiteralPath $lock -Force
}

$log = Join-Path $ArtifactDir 'unity_product_build.log'
$arguments = @(
  '-batchmode',
  '-projectPath', $ProjectPath,
  '-executeMethod', 'QuestFlight.Editor.ProductionBuild.PerformAndroidBuildBatch',
  '-logFile', $log
)
$previousDevelopment = $env:QUESTFLIGHT_DEVELOPMENT_BUILD
try {
  $env:QUESTFLIGHT_DEVELOPMENT_BUILD = if ($Development) { '1' } else { '0' }
  $process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -PassThru -WindowStyle Hidden
  if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw "Product Quest build exceeded ${TimeoutSeconds}s and was stopped. Log: $log"
  }
} finally {
  $env:QUESTFLIGHT_DEVELOPMENT_BUILD = $previousDevelopment
}

$exitCode = [int]$process.ExitCode
$logText = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { '' }
if ($logText -match 'Application will terminate with return code ([1-9][0-9]*)') {
  $exitCode = [int]$Matches[1]
}
if ($exitCode -ne 0) { throw "Product Quest build failed with exit code $exitCode. Log: $log" }

$apk = Join-Path $ProjectPath 'Builds\Android\QuestFlight-production.apk'
if (-not (Test-Path -LiteralPath $apk)) { throw "Unity exited without the product APK: $apk" }
$hash = Get-Sha256Hex -LiteralPath $apk
[ordered]@{
  generatedUtc = [DateTime]::UtcNow.ToString('O')
  applicationIdentifier = 'com.alexoviedo.t2.questflight'
  scene = 'Assets/Scenes/ProductionVerticalSlice.unity'
  apk = $apk
  bytes = (Get-Item -LiteralPath $apk).Length
  sha256 = $hash
  development = [bool]$Development
  nativeJsbsimRequired = $true
  unityPrototypeAllowed = $false
  projectPathPreflightPassed = $true
  longestUnityInputPathLength = $projectPathPreflight.longestInputPathLength
  maximumUnityInputPathLength = $projectPathPreflight.maximumInputPathLength
  streamingAssetsPreflightPassed = $true
  persistentStreamingAssetEntries = @($streamingAssetsPreflight.entries)
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactDir 'product_apk_manifest.json') -Encoding UTF8

Write-Host "Product APK: $apk"
Write-Host "SHA256: $hash"
Write-Host "Build evidence: $ArtifactDir"
