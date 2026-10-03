param(
    [string]$ProjectPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'QuestFlight'),
    [Parameter(Mandatory=$true)][string]$PythonPath,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [Parameter(Mandatory=$true)][string]$CachePath,
    [switch]$Audition,
    [switch]$Promote,
    [switch]$HeadsetCandidate
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$generator = Join-Path $repoRoot 'tools\audio\generate_kokoro_voice.py'
$arguments = @($generator, '--project', $ProjectPath, '--output', $OutputPath, '--cache', $CachePath, '--voice', 'am_michael')
if ($Audition) { $arguments += '--audition' }
else { $arguments += @('--extra-manifest', (Join-Path $repoRoot 'tools\audio\demo_voice_scripts.json')) }
if ($Promote) { $arguments += '--promote' }
if ($HeadsetCandidate) { $arguments += '--headset-candidate' }
& $PythonPath @arguments
if ($LASTEXITCODE -ne 0) { throw 'Kokoro authoring failed. Existing recordings were not silently replaced with a Windows voice.' }
