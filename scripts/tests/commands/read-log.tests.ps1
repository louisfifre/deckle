$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptPath = Join-Path $ScriptsDir 'read-log.ps1'

function Assert-True([bool]$Condition, [string]$Case) {
    if (-not $Condition) { throw "${Case}: condition was false" }
}

function Invoke-ReadLog([string[]]$Arguments) {
    $text = @(& pwsh -NoProfile -File $scriptPath -DataRoot $root @Arguments 2>&1 6>&1) -join "`n"
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = $text }
}

$root = Join-Path ([IO.Path]::GetTempPath()) "deckle-read-log-$([guid]::NewGuid())"
try {
    $null = New-Item -ItemType Directory -Path (Join-Path $root 'diagnostics')
    $now = [DateTime]::UtcNow
    $old = $now.AddHours(-3).ToString('o')
    $recent = $now.AddMinutes(-1).ToString('o')
    # Synthetic entries only: the real log holds a person's data.
    $entries = @(
        @{ timestamp = $old; level = 'Informational'; provider = 'Deckle-App'; event = 'Started'; message = 'The app started.'; payload = @{ build = 'x' } }
        @{ timestamp = $old; level = 'Warning'; provider = 'Deckle-Audio'; event = 'DeviceLost'; message = "The microphone went away.`nreason=unplugged"; payload = @{} }
        @{ timestamp = $recent; level = 'Error'; provider = 'Deckle-Llm'; event = 'RewriteFailed'; message = 'The rewrite stopped.'; payload = @{ attempt = 2 } }
    )
    $lines = @($entries | ForEach-Object { $_ | ConvertTo-Json -Compress })
    $lines += 'not json'
    [IO.File]::WriteAllText((Join-Path $root 'diagnostics\log.jsonl'), ($lines -join "`n") + "`n")
    [IO.File]::WriteAllText((Join-Path $root 'diagnostics\trace.jsonl'), ($entries[0] | ConvertTo-Json -Compress) + "`n")

    $all = Invoke-ReadLog @()
    Assert-True ($all.ExitCode -eq 0) 'a plain read succeeds'
    Assert-True ($all.Text -match '3 of 3 matching entries') 'every JSON entry is counted'
    Assert-True ($all.Text -match 'Deckle-Audio/DeviceLost  The microphone went away\. \| reason=unplugged') 'a multi-line message stays on one line'
    Assert-True ($all.Text -match '1 line\(s\) were not JSON') 'a broken line is reported, not fatal'

    $warnings = Invoke-ReadLog @('-Level', 'Warning')
    Assert-True ($warnings.Text -match '2 of 2' -and $warnings.Text -notmatch 'Started') 'the level keeps the more severe entries'

    $last = Invoke-ReadLog @('-Last', '1')
    Assert-True ($last.Text -match '1 of 3' -and $last.Text -match 'RewriteFailed') 'Last keeps the latest entries'

    $since = Invoke-ReadLog @('-SinceMinutes', '30')
    Assert-True ($since.Text -match '1 of 1' -and $since.Text -match 'RewriteFailed') 'SinceMinutes drops older entries'

    $match = Invoke-ReadLog @('-Match', 'Audio', '-Payload')
    Assert-True ($match.Text -match '1 of 1' -and $match.Text -match 'DeviceLost') 'Match filters on provider, event and message'

    $payload = Invoke-ReadLog @('-Match', 'Llm', '-Payload')
    Assert-True ($payload.Text -match 'attempt = 2') 'Payload prints the fields'

    $trace = Invoke-ReadLog @('-Trace')
    Assert-True ($trace.Text -match 'trace\.jsonl' -and $trace.Text -match '1 of 1') 'Trace reads trace.jsonl'

    $missing = @(& pwsh -NoProfile -File $scriptPath -DataRoot (Join-Path $root 'nowhere') 2>&1)
    Assert-True ($LASTEXITCODE -ne 0) 'a missing log fails'
    Assert-True (($missing -join ' ') -match 'Pass -DataRoot') 'the failure says how to point elsewhere'

    Write-Host 'read-log.tests.ps1: PASS' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
