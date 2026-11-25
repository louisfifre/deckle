<#
.SYNOPSIS
    Update the README pulse
.DESCRIPTION
    Regenerate the small "Development pulse" section in README.md from the
    local Git history. The section is bounded by invisible HTML comments so
    the visible README stays clean while automation has a stable replacement
    target. It also draws one small chart, the 7-day rolling mean of commits per
    day over the last 26 weeks as a smooth curve, as two SVG files under
    docs/media (a light and a dark one, which the section shows through a
    picture element and which stretch to the README column). The publication script runs
    this one after a release; it never pushes.
.NOTES
    section: Project
    order: 210
    action: README pulse / update | -Commit
#>

[CmdletBinding()]
param(
    # Repository/worktree root. Defaults to the repo holding this script.
    [string]$Target,

    # Override the README path. Mostly useful for tests.
    [string]$ReadmePath,

    # Commit the generated root README and its chart files locally when they
    # change. Never pushes.
    [switch]$Commit
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path $ScriptDir 'lib'
. (Join-Path $LibDir 'action-summary.ps1')

$WorkflowOutput = New-WorkflowOutput -Category 'readme'

$Workflow = 'Update README pulse'
$RepoRoot = $null
$CommitCreated = $false
$CommitSubject = 'docs(readme): refresh development pulse'
$ChartDays = 182
$ChartFiles = [ordered]@{
    light = 'docs/media/pulse-commits-light.svg'
    dark  = 'docs/media/pulse-commits-dark.svg'
}
$ChartPaths = @($ChartFiles.Values)

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "README development pulse update failed before completion." `
        -Details ([ordered]@{
            Worktree = $RepoRoot
            README   = $ReadmePath
            Committed = $CommitCreated
            Error    = $_.Exception.Message
        })
    throw
}

if ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Resolve-Path -LiteralPath $Target).Path
} else {
    $RepoRoot = Split-Path -Parent $ScriptDir
}

Write-OutputText -Text "Repo: $RepoRoot" -Role Muted

if (-not $ReadmePath) {
    $ReadmePath = Join-Path $RepoRoot 'README.md'
}
Write-OutputText -Text "README: $ReadmePath" -Role Muted

if (-not (Test-Path -LiteralPath $ReadmePath -PathType Leaf)) {
    throw "README.md not found: $ReadmePath"
}

$StartMarker = '<!-- deckle-stats:start -->'
$EndMarker   = '<!-- deckle-stats:end -->'
$PulsePattern = "(?s)$([regex]::Escape($StartMarker)).*?$([regex]::Escape($EndMarker))"
$Culture     = [System.Globalization.CultureInfo]::InvariantCulture

function Format-Count {
    param([Int64]$Value)
    return $Value.ToString('N0', $Culture)
}

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Args)
    $output = & git -C $RepoRoot @Args
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Args -join ' ') failed with exit code $LASTEXITCODE"
    }
    return $output
}

function Get-ReadmeWithoutPulse {
    param([Parameter(Mandatory)][string]$Content)

    $normalized = $Content -replace "\r?\n", "`n"
    return ([regex]::Replace($normalized, $PulsePattern, '')).TrimEnd([char]10)
}

if ($Commit) {
    $rootReadmePath = [IO.Path]::GetFullPath((Join-Path $RepoRoot 'README.md'))
    $resolvedReadmePath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $ReadmePath).Path)
    if (-not $resolvedReadmePath.Equals($rootReadmePath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'README commit is available only for the root README.md. Run without -Commit for another output path.'
    }

    $stagedPaths = @(Invoke-Git diff --cached --name-only)
    if ($stagedPaths.Count -gt 0) {
        throw 'README commit stopped because staged changes already exist. Commit or unstage them, then run again.'
    }

    $otherTrackedPaths = @(Invoke-Git diff --name-only | Where-Object { $_ -cne 'README.md' -and $_ -cnotin $ChartFiles.Values })
    if ($otherTrackedPaths.Count -gt 0) {
        throw "README commit stopped because tracked changes exist outside README.md and its chart:$($otherTrackedPaths -join ', '). Commit or revert them, then run again."
    }

    $readmeDirty = @(Invoke-Git diff --name-only -- README.md).Count -gt 0
    if ($readmeDirty) {
        $headReadme = @(Invoke-Git show 'HEAD:README.md') -join "`n"
        $workingReadme = Get-Content -LiteralPath $ReadmePath -Raw
        if ((Get-ReadmeWithoutPulse -Content $workingReadme) -cne (Get-ReadmeWithoutPulse -Content $headReadme)) {
            throw 'README commit stopped because README.md has changes outside the generated development pulse. Commit or revert them, then run again.'
        }
    }
}

