$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Script = Join-Path $ScriptsDir 'deploy-prod.ps1'

function Assert-True([bool]$Condition, [string]$Case) {
    if (-not $Condition) { throw "${Case}: condition was false" }
}

# A prod copy in a temp folder. A build is a folder holding a Deckle.exe whose
# content says which build it is; nothing here compiles, and the rollback never
# reads the exe's version, so a text file stands in.
function New-FakeBuild([string]$Folder, [string]$Label) {
    $null = New-Item -ItemType Directory -Path $Folder -Force
    [IO.File]::WriteAllText((Join-Path $Folder 'Deckle.exe'), $Label)
}

function Read-Label([string]$Folder) {
    $exe = Join-Path $Folder 'Deckle.exe'
    if (Test-Path -LiteralPath $exe) { return [IO.File]::ReadAllText($exe) }
    return $null
}

function Invoke-Rollback([string]$Root) {
    $text = @(& pwsh -NoProfile -File $Script -Rollback -Root $Root 2>&1 6>&1) -join "`n"
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = $text }
}

$base = Join-Path ([IO.Path]::GetTempPath()) "deckle-deploy-prod-$([guid]::NewGuid())"
try {
    # The previous build comes back as the build staged for the next start,
    # replacing one staged earlier; the running build is not touched.
    $root = Join-Path $base 'with-previous'
    New-FakeBuild (Join-Path $root 'current') 'running build'
    New-FakeBuild (Join-Path $root 'previous') 'previous build'
    New-FakeBuild (Join-Path $root 'next') 'build staged and never applied'
    $keepsCurrent = Join-Path $root 'current\Settings.txt'
    [IO.File]::WriteAllText($keepsCurrent, 'a file of the running build')
    $before = (Get-Item -LiteralPath $keepsCurrent).LastWriteTimeUtc

    $result = Invoke-Rollback $root
    Assert-True ($result.ExitCode -eq 0) 'a rollback with a previous build succeeds'
    Assert-True ((Read-Label (Join-Path $root 'next')) -eq 'previous build') 'the previous build is the one staged'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $root 'previous'))) 'the previous folder is emptied by the move, not copied'
    Assert-True ((Read-Label (Join-Path $root 'current')) -eq 'running build') 'the running build keeps its executable'
    Assert-True ([IO.File]::ReadAllText($keepsCurrent) -eq 'a file of the running build') 'the running build keeps its other files'
    Assert-True ((Get-Item -LiteralPath $keepsCurrent).LastWriteTimeUtc -eq $before) 'the running build is not rewritten'
    Assert-True ($result.Text -match 'The previous build is staged') 'the summary says the previous build is staged'

    # With no next\ waiting, the previous build is staged all the same.
    $root = Join-Path $base 'no-next'
    New-FakeBuild (Join-Path $root 'current') 'running build'
    New-FakeBuild (Join-Path $root 'previous') 'previous build'
    $result = Invoke-Rollback $root
    Assert-True ($result.ExitCode -eq 0) 'a rollback with nothing staged succeeds'
    Assert-True ((Read-Label (Join-Path $root 'next')) -eq 'previous build') 'the previous build is staged when nothing was'

    # Nothing to go back to: a clear message, and nothing moves.
    $root = Join-Path $base 'no-previous'
    New-FakeBuild (Join-Path $root 'current') 'running build'
    New-FakeBuild (Join-Path $root 'next') 'build staged and never applied'
    $result = Invoke-Rollback $root
    Assert-True ($result.ExitCode -ne 0) 'a rollback with no previous build fails'
    Assert-True ($result.Text -match 'No previous build under .*previous to roll back to') 'the failure says there is no previous build'
    Assert-True ($result.Text -match 'The prod copy was not updated; the running Deckle is untouched') 'the failure says the running Deckle is untouched'
    Assert-True ((Read-Label (Join-Path $root 'current')) -eq 'running build') 'a failed rollback leaves the running build'
    Assert-True ((Read-Label (Join-Path $root 'next')) -eq 'build staged and never applied') 'a failed rollback leaves the staged build'

    # A previous\ folder without its executable is not a build to go back to.
    $root = Join-Path $base 'empty-previous'
    New-FakeBuild (Join-Path $root 'current') 'running build'
    New-FakeBuild (Join-Path $root 'next') 'build staged and never applied'
    $null = New-Item -ItemType Directory -Path (Join-Path $root 'previous')
    $result = Invoke-Rollback $root
    Assert-True ($result.ExitCode -ne 0 -and $result.Text -match 'No previous build') 'an empty previous folder is refused'
    Assert-True ((Read-Label (Join-Path $root 'next')) -eq 'build staged and never applied') 'a refused rollback leaves the staged build'

    Write-Host 'deploy-prod.tests.ps1: PASS' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $base) { Remove-Item -LiteralPath $base -Recurse -Force }
}
