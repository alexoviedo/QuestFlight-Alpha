[CmdletBinding(SupportsShouldProcess)]
param(
  [string]$SourceRepository = 'C:\Users\ovied\Dev\T2\T2-QuestFlightLab',
  [string]$ProductRepository = '',
  [switch]$VerifyOnly,
  [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProductRepository)) {
  $ProductRepository = Split-Path $PSScriptRoot -Parent
}

$expectedCommit = 'cd4131fa7732a6db2d7387921dfef33e2c206970'
$expectedAssetHash = 'a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872'
$expectedAssetBytes = 26962216L
$expectedMetaHash = 'caf371d9b2ab80ae89d3d69a7eb5f257ebf3d17cf4eaad171084ed63384327ab'
$expectedImportedAssetsMetaHash = '78c1bd3c3d855c019bd27c7225273cc87a09bc7e67d5a20abd13171dabfb0cfa'
$expectedAircraftFolderMetaHash = '227fa9236093da64f92c691ead30e5169c1cc090bd19f636064b511b1462ee0b'
$expectedAuthor = 'KOG_THORNS (https://sketchfab.com/ioai25312)'
$expectedLicense = 'CC-BY-4.0 (http://creativecommons.org/licenses/by/4.0/)'
$expectedSource = 'https://sketchfab.com/3d-models/cessna172-d1b15841c29c43d0862667300bad55a4'

$sourceImportedRoot = Join-Path $SourceRepository 'QuestFlightLab\Assets\Resources\QuestFlightLab\ImportedAssets'
$sourceFolder = Join-Path $sourceImportedRoot 'Cessna172KogThorns'
$sourceAsset = Join-Path $sourceFolder 'cessna172.glb'
$sourceAssetMeta = "$sourceAsset.meta"
$sourceImportedAssetsMeta = "$sourceImportedRoot.meta"
$sourceFolderMeta = "$sourceFolder.meta"

$productImportedRoot = Join-Path $ProductRepository 'QuestFlight\Assets\Resources\QuestFlightLab\ImportedAssets'
$productFolder = Join-Path $productImportedRoot 'Cessna172KogThorns'
$productAsset = Join-Path $productFolder 'cessna172.glb'
$productAssetMeta = "$productAsset.meta"
$productImportedAssetsMeta = "$productImportedRoot.meta"
$productFolderMeta = "$productFolder.meta"
$productGitPath = 'QuestFlight/Assets/Resources/QuestFlightLab/ImportedAssets/Cessna172KogThorns/cessna172.glb'

