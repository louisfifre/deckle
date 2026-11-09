$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptPath = Join-Path $ScriptsDir 'inspect-context.ps1'

$root = Join-Path ([IO.Path]::GetTempPath()) "deckle-inspect-context-$([guid]::NewGuid())"
$jsonPath = Join-Path $root 'out\context.json'

function Invoke-TestGit {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    $output = & git -C $root @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed" }
    return $output
}

try {
    $null = New-Item -ItemType Directory -Path (Join-Path $root 'docs')
    Invoke-TestGit init --initial-branch=main | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'AGENTS.md') -Value "# Agents`n`nRule." -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $root 'docs\guide.md') -Value "# Guide`n`n## Part`n`nText." -Encoding utf8NoBOM
    Invoke-TestGit add AGENTS.md docs | Out-Null

    # Regression: the per-mode loop once reused the [string[]] parameter
    # $LoadingMode as its variable, so every run failed after the summary.
    $output = @(& pwsh -NoProfile -File $scriptPath -Target $root -SkipActivity -Json $jsonPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "inspect-context.ps1 failed: $($output -join "`n")"
    }
    if (-not (Test-Path -LiteralPath $jsonPath)) {
        throw 'inspect-context.ps1 -Json should write the JSON file'
    }
    $data = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
    if (@($data.Documents).Count -ne 2) {
        throw "Expected 2 documents in the JSON, got $(@($data.Documents).Count)"
    }

    Write-Host 'inspect-context.tests.ps1: PASS' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
