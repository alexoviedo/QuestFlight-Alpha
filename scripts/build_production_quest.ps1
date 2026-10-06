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

function Test-ProductLibraryIsolation {
  param([Parameter(Mandatory = $true)][string]$UnityProjectPath)

  $libraryPath = Join-Path $UnityProjectPath 'Library'
  if (-not (Test-Path -LiteralPath $libraryPath -PathType Container)) {
    throw "Production Library preflight failed: Library is missing. Open the canonical project once to complete its import before building: $UnityProjectPath"
  }
  $libraryItem = Get-Item -LiteralPath $libraryPath -Force
  if (($libraryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Production Library preflight failed: Library must belong to this project root and may not be a junction or symbolic link. project=$UnityProjectPath library=$libraryPath target=$($libraryItem.Target -join ',')"
  }

  # A Unity Library is tied to the absolute project root. Persist that identity
  # inside the ignored cache so a copied/shared Library cannot silently load
  # serialized product scripts as missing again.
  $identityPath = Join-Path $libraryPath 'QuestFlightProjectRoot.txt'
  if (Test-Path -LiteralPath $identityPath -PathType Leaf) {
    $recordedProjectPath = (Get-Content -LiteralPath $identityPath -Raw).Trim()
    if (-not [string]::Equals($recordedProjectPath, $UnityProjectPath, [StringComparison]::OrdinalIgnoreCase)) {
      throw "Production Library preflight failed: Library belongs to a different project root. recorded=$recordedProjectPath requested=$UnityProjectPath marker=$identityPath"
    }
  } else {
    Set-Content -LiteralPath $identityPath -Value $UnityProjectPath -Encoding UTF8 -NoNewline
  }

  [ordered]@{
    path = $libraryPath
    identityPath = $identityPath
    projectPath = $UnityProjectPath
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
    'QuestFlightAerialClipmap',
    'QuestFlightAerialClipmap.meta',
    'QuestFlightBuildingClipmap',
    'QuestFlightBuildingClipmap.meta',
    'QuestFlightBranchGroves',
    'QuestFlightBranchGroves.meta'
  )
  $allowedEntries = @($requiredEntries)
  $localOnlyEntries = @()
  $branchManifest = Get-Content -LiteralPath (Join-Path $UnityProjectPath 'Assets/Production/Environment/MapBranchGrovesV1/SOURCE_MANIFEST.json') -Raw | ConvertFrom-Json
  if ($branchManifest.schema -ne 'questflight.map-branch-groves.v1' -or -not $branchManifest.complete -or @($branchManifest.pages).Count -ne 1296) { throw 'Map branch grove manifest incomplete' }
  foreach ($page in $branchManifest.pages) {
    if ($page.file -notmatch '^branches_[0-9]{2}_[0-9]{2}\.bytes$') { throw 'Invalid branch page filename' }
    $pagePath=Join-Path (Join-Path $streamingRoot 'QuestFlightBranchGroves') $page.file
    if ((Get-Item -LiteralPath $pagePath).Length -ne $page.bytes -or (Get-FileHash -LiteralPath $pagePath -Algorithm SHA256).Hash -ne $page.sha256) { throw "Branch page integrity failure: $pagePath" }
  }
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

  # The September 6 accepted APK includes both clipmaps. Never silently strip
  # its aerial bundles or admit unrelated local StreamingAssets.
  $aerialDirectory = Join-Path $streamingRoot 'QuestFlightAerialClipmap'
  $aerialMeta = $aerialDirectory + '.meta'
  $aerialDirectoryPresent = Test-Path -LiteralPath $aerialDirectory -PathType Container
  $aerialMetaPresent = Test-Path -LiteralPath $aerialMeta -PathType Leaf
  if ($aerialDirectoryPresent -ne $aerialMetaPresent) {
    throw "Production StreamingAssets preflight failed: QuestFlightAerialClipmap and its paired .meta must either both exist or both be absent. Root: $streamingRoot"
  }

  $entryNames = @(Get-ChildItem -LiteralPath $streamingRoot -Force |
      ForEach-Object { $_.Name } |
      Sort-Object -Unique)
  $missing = @($requiredEntries | Where-Object { $_ -notin $entryNames })
  $unexpected = @($entryNames | Where-Object { $_ -notin $allowedEntries })
  if ($missing.Count -ne 0 -or $unexpected.Count -ne 0) {
    $missingText = if ($missing.Count -eq 0) { '<none>' } else { $missing -join ', ' }
    $unexpectedText = if ($unexpected.Count -eq 0) { '<none>' } else { $unexpected -join ', ' }
    throw "Production StreamingAssets preflight failed. missing=[$missingText]; unexpected=[$unexpectedText]. Both accepted clipmaps are required; unrelated entries fail closed. Root: $streamingRoot"
  }

  [ordered]@{
    root = $streamingRoot
    entries = $entryNames
    buildIncludedEntries = @($entryNames | Where-Object { $_ -notin $localOnlyEntries })
    requiredEntries = $requiredEntries
    localOnlyEntries = $localOnlyEntries
    packageOwnedJsbsimRecoveryPresent = [bool](Test-Path -LiteralPath $ownedJsbsimDirectory -PathType Container)
  }
}

function Get-StreamingAssetInventory {
  param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string[]]$EntryNames
  )

  $records = @()
  foreach ($entryName in $EntryNames) {
    $entryPath = Join-Path $Root $entryName
    if (Test-Path -LiteralPath $entryPath -PathType Leaf) {
      $item = Get-Item -LiteralPath $entryPath -Force
      $records += [ordered]@{
        path = $entryName.Replace('\', '/')
        bytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
      }
      continue
    }
    if (-not (Test-Path -LiteralPath $entryPath -PathType Container)) {
      throw "StreamingAssets transaction inventory is missing expected entry: $entryPath"
    }
    foreach ($item in Get-ChildItem -LiteralPath $entryPath -Recurse -File -Force | Sort-Object FullName) {
      $relative = [IO.Path]::GetRelativePath($Root, $item.FullName).Replace('\', '/')
      $records += [ordered]@{
        path = $relative
        bytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
      }
    }
  }
  @($records)
}

function Write-StreamingAssetsTransactionJournal {
  param(
    [Parameter(Mandatory = $true)]$Transaction,
    [Parameter(Mandatory = $true)][string]$Phase
  )

  $Transaction.phase = $Phase
  $Transaction.updatedUtc = [DateTime]::UtcNow.ToString('O')
  $Transaction | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Transaction.journalPath -Encoding UTF8
}

function Restore-ProductStreamingAssetsTransaction {
  param([Parameter(Mandatory = $true)]$Transaction)

  foreach ($entryName in $Transaction.entryNames) {
    $sourcePath = Join-Path $Transaction.sourceRoot $entryName
    $preservedPath = Join-Path $Transaction.preservationRoot $entryName
    $sourcePresent = Test-Path -LiteralPath $sourcePath
    $preservedPresent = Test-Path -LiteralPath $preservedPath
    if ($sourcePresent -and $preservedPresent) {
      throw "StreamingAssets transaction recovery found a collision and will not overwrite either copy. source=$sourcePath preserved=$preservedPath"
    }
    if (-not $sourcePresent -and -not $preservedPresent) {
      throw "StreamingAssets transaction recovery cannot find either copy of: $entryName"
    }
    if ($preservedPresent) {
      Move-Item -LiteralPath $preservedPath -Destination $sourcePath
    }
  }

  $restoredInventory = @(Get-StreamingAssetInventory -Root $Transaction.sourceRoot -EntryNames $Transaction.entryNames)
  $expectedJson = @($Transaction.inventory) | ConvertTo-Json -Depth 6 -Compress
  $restoredJson = $restoredInventory | ConvertTo-Json -Depth 6 -Compress
  if (-not [string]::Equals($expectedJson, $restoredJson, [StringComparison]::Ordinal)) {
    throw "StreamingAssets transaction restored its entries, but the SHA-256 inventory differs. Journal: $($Transaction.journalPath)"
  }
  Write-StreamingAssetsTransactionJournal -Transaction $Transaction -Phase 'restored'
}

function Restore-InterruptedStreamingAssetsTransaction {
  param(
    [Parameter(Mandatory = $true)][string]$TransactionRoot,
    [Parameter(Mandatory = $true)][string]$ExpectedProjectPath
  )

  $journalPath = Join-Path $TransactionRoot 'transaction.json'
  if (-not (Test-Path -LiteralPath $journalPath -PathType Leaf)) { return }
  $transaction = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
  if (-not [string]::Equals($transaction.projectPath, $ExpectedProjectPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "StreamingAssets recovery journal belongs to a different project. expected=$ExpectedProjectPath actual=$($transaction.projectPath) journal=$journalPath"
  }
  if ($transaction.phase -eq 'restored') { return }
  Write-Host "Recovering interrupted StreamingAssets build transaction: $journalPath"
  Restore-ProductStreamingAssetsTransaction -Transaction $transaction
  Write-Host 'Interrupted StreamingAssets build transaction restored and verified.'
}

function Begin-ProductStreamingAssetsTransaction {
  param(
    [Parameter(Mandatory = $true)][string]$UnityProjectPath,
    [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$EntryNames,
    [Parameter(Mandatory = $true)][string]$TransactionRoot
  )

  if ($EntryNames.Count -eq 0) { return $null }
  $sourceRoot = Join-Path $UnityProjectPath 'Assets\StreamingAssets'
  $preservationRoot = Join-Path $TransactionRoot 'preserved'
  New-Item -ItemType Directory -Force -Path $TransactionRoot | Out-Null
  if (Test-Path -LiteralPath $preservationRoot) {
    $existing = @(Get-ChildItem -LiteralPath $preservationRoot -Force)
    if ($existing.Count -ne 0) {
      throw "StreamingAssets transaction preservation directory is not empty after recovery: $preservationRoot"
    }
  } else {
    New-Item -ItemType Directory -Path $preservationRoot | Out-Null
  }

  $transaction = [pscustomobject][ordered]@{
    schemaVersion = 1
    projectPath = $UnityProjectPath
    sourceRoot = $sourceRoot
    preservationRoot = $preservationRoot
    journalPath = (Join-Path $TransactionRoot 'transaction.json')
    entryNames = @($EntryNames)
    inventory = @(Get-StreamingAssetInventory -Root $sourceRoot -EntryNames $EntryNames)
    phase = 'preparing'
    updatedUtc = [DateTime]::UtcNow.ToString('O')
  }
  Write-StreamingAssetsTransactionJournal -Transaction $transaction -Phase 'preserving'
  try {
    foreach ($entryName in $EntryNames) {
      Move-Item -LiteralPath (Join-Path $sourceRoot $entryName) -Destination (Join-Path $preservationRoot $entryName)
    }
    Write-StreamingAssetsTransactionJournal -Transaction $transaction -Phase 'preserved'
    return $transaction
  } catch {
    Restore-ProductStreamingAssetsTransaction -Transaction $transaction
    throw
  }
}

$libraryIsolationPreflight = Test-ProductLibraryIsolation -UnityProjectPath $ProjectPath
Write-Host "Production Library isolation preflight: PASS ($($libraryIsolationPreflight.path))"
$projectPathPreflight = Test-ProductProjectPathLength -UnityProjectPath $ProjectPath
Write-Host "Production project-path preflight: PASS ($($projectPathPreflight.longestInputPathLength)/$($projectPathPreflight.maximumInputPathLength) characters)"
$repositoryRoot = Split-Path -Parent $ProjectPath
$defaultArtifactBase = $repositoryRoot + '-product-extraction-artifacts'
$streamingAssetsTransactionRoot = Join-Path $defaultArtifactBase '_streaming_assets_build_transaction'
Restore-InterruptedStreamingAssetsTransaction -TransactionRoot $streamingAssetsTransactionRoot -ExpectedProjectPath $ProjectPath
$streamingAssetsPreflight = Test-ProductStreamingAssets -UnityProjectPath $ProjectPath
Write-Host "Production StreamingAssets preflight: PASS (included=$($streamingAssetsPreflight.buildIncludedEntries -join ', '); local-preserved=$($streamingAssetsPreflight.localOnlyEntries -join ', '))"
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
  $ArtifactDir = Join-Path $defaultArtifactBase "product_build_$stamp"
}
New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
$ArtifactDir = (Resolve-Path -LiteralPath $ArtifactDir).Path
$payloadTool = Join-Path $repositoryRoot 'tools\verify_scenery_payload.py'
$payloadInventory = Join-Path $ArtifactDir 'scenery_payload_inventory.json'
& python $payloadTool --streaming-root (Join-Path $ProjectPath 'Assets\StreamingAssets') --inventory $payloadInventory
if ($LASTEXITCODE -ne 0) { throw 'Authored scenery payload validation failed before build.' }
$sourceHead = (& git -C $repositoryRoot rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot record build source identity.' }
& python (Join-Path $repositoryRoot 'tools\record_source_identity.py') --root $repositoryRoot --output $ArtifactDir
if ($LASTEXITCODE -ne 0) { throw 'Cannot record build source changes.' }

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
$process = $null
$streamingAssetsTransaction = $null
try {
  $streamingAssetsTransaction = Begin-ProductStreamingAssetsTransaction `
    -UnityProjectPath $ProjectPath `
    -EntryNames @($streamingAssetsPreflight.localOnlyEntries) `
    -TransactionRoot $streamingAssetsTransactionRoot
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
} finally {
  if ($null -ne $streamingAssetsTransaction) {
    Restore-ProductStreamingAssetsTransaction -Transaction $streamingAssetsTransaction
    Write-Host "Local-only StreamingAssets restored and SHA-256 verified. Journal: $($streamingAssetsTransaction.journalPath)"
  }
}

if ($null -eq $process) { throw "Unity product build did not start. Log: $log" }
$exitCode = [int]$process.ExitCode
$logText = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { '' }
if ($logText -match 'Application will terminate with return code ([1-9][0-9]*)') {
  $exitCode = [int]$Matches[1]
}
if ($exitCode -ne 0) { throw "Product Quest build failed with exit code $exitCode. Log: $log" }

$apk = Join-Path $ProjectPath 'Builds\Android\QuestFlight-production.apk'
if (-not (Test-Path -LiteralPath $apk)) { throw "Unity exited without the product APK: $apk" }
& python $payloadTool --apk $apk --inventory $payloadInventory
if ($LASTEXITCODE -ne 0) { throw 'APK does not contain the authored scenery payload; do not install it.' }
# Check the packaged player, not just the requested build options. Diagnostic
# attachment capture must not silently turn on CPU profiling or script debugging.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$apkArchive = [IO.Compression.ZipFile]::OpenRead($apk)
try {
  $bootEntry = $apkArchive.GetEntry('assets/bin/Data/boot.config')
  if ($null -eq $bootEntry) { throw 'APK boot.config is missing; cannot verify player diagnostics.' }
  $bootReader = [IO.StreamReader]::new($bootEntry.Open())
  try { $bootConfig = $bootReader.ReadToEnd() } finally { $bootReader.Dispose() }
  if ($bootConfig -match '(?m)^(profiler-enable|player-connection-debug|wait-for-managed-debugger|wait-for-native-debugger)=1\s*$') {
    throw 'APK has automatic profiling or script debugging enabled; do not install it for product acceptance.'
  }
} finally { $apkArchive.Dispose() }
$hash = Get-Sha256Hex -LiteralPath $apk
[ordered]@{
  generatedUtc = [DateTime]::UtcNow.ToString('O')
  applicationIdentifier = 'com.alexoviedo.t2.questflight'
  scene = 'Assets/Scenes/ProductionVerticalSlice.unity'
  apk = $apk
  bytes = (Get-Item -LiteralPath $apk).Length
  sha256 = $hash
  sourceHead = $sourceHead
  sourceChanges = 'source_changes.patch'
  sourceIdentity = 'source_identity.json'
  sourceChangesArchive = 'source_changes.zip'
  sceneryPayloadVerified = $true
  sceneryPayloadInventory = $payloadInventory
  development = [bool]$Development
  unattendedProfilingDisabled = $true
  nativeJsbsimRequired = $true
  unityPrototypeAllowed = $false
  projectPathPreflightPassed = $true
  libraryIsolationPreflightPassed = $true
  libraryProjectIdentity = $libraryIsolationPreflight.projectPath
  longestUnityInputPathLength = $projectPathPreflight.longestInputPathLength
  maximumUnityInputPathLength = $projectPathPreflight.maximumInputPathLength
  streamingAssetsPreflightPassed = $true
  persistentStreamingAssetEntries = @($streamingAssetsPreflight.buildIncludedEntries)
  locallyPreservedStreamingAssetEntries = @($streamingAssetsPreflight.localOnlyEntries)
  streamingAssetsTransactionJournal = if ($null -eq $streamingAssetsTransaction) { $null } else { $streamingAssetsTransaction.journalPath }
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactDir 'product_apk_manifest.json') -Encoding UTF8

Write-Host "Product APK: $apk"
Write-Host "SHA256: $hash"
Write-Host "Build evidence: $ArtifactDir"
