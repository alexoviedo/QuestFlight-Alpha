function Get-NativeLibraryIdentity {
  param([Parameter(Mandatory)][string]$Path)
  $item = Get-Item -LiteralPath $Path -ErrorAction Stop
  [ordered]@{ path=$item.FullName; bytes=$item.Length; sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}

function Complete-NativePlatformBuild {
  param([Parameter(Mandatory)][string]$Platform,
        [Parameter(Mandatory)][string[]]$BuiltPaths,
        [Parameter(Mandatory)][string]$Destination,
        [switch]$SkipStaging)
  $built = @($BuiltPaths | ForEach-Object { Get-NativeLibraryIdentity $_ })
  $staged = @()
  if (-not $SkipStaging) {
    foreach ($library in $built) {
      $target = Join-Path $Destination ([IO.Path]::GetFileName($library.path))
      Copy-Item -LiteralPath $library.path -Destination $target -Force
      $identity = Get-NativeLibraryIdentity $target
      if ($identity.sha256 -ne $library.sha256 -or $identity.bytes -ne $library.bytes) {
        throw "Native package staging identity mismatch: $target"
      }
      $staged += $identity
    }
  }
  [ordered]@{ platform=$Platform; buildStatus='BUILT'; stagingStatus=$(if($SkipStaging){'SKIPPED'}else{'VERIFIED'}); builtLibraries=$built; stagedLibraries=$staged }
}
