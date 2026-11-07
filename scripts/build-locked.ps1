# build-locked.ps1 - Run one dotnet build or test under the machine-wide Deckle build lock.
#
# AGENTS.md forbids two builds at once on this machine, whatever the session.
# Parallel agents cannot see each other, so this script gives them one gate:
# it takes the named mutex `Global\Deckle.Build`, waits as long as another
# build holds it, then runs `dotnet build` (or `dotnet test`) with the
# project's mandatory flags, and releases the mutex when dotnet exits.
# Directory.Build.rsp still applies (low priority, no node reuse); the
# explicit `/m:4` stays because the CLI applies its default node count after
# response files.
#
# Examples:
#   pwsh -File scripts/build-locked.ps1 -Project src/Deckle.App/Deckle.App.csproj
#   pwsh -File scripts/build-locked.ps1 -Test -Project tests/Deckle.Shell.TaskbarCover.Tests/Deckle.Shell.TaskbarCover.Tests.csproj
#   pwsh -File scripts/build-locked.ps1 -Project Deckle.Tests.sln /v:q /bl:artifacts/build.binlog
#
# Extra dotnet arguments: write MSBuild switches with a slash (`/v:q`,
# `/bl:path`, `/p:Name=Value`) and dotnet options with two dashes
# (`--no-restore`). PowerShell reads any `-name:value` token as one of its own
# parameters before this script sees it: `-v:q` becomes `-Verbose`, `-p:x`
# collides with `-Project`, `-bl:path` is split in two. No param block can
# forward those intact, `--` included, so the slash form is the contract.
#
# No Header on purpose: -Project is mandatory, so the menu could not run
# this script without asking, and agents call it from their own builds.

# Named parameters only: with positional binding, the first extra argument
# after -Project landed in -Configuration and failed its ValidateSet.
[CmdletBinding(PositionalBinding = $false)]
param(
    # Project or solution to build. Relative paths resolve against the current
    # directory, so a worktree builds its own copy.
    [Parameter(Mandatory)]
    [string]$Project,
    # Run `dotnet test` instead of `dotnet build`. A test run builds first, so
    # it needs the lock as much as a build does.
    [switch]$Test,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    # Extra arguments passed to dotnet verbatim, after the mandatory ones.
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$DotnetArgs
)

$ErrorActionPreference = 'Stop'

$resolved = Resolve-Path -LiteralPath $Project
$verb = if ($Test) { 'test' } else { 'build' }
$arguments = @($verb, "$resolved", '-c', $Configuration, '-p:Platform=x64', '/m:4') + @($DotnetArgs)

# Global, so a build started from another session or another worktree of the
# same machine is seen. Abandoned mutexes (a killed build) are taken over: the
# lock protects the machine's build servers, not any shared state.
$mutex = [System.Threading.Mutex]::new($false, 'Global\Deckle.Build')
$acquired = $false
try {
    while (-not $acquired) {
        try {
            $acquired = $mutex.WaitOne([TimeSpan]::FromSeconds(30))
        } catch [System.Threading.AbandonedMutexException] {
            $acquired = $true
        }
        if (-not $acquired) {
            Write-Host "build-locked: another dotnet build is running, waiting..." -ForegroundColor DarkGray
        }
    }

    Write-Host "build-locked: dotnet $($arguments -join ' ')" -ForegroundColor DarkGray
    & dotnet @arguments
    exit $LASTEXITCODE
} finally {
    if ($acquired) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
