<#
.SYNOPSIS
    Take a performance pass
.DESCRIPTION
    Measures the built Deckle on a separate instance with its own data root, as the self-check runs it, and writes one dated JSON file of named metrics: the time to the self-check's ready state, the first-open cost of each window, the load time of each page the self-check walks, the memory and handle count of the warm app, and a dictation take on a test audio file, phase by phase as the engine logs them (PipelineTimings). Takes the already-built Deckle.exe of this checkout; it never builds. Compare two passes with perf-compare.ps1.
.PARAMETER Configuration
    Which build to measure. Debug by default, as the repository builds.
.PARAMETER Target
    The checkout whose build is measured. The checkout this script lives in by default.
.PARAMETER Label
    A short word kept in the file name and the file, to tell passes apart ("before-fix", "after-fix").
.PARAMETER OutputFolder
    Where the pass is written. artifacts\bench\ of the checkout by default.
.PARAMETER AudioFile
    The audio file of the dictation take. By default the test audio is built from the speech clip the repository already ships (src/Deckle.App/Assets/Sounds/speech.wav, a synthetic voice, no personal data) repeated -Repeat times. Never point this at a recording of a person.
.PARAMETER Repeat
    How many times the clip is repeated to make the test audio. Six gives about fifteen seconds.
.PARAMETER SkipDictation
    Leave the dictation take out. It needs the maintainer's models folder, which the run links read-only; without it it is reported skipped.
.PARAMETER Only
    Measure one window or view of the self-check instead of the whole walk (see self-check.ps1). The dictation take is left out of such a pass, and its figures compare only with passes of the same target.
.PARAMETER TimeoutSeconds
    How long the self-check may take, as self-check.ps1 reads it.
.NOTES
    order: 130
    action: Take a pass |
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$Target,
    [string]$Label,
    [string]$OutputFolder,
    [string]$AudioFile,
    [ValidateRange(1, 60)]
    [int]$Repeat = 6,
    [switch]$SkipDictation,
    [string]$Only,
    [ValidateRange(10, 1800)]
    [int]$TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'

