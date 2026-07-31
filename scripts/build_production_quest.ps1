[CmdletBinding()]
param(
  [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe',
  [string]$ProjectPath = 'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight',
  [string]$ArtifactDir = '',
  [ValidateRange(60, 3600)]
  [int]$TimeoutSeconds = 1200,
  [switch]$Development
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $UnityExe)) { throw "Unity not found: $UnityExe" }
if (-not (Test-Path -LiteralPath $ProjectPath)) { throw "Project not found: $ProjectPath" }

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
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactDir 'product_apk_manifest.json') -Encoding UTF8

Write-Host "Product APK: $apk"
Write-Host "SHA256: $hash"
Write-Host "Build evidence: $ArtifactDir"
