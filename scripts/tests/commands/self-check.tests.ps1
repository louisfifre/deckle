$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Script = Join-Path $ScriptsDir 'self-check.ps1'

function Assert-True([bool]$Condition, [string]$Case) {
    if (-not $Condition) { throw "${Case}: condition was false" }
}

function Invoke-SelfCheck([string[]]$Arguments) {
    $text = @(& pwsh -NoProfile -File $Script @Arguments 2>&1 6>&1) -join "`n"
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = $text }
}

# The target is an empty temp folder: no build exists there, so no app is
# started and nothing is compiled.
$target = Join-Path ([IO.Path]::GetTempPath()) "deckle-self-check-$([guid]::NewGuid())"
try {
    $null = New-Item -ItemType Directory -Path $target

    # Each app of the suite, with no build: the run fails at once and names the
    # executable it looked for.
    $exes = [ordered]@{ deckle = 'Deckle.exe'; mail = 'DeckleMail.exe'; folders = 'DeckleFolders.exe'; projects = 'DeckleProjects.exe'; browser = 'DeckleBrowser.exe' }
    foreach ($app in $exes.Keys) {
        $result = Invoke-SelfCheck @('-App', $app, '-Target', $target)
        Assert-True ($result.ExitCode -eq 1) "$app with no build exits 1"
        Assert-True ($result.Text -match ('no built app at .*' + [regex]::Escape($exes[$app]))) "$app with no build names $($exes[$app])"
        Assert-True ($result.Text -notmatch 'self-check: passed') "$app with no build is not reported as passed"
    }

    # The default app is the published Deckle, and the configuration picks the folder looked in.
    $result = Invoke-SelfCheck @('-Target', $target, '-Configuration', 'Release')
    Assert-True ($result.ExitCode -eq 1 -and $result.Text -match 'no built app at .*\\release\\Deckle\.exe') 'the default app is Deckle, looked for in the Release output'

    # An app the suite does not have is refused, and the message says which exist.
    $result = Invoke-SelfCheck @('-App', 'nonsense', '-Target', $target)
    Assert-True ($result.ExitCode -eq 1) 'an unknown app exits 1'
    Assert-True ($result.Text -match 'nonsense') 'the refusal names the value given'
    Assert-True ($result.Text -match 'deckle, mail, folders, projects or browser') 'the refusal lists the apps that exist'
    Assert-True ($result.Text -notmatch 'no built app') 'an unknown app is refused before any build is looked for'

    Write-Host 'self-check.tests.ps1: PASS' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