$RepoRoot = if ($Target) { (Resolve-Path -LiteralPath $Target).Path } else { Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
$SelfCheck = Join-Path $RepoRoot 'scripts\self-check.ps1'
if (-not (Test-Path -LiteralPath $SelfCheck)) { throw "perf-bench: no self-check.ps1 in $RepoRoot." }
if (-not $OutputFolder) { $OutputFolder = Join-Path $RepoRoot 'artifacts\bench' }
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null

$stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$RunFolder = Join-Path ([IO.Path]::GetTempPath()) "deckle-perf-bench\${stamp}_$PID"
New-Item -ItemType Directory -Force -Path $RunFolder | Out-Null

# The test audio: the repository's own speech clip, repeated. PCM16 mono only,
# which is what the clip is; anything else is refused rather than mangled.
function New-RepeatedClip([string]$Source, [int]$Times, [string]$Destination) {
    $b = [IO.File]::ReadAllBytes($Source)
    if ([Text.Encoding]::ASCII.GetString($b, 0, 4) -ne 'RIFF') { throw "perf-bench: $Source is not a RIFF file." }
    $pos = 12; $fmt = $null; $data = $null
    while ($pos + 8 -le $b.Length) {
        $id = [Text.Encoding]::ASCII.GetString($b, $pos, 4)
        $size = [BitConverter]::ToUInt32($b, $pos + 4)
        if ($id -eq 'fmt ') { $fmt = $b[($pos + 8)..($pos + 7 + $size)] }
        elseif ($id -eq 'data') { $data = $b[($pos + 8)..($pos + 7 + $size)]; break }
        $pos += 8 + $size + ($size % 2)
    }
    if (-not $fmt -or -not $data) { throw "perf-bench: $Source has no fmt or data chunk." }
    $channels = [BitConverter]::ToUInt16($fmt, 2)
    $rate = [BitConverter]::ToUInt32($fmt, 4)
    $bits = [BitConverter]::ToUInt16($fmt, 14)
    if ([BitConverter]::ToUInt16($fmt, 0) -ne 1) { throw "perf-bench: $Source is not PCM." }
    $dataLength = $data.Length * $Times
    $out = [IO.MemoryStream]::new()
    $w = [IO.BinaryWriter]::new($out)
    $w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([uint32](4 + 8 + $fmt.Length + 8 + $dataLength))
    $w.Write([Text.Encoding]::ASCII.GetBytes('WAVE'))
    $w.Write([Text.Encoding]::ASCII.GetBytes('fmt ')); $w.Write([uint32]$fmt.Length); $w.Write([byte[]]$fmt)
    $w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([uint32]$dataLength)
    for ($i = 0; $i -lt $Times; $i++) { $w.Write([byte[]]$data) }
    $w.Flush()
    [IO.File]::WriteAllBytes($Destination, $out.ToArray())
    return [double]$dataLength / ($rate * $channels * ($bits / 8))
}

function Get-WavSeconds([string]$Path) {
    # A file given by the caller: its duration from the header when it is PCM, else unknown (0).
    try {
        $b = [IO.File]::ReadAllBytes($Path)
        $rate = [BitConverter]::ToUInt32($b, 24); $block = [BitConverter]::ToUInt16($b, 32)
        $pos = 12
        while ($pos + 8 -le $b.Length) {
            $id = [Text.Encoding]::ASCII.GetString($b, $pos, 4); $size = [BitConverter]::ToUInt32($b, $pos + 4)
            if ($id -eq 'data') { return [double]$size / ($rate * $block) }
            $pos += 8 + $size + ($size % 2)
        }
    } catch { }
    return 0.0
}

$audioPath = $null; $audioSeconds = 0.0; $audioName = $null
if (-not $SkipDictation -and -not $Only) {
    if ($AudioFile) {
        $audioPath = (Resolve-Path -LiteralPath $AudioFile).Path
        $audioSeconds = Get-WavSeconds $audioPath
        $audioName = Split-Path -Leaf $audioPath
    } else {
        $clip = Join-Path $RepoRoot 'src\Deckle.App\Assets\Sounds\speech.wav'
        $audioPath = Join-Path $RunFolder 'bench-audio.wav'
        $audioSeconds = New-RepeatedClip $clip $Repeat $audioPath
        $audioName = "speech.wav x$Repeat"
    }
}

# The self-check inherits this process's environment: the variable asks the app
# for the take (App.SelfCheck.Perf.cs). self-check.ps1 is not touched.
$selfCheckOut = Join-Path $RunFolder 'out'
$arguments = @{
    Configuration = $Configuration
    Target        = $RepoRoot
    OutputFolder  = $selfCheckOut
    KeepData      = $true
    TimeoutSeconds = $TimeoutSeconds
}
if ($audioPath) { $arguments.LinkModels = $true; $env:DECKLE_BENCH_DICTATION = $audioPath }
if ($Only) { $arguments.Only = $Only }

Write-Host "perf-bench: $Configuration pass of $RepoRoot"
try {
    $selfCheckLines = @(& $SelfCheck @arguments *>&1 | ForEach-Object { "$_" })
    $selfCheckExit = $LASTEXITCODE
} finally {
    Remove-Item Env:\DECKLE_BENCH_DICTATION -ErrorAction SilentlyContinue
}
$selfCheckLines | Where-Object { $_ -match '^(FAILED|warning|self-check: (passed|FAILED))' } | ForEach-Object { Write-Host $_ }

$reportPath = Join-Path $selfCheckOut 'report.json'
if (-not (Test-Path -LiteralPath $reportPath)) { Write-Host "perf-bench: no report at $reportPath." -ForegroundColor Red; exit 1 }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$dataRoot = $report.data_root

# ---- metrics ---------------------------------------------------------------
$metrics = [ordered]@{}
$notes = [System.Collections.Generic.List[string]]::new()
function Add-Metric([string]$Name, $Value, [string]$Unit) {
    if ($null -eq $Value) { return }
    $metrics[$Name] = [ordered]@{ value = [math]::Round([double]$Value, 2); unit = $Unit }
}
function ToMb($Bytes) { if ($null -eq $Bytes) { $null } else { [double]$Bytes / 1MB } }

$perf = $report.perf
if ($perf) {
    Add-Metric 'startup.ready_ms' $perf.startup_ready_ms 'ms'
    if ($perf.footprint) {
        Add-Metric 'memory.working_set_mb' (ToMb $perf.footprint.working_set_bytes) 'mb'
        Add-Metric 'memory.private_working_set_mb' (ToMb $perf.footprint.private_working_set_bytes) 'mb'
        Add-Metric 'memory.private_bytes_mb' (ToMb $perf.footprint.private_bytes) 'mb'
        Add-Metric 'process.handles' $perf.footprint.handles 'count'
        Add-Metric 'process.threads' $perf.footprint.threads 'count'
    }
}

# Pages: the load time the self-check's walk measured for each page.
foreach ($s in @($report.surfaces | Where-Object { $_.page -and $_.loaded -and $_.technique -eq 'page-render' })) {
    Add-Metric "page.$($s.window)/$($s.page).load_ms" $s.ms 'ms'
}

# The run's own log: the first-open cost of each window and the take's phases.
$logPath = Join-Path $dataRoot 'diagnostics\log.jsonl'
$timings = $null
if (Test-Path -LiteralPath $logPath) {
    $stream = [IO.FileStream]::new($logPath, 'Open', 'Read', 'ReadWrite,Delete')
    $reader = [IO.StreamReader]::new($stream)
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            if ($line -notmatch 'WindowLoadComplete|PipelineTimings') { continue }
            try { $entry = $line | ConvertFrom-Json } catch { continue }
            if ($entry.event -eq 'WindowLoadComplete' -and -not $metrics.Contains("window.$($entry.payload.window).create_ms")) {
                Add-Metric "window.$($entry.payload.window).create_ms" $entry.payload.load_ms 'ms'
            }
            elseif ($entry.event -eq 'PipelineTimings') { $timings = $entry.payload }
        }
    } finally { $reader.Dispose(); $stream.Dispose() }
} else { $notes.Add('no log.jsonl in the run: window and phase figures are missing.') }

