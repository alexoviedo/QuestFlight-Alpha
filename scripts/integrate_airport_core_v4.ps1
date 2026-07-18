[CmdletBinding()]
param(
    [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe',
    [string]$ArtifactDir,
    [switch]$Bake
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "QuestFlight"
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
    $ArtifactDir = Join-Path $repoRoot "airport_core_v4_integration"
}
New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null

if (-not (Test-Path -LiteralPath $UnityExe)) {
    throw "Unity Editor was not found at $UnityExe"
}
$method = if ($Bake) {
    "QuestFlight.Editor.AirportCoreV4Authoring.RunBakeBatch"
} else {
    "QuestFlight.Editor.AirportCoreV4Authoring.RunBatch"
}
$log = Join-Path $ArtifactDir "airport_core_v4_integration.log"
$arguments = @(
    '-batchmode',
    '-nographics',
    '-quit',
    '-projectPath', ('"' + $projectPath + '"'),
    '-executeMethod', $method,
    '-logFile', ('"' + $log + '"')
)
$process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "Airport Core V4 integration failed with exit code $($process.ExitCode). See $log"
}
Write-Output "Airport Core V4 serialized integration complete: $log"
