function Get-VisualQaConfigurationSha256 {
  param([Parameter(Mandatory)][string]$ProjectPath)
  $inputs = @('ProjectSettings/ProjectSettings.asset','ProjectSettings/QualitySettings.asset','ProjectSettings/GraphicsSettings.asset','Assets/XR/Settings/OpenXRPackageSettings.asset')
  $lines = foreach($name in $inputs) {
    $path=Join-Path $ProjectPath $name
    if(-not (Test-Path -LiteralPath $path -PathType Leaf)){throw "Visual QA configuration input missing: $name"}
    $name + ':' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
  }
  $hash=[Security.Cryptography.SHA256]::Create()
  try { ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))))).Replace('-','').ToLowerInvariant() }
  finally {$hash.Dispose()}
}

function Get-VisualQaSourceSha256 {
  param([Parameter(Mandatory)][string]$ProjectPath,[Parameter(Mandatory)][string]$ArtifactDir)
  $repoRoot=Split-Path $ProjectPath -Parent
  $output=Join-Path $ArtifactDir 'source_identity'
  & python (Join-Path $repoRoot 'tools/record_source_identity.py') --root $repoRoot --output $output
  if($LASTEXITCODE -ne 0){throw 'Cannot bind Visual QA to current source inputs.'}
  (Get-FileHash -LiteralPath (Join-Path $output 'source_identity.json') -Algorithm SHA256).Hash.ToLowerInvariant()
}
