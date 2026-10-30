# Shared `key: value` reader for Markdown front matter and script Header notes.

function Format-KeyValueBlock {
    param([AllowEmptyCollection()][System.Collections.Generic.List[string]]$Lines)

    if ($null -eq $Lines -or $Lines.Count -eq 0) { return '' }

    $minIndent = $null
    foreach ($line in $Lines) {
        if ($line.Trim() -eq '') { continue }
        $indent = ([regex]::Match($line, '^\s*')).Value.Length
        if ($null -eq $minIndent -or $indent -lt $minIndent) { $minIndent = $indent }
    }
    if ($null -eq $minIndent) { return '' }

    $normalized = foreach ($line in $Lines) {
        if ($line.Length -ge $minIndent) { $line.Substring($minIndent) } else { $line.TrimStart() }
    }
    return (($normalized -join ' ') -replace '\s+', ' ').Trim()
}

function Read-KeyValueLines {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][AllowEmptyCollection()][AllowEmptyString()][string[]]$Lines
    )

    $pairs = [System.Collections.Generic.List[object]]::new()
    if ($null -eq $Lines) { return @() }

    $index = 0
    while ($index -lt $Lines.Count) {
        $line = [string]$Lines[$index]
        if ($line -match '^([A-Za-z0-9_-]+)\s*:\s*(.*)$') {
            $key = $Matches[1].ToLowerInvariant()
            $value = $Matches[2].Trim()

            # A YAML block scalar folds every following line until the next key.
            if ($value -match '^[|>][+-]?$') {
                $blockLines = [System.Collections.Generic.List[string]]::new()
                $index++
                while ($index -lt $Lines.Count) {
                    $blockLine = [string]$Lines[$index]
                    if ($blockLine.Trim() -eq '---' -or $blockLine -match '^[A-Za-z0-9_-]+\s*:') {
                        $index--
                        break
                    }
                    $blockLines.Add($blockLine)
                    $index++
                }
                $value = Format-KeyValueBlock -Lines $blockLines
            }

            if ($value.Length -ge 2 -and
                (($value.StartsWith('"') -and $value.EndsWith('"')) -or
                 ($value.StartsWith("'") -and $value.EndsWith("'")))) {
                $value = $value.Substring(1, $value.Length - 2)
            }
            $pairs.Add([pscustomobject]@{ Key = $key; Value = $value })
        }
        $index++
    }
    return @($pairs)
}

function ConvertTo-KeyValueTable {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()][AllowEmptyCollection()][object[]]$Pairs)

    $table = @{}
    foreach ($pair in @($Pairs)) {
        if ($null -eq $pair) { continue }
        $table[$pair.Key] = $pair.Value
    }
    return $table
}
