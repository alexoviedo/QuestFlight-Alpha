param([string]$ProjectPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'QuestFlight'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$voiceRoot = Join-Path $ProjectPath 'Assets\Production\Training\Voice'
$manifest = Get-Content -LiteralPath (Join-Path $ProjectPath 'Assets\Production\Training\VoiceScripts.json') -Raw | ConvertFrom-Json
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $synth.SelectVoice('Microsoft David Desktop')
    $synth.Rate = 0
    foreach ($clip in $manifest.clips) {
        if ($clip.id -notmatch '^[a-z0-9_]+$') { throw 'Invalid authored clip identifier' }
        $destination = Join-Path $voiceRoot ($clip.id + '.wav')
        $synth.SetOutputToWaveFile($destination)
        $synth.Speak([string]$clip.text)
        $synth.SetOutputToNull()
    }
} finally { $synth.Dispose() }
Write-Output ('Generated {0} offline narration clips.' -f $manifest.clips.Count)
