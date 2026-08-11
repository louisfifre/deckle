#!/usr/bin/env pwsh
#Requires -Version 7
<#
.SYNOPSIS
    Enforce Deckle's sole maintainer identity and reject agent attribution markers.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$CommitMessagePath
)

$ErrorActionPreference = 'Stop'
$ExpectedName = 'Louis'
$ExpectedEmail = 'git@louisfifre.com'

function Get-GitIdentity {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('GIT_AUTHOR_IDENT', 'GIT_COMMITTER_IDENT')]
        [string]$Variable
    )

    $identityText = (& git var $Variable 2>&1) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Commit rejected: Git could not resolve $Variable. $identityText"
        exit 1
    }

    if ($identityText.Trim() -notmatch '^(?<name>.+) <(?<email>[^<>]+)> \d+ [+-]\d{4}$') {
        Write-Error "Commit rejected: Git returned an unreadable $Variable value: $identityText"
        exit 1
    }

    return [pscustomobject]@{
        Name  = $Matches['name']
        Email = $Matches['email']
    }
}

foreach ($identityVariable in @('GIT_AUTHOR_IDENT', 'GIT_COMMITTER_IDENT')) {
    $identity = Get-GitIdentity -Variable $identityVariable
    if ($identity.Name -cne $ExpectedName -or $identity.Email -cne $ExpectedEmail) {
        $role = if ($identityVariable -eq 'GIT_AUTHOR_IDENT') { 'author' } else { 'committer' }
        Write-Error "Commit rejected: Git $role identity is '$($identity.Name) <$($identity.Email)>'. Expected '$ExpectedName <$ExpectedEmail>'. Correct the effective Git identity and retry."
        exit 1
    }
}

if (-not (Test-Path -LiteralPath $CommitMessagePath -PathType Leaf)) {
    Write-Error "Commit message file not found: $CommitMessagePath"
    exit 1
}

$message = Get-Content -LiteralPath $CommitMessagePath -Raw
$forbiddenMarker = [regex]::Match(
    $message,
    '(?im)^[ \t]*(?<marker>Co-Authored-By\s*:|Generated(?:-|\s+)with\b|Generated-By\s*:|AI-Generated(?:-By)?\s*:|Assisted-By\s*:).*$'
)

if ($forbiddenMarker.Success) {
    $marker = $forbiddenMarker.Groups['marker'].Value.Trim()
    Write-Error "Commit rejected: '$marker' attribution is forbidden. Deckle commits ship under the maintainer's sole identity; remove the attribution marker and retry."
    exit 1
}

exit 0
