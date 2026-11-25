<#
.SYNOPSIS
    Update the README pulse
.DESCRIPTION
    Regenerate the small "Development pulse" section in README.md from the
    local Git history. The section is bounded by invisible HTML comments so
    the visible README stays clean while automation has a stable replacement
    target. It also draws one small chart, the 7-day rolling mean of commits per
    day over the last 26 weeks as a smooth curve, as pictures under
    docs/media, a light and a dark one of each, which the section shows through
    picture elements: the curve, which stretches with the README column, and
    three small labels of fixed size (start date, end date, peak date) that
    scale uniformly, so nothing that must keep its shape is ever stretched. The publication script runs
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
$LabelFiles = [ordered]@{
    start = [ordered]@{ light = 'docs/media/pulse-label-start-light.svg'; dark = 'docs/media/pulse-label-start-dark.svg' }
    end   = [ordered]@{ light = 'docs/media/pulse-label-end-light.svg';   dark = 'docs/media/pulse-label-end-dark.svg' }
    peak  = [ordered]@{ light = 'docs/media/pulse-label-peak-light.svg';  dark = 'docs/media/pulse-label-peak-dark.svg' }
}
$PulseFontSize = 13
$PulseLabelHeight = 16
$ChartPaths = @($ChartFiles.Values) + @($LabelFiles.Values | ForEach-Object { $_.Values })

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

    $otherTrackedPaths = @(Invoke-Git diff --name-only | Where-Object { $_ -cne 'README.md' -and $_ -cnotin $ChartPaths })
    if ($otherTrackedPaths.Count -gt 0) {
        throw "README commit stopped because tracked changes exist outside README.md and its pictures: $($otherTrackedPaths -join ', '). Commit or revert them, then run again."
    }

    $readmeDirty = @(Invoke-Git diff --name-only -- README.md).Count -gt 0
    if ($readmeDirty) {
        # git writes the committed README as UTF-8; decode it as UTF-8 whatever
        # the console's output encoding is, then put that encoding back.
        $previousEncoding = [Console]::OutputEncoding
        try {
            [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
            $headReadme = @(Invoke-Git show 'HEAD:README.md') -join "`n"
        } finally {
            [Console]::OutputEncoding = $previousEncoding
        }
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
# drawing is an SVG because a README can only show images; it is 100% wide and
# a fixed height, with only the curve stretching. No script, no style block,
# no font file: system fonts only.
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

    $means = $Daily.Means
    $days = $means.Count
    $peak = [double]($means | Measure-Object -Maximum).Maximum
    $peakIndex = [array]::IndexOf($means, $peak)

    # The picture holds the curve, its area and the peak marker, nothing that
    # must keep its shape: it has a viewBox and no width or height, so it fills
    # the box the README gives it and stretches freely (preserveAspectRatio
    # none). The strokes keep their thickness (non-scaling-stroke). The README
    # gives it a height of 40 pixels, so one unit of y is one pixel. Room is
    # left in the box so that the stroke and the marker are never clipped: the
    # curve spans y 5 to 37 (the marker, 4 pixels of radius, needs 4 above the
    # peak) and x 12 to 788 (the same 1.5 percent each side as the labels'
    # column edge). The marker is a zero-length path with a round cap, so it
    # stays round whatever the stretch; a circle would become an ellipse. The
    # background is left transparent. The text lives in the label pictures.
    $boxWidth = 800; $boxHeight = 40
    $left = 12; $right = 788
    $curveTop = 5; $curveBottom = 37
    $xs = [double[]]::new($days); $ys = [double[]]::new($days)
    foreach ($i in 0..($days - 1)) {
        $xs[$i] = $left + ($right - $left) * $i / ($days - 1)
        $level = if ($peak -gt 0) { [math]::Sqrt($means[$i] / $peak) } else { 0.0 }
        $ys[$i] = $curveBottom - ($curveBottom - $curveTop) * $level
    }
    $line = ConvertTo-SmoothPath -X $xs -Y $ys -Top $curveTop -Bottom $curveBottom
    $area = '{0} L{1} {3} L{2} {3} Z' -f $line, (Format-Coordinate $xs[$days - 1]), (Format-Coordinate $xs[0]), $boxHeight

    $firstLabel = $Daily.First.ToString('d MMM yyyy', $Culture)
    $lastLabel  = $Daily.First.AddDays($days - 1).ToString('d MMM yyyy', $Culture)
    $peakDay    = $Daily.First.AddDays($peakIndex).ToString('d MMM', $Culture)
    $peakPerDay = $peak.ToString('0.#', $Culture)

    $marker = @()
    if ($peak -gt 0) {
        $mx = Format-Coordinate $xs[$peakIndex]; $my = Format-Coordinate $ys[$peakIndex]
        $marker = @(('  <path d="M{0} {1} L{0} {1}" fill="none" stroke="{2}" stroke-width="8" stroke-linecap="round" vector-effect="non-scaling-stroke"/>' -f $mx, $my, $accent))
    }

    $lines = @(
        ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {0} {1}" preserveAspectRatio="none" role="img" aria-labelledby="t d">' -f $boxWidth, $boxHeight),
        '  <title id="t">Commits per day, last 26 weeks, smoothed over 7 days</title>',
        ('  <desc id="d">{0} commits between {1} and {2}; the busiest 7 days averaged {3} a day, around {4}.</desc>' -f $Daily.Total, $firstLabel, $lastLabel, $peakPerDay, $peakDay),
        '  <defs>',
        '    <linearGradient id="fade" x1="0" y1="0" x2="0" y2="1">',
        ('      <stop offset="0" stop-color="{0}" stop-opacity="0.38"/>' -f $accent),
        ('      <stop offset="1" stop-color="{0}" stop-opacity="0"/>' -f $accent),
        '    </linearGradient>',
        '  </defs>',
        ('  <path d="{0}" fill="url(#fade)" stroke="none"/>' -f $area),
        ('  <path d="{0}" fill="none" stroke="{1}" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" vector-effect="non-scaling-stroke"/>' -f $line, $accent)
    ) + $marker + @('</svg>')
    return (($lines -join "`n") + "`n")
}

# One label of the pulse as a picture of fixed size: the browser scales it
# uniformly, so the text never gets squeezed. It is as wide as its text needs
# (the character count times a safe allowance per character, since the fonts
# differ from one system to the next) and the text is anchored to the side the
# label is pinned to, so any spare room falls on the side facing the middle.
function Get-PulseLabelSize {
    param([Parameter(Mandatory)][string]$Text)
    return [pscustomobject]@{ Width = [int][math]::Ceiling($Text.Length * $PulseFontSize * 0.65) + 4; Height = $PulseLabelHeight }
}

function New-PulseLabelSvg {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][ValidateSet('start', 'end', 'peak')][string]$Kind,
        [Parameter(Mandatory)][ValidateSet('light', 'dark')][string]$Theme
    )

    $accent = if ($Theme -eq 'dark') { '#8B5CFF' } else { '#6366FF' }
    $muted  = if ($Theme -eq 'dark') { '#9198A1' } else { '#59636E' }
    $size = Get-PulseLabelSize -Text $Text
    $anchor = if ($Kind -eq 'start') { 'start' } else { 'end' }
    $x = if ($Kind -eq 'start') { 0 } else { $size.Width }
    $fill = if ($Kind -eq 'peak') { $accent } else { $muted }
    $weight = if ($Kind -eq 'peak') { ' font-weight="600"' } else { '' }

    $lines = @(
        ('<svg xmlns="http://www.w3.org/2000/svg" width="{0}" height="{1}" viewBox="0 0 {0} {1}">' -f $size.Width, $size.Height),
        ('  <text x="{0}" y="12" text-anchor="{1}" font-family="-apple-system, BlinkMacSystemFont, ''Segoe UI'', Helvetica, Arial, sans-serif" font-size="{2}"{3} fill="{4}">{5}</text>' -f $x, $anchor, $PulseFontSize, $weight, $fill, $Text),
        '</svg>'
    )
    return (($lines -join "`n") + "`n")
}