function Get-LowerSha256([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "Required file is missing: $Path"
  }
  $stream = [IO.File]::OpenRead($Path)
  try {
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
      $bytes = $hasher.ComputeHash($stream)
      return ([BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
    }
    finally {
      $hasher.Dispose()
    }
  }
  finally {
    $stream.Dispose()
  }
}

function Assert-File([string]$Path, [string]$ExpectedHash, [long]$ExpectedBytes = -1L) {
  $item = Get-Item -LiteralPath $Path
  if ($ExpectedBytes -ge 0 -and $item.Length -ne $ExpectedBytes) {
    throw "Unexpected byte length for $Path. Expected $ExpectedBytes, found $($item.Length)."
  }
  $actualHash = Get-LowerSha256 $Path
  if ($actualHash -ne $ExpectedHash) {
    throw "SHA-256 mismatch for $Path. Expected $ExpectedHash, found $actualHash. This is not the audited aircraft asset."
  }
}

function Assert-EmbeddedLicense([string]$Path) {
  $bytes = [IO.File]::ReadAllBytes($Path)
  if ($bytes.Length -lt 20 -or [BitConverter]::ToUInt32($bytes, 0) -ne 0x46546C67) {
    throw "Not a GLB file: $Path"
  }
  if ([BitConverter]::ToUInt32($bytes, 4) -ne 2) {
    throw "Only the audited GLB version 2 container is accepted: $Path"
  }
  $jsonLength = [BitConverter]::ToUInt32($bytes, 12)
  if ([BitConverter]::ToUInt32($bytes, 16) -ne 0x4E4F534A -or 20L + $jsonLength -gt $bytes.Length) {
    throw "The audited GLB JSON chunk is missing or malformed: $Path"
  }
  $jsonText = [Text.Encoding]::UTF8.GetString($bytes, 20, $jsonLength).TrimEnd([char]0, [char]32)
  $gltf = $jsonText | ConvertFrom-Json
  $extras = $gltf.asset.extras
  if ($null -eq $extras) { throw "The GLB has no embedded Sketchfab provenance: $Path" }
  if ([string]$extras.author -ne $expectedAuthor -or
      [string]$extras.license -ne $expectedLicense -or
      [string]$extras.source -ne $expectedSource -or
      [string]$extras.title -ne 'Cessna172') {
    throw "The GLB embedded provenance does not match the audited KOG_THORNS CC BY 4.0 model."
  }
}

function Copy-AuditedFile([string]$Source, [string]$Destination, [string]$ExpectedHash) {
  if (Test-Path -LiteralPath $Destination) {
    $existingHash = Get-LowerSha256 $Destination
    if ($existingHash -eq $ExpectedHash) {
      Write-Host "Already verified: $Destination"
      return
    }
    if (-not $Force) {
      throw "Destination exists with a different hash: $Destination. Refusing to overwrite without -Force."
    }
  }

  if ($PSCmdlet.ShouldProcess($Destination, "Copy audited aircraft dependency from $Source")) {
    $parent = Split-Path $Destination -Parent
    if (-not (Test-Path -LiteralPath $parent)) {
      New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force:$Force
  }
}

if (-not (Test-Path -LiteralPath (Join-Path $SourceRepository '.git'))) {
  throw "SourceRepository is not the frozen R&D Git worktree: $SourceRepository"
}

$sourceHead = (& git -C $SourceRepository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceHead -ne $expectedCommit) {
  throw "R&D source must be frozen at $expectedCommit; found $sourceHead."
}

Assert-File $sourceAsset $expectedAssetHash $expectedAssetBytes
Assert-File $sourceAssetMeta $expectedMetaHash
Assert-File $sourceImportedAssetsMeta $expectedImportedAssetsMetaHash
Assert-File $sourceFolderMeta $expectedAircraftFolderMetaHash
Assert-EmbeddedLicense $sourceAsset

if ($VerifyOnly) {
  Assert-File $productAsset $expectedAssetHash $expectedAssetBytes
  Assert-File $productAssetMeta $expectedMetaHash
  Assert-File $productImportedAssetsMeta $expectedImportedAssetsMetaHash
  Assert-File $productFolderMeta $expectedAircraftFolderMetaHash
  Assert-EmbeddedLicense $productAsset
  Write-Host "Verified audited CC BY 4.0 aircraft asset: $productAsset"
  exit 0
}

$attribute = (& git -C $ProductRepository check-attr filter -- $productGitPath) -join "`n"
if ($LASTEXITCODE -ne 0 -or $attribute -notmatch ': filter: lfs$') {
  throw "Git LFS is not configured for $productGitPath. Preserve .gitattributes before syncing the binary."
}

Copy-AuditedFile $sourceImportedAssetsMeta $productImportedAssetsMeta $expectedImportedAssetsMetaHash
Copy-AuditedFile $sourceFolderMeta $productFolderMeta $expectedAircraftFolderMetaHash
Copy-AuditedFile $sourceAssetMeta $productAssetMeta $expectedMetaHash
Copy-AuditedFile $sourceAsset $productAsset $expectedAssetHash

if ($WhatIfPreference) {
  Write-Host 'WhatIf complete: source commit, hashes, embedded license metadata, and Git LFS routing are valid.'
  exit 0
}

Assert-File $productAsset $expectedAssetHash $expectedAssetBytes
Assert-File $productAssetMeta $expectedMetaHash
Assert-EmbeddedLicense $productAsset

Write-Host "Synced audited aircraft asset: $productAsset"
Write-Host 'Classification A: KOG_THORNS Cessna172, CC BY 4.0.'
Write-Host 'Attribution is recorded in THIRD_PARTY_NOTICES.md. This script does not stage or commit files.'
