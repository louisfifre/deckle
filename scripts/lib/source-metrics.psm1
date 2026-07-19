# Source metrics shared by the repository inventory and its focused tests.

Set-StrictMode -Version Latest

function Measure-CSharpEffectiveLines {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][AllowEmptyString()][string[]]$Lines)

    $effective = 0
    $inBlockComment = $false
    $rawQuoteCount = 0

    foreach ($line in $Lines) {
        $hasCode = $false
        $inString = $false
        $inVerbatimString = $false
        $inChar = $false
        $escaped = $false
        $i = 0

        while ($i -lt $line.Length) {
            if ($rawQuoteCount -gt 0) {
                $quotes = 0
                while (($i + $quotes) -lt $line.Length -and $line[$i + $quotes] -eq '"') { $quotes++ }
                if ($quotes -ge $rawQuoteCount) {
                    $rawQuoteCount = 0
                    $i += $quotes
                } else {
                    $i++
                }
                continue
            }

            if ($inBlockComment) {
                if (($i + 1) -lt $line.Length -and $line[$i] -eq '*' -and $line[$i + 1] -eq '/') {
                    $inBlockComment = $false
                    $i += 2
                } else {
                    $i++
                }
                continue
            }

            if ($inString) {
                if ($escaped) { $escaped = $false; $i++; continue }
                if ($line[$i] -eq '\') { $escaped = $true; $i++; continue }
                if ($line[$i] -eq '"') { $inString = $false }
                $i++
                continue
            }

            if ($inVerbatimString) {
                if ($line[$i] -eq '"') {
                    if (($i + 1) -lt $line.Length -and $line[$i + 1] -eq '"') { $i += 2; continue }
                    $inVerbatimString = $false
                }
                $i++
                continue
            }

            if ($inChar) {
                if ($escaped) { $escaped = $false; $i++; continue }
                if ($line[$i] -eq '\') { $escaped = $true; $i++; continue }
                if ($line[$i] -eq "'") { $inChar = $false }
                $i++
                continue
            }

            if ([char]::IsWhiteSpace($line[$i])) { $i++; continue }
            if (($i + 1) -lt $line.Length -and $line[$i] -eq '/' -and $line[$i + 1] -eq '/') { break }
            if (($i + 1) -lt $line.Length -and $line[$i] -eq '/' -and $line[$i + 1] -eq '*') {
                $inBlockComment = $true
                $i += 2
                continue
            }

            $hasCode = $true
            if ($line[$i] -eq '@' -and ($i + 1) -lt $line.Length -and $line[$i + 1] -eq '"') {
                $inVerbatimString = $true
                $i += 2
                continue
            }
            if ($line[$i] -eq '"') {
                $quotes = 1
                while (($i + $quotes) -lt $line.Length -and $line[$i + $quotes] -eq '"') { $quotes++ }
                if ($quotes -ge 3) {
                    $rawQuoteCount = $quotes
                    $i += $quotes
                } else {
                    $inString = $true
                    $i++
                }
                continue
            }
            if ($line[$i] -eq "'") { $inChar = $true }
            $i++
        }

        if ($hasCode) { $effective++ }
    }

    return $effective
}

function Measure-ReswEntries {
    param([Parameter(Mandatory)][AllowEmptyCollection()][AllowEmptyString()][string[]]$Lines)

    [xml]$document = $Lines -join "`n"
    return @($document.root.data).Count
}

Export-ModuleMember -Function Measure-CSharpEffectiveLines, Measure-ReswEntries
