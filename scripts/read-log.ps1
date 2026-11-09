<#
.SYNOPSIS
    Read Deckle's log
.DESCRIPTION
    Prints the latest entries of Deckle's diagnostics log, one line each:
    time, level, provider and event, message. log.jsonl keeps every warning,
    error and milestone; -Trace reads trace.jsonl, the fuller operational
    stream. Filters narrow by level, age and text, so a reader never needs a
    hand-written JSON pipeline. Read-only.
.NOTES
    section: Run
    order: 150
    action: Inspect / log |
    action: Inspect / trace | -Trace
#>

[CmdletBinding()]
param(
    # Read trace.jsonl instead of log.jsonl.
    [switch]$Trace,
    # How many of the latest matching entries to print.
    [ValidateRange(1, 100000)]
    [int]$Last = 40,
    # Lowest level kept: Error keeps Critical and Error, Warning adds warnings.
    # Checked below rather than by ValidateSet, which the menu would read as
    # Variants to choose from.
    [string]$Level = 'Verbose',
    # Keep entries newer than this many minutes.
    [ValidateRange(0, 525600)]
    [int]$SinceMinutes = 0,
    # Keep entries whose provider, event or message matches this regex.
    [string]$Match,
    # Print each entry's payload fields after its line.
    [switch]$Payload,
    # The data root to read; defaults to DECKLE_DATA_ROOT, else the app's
    # default under LOCALAPPDATA, the one the prod copy uses.
    [string]$DataRoot
)

$ErrorActionPreference = 'Stop'

# .NET EventLevel order: a lower rank is more severe.
$rank = @{ Critical = 1; Error = 2; Warning = 3; Informational = 4; Verbose = 5 }
if (-not $rank.ContainsKey($Level)) {
    Write-Error "-Level '$Level' is not a level. Use Critical, Error, Warning, Informational or Verbose."
    exit 1
}

if (-not $DataRoot) {
    $DataRoot = if ($env:DECKLE_DATA_ROOT) { $env:DECKLE_DATA_ROOT } else { Join-Path $env:LOCALAPPDATA 'Deckle' }
}
$fileName = if ($Trace) { 'trace.jsonl' } else { 'log.jsonl' }
$path = Join-Path $DataRoot "diagnostics\$fileName"
if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    Write-Error "No $fileName under '$DataRoot\diagnostics'. Pass -DataRoot for another data root (a self-check keeps its own)."
    exit 1
}

# The running app keeps the file open for writing: share it rather than
# lock it, or the read fails while Deckle runs.
$stream = [IO.FileStream]::new($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
$reader = [IO.StreamReader]::new($stream, [Text.UTF8Encoding]::new($false))
$lines = [System.Collections.Generic.List[string]]::new()
try {
    while ($null -ne ($line = $reader.ReadLine())) { if ($line) { $lines.Add($line) } }
} finally {
    $reader.Dispose()
}

$maxRank = $rank[$Level]
$cutoff = if ($SinceMinutes -gt 0) { (Get-Date).AddMinutes(-$SinceMinutes) } else { $null }

$kept = [System.Collections.Generic.List[object]]::new()
$unreadable = 0
foreach ($line in $lines) {
    try { $entry = $line | ConvertFrom-Json } catch { $unreadable++; continue }
    $entryRank = $rank[[string]$entry.level]
    if ($entryRank -and $entryRank -gt $maxRank) { continue }
    if ($cutoff -and $entry.timestamp -and ([datetime]$entry.timestamp).ToLocalTime() -lt $cutoff) { continue }
    if ($Match -and ("$($entry.provider) $($entry.event) $($entry.message)" -notmatch $Match)) { continue }
    $kept.Add($entry)
}

$shown = if ($kept.Count -gt $Last) { $kept.GetRange($kept.Count - $Last, $Last) } else { $kept }
$colors = @{ Critical = 'Red'; Error = 'Red'; Warning = 'Yellow'; Informational = 'Gray'; Verbose = 'DarkGray' }

Write-Host "$path — $($shown.Count) of $($kept.Count) matching entries ($($lines.Count) in the file)" -ForegroundColor DarkGray
foreach ($entry in $shown) {
    $time = if ($entry.timestamp) { ([datetime]$entry.timestamp).ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss.fff') } else { '?' }
    $message = ([string]$entry.message) -replace '\r?\n', ' | '
    $level = [string]$entry.level
    $color = if ($colors.ContainsKey($level)) { $colors[$level] } else { 'Gray' }
    Write-Host ('{0} {1,-13} {2}/{3}  {4}' -f $time, $level, $entry.provider, $entry.event, $message) -ForegroundColor $color
    if ($Payload -and $entry.payload) {
        foreach ($property in $entry.payload.PSObject.Properties) {
            Write-Host ("    {0} = {1}" -f $property.Name, $property.Value) -ForegroundColor DarkGray
        }
    }
}
if ($unreadable -gt 0) {
    Write-Host "$unreadable line(s) were not JSON and were skipped." -ForegroundColor DarkGray
}
