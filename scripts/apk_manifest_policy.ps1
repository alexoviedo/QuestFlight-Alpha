function Test-Quest3ManifestDump {
  param([Parameter(Mandatory)][string]$Badging,[Parameter(Mandatory)][string]$XmlTree)
  $targets = [regex]::Matches($Badging, "(?m)^targetSdkVersion:'([0-9]+)'\s*$")
  if ($targets.Count -ne 1 -or $targets[0].Groups[1].Value -ne '34') {
    throw 'Packaged Android target SDK must be exactly 34.'
  }
  $package = [regex]::Match($Badging, "(?m)^package: name='([^']+)'")
  if (-not $package.Success -or $package.Groups[1].Value -ne 'com.alexoviedo.t2.questflight') {
    throw 'Packaged application identifier differs from QuestFlight.'
  }
  $supported = [Collections.Generic.List[string]]::new()
  $lines = $XmlTree -split '\r?\n'
  for ($i=0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -notmatch '^(\s*)E: meta-data(?:\s|$)') { continue }
    $indent = $Matches[1].Length
    $name = $null; $value = $null
    for ($j=$i+1; $j -lt $lines.Count; $j++) {
      if ($lines[$j] -match '^(\s*)E:' -and $Matches[1].Length -le $indent) { break }
      if ($lines[$j] -match 'A: android:name(?:\([^)]*\))?="([^"]*)"') { $name=$Matches[1] }
      if ($lines[$j] -match 'A: android:value(?:\([^)]*\))?="([^"]*)"') { $value=$Matches[1] }
    }
    if ($name -eq 'com.oculus.supportedDevices') { $supported.Add([string]$value) }
  }
  if ($supported.Count -ne 1 -or $supported[0] -cne 'quest3') {
    throw 'Packaged supportedDevices must contain exactly one literal quest3 value.'
  }
  [ordered]@{ targetSdkVersion=34; supportedDevices=@('quest3'); applicationIdentifier=$package.Groups[1].Value }
}

function Get-Quest3ApkManifestIdentity {
  param([Parameter(Mandatory)][string]$Apk,[Parameter(Mandatory)][string]$UnityExe,[Parameter(Mandatory)][string]$ArtifactDir)
  $sdk=Join-Path (Split-Path $UnityExe -Parent) 'Data/PlaybackEngines/AndroidPlayer/SDK'
  $aapt=@(Get-ChildItem -LiteralPath (Join-Path $sdk 'build-tools') -Filter aapt.exe -File -Recurse | Sort-Object FullName -Descending | Select-Object -First 1)
  if ($aapt.Count -ne 1) { throw 'Unity Android SDK aapt is unavailable; packaged manifest verification is required.' }
  $badging=(& $aapt[0].FullName dump badging $Apk | Out-String)
  if ($LASTEXITCODE -ne 0) { throw 'aapt could not inspect packaged Android badging.' }
  $xml=(& $aapt[0].FullName dump xmltree $Apk AndroidManifest.xml | Out-String)
  if ($LASTEXITCODE -ne 0) { throw 'aapt could not inspect packaged Android manifest metadata.' }
  Set-Content -LiteralPath (Join-Path $ArtifactDir 'apk_badging.txt') -Value $badging -Encoding UTF8
  Set-Content -LiteralPath (Join-Path $ArtifactDir 'apk_manifest_tree.txt') -Value $xml -Encoding UTF8
  Test-Quest3ManifestDump -Badging $badging -XmlTree $xml
}
