#!/usr/bin/env pwsh
#Requires -Version 7
<#
.SYNOPSIS
    Reject commit messages that attribute authorship or generation to another agent.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$CommitMessagePath
)

$ErrorActionPreference = 'Stop'

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
