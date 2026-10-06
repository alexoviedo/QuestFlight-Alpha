param(
  [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe',
  [string]$ProjectPath = 'C:\Users\ovied\Dev\T2\T2-QuestFlight\QuestFlight',
  [ValidateSet('EditMode','PlayMode')]
  [string]$TestPlatform = 'EditMode',
  [string]$ArtifactDir = '',
  [int]$TimeoutSeconds = 300,
  [string]$TestFilter = ''
)

$ErrorActionPreference = 'Stop'

if (!(Test-Path $UnityExe)) { throw "Unity not found: $UnityExe" }
if (!(Test-Path $ProjectPath)) { throw "Project not found: $ProjectPath" }

if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
  $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
  $ArtifactDir = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-extraction-artifacts\product_unity_tests_$stamp"
}

New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
$unityTestPlatform = $TestPlatform.ToLowerInvariant()
$log = Join-Path $ArtifactDir "unity_${TestPlatform}_tests.log"
$results = Join-Path $ArtifactDir "unity_${TestPlatform}_test_results.xml"
$lock = Join-Path $ProjectPath 'Temp\UnityLockfile'
if (Test-Path $lock) {
  if (Get-Process Unity -ErrorAction SilentlyContinue) {
    throw "Unity is already running; refusing to remove the active project lock: $lock"
  }
  Remove-Item -LiteralPath $lock -Force
}
$args = @(
  '-batchmode',
  '-buildTarget', 'Android',
  '-projectPath', $ProjectPath,
  '-runTests',
  '-testPlatform', $unityTestPlatform,
  '-testResults', $results,
  '-logFile', $log
)

if ($TestFilter) { $args += @('-testFilter', $TestFilter) }

$process = Start-Process -FilePath $UnityExe -ArgumentList $args -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
  Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
  throw "Unity $TestPlatform tests exceeded ${TimeoutSeconds}s and were stopped. Log: $log Results: $results"
}
if ([int]$process.ExitCode -ne 0) {
  throw "Unity $TestPlatform tests failed with exit code $($process.ExitCode). Log: $log Results: $results"
}

if (!(Test-Path $results)) {
  throw "Unity $TestPlatform tests exited with code 0 but did not produce test results. Log: $log Expected results: $results"
}

[xml]$testXml = Get-Content -LiteralPath $results -Raw
if ($testXml.'test-run'.result -ne 'Passed' -or [int]$testXml.'test-run'.failed -gt 0) {
  throw "Unity $TestPlatform result is $($testXml.'test-run'.result), failed=$($testXml.'test-run'.failed). Results: $results"
}
Write-Host "Unity $TestPlatform test results: $results"