if ($audioPath) {
    $d = $perf.dictation
    if (-not $d) {
        $notes.Add('dictation: the app reported no take.')
    }
    elseif ($d.outcome -ne 'ok') {
        $notes.Add("dictation: $($d.outcome) ($($d.reason)).")
    }
    else {
        Add-Metric 'dictation.wall_ms' $d.wall_ms 'ms'
        if ($audioSeconds -gt 0) { Add-Metric 'dictation.realtime_factor' ($d.wall_ms / 1000.0 / $audioSeconds) 'ratio' }
        if ($timings) {
            Add-Metric 'dictation.model_load_ms' $timings.model_load_ms 'ms'
            Add-Metric 'dictation.asr_init_ms' $timings.whisper_init_ms 'ms'
            Add-Metric 'dictation.asr_ms' $timings.whisper_ms 'ms'
        } else { $notes.Add('dictation: no PipelineTimings line in the log.') }
        if ($perf.footprint_after_dictation) {
            Add-Metric 'memory_after_dictation.private_working_set_mb' (ToMb $perf.footprint_after_dictation.private_working_set_bytes) 'mb'
            Add-Metric 'process_after_dictation.handles' $perf.footprint_after_dictation.handles 'count'
        }
    }
}

# ---- the file --------------------------------------------------------------
$git = { param($a) try { (& git -C $RepoRoot @a 2>$null | Select-Object -First 1) } catch { $null } }
$cpu = try { (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim() } catch { $null }
$result = [ordered]@{
    schema        = 1
    taken_at      = (Get-Date).ToString('o')
    label         = $Label
    configuration = $Configuration
    version       = $report.version
    commit        = & $git @('rev-parse', '--short', 'HEAD')
    dirty         = [bool](& $git @('status', '--porcelain'))
    target        = if ($Only) { $Only } else { 'full' }
    outcome       = if ($selfCheckExit -eq 0) { 'passed' } else { 'failed' }
    machine       = [ordered]@{ cpu = $cpu; logical_cores = [Environment]::ProcessorCount }
    audio         = if ($audioPath) { [ordered]@{ source = $audioName; seconds = [math]::Round($audioSeconds, 2) } } else { $null }
    notes         = @($notes)
    metrics       = $metrics
}
$name = "perf-$stamp" + $(if ($Label) { "-$($Label -replace '[^A-Za-z0-9._-]', '-')" } else { '' }) + "-$($Configuration.ToLowerInvariant()).json"
$outPath = Join-Path $OutputFolder $name
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $outPath -Encoding utf8

# The run's data root held links into the maintainer's folders: they go first,
# so the removal never follows them (as self-check.ps1 does).
foreach ($folder in 'models') {
    $link = Join-Path $dataRoot $folder
    if (Test-Path -LiteralPath $link) { (Get-Item -LiteralPath $link).Delete() }
}
try { Remove-Item -LiteralPath $dataRoot -Recurse -Force -ErrorAction Stop } catch { }
try { Remove-Item -LiteralPath $RunFolder -Recurse -Force -ErrorAction Stop } catch { }

Write-Host ''
foreach ($m in $metrics.GetEnumerator()) { Write-Host ("  {0,-48} {1,12} {2}" -f $m.Key, $m.Value.value, $m.Value.unit) }
foreach ($n in $notes) { Write-Host "note: $n" -ForegroundColor Yellow }
Write-Host "perf-bench: $($metrics.Count) metrics, self-check $($result.outcome)"
Write-Host "pass: $outPath"
if ($selfCheckExit -ne 0) { exit 1 }
exit 0
