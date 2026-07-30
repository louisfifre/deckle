$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'text-input.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$emptySubmission = New-MenuTextResult -Value '   '
$cancellation = New-MenuTextResult -Cancelled
$valueSubmission = New-MenuTextResult -Value '  artifacts/native  '

Assert-Equal 'Submitted' $emptySubmission.Status 'Enter on blank input remains an explicit submission'
Assert-Equal '' $emptySubmission.Value 'blank input keeps the documented default signal'
Assert-Equal 'Cancelled' $cancellation.Status 'Escape remains distinguishable from blank input'
Assert-Equal $null $cancellation.Value 'cancelled input carries no value'
Assert-Equal 'Submitted' $valueSubmission.Status 'typed input is submitted'
Assert-Equal 'artifacts/native' $valueSubmission.Value 'typed input is trimmed once'

Write-Host 'text-input.tests.ps1: PASS' -ForegroundColor Green
