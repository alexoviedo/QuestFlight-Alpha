[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'standalone_quest_gate_policy.ps1')

$cases = @(
  @('FAIL_MACHINE_GATE', 'FAIL_ATTEMPTED'),
  @('FAIL_INCOMPLETE_RUNTIME_EVIDENCE', 'FAIL_ATTEMPTED'),
  @('FAIL_INCOMPLETE_VRAPI_EVIDENCE', 'FAIL_ATTEMPTED'),
  @('FAIL_THREE_RUN_QUEST_GATE', 'FAIL_ATTEMPTED'),
  @('FAIL_APK_CHANGED_DURING_GATE', 'FAIL_ATTEMPTED'),
  @('MACHINE_GATE_PASS_HUMAN_WITNESS_REQUIRED', 'MACHINE_PASS_HUMAN_WITNESS_REQUIRED'),
  @('NOT_RUN_BLOCKED_HEADSET_TEMPORAL_ACCEPTANCE', 'REPORT_BLOCKED_OR_NOT_RUN'),
  @('BLOCKED_HUMAN_ACTION_REQUIRED', 'REPORT_BLOCKED_OR_NOT_RUN'),
  @('UNRECOGNIZED_CLASSIFICATION', 'REPORT_OTHER_NONPASS')
)

foreach ($case in $cases) {
  $actual = Get-StandaloneQuestTemporalClassificationDisposition -Classification $case[0]
  if ($actual -ne $case[1]) {
    throw "Classification policy mismatch for '$($case[0])': expected '$($case[1])', got '$actual'."
  }
}

Write-Host "Standalone Quest gate classification policy tests passed: $($cases.Count)/$($cases.Count)"
