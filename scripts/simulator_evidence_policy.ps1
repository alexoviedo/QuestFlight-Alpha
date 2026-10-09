function Test-SimulatorSessionEvidence {
  param($Report,[int]$ExitCode,[bool]$TimedOut,[string]$RunId,[string]$PlayerSha256,[string]$SourceIdentitySha256,[string]$SessionId)
  if($TimedOut -or $ExitCode -ne 0 -or $null -eq $Report){return $false}
  foreach($field in @('passed','runId','playerSha256','sourceIdentitySha256','sessionId','errors')) {
    if($Report -is [Collections.IDictionary]) { if(-not $Report.Contains($field)){return $false} }
    elseif($null -eq $Report.PSObject.Properties[$field]) {return $false}
  }
  if($Report.passed -isnot [bool]){return $false}
  return [bool]$Report.passed -and $Report.runId -eq $RunId -and
    $Report.playerSha256 -eq $PlayerSha256 -and $Report.sourceIdentitySha256 -eq $SourceIdentitySha256 -and
    $Report.sessionId -eq $SessionId -and @($Report.errors).Count -eq 0
}
