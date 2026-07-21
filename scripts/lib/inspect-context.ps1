# inspect-context.ps1 - Inventory the repository's tracked Markdown context.

[CmdletBinding()]
param(
    [string]$Target,
    [switch]$Pick,
    [string]$Json
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
. (Join-Path $ScriptDir 'action-summary.ps1')
Import-Module (Join-Path $ScriptDir 'context-inventory.psm1') -Force

$Workflow = 'Show context stats'
if ($Pick) {
    Import-Module (Join-Path $ScriptDir '_menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    $RepoRoot = (Resolve-Path -LiteralPath $Target).Path
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)
}

$documents = @(Get-ContextInventory -RepoRoot $RepoRoot)
$groups = @($documents | Group-Object Kind | ForEach-Object {
    [pscustomobject]@{
        Kind            = $_.Name
        Files           = $_.Count
        Lines           = ($_.Group | Measure-Object Lines -Sum).Sum
        Bytes           = ($_.Group | Measure-Object Bytes -Sum).Sum
        EstimatedTokens = ($_.Group | Measure-Object EstimatedTokens -Sum).Sum
    }
} | Sort-Object EstimatedTokens -Descending)

function Write-Section {
    param([Parameter(Mandatory)][string]$Title)
    Write-Host ''
    $bold = [char]27 + '[1m'
    $reset = [char]27 + '[0m'
    Write-Host ("{0}== {1} =={2}" -f $bold, $Title, $reset) -ForegroundColor Cyan
}

function Format-Cell {
    param([AllowEmptyString()][string]$Text, [int]$Width, [switch]$Right)
    if ($Text.Length -gt $Width) { $Text = $Text.Substring(0, $Width - 3) + '...' }
    if ($Right) { return $Text.PadLeft($Width) }
    return $Text.PadRight($Width)
}

function Format-Number {
    param($Value)
    return '{0:N0}' -f $Value
}

function Write-SummaryRow {
    param($Kind, $Files, $Lines, $Bytes, $Tokens, [switch]$Header)
    $line = (Format-Cell $Kind 22) + '  ' +
            (Format-Cell $Files 7 -Right) + '  ' +
            (Format-Cell $Lines 10 -Right) + '  ' +
            (Format-Cell $Bytes 11 -Right) + '  ' +
            (Format-Cell $Tokens 14 -Right)
    Write-Host $line -ForegroundColor $(if ($Header) { 'DarkGray' } else { 'Gray' })
}

function Write-DocumentRow {
    param($Document)
    $line = '  ' + (Format-Cell $Document.Path 69) + '  ' +
            (Format-Cell (Format-Number $Document.Lines) 8 -Right) + '  ' +
            (Format-Cell ('{0:N1} KB' -f ($Document.Bytes / 1KB)) 11 -Right) + '  ' +
            (Format-Cell (Format-Number $Document.EstimatedTokens) 14 -Right)
    Write-Host $line
}

$totalLines = ($documents | Measure-Object Lines -Sum).Sum
$totalBytes = ($documents | Measure-Object Bytes -Sum).Sum
$totalTokens = ($documents | Measure-Object EstimatedTokens -Sum).Sum

Write-Host "Repo: $RepoRoot" -ForegroundColor DarkGray
Write-Host 'Token counts are estimates at 4 characters per token; exact counts vary by model.' -ForegroundColor DarkGray

Write-Section 'Context summary (tracked Markdown)'
Write-SummaryRow 'Document type' 'Files' 'Lines' 'Size' 'Est. tokens' -Header
Write-SummaryRow '-------------' '-----' '-----' '----' '-----------' -Header
foreach ($group in $groups) {
    Write-SummaryRow $group.Kind (Format-Number $group.Files) (Format-Number $group.Lines) `
        ('{0:N1} KB' -f ($group.Bytes / 1KB)) (Format-Number $group.EstimatedTokens)
}
Write-Host ''
Write-Host ("Context inventory total: {0:N0} document(s)  /  {1:N0} lines  /  {2:N1} KB  /  ~{3:N0} tokens" -f `
    $documents.Count, $totalLines, ($totalBytes / 1KB), $totalTokens) -ForegroundColor Cyan

Write-Section 'Document details (by type)'
foreach ($group in ($documents | Group-Object Kind | Sort-Object Name)) {
    Write-Host ''
    $heading = "--- $($group.Name) "
    Write-Host ($heading.PadRight(112, '-')) -ForegroundColor DarkCyan
    Write-Host ((Format-Cell '  Path' 71) + '  ' + (Format-Cell 'Lines' 8 -Right) + '  ' +
        (Format-Cell 'Size' 11 -Right) + '  ' + (Format-Cell 'Est. tokens' 14 -Right)) -ForegroundColor DarkGray
    foreach ($document in ($group.Group | Sort-Object EstimatedTokens -Descending)) {
        Write-DocumentRow $document
    }
}

if ($Json) {
    $jsonParent = Split-Path -Parent $Json
    if ($jsonParent -and -not (Test-Path -LiteralPath $jsonParent)) {
        New-Item -ItemType Directory -Path $jsonParent | Out-Null
    }
    [pscustomobject]@{ Groups = $groups; Documents = $documents } |
        ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $Json -Encoding UTF8
    Write-Host "`nWrote $Json" -ForegroundColor DarkGray
}

Write-DeckleActionSummary -Workflow $Workflow -Result Success `
    -Sentence "Deckle's tracked Markdown context was inventoried without modifying it." `
    -Details ([ordered]@{
        Worktree = $RepoRoot
        Documents = $documents.Count
        Lines = ('{0:N0}' -f $totalLines)
        Size = ('{0:N1} KB' -f ($totalBytes / 1KB))
        'Estimated tokens' = ('{0:N0}' -f $totalTokens)
        'JSON output' = $Json
    })