Write-WorkflowStep -Output $WorkflowOutput -Message 'Collect Git history'
$commitCount = [int64](Invoke-Git rev-list --count HEAD)
$dates       = @(Invoke-Git log --date=short --format=%ad HEAD)
$firstDate   = if ($dates.Count -gt 0) { $dates[-1] } else { 'n/a' }
$activeDays  = [int64](@($dates | Sort-Object -Unique).Count)
Write-WorkflowMessage -Output $WorkflowOutput -Message ("{0} commits across {1} active day(s)" -f (Format-Count $commitCount), (Format-Count $activeDays))

Write-WorkflowStep -Output $WorkflowOutput -Message 'Measure churn'
$additions = [int64]0
$deletions = [int64]0
foreach ($line in (Invoke-Git log --numstat --pretty=tformat: HEAD)) {
    if ($line -match '^(\d+)\s+(\d+)\s+.+$') {
        $additions += [int64]$matches[1]
        $deletions += [int64]$matches[2]
    }
}
$touched = $additions + $deletions
Write-WorkflowMessage -Output $WorkflowOutput -Message ("{0} added / {1} touched lines" -f (Format-Count $additions), (Format-Count $touched))

Write-WorkflowStep -Output $WorkflowOutput -Message 'Measure tracked text files'
$trackedTextLines = [int64]0
$trackedTextFiles = [int64]0
foreach ($path in (Invoke-Git ls-files)) {
    $absolute = Join-Path $RepoRoot $path
    if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) { continue }

    $bytes = [System.IO.File]::ReadAllBytes($absolute)
    if ($bytes.Length -gt 0 -and [Array]::IndexOf($bytes, [byte]0) -ge 0) {
        continue
    }

    $lines = [int64]0
    foreach ($byte in $bytes) {
        if ($byte -eq 10) { $lines++ }
    }
    if ($bytes.Length -gt 0 -and $bytes[$bytes.Length - 1] -ne 10) {
        $lines++
    }

    $trackedTextLines += $lines
    $trackedTextFiles++
}
Write-WorkflowMessage -Output $WorkflowOutput -Message ("{0} tracked text files / {1} current lines" -f (Format-Count $trackedTextFiles), (Format-Count $trackedTextLines))

# One point per day over the last 26 weeks (182 days, the last one being
# today), drawn as the 7-day rolling mean of commits per day: a smooth line
# with a soft area under it. The accent is the logo's rest-gradient violet
# (logo/palettes.md): #6366FF on a light page, #8B5CFF on a dark one. The
# drawing is an SVG because a README can only show images; it has a viewBox and
# no width or height so that it fills the README column. No script, no style
# block, no font file: system fonts only.
#
# The height is a square-root scale: one very busy stretch would otherwise
# flatten every other week to a line near the floor. Nothing is capped or
# hidden; the curve keeps its order and the peak keeps the full height.
function Get-DailyMeans {
    param([string[]]$CommitDates, [int]$Days, [int]$Window)

    $today = (Get-Date).Date
    $first = $today.AddDays(-($Days - 1))
    $pad = $Window - 1
    $daily = [int[]]::new($Days + $pad)
    foreach ($text in $CommitDates) {
        $day = [datetime]::ParseExact($text, 'yyyy-MM-dd', $Culture)
        $index = [int][math]::Floor(($day - $first).TotalDays) + $pad
        if ($index -ge 0 -and $index -lt $daily.Count) { $daily[$index]++ }
    }
    $means = [double[]]::new($Days)
    $total = 0
    foreach ($i in 0..($Days - 1)) {
        $sum = 0
        foreach ($k in 0..($Window - 1)) { $sum += $daily[$i + $k] }
        $means[$i] = $sum / $Window
        $total += $daily[$i + $pad]
    }
    return [pscustomobject]@{ First = $first; Means = $means; Total = $total }
}

