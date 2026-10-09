[CmdletBinding()]
param(
    [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe',
    [string]$ProjectPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'QuestFlight'),
    [string]$ArtifactDir = '',
    [ValidateRange(320, 4096)] [int]$Width = 1920,
    [ValidateRange(320, 4096)] [int]$Height = 1080,
    [ValidateRange(60, 1800)] [int]$TimeoutSeconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'process_arguments.ps1')

if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $ArtifactDir = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-extraction-artifacts\aerial_scenery_qa_$stamp"
}
New-Item -ItemType Directory -Path $ArtifactDir -Force | Out-Null
$ArtifactDir = (Resolve-Path -LiteralPath $ArtifactDir).Path
$log = Join-Path $ArtifactDir 'unity_aerial_scenery_visual_qa.log'

$previousDir = $env:QFL_AERIAL_SCENERY_QA_DIR
$previousWidth = $env:QFL_AERIAL_SCENERY_QA_WIDTH
$previousHeight = $env:QFL_AERIAL_SCENERY_QA_HEIGHT
try {
    $env:QFL_AERIAL_SCENERY_QA_DIR = $ArtifactDir
    $env:QFL_AERIAL_SCENERY_QA_WIDTH = "$Width"
    $env:QFL_AERIAL_SCENERY_QA_HEIGHT = "$Height"
    $arguments = @(
        '-batchmode', '-quit',
        '-projectPath', $ProjectPath,
        '-executeMethod', 'QuestFlight.Editor.AerialSceneryVisualQaBatchRunner.RunBatch',
        '-logFile', $log
    )
    $process = Start-Process -FilePath $UnityExe -ArgumentList (ConvertTo-ProcessArgumentString $arguments) -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        throw "Aerial scenery Visual QA exceeded ${TimeoutSeconds}s and was stopped. Log: $log"
    }
    if ($process.ExitCode -ne 0) {
        throw "Aerial scenery Visual QA failed with exit code $($process.ExitCode). Log: $log"
    }
}
finally {
    $env:QFL_AERIAL_SCENERY_QA_DIR = $previousDir
    $env:QFL_AERIAL_SCENERY_QA_WIDTH = $previousWidth
    $env:QFL_AERIAL_SCENERY_QA_HEIGHT = $previousHeight
}

$report = Join-Path $ArtifactDir 'aerial_scenery_visual_qa.json'
if (-not (Test-Path -LiteralPath $report -PathType Leaf)) {
    throw "Unity exited without the aerial scenery QA report: $report"
}
$data = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
$failed = @($data.shots | Where-Object { -not $_.nonBlank })
if ($failed.Count -gt 0) {
    throw "Aerial scenery QA contains $($failed.Count) blank capture(s): $($failed.id -join ', ')"
}
Write-Host "Aerial scenery Visual QA: $report"
Write-Host "Contact sheet: $($data.contactSheetPath)"
