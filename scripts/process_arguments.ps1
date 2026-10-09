# Start-Process joins ArgumentList elements. Encode Windows argv explicitly so
# Windows PowerShell 5.1 and PowerShell 7 preserve empty values, quotes and slashes.
function ConvertTo-ProcessArgumentString {
  param([Parameter(Mandatory)][AllowEmptyCollection()][AllowEmptyString()][string[]]$Arguments)
  $encoded = foreach ($argument in $Arguments) {
    $value = [regex]::Replace($argument, '(\\*)"', '$1$1\"')
    $value = [regex]::Replace($value, '(\\+)$', '$1$1')
    '"' + $value + '"'
  }
  $encoded -join ' '
}
