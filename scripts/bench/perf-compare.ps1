<#
.SYNOPSIS
    Compare two performance passes
.DESCRIPTION
    Reads two files written by perf-bench.ps1 and lists every metric with its change, flagging as a regression each one that got worse by more than the threshold. Every metric is "lower is better" (durations, memory, handles). A change smaller than the metric's own noise floor (50 ms, 10 MB, 25 handles, 0.2 of real time) is never flagged, whatever its percentage, so a 3 ms page that takes 5 ms is not news. Exit code: 0 no regression, 1 at least one, 2 the files cannot be compared.
.PARAMETER Before
    The earlier pass: a file, or "latest-1" for the second most recent pass of the folder.
.PARAMETER After
    The later pass: a file, or "latest" for the most recent pass of the folder. Latest by default.
.PARAMETER ThresholdPercent
    How much worse, in percent of the before value, counts as a regression. 25 by default: on one machine, two passes of the same build differ by ten to twenty percent.
.PARAMETER Folder
    Where the passes are looked for when a name is not a file. artifacts\bench\ of the checkout by default.
.NOTES
    order: 131
    action: Compare passes |
#>

[CmdletBinding()]
param(
    [string]$Before = 'latest-1',
    [string]$After = 'latest',
    [ValidateRange(1, 1000)]
    [double]$ThresholdPercent = 25,
    [string]$Folder
)

$ErrorActionPreference = 'Stop'

if (-not $Folder) { $Folder = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'artifacts\bench' }

# The smallest change worth a word, per unit.
$floors = @{ ms = 50.0; mb = 10.0; count = 25.0; ratio = 0.2 }

function Resolve-Pass([string]$Name) {
    if (Test-Path -LiteralPath $Name -PathType Leaf) { return (Resolve-Path -LiteralPath $Name).Path }
    $passes = @(Get-ChildItem -LiteralPath $Folder -Filter 'perf-*.json' -ErrorAction SilentlyContinue | Sort-Object Name)
    if ($Name -match '^latest(?:-(\d+))?$') {
        $back = if ($Matches[1]) { [int]$Matches[1] } else { 0 }
        if ($passes.Count -le $back) { throw "perf-compare: '$Name' asks for a pass the folder $Folder does not hold ($($passes.Count) there)." }
        return $passes[$passes.Count - 1 - $back].FullName
    }
    $inFolder = Join-Path $Folder $Name
    if (Test-Path -LiteralPath $inFolder -PathType Leaf) { return $inFolder }
    throw "perf-compare: '$Name' is neither a file nor a pass of $Folder."
}

# The verdict on one metric, apart from the output so a test can hand it figures.
function Get-MetricVerdict([double]$BeforeValue, [double]$AfterValue, [string]$Unit, [double]$Threshold) {
    $floor = if ($floors.ContainsKey($Unit)) { $floors[$Unit] } else { 0.0 }
    $delta = $AfterValue - $BeforeValue
    $percent = if ($BeforeValue -ne 0) { $delta / [math]::Abs($BeforeValue) * 100 } else { $null }
    $beyond = [math]::Abs($delta) -gt $floor -and ($null -eq $percent -or [math]::Abs($percent) -gt $Threshold)
    $verdict = if (-not $beyond) { 'same' } elseif ($delta -gt 0) { 'REGRESSION' } else { 'improved' }
    [pscustomobject]@{ Delta = $delta; Percent = $percent; Verdict = $verdict }
}

try {
    $beforePath = Resolve-Pass $Before
    $afterPath = Resolve-Pass $After
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 2
}
$b = Get-Content -LiteralPath $beforePath -Raw | ConvertFrom-Json
$a = Get-Content -LiteralPath $afterPath -Raw | ConvertFrom-Json

Write-Host ("before: {0}  [{1}, {2}, commit {3}{4}, {5}]" -f (Split-Path -Leaf $beforePath), $b.configuration, $b.label, $b.commit, $(if ($b.dirty) { '+' } else { '' }), $b.target)
Write-Host ("after : {0}  [{1}, {2}, commit {3}{4}, {5}]" -f (Split-Path -Leaf $afterPath), $a.configuration, $a.label, $a.commit, $(if ($a.dirty) { '+' } else { '' }), $a.target)

if ($b.configuration -ne $a.configuration -or $b.target -ne $a.target) {
    Write-Host "perf-compare: the passes do not measure the same thing (configuration or target differ)." -ForegroundColor Red
    exit 2
}
if ($b.machine.cpu -ne $a.machine.cpu) {
    Write-Host 'warning: the passes were taken on different processors.' -ForegroundColor Yellow
}

$names = [System.Collections.Generic.List[string]]::new()
foreach ($n in $b.metrics.PSObject.Properties.Name) { $names.Add($n) }
foreach ($n in $a.metrics.PSObject.Properties.Name) { if (-not $names.Contains($n)) { $names.Add($n) } }

$rows = foreach ($n in $names) {
    $bm = $b.metrics.PSObject.Properties[$n]; $am = $a.metrics.PSObject.Properties[$n]
    if (-not $bm -or -not $am) {
        [pscustomobject]@{ Metric = $n; Before = $bm.Value.value; After = $am.Value.value; Unit = ($bm ?? $am).Value.unit; Change = ''; Verdict = 'missing on one side' }
        continue
    }
    $v = Get-MetricVerdict ([double]$bm.Value.value) ([double]$am.Value.value) $bm.Value.unit $ThresholdPercent
    $change = if ($null -eq $v.Percent) { '{0:+0.##;-0.##;0}' -f $v.Delta } else { '{0:+0.##;-0.##;0} ({1:+0;-0;0}%)' -f $v.Delta, $v.Percent }
    [pscustomobject]@{ Metric = $n; Before = $bm.Value.value; After = $am.Value.value; Unit = $bm.Value.unit; Change = $change; Verdict = $v.Verdict }
}

Write-Host ''
Write-Host ('{0,-48} {1,11} {2,11}  {3,-18} {4}' -f 'metric', 'before', 'after', 'change', 'verdict')
foreach ($r in $rows) {
    $line = '{0,-48} {1,11} {2,11}  {3,-18} {4}' -f $r.Metric, $r.Before, $r.After, $r.Change, $r.Verdict
    $color = switch ($r.Verdict) { 'REGRESSION' { 'Red' } 'improved' { 'Green' } 'missing on one side' { 'Yellow' } default { $null } }
    if ($color) { Write-Host $line -ForegroundColor $color } else { Write-Host $line }
}

$regressions = @($rows | Where-Object Verdict -eq 'REGRESSION')
$improved = @($rows | Where-Object Verdict -eq 'improved')
Write-Host ''
Write-Host ("{0} metric(s): {1} regression(s), {2} improved, threshold {3}% beyond the noise floor" -f @($rows).Count, $regressions.Count, $improved.Count, $ThresholdPercent)
if ($regressions.Count -gt 0) { exit 1 }
exit 0
