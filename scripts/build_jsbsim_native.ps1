param(
  [string]$UnityRoot = 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1',
  [string]$ArtifactDir = '',
  [string]$SourceDir = '',
  [switch]$SkipWindows,
  [switch]$SkipAndroid,
  [switch]$SkipPackageStaging
)

$ErrorActionPreference = 'Stop'
$Revision = '3b25f25e49b42d0489c04ac805674fc1450ca579'
$Repository = 'https://github.com/JSBSim-Team/jsbsim.git'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$BridgeSource = Join-Path $RepoRoot 'tools\native\jsbsim_bridge'
$PackageRoot = Join-Path $RepoRoot 'QuestFlight\Packages\com.alexoviedo.questflight.jsbsim'
$DataRoot = Join-Path $PackageRoot 'RuntimeData\JSBSim-1.3.1'
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
  $ArtifactDir = "C:\Users\ovied\Dev\T2\T2-QuestFlight-product-artifacts\jsbsim_native_$(Get-Date -Format yyyyMMdd_HHmmss)"
}
$ArtifactDir = [IO.Path]::GetFullPath($ArtifactDir)
if ([string]::IsNullOrWhiteSpace($SourceDir)) { $SourceDir = Join-Path $ArtifactDir 'jsbsim-src' }
$CMake = Join-Path $UnityRoot 'Editor\Data\PlaybackEngines\AndroidPlayer\SDK\cmake\3.22.1\bin\cmake.exe'
$Ninja = Join-Path $UnityRoot 'Editor\Data\PlaybackEngines\AndroidPlayer\SDK\cmake\3.22.1\bin\ninja.exe'
$Ndk = Join-Path $UnityRoot 'Editor\Data\PlaybackEngines\AndroidPlayer\NDK'
$Toolchain = Join-Path $Ndk 'build\cmake\android.toolchain.cmake'
$ReadElf = Join-Path $Ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-readelf.exe'
$Strip = Join-Path $Ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-strip.exe'

foreach ($required in @($BridgeSource,$PackageRoot,$DataRoot,$CMake,$Ninja,$Toolchain,$ReadElf,$Strip)) {
  if (!(Test-Path $required)) { throw "Required native-build input not found: $required" }
}
New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
if (!(Test-Path (Join-Path $SourceDir '.git'))) { git clone --filter=blob:none --no-checkout $Repository $SourceDir }
git -C $SourceDir fetch --depth 1 origin $Revision
git -C $SourceDir checkout --detach $Revision
$ActualRevision = (git -C $SourceDir rev-parse HEAD).Trim()
if ($ActualRevision -ne $Revision) { throw "JSBSim revision mismatch: expected $Revision, got $ActualRevision" }
if ((git -C $SourceDir status --porcelain).Count -ne 0) { throw 'Pinned JSBSim source checkout is dirty.' }

