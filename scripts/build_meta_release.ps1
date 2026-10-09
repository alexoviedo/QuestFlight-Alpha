[CmdletBinding()]
param(
  [string]$ProjectPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'QuestFlight'),
  [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe',
  [string]$SigningConfig = (Join-Path $env:USERPROFILE '.questflight\signing\meta-alpha-signing.json'),
  [string]$ArtifactDir = '',
  [ValidateRange(60, 3600)]
  [int]$TimeoutSeconds = 1200
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not (Test-Path -LiteralPath $UnityExe -PathType Leaf)) { throw "Unity not found: $UnityExe" }

if (-not (Test-Path -LiteralPath $SigningConfig)) {
  throw "QuestFlight Meta signing configuration was not found: $SigningConfig"
}

$config = Get-Content -LiteralPath $SigningConfig -Raw | ConvertFrom-Json
$required = @('keystorePath', 'storePassword', 'keyAlias', 'keyPassword')
foreach ($name in $required) {
  $property = $config.PSObject.Properties[$name]
  if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
    throw "QuestFlight Meta signing configuration is missing '$name'."
  }
}
if (-not (Test-Path -LiteralPath ([string]$config.keystorePath))) {
  throw "QuestFlight Meta release keystore was not found: $($config.keystorePath)"
}

if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
  $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
  $ArtifactDir = Join-Path ((Split-Path $ProjectPath -Parent) + '-product-artifacts') "meta_release_$stamp"
}

$projectSettingsPath = Join-Path $ProjectPath 'ProjectSettings\ProjectSettings.asset'
$projectSettingsBytes = [IO.File]::ReadAllBytes($projectSettingsPath)

$previous = @{
  Require = $env:QUESTFLIGHT_REQUIRE_RELEASE_SIGNING
  Keystore = $env:QUESTFLIGHT_ANDROID_KEYSTORE_PATH
  StorePassword = $env:QUESTFLIGHT_ANDROID_KEYSTORE_PASSWORD
  Alias = $env:QUESTFLIGHT_ANDROID_KEYALIAS
  AliasPassword = $env:QUESTFLIGHT_ANDROID_KEYALIAS_PASSWORD
}

try {
  $env:QUESTFLIGHT_REQUIRE_RELEASE_SIGNING = '1'
  $env:QUESTFLIGHT_ANDROID_KEYSTORE_PATH = [string]$config.keystorePath
  $env:QUESTFLIGHT_ANDROID_KEYSTORE_PASSWORD = [string]$config.storePassword
  $env:QUESTFLIGHT_ANDROID_KEYALIAS = [string]$config.keyAlias
  $env:QUESTFLIGHT_ANDROID_KEYALIAS_PASSWORD = [string]$config.keyPassword

  & (Join-Path $PSScriptRoot 'build_production_quest.ps1') `
    -ProjectPath $ProjectPath `
    -UnityExe $UnityExe `
    -ArtifactDir $ArtifactDir `
    -TimeoutSeconds $TimeoutSeconds
} finally {
  $env:QUESTFLIGHT_REQUIRE_RELEASE_SIGNING = $previous.Require
  $env:QUESTFLIGHT_ANDROID_KEYSTORE_PATH = $previous.Keystore
  $env:QUESTFLIGHT_ANDROID_KEYSTORE_PASSWORD = $previous.StorePassword
  $env:QUESTFLIGHT_ANDROID_KEYALIAS = $previous.Alias
  $env:QUESTFLIGHT_ANDROID_KEYALIAS_PASSWORD = $previous.AliasPassword
  [IO.File]::WriteAllBytes($projectSettingsPath, $projectSettingsBytes)
}

Write-Host "Meta release build evidence: $ArtifactDir"
