<#
.SYNOPSIS
    Update the README pulse
.DESCRIPTION
    Regenerate the small "Development pulse" section in README.md from the
    local Git history. The section is bounded by invisible HTML comments so
    the visible README stays clean while automation has a stable replacement
    target. It also draws one small chart, commits per week over the last 26
    weeks, as two SVG files under docs/media (a light and a dark one, which
    the section shows through a picture element). The publication script runs
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
$ChartWeeks = 26
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

# One bar per week, Monday to Sunday, the last bar being the week that holds
# today. The accent is the logo's rest-gradient violet (logo/palettes.md):
# #6366FF on a light page, #8B5CFF on a dark one. The other marks are that
# accent thinned and GitHub's own muted greys. No script, no style block, no
# font file: system fonts only.
function Get-WeeklyCommits {
    param([string[]]$CommitDates, [int]$Weeks)

    $today = (Get-Date).Date
    $lastMonday = $today.AddDays(-(([int]$today.DayOfWeek + 6) % 7))
    $firstMonday = $lastMonday.AddDays(-7 * ($Weeks - 1))
    $counts = [int[]]::new($Weeks)
    foreach ($text in $CommitDates) {
        $day = [datetime]::ParseExact($text, 'yyyy-MM-dd', $Culture)
        $index = [int][math]::Floor(($day - $firstMonday).TotalDays / 7)
        if ($index -ge 0 -and $index -lt $Weeks) { $counts[$index]++ }
    }
    return [pscustomobject]@{ First = $firstMonday; Counts = $counts }
}

function Format-Coordinate {
    param([double]$Value)
    return $Value.ToString('0.##', $Culture)
}

function New-PulseChartSvg {
    param(
        [Parameter(Mandatory)][pscustomobject]$Weekly,
        [Parameter(Mandatory)][ValidateSet('light', 'dark')][string]$Theme
    )

    $accent = if ($Theme -eq 'dark') { '#8B5CFF' } else { '#6366FF' }
    $muted  = if ($Theme -eq 'dark') { '#9198A1' } else { '#59636E' }
    $rule   = if ($Theme -eq 'dark') { '#3D444D' } else { '#D1D9E0' }

    $counts = $Weekly.Counts
    $weeks = $counts.Count
    $peak = [int]($counts | Measure-Object -Maximum).Maximum
    $peakIndex = [array]::IndexOf($counts, $peak)
    $total = [int]($counts | Measure-Object -Sum).Sum

    $width = 720; $height = 120
    $left = 8.0; $right = 712.0
    $baseline = 86.0; $tallest = 56.0
    $slot = ($right - $left) / $weeks
    $barWidth = 8.0
    $firstLabel = $Weekly.First.ToString('d MMM yyyy', $Culture)
    $lastLabel  = $Weekly.First.AddDays(7 * ($weeks - 1)).ToString('d MMM yyyy', $Culture)
    $peakWeek   = $Weekly.First.AddDays(7 * $peakIndex).ToString('d MMM', $Culture)

    $bars = foreach ($i in 0..($weeks - 1)) {
        $count = $counts[$i]
        $barHeight = if ($count -gt 0 -and $peak -gt 0) { [math]::Max(3.0, $tallest * $count / $peak) } else { 2.0 }
        $x = $left + $slot * $i + ($slot - $barWidth) / 2
        $isPeak = ($peak -gt 0 -and $i -eq $peakIndex)
        $opacity = if ($isPeak) { '1' } elseif ($count -gt 0) { '0.45' } else { '0.18' }
        '  <rect x="{0}" y="{1}" width="{2}" height="{3}" rx="2" fill="{4}" fill-opacity="{5}"/>' -f `
            (Format-Coordinate $x), (Format-Coordinate ($baseline - $barHeight)), (Format-Coordinate $barWidth), (Format-Coordinate $barHeight), $accent, $opacity
    }

    $peakText = $null
    if ($peak -gt 0) {
        $peakX = $left + $slot * $peakIndex + $slot / 2
        $anchor = 'middle'
        if ($peakX -lt 130) { $anchor = 'start'; $peakX = $peakX - $barWidth / 2 }
        elseif ($peakX -gt $width - 130) { $anchor = 'end'; $peakX = $peakX + $barWidth / 2 }
        $peakText = '  <text x="{0}" y="{1}" text-anchor="{2}" font-size="11" font-weight="600" fill="{3}">{4} commits, week of {5}</text>' -f `
            (Format-Coordinate $peakX), (Format-Coordinate ($baseline - $tallest - 8)), $anchor, $accent, $peak, $peakWeek
    }

    $lines = @(
        ('<svg xmlns="http://www.w3.org/2000/svg" width="{0}" height="{1}" viewBox="0 0 {0} {1}" role="img" aria-labelledby="t d">' -f $width, $height),
        ('  <title id="t">Commits per week, last {0} weeks</title>' -f $weeks),
        ('  <desc id="d">{0} commits between {1} and {2}; the busiest week had {3}.</desc>' -f $total, $firstLabel, $lastLabel, $peak),
        '  <g font-family="-apple-system, BlinkMacSystemFont, ''Segoe UI'', Helvetica, Arial, sans-serif">',
        ('  <line x1="{0}" y1="{1}" x2="{2}" y2="{1}" stroke="{3}" stroke-width="1"/>' -f (Format-Coordinate $left), (Format-Coordinate ($baseline + 3)), (Format-Coordinate $right), $rule)
    ) + @($bars) + @($peakText) + @(
        ('  <text x="{0}" y="106" text-anchor="start" font-size="11" fill="{1}">{2}</text>' -f (Format-Coordinate $left), $muted, $firstLabel),
        ('  <text x="{0}" y="106" text-anchor="end" font-size="11" fill="{1}">{2}</text>' -f (Format-Coordinate $right), $muted, $lastLabel),
        '  </g>',
        '</svg>'
    )
    return ((@($lines | Where-Object { $_ }) -join "`n") + "`n")
}

Write-WorkflowStep -Output $WorkflowOutput -Message 'Draw the weekly commit chart'
$weekly = Get-WeeklyCommits -CommitDates $dates -Weeks $ChartWeeks
$chartTotal = [int]($weekly.Counts | Measure-Object -Sum).Sum
$chartPeak = [int]($weekly.Counts | Measure-Object -Maximum).Maximum
$utf8NoBomChart = [System.Text.UTF8Encoding]::new($false)
foreach ($theme in $ChartFiles.Keys) {
    $chartPath = Join-Path $RepoRoot $ChartFiles[$theme]
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $chartPath)
    $svg = New-PulseChartSvg -Weekly $weekly -Theme $theme
    if (-not (Test-Path -LiteralPath $chartPath) -or [System.IO.File]::ReadAllText($chartPath) -cne $svg) {
        [System.IO.File]::WriteAllText($chartPath, $svg, $utf8NoBomChart)
    }
}
Write-WorkflowMessage -Output $WorkflowOutput -Message ('{0} commits over {1} weeks, busiest week {2}' -f (Format-Count $chartTotal), $ChartWeeks, $chartPeak)

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
  <img alt="Commits per week over the last $ChartWeeks weeks: $(Format-Count $chartTotal) in total, the busiest week had $(Format-Count $chartPeak)." src="$($ChartFiles['light'])" width="720">
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
