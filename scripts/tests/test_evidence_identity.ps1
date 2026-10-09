[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$scriptsRoot=Split-Path $PSScriptRoot -Parent
. (Join-Path $scriptsRoot 'simulator_evidence_policy.ps1')
. (Join-Path $scriptsRoot 'apk_manifest_policy.ps1')
function Assert($Condition,[string]$Message) { if(-not $Condition){throw $Message} }
$identity=@{RunId='current-run';PlayerSha256=('a'*64);SourceIdentitySha256=('b'*64);SessionId='standing'}
$good=@{passed=$true;runId=$identity.RunId;playerSha256=$identity.PlayerSha256;sourceIdentitySha256=$identity.SourceIdentitySha256;sessionId=$identity.SessionId;errors=@()}
Assert (Test-SimulatorSessionEvidence -Report $good -ExitCode 0 -TimedOut $false @identity) 'Matching current report failed'
foreach($process in @(@{exit=1;timeout=$false},@{exit=0;timeout=$true})) {
  Assert (-not (Test-SimulatorSessionEvidence -Report $good -ExitCode $process.exit -TimedOut $process.timeout @identity)) 'Failed/timed-out process accepted old PASS'
}
foreach($field in @('runId','playerSha256','sourceIdentitySha256','sessionId','passed','errors')) {
  $copy=$good.Clone();$copy.Remove($field)
  Assert (-not (Test-SimulatorSessionEvidence -Report $copy -ExitCode 0 -TimedOut $false @identity)) "Missing required field accepted: $field"
}
foreach($field in @('runId','playerSha256','sourceIdentitySha256','sessionId')) {
  $copy=$good.Clone();$copy[$field]='old'
  Assert (-not (Test-SimulatorSessionEvidence -Report $copy -ExitCode 0 -TimedOut $false @identity)) "Stale identity accepted: $field"
}
$copy=$good.Clone();$copy.passed='true'
Assert (-not (Test-SimulatorSessionEvidence -Report $copy -ExitCode 0 -TimedOut $false @identity)) 'String PASS accepted'
$copy=$good.Clone();$copy.errors=@('render failed')
Assert (-not (Test-SimulatorSessionEvidence -Report $copy -ExitCode 0 -TimedOut $false @identity)) 'Failed report errors accepted'
$badging="package: name='com.alexoviedo.t2.questflight' versionCode='1'`ntargetSdkVersion:'34'"
$xml=@'
E: manifest (line=1)
  E: application (line=3)
    E: meta-data (line=5)
      A: android:name(0x01010003)="com.oculus.supportedDevices" (Raw: "com.oculus.supportedDevices")
      A: android:value(0x01010024)="quest3" (Raw: "quest3")
    E: meta-data (line=6)
      A: android:name(0x01010003)="unrelated"
      A: android:value(0x01010024)="quest|quest2"
'@
$result=Test-Quest3ManifestDump -Badging $badging -XmlTree $xml
Assert ($result.targetSdkVersion -eq 34 -and $result.supportedDevices[0] -eq 'quest3') 'Current exact APK manifest rejected'
foreach($case in @(
  @{badging=$badging.Replace("'34'","'32'");xml=$xml},
  @{badging=$badging;xml=$xml.Replace('="quest3"','="quest|quest2|quest3"')},
  @{badging=$badging;xml=$xml.Replace('="quest3"','="quest3|eureka"')},
  @{badging=$badging;xml=$xml.Replace('com.oculus.supportedDevices','other')},
  @{badging=$badging.Replace('com.alexoviedo.t2.questflight','other.app');xml=$xml},
  @{badging=$badging;xml=($xml+"`n    E: meta-data`n      A: android:name=`"com.oculus.supportedDevices`"`n      A: android:value=`"quest3`"")}
)) {
  $rejected=$false
  try {Test-Quest3ManifestDump -Badging $case.badging -XmlTree $case.xml | Out-Null} catch {$rejected=$true}
  Assert $rejected 'Invalid APK target, package, devices or duplicate metadata accepted'
}
Write-Output 'PASS: current simulator identity, stale/failed/timeout/missing controls; APK exact Quest3 SDK34 plus legacy SDK/devices/duplicates negative controls.'