function Format-Coordinate {
    param([double]$Value)
    return $Value.ToString('0.##', $Culture)
}

# Catmull-Rom through the points, written as cubic Beziers. Control points
# stay between the top and the baseline so the curve never leaves the chart.
function ConvertTo-SmoothPath {
    param([double[]]$X, [double[]]$Y, [double]$Top, [double]$Bottom)

    $n = $X.Count
    $parts = [System.Collections.Generic.List[string]]::new()
    $parts.Add(('M{0} {1}' -f (Format-Coordinate $X[0]), (Format-Coordinate $Y[0])))
    foreach ($i in 0..($n - 2)) {
        $p0 = [math]::Max(0, $i - 1); $p3 = [math]::Min($n - 1, $i + 2)
        $c1x = $X[$i] + ($X[$i + 1] - $X[$p0]) / 6
        $c1y = [math]::Min($Bottom, [math]::Max($Top, $Y[$i] + ($Y[$i + 1] - $Y[$p0]) / 6))
        $c2x = $X[$i + 1] - ($X[$p3] - $X[$i]) / 6
        $c2y = [math]::Min($Bottom, [math]::Max($Top, $Y[$i + 1] - ($Y[$p3] - $Y[$i]) / 6))
        $parts.Add(('C{0} {1} {2} {3} {4} {5}' -f (Format-Coordinate $c1x), (Format-Coordinate $c1y), (Format-Coordinate $c2x), (Format-Coordinate $c2y), (Format-Coordinate $X[$i + 1]), (Format-Coordinate $Y[$i + 1])))
    }
    return ($parts -join ' ')
}

function New-PulseChartSvg {
    param(
        [Parameter(Mandatory)][pscustomobject]$Daily,
        [Parameter(Mandatory)][ValidateSet('light', 'dark')][string]$Theme
    )

    $accent = if ($Theme -eq 'dark') { '#8B5CFF' } else { '#6366FF' }
    $muted  = if ($Theme -eq 'dark') { '#9198A1' } else { '#59636E' }
    $halo   = if ($Theme -eq 'dark') { '#0D1117' } else { '#FFFFFF' }

    $means = $Daily.Means
    $days = $means.Count
    $peak = [double]($means | Measure-Object -Maximum).Maximum
    $peakIndex = [array]::IndexOf($means, $peak)

    # About 8:1. The labels are large for the box so that they stay readable
    # when the README scales it down to a phone-sized column.
    $width = 800; $height = 100
    $left = 14.0; $right = 786.0
    $top = 32.0; $baseline = 72.0
    $fontSize = 19
    $xs = [double[]]::new($days); $ys = [double[]]::new($days)
    foreach ($i in 0..($days - 1)) {
        $xs[$i] = $left + ($right - $left) * $i / ($days - 1)
        $level = if ($peak -gt 0) { [math]::Sqrt($means[$i] / $peak) } else { 0.0 }
        $ys[$i] = $baseline - ($baseline - $top) * $level
    }
    $line = ConvertTo-SmoothPath -X $xs -Y $ys -Top $top -Bottom $baseline
    $area = '{0} L{1} {3} L{2} {3} Z' -f $line, (Format-Coordinate $xs[$days - 1]), (Format-Coordinate $xs[0]), (Format-Coordinate $baseline)

    $firstLabel = $Daily.First.ToString('d MMM yyyy', $Culture)
    $lastLabel  = $Daily.First.AddDays($days - 1).ToString('d MMM yyyy', $Culture)
    $peakDay    = $Daily.First.AddDays($peakIndex).ToString('d MMM', $Culture)
    $peakPerDay = $peak.ToString('0.#', $Culture)

    $marker = @()
    if ($peak -gt 0) {
        $anchor = 'middle'; $labelX = $xs[$peakIndex]
        if ($labelX -lt 80) { $anchor = 'start'; $labelX = $left }
        elseif ($labelX -gt $width - 80) { $anchor = 'end'; $labelX = $right }
        $marker = @(
            ('  <circle cx="{0}" cy="{1}" r="4" fill="{2}" stroke="{3}" stroke-width="2"/>' -f (Format-Coordinate $xs[$peakIndex]), (Format-Coordinate $ys[$peakIndex]), $accent, $halo),
            ('  <text x="{0}" y="{1}" text-anchor="{2}" font-size="{3}" font-weight="600" fill="{4}">{5}</text>' -f (Format-Coordinate $labelX), (Format-Coordinate ($ys[$peakIndex] - 11)), $anchor, $fontSize, $accent, $peakDay)
        )
    }

    $lines = @(
        ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {0} {1}" role="img" aria-labelledby="t d">' -f $width, $height),
        '  <title id="t">Commits per day, last 26 weeks, smoothed over 7 days</title>',
        ('  <desc id="d">{0} commits between {1} and {2}; the busiest 7 days averaged {3} a day, around {4}.</desc>' -f $Daily.Total, $firstLabel, $lastLabel, $peakPerDay, $peakDay),
        '  <defs>',
        '    <linearGradient id="fade" x1="0" y1="0" x2="0" y2="1">',
        ('      <stop offset="0" stop-color="{0}" stop-opacity="0.38"/>' -f $accent),
        ('      <stop offset="1" stop-color="{0}" stop-opacity="0"/>' -f $accent),
        '    </linearGradient>',
        '  </defs>',
        '  <g font-family="-apple-system, BlinkMacSystemFont, ''Segoe UI'', Helvetica, Arial, sans-serif">',
        ('  <path d="{0}" fill="url(#fade)" stroke="none"/>' -f $area),
        ('  <path d="{0}" fill="none" stroke="{1}" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>' -f $line, $accent)
    ) + $marker + @(
        ('  <text x="{0}" y="95" text-anchor="start" font-size="{1}" fill="{2}">{3}</text>' -f (Format-Coordinate $left), $fontSize, $muted, $firstLabel),
        ('  <text x="{0}" y="95" text-anchor="end" font-size="{1}" fill="{2}">{3}</text>' -f (Format-Coordinate $right), $fontSize, $muted, $lastLabel),
        '  </g>',
        '</svg>'
    )
    return (($lines -join "`n") + "`n")
}

