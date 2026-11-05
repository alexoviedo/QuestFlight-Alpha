param(
    [Parameter(Mandatory=$true)][string]$Method,
    [Parameter(Mandatory=$true)][string]$ArtifactName,
    [switch]$RecordSource,
    [switch]$QuitAfterMethod
)
$ErrorActionPreference='Stop'
$taskRepo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$taskArtifacts='C:/Users/ovied/Dev/T2/T2-QuestFlight-product-artifacts/soaring_autonomous_v2_20261013'
if($ArtifactName -notmatch '^[a-zA-Z0-9_-]+$'){throw 'Use a simple external artifact directory name.'}
$taskOutput=Join-Path $taskArtifacts $ArtifactName
if(Test-Path -LiteralPath $taskOutput){throw "Artifact directory already exists; preserve it and use a new name: $taskOutput"}
New-Item -ItemType Directory -Path $taskOutput | Out-Null
$env:QF_SOARING_QA=$taskOutput
$env:QF_SGS_GEOMETRY=Join-Path $taskArtifacts 'donor_derivative/sgs_geometry.json'
if($RecordSource){
    & python (Join-Path $taskRepo 'tools/record_source_identity.py') --root $taskRepo --output (Join-Path $taskOutput 'source')
    if($LASTEXITCODE -ne 0){throw 'Source identity failed; no capture attempted.'}
}
$taskLog=Join-Path $taskOutput 'unity.log'
$taskArgs=@('-batchmode','-buildTarget','Android','-projectPath',(Join-Path $taskRepo 'QuestFlight'),'-executeMethod',$Method,'-logFile',$taskLog)
if($QuitAfterMethod){$taskArgs+='-quit'}
. (Join-Path $taskRepo 'scripts/process_arguments.ps1')
if(Get-Process Unity -ErrorAction SilentlyContinue){throw 'Unity is already running; no second project owner started.'}
$taskProcess=Start-Process -FilePath 'C:/Program Files/Unity/Hub/Editor/6000.3.8f1/Editor/Unity.exe' -ArgumentList (ConvertTo-ProcessArgumentString $taskArgs) -WindowStyle Hidden -PassThru
$taskProcess.WaitForExit()
if($taskProcess.ExitCode -ne 0){throw "Unity stage failed $($taskProcess.ExitCode): $taskLog"}
Write-Output "Completed $Method; evidence $taskOutput"
