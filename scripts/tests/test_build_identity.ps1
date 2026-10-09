$ErrorActionPreference = 'Stop'
$scriptsRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $scriptsRoot 'process_arguments.ps1')
. (Join-Path $scriptsRoot 'native_build_identity.ps1')
function Assert([bool]$Condition, [string]$Message) { if(-not $Condition){throw $Message} }
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('QuestFlight M01 ' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
try {
  $echoScript = Join-Path $fixtureRoot 'argument echo.py'
  Set-Content -LiteralPath $echoScript -Encoding utf8 -Value 'import json,sys; print(json.dumps(sys.argv[1:], ensure_ascii=True))'
  $values = @('C:\Quest Reviews\candidate\', 'Unicode-é-飞', 'embedded"quote', '', 'two\\slashes\\', 'filter with spaces')
  $stdout = Join-Path $fixtureRoot 'arguments.json'
  $python = (Get-Command python -ErrorAction Stop).Source
  $process = Start-Process -FilePath $python -ArgumentList (ConvertTo-ProcessArgumentString (@($echoScript) + $values)) -RedirectStandardOutput $stdout -WindowStyle Hidden -Wait -PassThru
  Assert ($process.ExitCode -eq 0) 'Argument echo process failed'
  $received = Get-Content -LiteralPath $stdout -Raw | ConvertFrom-Json
  Assert ($received.Count -eq $values.Count) 'Argument count changed'
  for($i=0;$i -lt $values.Count;$i++){Assert ($received[$i] -ceq $values[$i]) "Argument $i changed"}
  $flightWrapper=Join-Path (Split-Path $scriptsRoot -Parent) 'tools/flight_dynamics/run_validation.ps1'
  $flightSource=Get-Content -LiteralPath $flightWrapper -Raw
  Assert ($flightSource.Contains('-ArgumentList (ConvertTo-ProcessArgumentString $arguments)')) 'Flight evidence wrapper bypasses shared argv serialization'
  $flightArgs=@('-batchmode','-quit','-projectPath',(Join-Path $fixtureRoot 'selected checkout\QuestFlight'),'-executeMethod','QuestFlight.Editor.FlightDynamicsValidation.FlightDynamicsEvidenceBatch.RunBatch','-logFile',(Join-Path $fixtureRoot 'evidence folder\unity flight.log'))
  $process=Start-Process -FilePath $python -ArgumentList (ConvertTo-ProcessArgumentString (@($echoScript)+$flightArgs)) -RedirectStandardOutput $stdout -WindowStyle Hidden -Wait -PassThru
  Assert ($process.ExitCode -eq 0) 'Flight evidence argv fixture failed'
  $received=Get-Content -LiteralPath $stdout -Raw | ConvertFrom-Json
  Assert ($received.Count -eq $flightArgs.Count) 'Flight evidence arguments split'
  for($i=0;$i -lt $flightArgs.Count;$i++){Assert ($received[$i] -ceq $flightArgs[$i]) "Flight evidence argument $i changed"}
  # Negative control reproduces the original Start-Process array serialization.
  $process = Start-Process -FilePath $python -ArgumentList (@('"'+$echoScript+'"') + @($values | Where-Object { $_ -ne '' })) -RedirectStandardOutput $stdout -WindowStyle Hidden -Wait -PassThru
  $received = Get-Content -LiteralPath $stdout -Raw | ConvertFrom-Json
  Assert (($received | ConvertTo-Json -Compress) -cne ($values | ConvertTo-Json -Compress)) 'Original unquoted argument bug was not reproduced'

  foreach($checkout in @('first checkout','second checkout')) {
    $root=Join-Path $fixtureRoot $checkout
    $scriptDir=Join-Path $root 'scripts'
    $settings=Join-Path $root 'QuestFlight\ProjectSettings\ProjectSettings.asset'
    New-Item -ItemType Directory -Path $scriptDir,(Split-Path $settings -Parent) -Force | Out-Null
    Set-Content -LiteralPath $settings -NoNewline -Value $checkout
    $settingsHash=(Get-FileHash -LiteralPath $settings).Hash
    Copy-Item -LiteralPath (Join-Path $scriptsRoot 'build_meta_release.ps1') -Destination $scriptDir
    $child=@'
param($ProjectPath,$UnityExe,$ArtifactDir,$TimeoutSeconds)
@{project=$ProjectPath;unity=$UnityExe;artifact=$ArtifactDir} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactDir 'binding.json')
Set-Content -LiteralPath (Join-Path $ProjectPath 'ProjectSettings\ProjectSettings.asset') -Value 'mock child changed settings'
'@
    Set-Content -LiteralPath (Join-Path $scriptDir 'build_production_quest.ps1') -Value $child
    $key=Join-Path $root 'dummy.key';Set-Content -LiteralPath $key -Value 'fixture only, not a keystore'
    $config=Join-Path $root 'signing.json'
    @{keystorePath=$key;storePassword='fixture';keyAlias='fixture';keyPassword='fixture'} | ConvertTo-Json | Set-Content -LiteralPath $config
    $artifacts=Join-Path $root 'artifacts';New-Item -ItemType Directory -Path $artifacts | Out-Null
    & (Join-Path $scriptDir 'build_meta_release.ps1') -SigningConfig $config -UnityExe $python -ArtifactDir $artifacts
    $binding=Get-Content -LiteralPath (Join-Path $artifacts 'binding.json') -Raw | ConvertFrom-Json
    Assert ($binding.project -eq (Join-Path $root 'QuestFlight')) 'Release selected wrong fixture checkout'
    Assert ($binding.unity -eq $python) 'Release did not forward editor'
    Assert ((Get-FileHash -LiteralPath $settings).Hash -eq $settingsHash) 'Release did not restore selected settings'
    Add-Content -LiteralPath (Join-Path $scriptDir 'build_production_quest.ps1') -Value "throw 'fixture failure'"
    $rejected=$false
    try { & (Join-Path $scriptDir 'build_meta_release.ps1') -SigningConfig $config -UnityExe $python -ArtifactDir $artifacts } catch { $rejected=$true }
    Assert $rejected 'Mock child failure was swallowed'
    Assert ((Get-FileHash -LiteralPath $settings).Hash -eq $settingsHash) 'Failed child did not restore settings'
  }
  $built=Join-Path $fixtureRoot 'built';$staged=Join-Path $fixtureRoot 'staged'
  New-Item -ItemType Directory -Path $built,$staged | Out-Null
  $output=Join-Path $built 'native.dll';$old=Join-Path $staged 'native.dll'
  Set-Content -LiteralPath $output -Value 'fresh';Set-Content -LiteralPath $old -Value 'old'
  $result=Complete-NativePlatformBuild -Platform fixture -BuiltPaths @($output) -Destination $staged -SkipStaging
  Assert ($result.stagingStatus -eq 'SKIPPED' -and $result.stagedLibraries.Count -eq 0) 'Skipped staging claimed verification'
  Assert ($result.builtLibraries[0].sha256 -ne (Get-NativeLibraryIdentity $old).sha256) 'Built identity reused staged bytes'
  $result=Complete-NativePlatformBuild -Platform fixture -BuiltPaths @($output) -Destination $staged
  Assert ($result.stagedLibraries[0].sha256 -eq $result.builtLibraries[0].sha256) 'Staging identity mismatch'
  $rejected=$false
  try { & (Join-Path $scriptsRoot 'build_jsbsim_native.ps1') -SkipWindows -SkipAndroid } catch {$rejected=$true}
  Assert $rejected 'No-build invocation passed'
  # Exercise the actual historical-validator allowlist expression against current
  # promoted tooling and forbidden counterexamples without invoking Unity/builds.
  $tokens=$null;$parseErrors=$null
  $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $scriptsRoot 'validate_product.ps1'),[ref]$tokens,[ref]$parseErrors)
  Assert ($parseErrors.Count -eq 0) 'Historical validation script does not parse'
  $assignment=$ast.Find({param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$approvedJsbsimPromotion'},$true)
  $approvedPolicy=& ([scriptblock]::Create($assignment.Right.Extent.Text))
  foreach($path in @('scripts/build_jsbsim_native.ps1','tools/native/jsbsim_bridge/src/qfl_jsbsim_native.cpp','QuestFlight/Packages/com.alexoviedo.questflight.jsbsim/Runtime/JSBSimRuntimeData.cs','docs/evidence/NATIVE_JSBSIM_SOLE_AUTHORITY_V1_2026-08-13.md')) {
    Assert ($path -match $approvedPolicy) "Promoted native tool excluded: $path"
  }
  foreach($path in @('tools/JSBSimLegacy.cs','QuestFlight/Assets/Scripts/Flight/JSBSimPrototype.cs','scripts/build_JSBSim_unreviewed.ps1')) {
    Assert ($path -notmatch $approvedPolicy) "Unreviewed native path allowed: $path"
  }
  foreach($version in @('a','b')) {
    $evidence=Join-Path $fixtureRoot ('evidence ' + $version)
    New-Item -ItemType Directory -Path $evidence | Out-Null
    $apk=Join-Path $evidence 'historical.apk';Set-Content -LiteralPath $apk -Value ('APK fixture ' + $version)
    $head=$version * 40
    @{head=$head;changes=@()} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'source_identity.json')
    Set-Content -LiteralPath (Join-Path $evidence 'source_changes.patch') -Value ''
    Set-Content -LiteralPath (Join-Path $evidence 'source_changes.zip') -Value 'fixture archive'
    @{schema='questflight.scenery-payload.v1';files=@(@{path='assets/fixture';bytes=1;sha256=('e'*64)})} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'scenery_payload_inventory.json')
    $manifest=Join-Path $evidence 'product_apk_manifest.json'
    $sourceEvidence=@('source_identity.json','source_changes.patch','source_changes.zip','scenery_payload_inventory.json' | ForEach-Object {@{file=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $evidence $_)).Hash.ToLowerInvariant()}})
    @{sha256=(Get-FileHash -LiteralPath $apk).Hash.ToLowerInvariant();bytes=(Get-Item $apk).Length;sourceHead=$head;sourceIdentity='source_identity.json';sourceChanges='source_changes.patch';sourceChangesArchive='source_changes.zip';sceneryPayloadInventory='scenery_payload_inventory.json';sourceEvidence=$sourceEvidence} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifest
    $package=Join-Path $fixtureRoot ('package ' + $version)
    & (Join-Path $scriptsRoot 'package_tester_candidate.ps1') -Destination $package -Apk $apk -BuildManifest $manifest
    $candidate=Get-Content -LiteralPath (Join-Path $package 'candidate.json') -Raw | ConvertFrom-Json
    Assert ($candidate.sourceHead -eq $head) 'Packaging substituted current HEAD for historical build identity'
    Assert (Test-Path -LiteralPath (Join-Path $package 'source_identity.json')) 'Package omitted source evidence'
    Assert (Test-Path -LiteralPath (Join-Path $package 'scenery_payload_inventory.json')) 'Package omitted hash-bound ignored scenery evidence'
    $inventory=Join-Path $evidence 'scenery_payload_inventory.json'
    $savedInventory=[IO.File]::ReadAllBytes($inventory)
    Set-Content -LiteralPath $inventory -Value 'changed ignored scenery inventory'
    $rejected=$false
    try { & (Join-Path $scriptsRoot 'package_tester_candidate.ps1') -Destination (Join-Path $fixtureRoot 'rejected ignored inventory') -Apk $apk -BuildManifest $manifest } catch {$rejected=$true}
    Assert $rejected 'Mismatching ignored payload evidence accepted'
    [IO.File]::WriteAllBytes($inventory,$savedInventory)
    $other=Join-Path $fixtureRoot ('wrong APK ' + $version);Set-Content -LiteralPath $other -Value 'different APK bytes'
    $badDestination=Join-Path $fixtureRoot ('rejected package ' + $version)
    $rejected=$false
    try { & (Join-Path $scriptsRoot 'package_tester_candidate.ps1') -Destination $badDestination -Apk $other -BuildManifest $manifest } catch {$rejected=$true}
    Assert $rejected 'Mismatching APK/build manifest accepted'
    Assert (-not (Test-Path -LiteralPath $badDestination)) 'Mismatching APK created a partial candidate'
    @{head=('c' * 40);changes=@()} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'source_identity.json')
    $rejected=$false
    try { & (Join-Path $scriptsRoot 'package_tester_candidate.ps1') -Destination $badDestination -Apk $apk -BuildManifest $manifest } catch {$rejected=$true}
    Assert $rejected 'Contradictory source evidence accepted'
  }
  Write-Output 'PASS: argument echo/negative control, two release checkout bindings and failure restoration, native staging/skips, historical APK provenance and mismatches.'
} finally {
  $resolved=[IO.Path]::GetFullPath($fixtureRoot)
  if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture cleanup escaped temp root'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