Write-WorkflowStep -Output $WorkflowOutput -Message 'Draw the daily commit curve'
$daily = Get-DailyMeans -CommitDates $dates -Days $ChartDays -Window 7
$chartTotal = [int]$daily.Total
$chartPeak = ([double]($daily.Means | Measure-Object -Maximum).Maximum).ToString('0.#', $Culture)
$utf8NoBomChart = [System.Text.UTF8Encoding]::new($false)
foreach ($theme in $ChartFiles.Keys) {
    $chartPath = Join-Path $RepoRoot $ChartFiles[$theme]
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $chartPath)
    $svg = New-PulseChartSvg -Daily $daily -Theme $theme
    if (-not (Test-Path -LiteralPath $chartPath) -or [System.IO.File]::ReadAllText($chartPath) -cne $svg) {
        [System.IO.File]::WriteAllText($chartPath, $svg, $utf8NoBomChart)
    }
}
Write-WorkflowMessage -Output $WorkflowOutput -Message ('{0} commits over {1} days, busiest 7 days averaged {2} a day' -f (Format-Count $chartTotal), $ChartDays, $chartPeak)

$generatedDate = (Get-Date).ToString('yyyy-MM-dd', $Culture)

function Write-StatsSummary {
    Write-Host ""
    Write-OutputText -Text 'Development pulse' -Role Category
    Write-Host ("  First commit          : {0}" -f $firstDate)
    Write-Host ("  Commits               : {0}" -f (Format-Count $commitCount))
    Write-Host ("  Active days           : {0}" -f (Format-Count $activeDays))
    Write-Host ("  Lines added           : {0}" -f (Format-Count $additions))
    Write-Host ("  Lines touched         : {0}" -f (Format-Count $touched))
    Write-Host ("  Current tracked lines : {0}" -f (Format-Count $trackedTextLines))
    Write-Host ("  Tracked text files    : {0}" -f (Format-Count $trackedTextFiles))
    Write-Host ("  Generated on          : {0}" -f $generatedDate)
    Write-Host ""
}

