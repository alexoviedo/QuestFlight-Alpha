function Get-StandaloneQuestTemporalClassificationDisposition {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Classification
  )

  if ($Classification -like 'FAIL_*') { return 'FAIL_ATTEMPTED' }
  if ($Classification -eq 'MACHINE_GATE_PASS_HUMAN_WITNESS_REQUIRED') {
    return 'MACHINE_PASS_HUMAN_WITNESS_REQUIRED'
  }
  if ($Classification -like 'BLOCKED_*' -or $Classification -like 'NOT_RUN_*') {
    return 'REPORT_BLOCKED_OR_NOT_RUN'
  }
  return 'REPORT_OTHER_NONPASS'
}
