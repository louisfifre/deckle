<#
.SYNOPSIS
    Validate WinUI resources
.DESCRIPTION
    Read-only audit of the WinUI resource inventories. Reports missing
    resource keys, divergences between required mirrors and module copies
    that look unused.
.NOTES
    section: Checks
    order: 410
#>

[CmdletBinding()]
param(
    [string]$Target,
    [string]$Allowlist,
    [string]$Json,
    [int]$MaxItems = 50,
    [switch]$FailOnFindings
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
Import-Module (Join-Path $LibDir 'resource-inventory.psm1') -Force

if ($Target) {
    $RepoRoot = (Resolve-Path -LiteralPath $Target).Path
} else {
    $RepoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
}
if (-not $Allowlist) { $Allowlist = Join-Path $RepoRoot 'scripts/resource-validation.allowlist.json' }

$result = Invoke-ResourceInventory -RepoRoot $RepoRoot -AllowlistPath $Allowlist

Write-Host "Repo: $RepoRoot" -ForegroundColor DarkGray
Write-Host "Resource maps: $($result.Maps) / keys: $($result.Keys) / dynamic rules: $($result.DynamicRules) / required keys: $($result.RequiredRules)" -ForegroundColor Cyan

foreach ($section in @(
    @{ Title = 'Missing resource keys'; Items = $result.Missing; Color = [ConsoleColor]::Red },
    @{ Title = 'Required mirror divergences'; Items = $result.Divergences; Color = [ConsoleColor]::Yellow },
    @{ Title = 'Potentially unused module copies'; Items = $result.PotentiallyUnused; Color = [ConsoleColor]::DarkYellow }
)) {
    Write-Host "`n== $($section.Title) ($(@($section.Items).Count)) ==" -ForegroundColor $section.Color
    $items = @($section.Items)
    foreach ($item in @($items | Select-Object -First $MaxItems)) {
        Write-Host ("  {0}  {1}" -f $item.Assembly, $item.Key)
    }
    if ($items.Count -gt $MaxItems) {
        Write-Host ("  ... {0} more; use -Json for the complete inventory" -f ($items.Count - $MaxItems)) -ForegroundColor DarkGray
    }
}

if ($Json) {
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Json -Encoding UTF8
    Write-Host "`nWrote $Json" -ForegroundColor DarkGray
}

$findingCount = @($result.Missing).Count + @($result.Divergences).Count
Write-ActionSummary -Workflow 'Validate resources' -Result Success `
    -Sentence "Deckle resource inventories were audited without modifying them." `
    -Details ([ordered]@{
        Worktree = $RepoRoot
        'Missing keys' = @($result.Missing).Count
        Divergences = @($result.Divergences).Count
        'Potentially unused' = @($result.PotentiallyUnused).Count
        Allowlist = $Allowlist
    })

if ($FailOnFindings -and $findingCount -gt 0) {
    throw "Resource validation found $findingCount blocking finding(s)."
}
