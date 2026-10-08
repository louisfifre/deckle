# A plain comment, not a Header: this script stays out of the menu by choice.

# Reading the catalog must never run this body; the marker proves it.
$marker = Join-Path $PSScriptRoot 'no-header.executed'
Set-Content -LiteralPath $marker -Value 'executed'
