$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'update-readme-stats.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) "deckle-readme-stats-$([guid]::NewGuid())"

function Invoke-TestGit {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    $output = & git -C $root @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed" }
    return $output
}

try {
    $null = New-Item -ItemType Directory -Path $root
    Invoke-TestGit init --initial-branch=main | Out-Null
    Invoke-TestGit config user.name 'Deckle Tests' | Out-Null
    Invoke-TestGit config user.email 'deckle-tests@example.invalid' | Out-Null
    Invoke-TestGit config core.autocrlf false | Out-Null

    @'
# Deckle test

<!-- deckle-stats:start -->
old pulse
<!-- deckle-stats:end -->

---

Stable content.
'@ | Set-Content -LiteralPath (Join-Path $root 'README.md') -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $root 'tracked.txt') -Value 'tracked' -Encoding utf8NoBOM
    Invoke-TestGit add README.md tracked.txt | Out-Null
    Invoke-TestGit commit -m 'test: seed repository' | Out-Null

    & $scriptPath -Target $root
    if (-not @(Invoke-TestGit diff --name-only -- README.md).Count) {
        throw 'README update should leave a pending generated change without -Commit'
    }

    & $scriptPath -Target $root -Commit
    $subject = @(Invoke-TestGit log -1 --format=%s)[0]
    if ($subject -cne 'docs(readme): refresh development pulse') {
        throw "README update created the wrong commit: $subject"
    }
    if (@(Invoke-TestGit status --porcelain).Count -ne 0) {
        throw 'README commit should leave the repository clean'
    }

    $readmePath = Join-Path $root 'README.md'
    $manualEdit = (Get-Content -LiteralPath $readmePath -Raw) -replace '# Deckle test', '# Manual edit'
    [IO.File]::WriteAllText($readmePath, $manualEdit, [Text.UTF8Encoding]::new($false))
    $failureOutput = @(& pwsh -NoProfile -File $scriptPath -Target $root -Commit 2>&1)
    if ($LASTEXITCODE -eq 0) {
        throw 'Expected README commit to reject manual content changes'
    }
    if (($failureOutput -join "`n") -notlike '*changes outside the generated development pulse*') {
        throw 'README commit rejection should explain how to preserve manual changes'
    }

    Write-Host 'update-readme-stats.tests.ps1: PASS' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