function Import-VcVars {
  $VsWhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
  $VsPath = (& $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
  if ([string]::IsNullOrWhiteSpace($VsPath)) { throw 'Visual Studio C++ Build Tools were not found.' }
  $VcVars = Join-Path $VsPath 'VC\Auxiliary\Build\vcvars64.bat'
  foreach ($line in (cmd.exe /s /c "`"$VcVars`" >nul && set")) {
    $index = $line.IndexOf('='); if ($index -gt 0) { [Environment]::SetEnvironmentVariable($line.Substring(0,$index),$line.Substring($index+1),'Process') }
  }
}

$Results = @()
if (!$SkipWindows) {
  Import-VcVars
  $Build = Join-Path $ArtifactDir 'build-windows-x64'
  & $CMake -S $BridgeSource -B $Build -G Ninja "-DCMAKE_MAKE_PROGRAM=$Ninja" '-DCMAKE_BUILD_TYPE=Release' "-DJSBSIM_SOURCE_DIR=$SourceDir"
  if ($LASTEXITCODE) { throw "Windows CMake configure failed: $LASTEXITCODE" }
  & $CMake --build $Build --target qfl_jsbsim_native qfl_jsbsim_smoke
  if ($LASTEXITCODE) { throw "Windows native build failed: $LASTEXITCODE" }
  $Wrapper = Get-ChildItem $Build -Recurse -Filter qfl_jsbsim_native.dll | Select-Object -First 1
  $Jsbsim = Get-ChildItem $Build -Recurse -Filter JSBSim.dll | Select-Object -First 1
  $Smoke = Get-ChildItem $Build -Recurse -Filter qfl_jsbsim_smoke.exe | Select-Object -First 1
  if (!$Wrapper -or !$Jsbsim -or !$Smoke) { throw 'Windows outputs are incomplete.' }
  $OldPath=$env:PATH; $env:PATH="$($Wrapper.DirectoryName);$($Jsbsim.DirectoryName);$env:PATH"
  try { $SmokeJson = (& $Smoke.FullName $DataRoot | Select-Object -Last 1); if($LASTEXITCODE){throw "Native smoke failed: $LASTEXITCODE"} }
  finally { $env:PATH=$OldPath }
  $SmokeJson | Set-Content (Join-Path $ArtifactDir 'windows_native_smoke.json') -Encoding utf8
  if (!$SkipPackageStaging) {
    $Destination=Join-Path $PackageRoot 'Runtime\Plugins\Windows\x86_64'
    Copy-Item $Wrapper.FullName (Join-Path $Destination 'qfl_jsbsim_native.dll') -Force
    Copy-Item $Jsbsim.FullName (Join-Path $Destination 'JSBSim.dll') -Force
  }
  $Results += @{platform='windows-x86_64';smoke=($SmokeJson|ConvertFrom-Json)}
}

if (!$SkipAndroid) {
  $Build = Join-Path $ArtifactDir 'build-android-arm64'
  & $CMake -S $BridgeSource -B $Build -G Ninja "-DCMAKE_MAKE_PROGRAM=$Ninja" '-DCMAKE_BUILD_TYPE=Release' "-DJSBSIM_SOURCE_DIR=$SourceDir" "-DCMAKE_TOOLCHAIN_FILE=$Toolchain" '-DANDROID_ABI=arm64-v8a' '-DANDROID_PLATFORM=android-25' '-DANDROID_STL=c++_shared'
  if ($LASTEXITCODE) { throw "Android CMake configure failed: $LASTEXITCODE" }
  & $CMake --build $Build --target qfl_jsbsim_native
  if ($LASTEXITCODE) { throw "Android native build failed: $LASTEXITCODE" }
  $Wrapper=Get-ChildItem $Build -Recurse -Filter libqfl_jsbsim_native.so|Select-Object -First 1
  $Jsbsim=Get-ChildItem $Build -Recurse -Filter libJSBSim.so|Select-Object -First 1
  if(!$Wrapper -or !$Jsbsim){throw 'Android outputs are incomplete.'}
  & $Strip --strip-unneeded $Wrapper.FullName $Jsbsim.FullName
  foreach ($library in @($Wrapper, $Jsbsim)) {
    $programHeaders = (& $ReadElf -l $library.FullName) -join "`n"
    $loadAlignments = [regex]::Matches(
      $programHeaders,
      '(?m)^\s*LOAD\s+.*?\s+(0x[0-9a-fA-F]+)\s*$') |
      ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() }
    if ($loadAlignments.Count -eq 0 -or @($loadAlignments | Where-Object { $_ -ne '0x4000' }).Count -gt 0) {
      throw "Android native library is not 16 KiB ELF-aligned: $($library.FullName); LOAD alignments=$($loadAlignments -join ',')"
    }
  }
  (& $ReadElf -h -d -s $Wrapper.FullName) | Set-Content (Join-Path $ArtifactDir 'android_wrapper_inspection.txt') -Encoding utf8
  if (!$SkipPackageStaging) {
    $Destination=Join-Path $PackageRoot 'Runtime\Plugins\Android\arm64-v8a'
    Copy-Item $Wrapper.FullName (Join-Path $Destination 'libqfl_jsbsim_native.so') -Force
    Copy-Item $Jsbsim.FullName (Join-Path $Destination 'libJSBSim.so') -Force
  }
  $Results += @{platform='android-arm64';wrapper_bytes=$Wrapper.Length;jsbsim_bytes=$Jsbsim.Length}
}

$PluginHashes = Get-ChildItem (Join-Path $PackageRoot 'Runtime\Plugins') -Recurse -File | Where-Object {$_.Extension -in '.dll','.so'} | ForEach-Object {
  @{path=$_.FullName.Substring($RepoRoot.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
@{generated_utc=[DateTime]::UtcNow.ToString('O');status='PASS';revision=$Revision;api_version=2;results=$Results;plugins=$PluginHashes} |
  ConvertTo-Json -Depth 8 | Set-Content (Join-Path $ArtifactDir 'jsbsim_native_gate_report.json') -Encoding utf8
Write-Host "JSBSim native gate artifacts: $ArtifactDir"
