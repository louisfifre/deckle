$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptPath = Join-Path $ScriptsDir 'bench\perf-compare.ps1'
$benchPath = Join-Path $ScriptsDir 'bench\perf-bench.ps1'

function Assert-True([bool]$Condition, [string]$Case) {
    if (-not $Condition) { throw "${Case}: condition was false" }
}

# Both scripts must parse: a bench that cannot be read is found here, not on the
# day someone needs a measure.
foreach ($path in $scriptPath, $benchPath) {
    $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errors)
    Assert-True ($errors.Count -eq 0) "$(Split-Path -Leaf $path) parses"
}

function New-Pass([string]$Folder, [string]$Name, [hashtable]$Metrics, [string]$Configuration = 'Debug', [string]$Target = 'full') {
    $m = [ordered]@{}
    foreach ($key in $Metrics.Keys) { $m[$key] = [ordered]@{ value = $Metrics[$key][0]; unit = $Metrics[$key][1] } }
    $pass = [ordered]@{
        schema = 1; taken_at = '2026-01-01T00:00:00Z'; label = $Name; configuration = $Configuration
        commit = 'abc1234'; dirty = $false; target = $Target; outcome = 'passed'
        machine = [ordered]@{ cpu = 'test cpu'; logical_cores = 8 }; metrics = $m
    }
    $path = Join-Path $Folder "perf-$Name.json"
    $pass | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $path -Encoding utf8
    return $path
}

function Invoke-Compare([string[]]$Arguments) {
    $text = @(& pwsh -NoProfile -File $scriptPath @Arguments 2>&1 6>&1) -join "`n"
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = $text }
}

$root = Join-Path ([IO.Path]::GetTempPath()) "deckle-perf-compare-$([guid]::NewGuid())"
try {
    $null = New-Item -ItemType Directory -Path $root
    # Synthetic figures only.
    $before = New-Pass $root '1-before' @{
        'startup.ready_ms'        = 1000, 'ms'
        'page.a.load_ms'          = 400, 'ms'
        'page.tiny.load_ms'       = 3, 'ms'
        'memory.private_mb'       = 400, 'mb'
        'process.handles'         = 4000, 'count'
        'dictation.asr_ms'        = 2000, 'ms'
        'only.before.ms'          = 10, 'ms'
    }
    $after = New-Pass $root '2-after' @{
        'startup.ready_ms'        = 1500, 'ms'     # +50 percent, beyond the floor: regression
        'page.a.load_ms'          = 300, 'ms'      # -25 percent but beyond the threshold: improved at 20, same at 25
        'page.tiny.load_ms'       = 9, 'ms'        # +200 percent, under the 50 ms floor: same
        'memory.private_mb'       = 405, 'mb'      # within the threshold: same
        'process.handles'         = 4010, 'count'
        'dictation.asr_ms'        = 1000, 'ms'     # -50 percent: improved
        'only.after.ms'           = 10, 'ms'
    }

    $result = Invoke-Compare @('-Before', $before, '-After', $after)
    Assert-True ($result.ExitCode -eq 1) 'a regression ends with 1'
    Assert-True ($result.Text -match 'startup\.ready_ms.*REGRESSION') 'a slower startup is flagged'
    Assert-True ($result.Text -match 'page\.tiny\.load_ms.*same') 'a change under the noise floor is not flagged'
    Assert-True ($result.Text -match 'dictation\.asr_ms.*improved') 'a faster phase is reported improved'
    Assert-True ($result.Text -match 'memory\.private_mb.*same') 'a small memory change is not flagged'
    Assert-True ($result.Text -match 'only\.before\.ms.*missing' -and $result.Text -match 'only\.after\.ms.*missing') 'a metric on one side is told apart'
    Assert-True ($result.Text -match '1 regression\(s\)') 'the summary counts the regressions'

    $strict = Invoke-Compare @('-Before', $before, '-After', $after, '-ThresholdPercent', '20')
    Assert-True ($strict.Text -match 'page\.a\.load_ms.*improved') 'a lower threshold reports the smaller gain'

    $none = Invoke-Compare @('-Before', $before, '-After', $before)
    Assert-True ($none.ExitCode -eq 0 -and $none.Text -match '0 regression\(s\)') 'a pass against itself is clean'

    $release = New-Pass $root '3-release' @{ 'startup.ready_ms' = 1000, 'ms' } 'Release'
    $mismatch = Invoke-Compare @('-Before', $before, '-After', $release)
    Assert-True ($mismatch.ExitCode -eq 2) 'passes of two configurations are refused'

    $latest = Invoke-Compare @('-Folder', $root, '-Before', 'latest-2', '-After', 'latest')
    Assert-True ($latest.Text -match 'perf-1-before' -and $latest.Text -match 'perf-3-release') 'latest and latest-N resolve by name order'

    $missing = Invoke-Compare @('-Folder', $root, '-Before', 'latest-9')
    Assert-True ($missing.ExitCode -eq 2) 'a pass the folder does not hold fails'

    Write-Host 'perf-compare.tests.ps1: PASS' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