Write-WorkflowStep -Output $WorkflowOutput -Message 'Render README development pulse'
$section = @"
$StartMarker
## Development pulse

| First commit | Commits | Active days | Lines added | Lines touched | Current tracked lines |
|---:|---:|---:|---:|---:|---:|
| $firstDate | $(Format-Count $commitCount) | $(Format-Count $activeDays) | $(Format-Count $additions) | $(Format-Count $touched) | $(Format-Count $trackedTextLines) |

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="$($ChartFiles['dark'])">
  <source media="(prefers-color-scheme: light)" srcset="$($ChartFiles['light'])">
  <img alt="Commits per day, smoothed over 7 days, over the last 26 weeks: $(Format-Count $chartTotal) in total, the busiest stretch averaged $chartPeak a day." src="$($ChartFiles['light'])" width="100%">
</picture>

<sub>Generated from Git history on $generatedDate. Counts include tracked text files only for the current line total.</sub>
$EndMarker
"@

$readme = Get-Content -LiteralPath $ReadmePath -Raw
if ([regex]::IsMatch($readme, $PulsePattern)) {
    $updated = [regex]::Replace($readme, $PulsePattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $section })
    Write-WorkflowMessage -Output $WorkflowOutput -Message "Existing generated section found"
} else {
    $dividerPattern = "(?m)^---\r?\n"
    $dividerMatch = [regex]::Match($readme, $dividerPattern)
    if (-not $dividerMatch.Success) {
        throw "Could not find README insertion point. Add $StartMarker / $EndMarker manually."
    }
    $updated = $readme.Insert($dividerMatch.Index, "$section`n`n")
    Write-WorkflowMessage -Output $WorkflowOutput -Message "Generated section was missing; inserted before the first README divider" -Role Warning
}

$normalizedReadme = $readme -replace "\r?\n", "`n"
$normalizedUpdated = $updated -replace "\r?\n", "`n"

if ($normalizedUpdated -ne $normalizedReadme) {
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText((Resolve-Path -LiteralPath $ReadmePath).Path, $normalizedUpdated, $utf8NoBom)
    Write-StatsSummary
    Write-WorkflowStep -Output $WorkflowOutput -Message 'Done'
    Write-WorkflowMessage -Output $WorkflowOutput -Message "README.md development pulse updated"
    $readmeChanged = $true
} else {
    Write-StatsSummary
    Write-WorkflowStep -Output $WorkflowOutput -Message 'Done'
    Write-WorkflowMessage -Output $WorkflowOutput -Message "README.md development pulse already up to date"
    $readmeChanged = $false
}

if ($Commit) {
    $readmePending = @(Invoke-Git status --porcelain -- README.md @ChartPaths).Count -gt 0
    if ($readmePending) {
        Write-WorkflowStep -Output $WorkflowOutput -Message 'Commit README development pulse'
        Invoke-Git add -- README.md @ChartPaths | Out-Null
        Invoke-Git commit -m $CommitSubject | Out-Null
        $CommitCreated = $true
        Write-WorkflowMessage -Output $WorkflowOutput -Message $CommitSubject
    } else {
        Write-WorkflowStep -Output $WorkflowOutput -Message 'Commit README development pulse'
        Write-WorkflowMessage -Output $WorkflowOutput -Message 'No commit needed; README.md already matches HEAD'
    }
}

Write-ActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence $(if ($CommitCreated) { "README development pulse was regenerated and committed." } elseif ($readmeChanged) { "README development pulse was regenerated and written." } else { "README development pulse was already up to date." }) `
    -Details ([ordered]@{
        Worktree              = $RepoRoot
        README                = $ReadmePath
        Changed               = $(if ($readmeChanged) { 'Yes' } else { 'No' })
        Committed             = $(if ($Commit) { $(if ($CommitCreated) { 'Yes' } else { 'No change' }) } else { 'Not requested' })
        Commit                = $(if ($CommitCreated) { $CommitSubject } else { $null })
        Commits               = (Format-Count $commitCount)
        'Active days'         = (Format-Count $activeDays)
        'Tracked text files'  = (Format-Count $trackedTextFiles)
        'Current text lines'  = (Format-Count $trackedTextLines)
        'Generated on'        = $generatedDate
    })
