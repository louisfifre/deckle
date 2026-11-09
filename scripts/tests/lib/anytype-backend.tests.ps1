$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Repo = Split-Path -Parent $ScriptsDir
Import-Module (Join-Path $ScriptsDir 'lib\anytype-backend.psm1') -Force

function Assert-Throws([scriptblock]$Action, [string]$MessageFragment) {
    try {
        & $Action
        throw "Expected failure containing '$MessageFragment'"
    } catch {
        if ($_.Exception.Message -notlike "*$MessageFragment*") { throw }
    }
}

function New-Provider([string]$Root, [string[]]$Versions, [string]$Active) {
    $provider = Join-Path $Root 'provider'
    foreach ($version in $Versions) {
        $folder = Join-Path $provider "versions\$version"
        $null = New-Item -ItemType Directory -Force -Path $folder
        [IO.File]::WriteAllBytes((Join-Path $folder 'anytype.exe'), [byte[]](77, 90))
    }
    if ($Active) {
        $null = New-Item -ItemType Directory -Force -Path $provider
        Set-Content -LiteralPath (Join-Path $provider 'active.json') -NoNewline `
            -Value "{`"Version`":`"$Active`",`"RelativeExecutable`":`"versions/$Active/anytype.exe`"}"
    }
    return $provider
}

$official = { param($Executable) 'anytype-cli v0.5.2 (2026-11-05 09:23:09)' }

$root = Join-Path ([IO.Path]::GetTempPath()) "deckle-anytype-backend-$([guid]::NewGuid())"
try {
    # The pin is read from the app's own source, the one the deploy reads.
    $real = Get-AnytypeBackendPin -SourceText (Get-Content -Raw -LiteralPath (Join-Path $Repo 'src\Deckle.Anytype\Backend\BackendInstallation.cs'))
    if ($real.Version -notmatch '^\d+\.\d+\.\d+$' -or $real.Sha256 -notmatch '^[0-9a-f]{64}$' -or $real.SizeBytes -le 0) {
        throw 'The pin of BackendInstallation.cs was not parsed'
    }
    $pin = Get-AnytypeBackendPin -SourceText @'
public static BackendBundle CurrentBundle { get; } = new(
    Version:     "9.8.7",
    Url:         "https://example.test/anytype.zip",
    Sha256:      "abc",
    SizeBytes:   50_396_862L,
    DisplayName: "Anytype backend (anytype-cli)");
'@
    if ($pin.Version -cne '9.8.7' -or $pin.SizeBytes -ne 50396862) { throw 'The pin fields were not parsed' }
    Assert-Throws { Get-AnytypeBackendPin -SourceText 'class Empty {}' } 'CurrentBundle declaration not found'

    # Each state the deploy summary names.
    $cases = @(
        @{ Name = 'aligned'; Versions = @('0.5.2'); Active = '0.5.2' }
        @{ Name = 'staged';  Versions = @('0.5.0', '0.5.2'); Active = '0.5.0' }
        @{ Name = 'differs'; Versions = @('0.5.0'); Active = '0.5.0' }
        @{ Name = 'absent';  Versions = @('0.5.0'); Active = $null }
        @{ Name = 'invalid'; Versions = @(); Active = '0.5.0' }
    )
    foreach ($case in $cases) {
        $provider = New-Provider (Join-Path $root "status-$($case.Name)") $case.Versions $case.Active
        $status = Get-AnytypeBackendPinStatus -PinnedVersion '0.5.2' -ProviderDirectory $provider
        if ($status.State -cne $case.Name) { throw "Expected state $($case.Name), got $($status.State): $($status.Line)" }
    }

    # A preview writes nothing, even with every blocker running.
    $switchRoot = Join-Path $root 'switch'
    $provider = New-Provider $switchRoot @('0.5.0', '0.5.2') '0.5.0'
    $data = Join-Path $switchRoot 'anytype\data'
    $config = Join-Path $switchRoot '.anytype'
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $data 'account'), $config
    Set-Content -LiteralPath (Join-Path $data 'account\store.db') -Value 'database'
    Set-Content -LiteralPath (Join-Path $config 'config.json') -Value '{}'
    $activeBefore = Get-Content -Raw -LiteralPath (Join-Path $provider 'active.json')
    $blockers = @([pscustomobject]@{ Id = 1; Kind = 'bot'; Path = 'x' })
    $arguments = @{
        Version = '0.5.2'; ProviderDirectory = $provider; DataDirectory = $data
        ConfigDirectory = $config; Stamp = 'test'; ReadVersion = $official
    }

    $preview = Invoke-AnytypeBackendSwitch @arguments -Blockers $blockers
    if ($preview.Result -cne 'Preview' -or $preview.Copies.Count -ne 2) { throw 'The preview did not plan both copies' }
    if ((Get-Content -Raw -LiteralPath (Join-Path $provider 'active.json')) -cne $activeBefore) { throw 'The preview wrote active.json' }
    if (Test-Path -LiteralPath "$data.backup-test") { throw 'The preview copied the data' }

    # A running blocker refuses the switch before anything is copied.
    Assert-Throws { Invoke-AnytypeBackendSwitch @arguments -Blockers $blockers -Apply } 'Still running: bot PID 1'
    if (Test-Path -LiteralPath "$data.backup-test") { throw 'A refused switch copied the data' }

    # An executable that is not the version asked for is refused.
    $wrongBuild = $arguments.Clone()
    $wrongBuild.ReadVersion = { param($Executable) 'anytype-cli v0.5.0-dirty (deckle)' }
    Assert-Throws { Invoke-AnytypeBackendSwitch @wrongBuild -Apply } 'not anytype-cli v0.5.2'

    $applied = Invoke-AnytypeBackendSwitch @arguments -Apply
    if ($applied.Result -cne 'Success') { throw 'The switch did not succeed' }
    $written = [IO.File]::ReadAllBytes((Join-Path $provider 'active.json'))
    $expected = [Text.UTF8Encoding]::new($false).GetBytes('{"Version":"0.5.2","RelativeExecutable":"versions/0.5.2/anytype.exe"}')
    if ([Convert]::ToBase64String($written) -cne [Convert]::ToBase64String($expected)) {
        throw 'active.json is not the manifest BackendProviderStore writes'
    }
    if ((Get-Content -Raw -LiteralPath (Join-Path $provider 'active.json.0.5.0.bak')) -cne $activeBefore) {
        throw 'The previous active.json was not backed up'
    }
    if ((Get-Content -Raw -LiteralPath "$data.backup-test\account\store.db") -notmatch 'database' -or
        -not (Test-Path -LiteralPath "$config.backup-test\config.json")) {
        throw 'The data and config folders were not copied'
    }
    if (@(Get-ChildItem -LiteralPath $provider -Filter 'active.*.tmp').Count -ne 0) { throw 'A temporary manifest was left behind' }

    $again = Invoke-AnytypeBackendSwitch @arguments -Apply
    if ($again.Result -cne 'Skipped') { throw 'A second switch to the active version should be skipped' }

    $unstaged = $arguments.Clone()
    $unstaged.Version = '0.6.0'
    Assert-Throws { Invoke-AnytypeBackendSwitch @unstaged -Apply } 'is not staged'

    Write-Host 'anytype-backend tests passed'
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
