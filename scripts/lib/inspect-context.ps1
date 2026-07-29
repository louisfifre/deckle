# inspect-context.ps1 - Inventory the repository's tracked Markdown context.

[CmdletBinding()]
param(
    [string]$Target,
    [switch]$Pick,
    [string]$RelativePath,
    [string[]]$LoadingMode = @(),
    [string[]]$DocumentType = @(),
    [switch]$SkipActivity,
    [string]$Json,
    [switch]$PassThru
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

$documents = @(Get-ContextInventory `
    -RepoRoot $RepoRoot `
    -RelativePath $RelativePath `
    -LoadingModes $LoadingMode `
    -DocumentTypes $DocumentType `
    -IncludeActivity:(-not $SkipActivity))
$groups = @($documents | Group-Object LoadingMode | ForEach-Object {
    [pscustomobject]@{
        LoadingMode     = $_.Name
        Files           = $_.Count
        Added1Day       = @($_.Group | Where-Object Added1Day).Count
        Added7Days      = @($_.Group | Where-Object Added7Days).Count
        Added30Days     = @($_.Group | Where-Object Added30Days).Count
        Sections        = ($_.Group | Measure-Object Sections -Sum).Sum
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
    param([AllowEmptyString()][string]$Text, [int]$Width, [switch]$Right, [switch]$TruncateStart)
    if ($Text.Length -gt $Width) {
        $Text = if ($TruncateStart) {
            '...' + $Text.Substring($Text.Length - ($Width - 3))
        } else {
            $Text.Substring(0, $Width - 3) + '...'
        }
    }
    if ($Right) { return $Text.PadLeft($Width) }
    return $Text.PadRight($Width)
}

function Format-Number {
    param($Value)
    return '{0:N0}' -f $Value
}

function Get-DocumentDisplayPath {
    param($Document)

    $directory = [System.IO.Path]::GetDirectoryName($Document.Path) -replace '\\', '/'
    return $(if ($directory) { $directory } else { '.' })
}

function Write-SummaryRow {
    param($LoadingMode, $Files, $New1d, $New7d, $New30d, $Sections, $Lines, $Bytes, $Tokens, [switch]$Header)
    $line = (Format-Cell $LoadingMode 22) + '  ' +
            (Format-Cell $Files 7 -Right) + '  ' +
            (Format-Cell $New1d 6 -Right) + '  ' +
            (Format-Cell $New7d 6 -Right) + '  ' +
            (Format-Cell $New30d 7 -Right) + '  ' +
            (Format-Cell $Sections 9 -Right) + '  ' +
            (Format-Cell $Lines 7 -Right) + '  ' +
            (Format-Cell $Bytes 9 -Right) + '  ' +
            (Format-Cell $Tokens 11 -Right)
    Write-Host $line -ForegroundColor $(if ($Header) { 'DarkGray' } else { 'Gray' })
}

function Write-DocumentRow {
    param($Document)
    $displayPath = Get-DocumentDisplayPath $Document
    $line = '  ' + (Format-Cell $displayPath 32 -TruncateStart) + '  ' +
            (Format-Cell $Document.Modified 10) + '  ' +
            (Format-Cell $(if ($Document.Added1Day) { 'Yes' } else { '-' }) 6 -Right) + '  ' +
            (Format-Cell $(if ($Document.Added7Days) { 'Yes' } else { '-' }) 6 -Right) + '  ' +
            (Format-Cell $(if ($Document.Added30Days) { 'Yes' } else { '-' }) 7 -Right) + '  ' +
            (Format-Cell (Format-Number $Document.Sections) 8 -Right) + '  ' +
            (Format-Cell (Format-Number $Document.Lines) 6 -Right) + '  ' +
            (Format-Cell ('{0:N1} KB' -f ($Document.Bytes / 1KB)) 8 -Right) + '  ' +
            (Format-Cell (Format-Number $Document.EstimatedTokens) 11 -Right)
    Write-Host $line
}

$totalLines = ($documents | Measure-Object Lines -Sum).Sum
$totalSections = ($documents | Measure-Object Sections -Sum).Sum
$total1Day = @($documents | Where-Object Added1Day).Count
$total7Days = @($documents | Where-Object Added7Days).Count
$total30Days = @($documents | Where-Object Added30Days).Count
$totalBytes = ($documents | Measure-Object Bytes -Sum).Sum
$totalTokens = ($documents | Measure-Object EstimatedTokens -Sum).Sum

if ($PassThru) {
    return [pscustomobject]@{
        Worktree = $RepoRoot
        Totals = [pscustomobject]@{
            Documents       = $documents.Count
            Added1Day       = $total1Day
            Added7Days      = $total7Days
            Added30Days     = $total30Days
            Sections        = $totalSections
            Lines           = $totalLines
            Bytes           = $totalBytes
            EstimatedTokens = $totalTokens
        }
        Groups = $groups
        Documents = $documents
    }
}

Write-Host "Repo: $RepoRoot" -ForegroundColor DarkGray
Write-Host 'Token counts are estimates at 4 characters per token; exact counts vary by model.' -ForegroundColor DarkGray
Write-Host 'Sections count ATX Markdown headings (# through ######) outside fenced code blocks.' -ForegroundColor DarkGray
Write-Host 'Automatic instructions load only when their directory scope applies; the category total is not a per-request cost.' -ForegroundColor DarkGray

Write-Section 'Context summary (tracked Markdown)'
Write-SummaryRow 'Document type' 'Files' 'New 1d' 'New 7d' 'New 30d' 'Sections' 'Lines' 'Size' 'Est. tokens' -Header
Write-SummaryRow '-------------' '-----' '------' '------' '-------' '--------' '-----' '----' '-----------' -Header
foreach ($group in $groups) {
    Write-SummaryRow $group.LoadingMode (Format-Number $group.Files) (Format-Number $group.Added1Day) `
        (Format-Number $group.Added7Days) (Format-Number $group.Added30Days) `
        (Format-Number $group.Sections) (Format-Number $group.Lines) `
        ('{0:N1} KB' -f ($group.Bytes / 1KB)) (Format-Number $group.EstimatedTokens)
}
Write-Host ''
Write-Host ("Context inventory total: {0:N0} document(s)  /  new: {1:N0} in 1d, {2:N0} in 7d, {3:N0} in 30d  /  {4:N0} section(s)  /  {5:N0} lines  /  {6:N1} KB  /  ~{7:N0} tokens" -f `
    $documents.Count, $total1Day, $total7Days, $total30Days, $totalSections, $totalLines, ($totalBytes / 1KB), $totalTokens) -ForegroundColor Cyan

foreach ($loadingMode in @('Automatic instructions', 'On-demand references')) {
    Write-Section $loadingMode
    $modeDocuments = @($documents | Where-Object LoadingMode -eq $loadingMode)
    foreach ($typeGroup in ($modeDocuments | Group-Object DocumentType | Sort-Object Name)) {
        Write-Host ''
        $heading = "--- $($typeGroup.Name) "
        Write-Host ($heading.PadRight(112, '-')) -ForegroundColor DarkCyan
        Write-Host ((Format-Cell '  Path' 34) + '  ' + (Format-Cell 'Modified' 10) + '  ' +
            (Format-Cell 'New 1d' 6 -Right) + '  ' + (Format-Cell 'New 7d' 6 -Right) + '  ' +
            (Format-Cell 'New 30d' 7 -Right) + '  ' +
            (Format-Cell 'Sections' 8 -Right) + '  ' + (Format-Cell 'Lines' 6 -Right) + '  ' +
            (Format-Cell 'Size' 8 -Right) + '  ' + (Format-Cell 'Est. tokens' 11 -Right)) -ForegroundColor DarkGray
        foreach ($document in ($typeGroup.Group | Sort-Object `
            @{ Expression = 'Modified'; Descending = $true },
            @{ Expression = 'EstimatedTokens'; Descending = $true })) {
            Write-DocumentRow $document
        }
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
        'Added in last day' = $total1Day
        'Added in last 7 days' = $total7Days
        'Added in last 30 days' = $total30Days
        Sections = ('{0:N0}' -f $totalSections)
        Lines = ('{0:N0}' -f $totalLines)
        Size = ('{0:N1} KB' -f ($totalBytes / 1KB))
        'Estimated tokens' = ('{0:N0}' -f $totalTokens)
        'JSON output' = $Json
    })