function Write-GeneratedFile {
    param([string]$RelativePath, [string]$Content)
    $path = Join-Path $RepoRoot $RelativePath
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path)
    if (-not (Test-Path -LiteralPath $path) -or [System.IO.File]::ReadAllText($path) -cne $Content) {
        [System.IO.File]::WriteAllText($path, $Content, [System.Text.UTF8Encoding]::new($false))
    }
}

Write-WorkflowStep -Output $WorkflowOutput -Message 'Draw the daily commit curve'
$daily = Get-DailyMeans -CommitDates $dates -Days $ChartDays -Window 7
$chartTotal = [int]$daily.Total
$chartPeak = ([double]($daily.Means | Measure-Object -Maximum).Maximum).ToString('0.#', $Culture)
$hasPeak = [double]$chartPeak -gt 0
$startText = $daily.First.ToString('d MMM yyyy', $Culture)
$endText   = $daily.First.AddDays($ChartDays - 1).ToString('d MMM yyyy', $Culture)
$peakText  = $daily.First.AddDays([array]::IndexOf($daily.Means, [double]($daily.Means | Measure-Object -Maximum).Maximum)).ToString('d MMM', $Culture)
$labelTexts = @{ start = $startText; end = $endText; peak = $peakText }
foreach ($theme in $ChartFiles.Keys) {
    Write-GeneratedFile -RelativePath $ChartFiles[$theme] -Content (New-PulseChartSvg -Daily $daily -Theme $theme)
    foreach ($kind in $LabelFiles.Keys) {
        if ($kind -eq 'peak' -and -not $hasPeak) { continue }
        Write-GeneratedFile -RelativePath $LabelFiles[$kind][$theme] -Content (New-PulseLabelSvg -Text $labelTexts[$kind] -Kind $kind -Theme $theme)
    }
}
Write-WorkflowMessage -Output $WorkflowOutput -Message ('{0} commits over {1} days, busiest 7 days averaged {2} a day' -f (Format-Count $chartTotal), $ChartDays, $chartPeak)

