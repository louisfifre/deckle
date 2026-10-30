# Steps shared by the scripts of the git pack, dot-sourced by each of them.
#
# This file has no Header on purpose: the menu lists scripts a person can run,
# and this one only serves the others, so it stays hidden beside them.

# The repository a script works on. -Target wins; without it the repository
# is the one that holds the script, so an installed copy works on its host.
function Get-TargetRoot {
    [CmdletBinding()]
    param(
        [string]$Target,
        [Parameter(Mandatory)][string]$FallbackPath
    )

    $from = $FallbackPath
    if ($Target) {
        # Resolved here because git -C reads the process directory, which can
        # differ from the PowerShell location a relative -Target refers to.
        if (-not (Test-Path -LiteralPath $Target -PathType Container)) {
            throw "Target not found: $Target"
        }
        $from = (Resolve-Path -LiteralPath $Target).Path
    }
    $lines = @(& git -C $from rev-parse --show-toplevel 2>&1 | ForEach-Object { [string]$_ })
    if ($LASTEXITCODE -ne 0) {
        throw "'$from' is not inside a git repository. Pass -Target with the root of one. git said: $($lines -join ' ')"
    }
    return $lines[0].Trim()
}

# Runs git in the repository and throws a sentence when it fails, so no caller
# forgets to read the exit code.
function Invoke-Git {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$Failure
    )

    $lines = @(& git -C $Root @Arguments 2>&1 | ForEach-Object { [string]$_ })
    if ($LASTEXITCODE -ne 0) {
        $detail = if ($lines.Count -gt 0) { " git said: $($lines -join ' ')" } else { '' }
        throw "$Failure$detail"
    }
    return $lines
}

# True when the reference resolves; a missing reference is an answer here, not
# an error.
function Test-GitReference {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Reference
    )

    & git -C $Root rev-parse --verify --quiet $Reference *> $null
    return ($LASTEXITCODE -eq 0)
}
