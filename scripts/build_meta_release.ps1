[CmdletBinding()]
param(
  [string]$SigningConfig = (Join-Path $env:USERPROFILE '.questflight\signing\meta-alpha-signing.json'),
  [string]$ArtifactDir = '',
  [ValidateRange(60, 3600)]
  [int]$TimeoutSeconds = 1200
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
  $ArtifactDir = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\meta_release_$stamp"
}

$projectSettingsPath = Join-Path $PSScriptRoot '..\QuestFlight\ProjectSettings\ProjectSettings.asset'
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