function Get-PulsePicture {
    param([string]$Light, [string]$Dark, [string]$Alt, [string]$Size, [string]$Align)
    $alignAttribute = if ($Align) { " align=`"$Align`"" } else { '' }
    return @(
        '<picture>',
        "  <source media=`"(prefers-color-scheme: dark)`" srcset=`"$Dark`">",
        "  <source media=`"(prefers-color-scheme: light)`" srcset=`"$Light`">",
        "  <img alt=`"$Alt`" src=`"$Light`" $Size$alignAttribute>",
        '</picture>'
    ) -join "`n"
}

# Pictures that must keep their shape are fixed-size images floated to the side
# they are pinned to; only the curve is 100% wide. No style, no class: only
# what the README sanitizer keeps.
function Get-PulseLabelPicture {
    param([string]$Kind, [string]$Align, [string]$Alt)
    $size = Get-PulseLabelSize -Text $labelTexts[$Kind]
    return Get-PulsePicture -Light $LabelFiles[$Kind]['light'] -Dark $LabelFiles[$Kind]['dark'] -Alt $Alt -Size ('width="{0}" height="{1}"' -f $size.Width, $size.Height) -Align $Align
}

$peakBlock = if ($hasPeak) { (Get-PulseLabelPicture -Kind peak -Align right -Alt "Peak on $peakText") + "`n<br clear=`"all`">`n" } else { '' }
$curveBlock = Get-PulsePicture -Light $ChartFiles['light'] -Dark $ChartFiles['dark'] -Alt "Commits per day, smoothed over 7 days, over the last 26 weeks: $(Format-Count $chartTotal) in total, the busiest stretch averaged $chartPeak a day." -Size 'width="100%" height="40"' -Align ''
$startBlock = Get-PulseLabelPicture -Kind start -Align left -Alt "From $startText"
$endBlock = Get-PulseLabelPicture -Kind end -Align right -Alt "To $endText"

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

$peakBlock$curveBlock
$startBlock
$endBlock
<br clear="all">

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