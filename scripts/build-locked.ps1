# build-locked.ps1 - Run one dotnet build or test under the machine-wide Deckle build lock.
#
# AGENTS.md allows at most three builds at once on this machine, whatever the
# session. Parallel agents cannot see each other, so this script gives them one
# gate: three named mutexes, one per slot. It takes the first free slot, waits
# as long as all three are held, then runs `dotnet build` (or `dotnet test`)
# with the project's mandatory flags, and releases its slot when dotnet exits.
#
# Why three slots and not one: with a single slot, fourteen sessions queued
# for over an hour behind one build while the machine sat at 25 % CPU
# (2026-11-07, 8 cores / 16 logical processors). Each worktree builds into its
# own bin/ and obj/, and the shared VBCSCompiler server takes concurrent
# compilations, so builds in different worktrees do not collide. Three builds
# of four nodes each leave four logical processors to the desktop, at low
# priority. If memory runs short, lower the slot count here.
#
# Why mutexes and not a semaphore: a killed build abandons its mutex, which
# the next waiter takes over; a semaphore count a killed build never released
# would stay lost until every process holding the semaphore exits.
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
# same machine is seen. The first slot keeps the old single-lock name, so a
# worktree still running an older copy of this script, which knows only that
# name, counts as one of the three. build-server-watchdog.ps1 reads the same
# names: change them together. Abandoned mutexes (a killed build) are taken
# over: the lock protects the machine's capacity, not any shared state.
$slotNames = 'Global\Deckle.Build', 'Global\Deckle.Build.2', 'Global\Deckle.Build.3'
$slots = [System.Threading.Mutex[]]@($slotNames | ForEach-Object { [System.Threading.Mutex]::new($false, $_) })
$held = [System.Threading.WaitHandle]::WaitTimeout
try {
    while ($held -eq [System.Threading.WaitHandle]::WaitTimeout) {
        try {
            $held = [System.Threading.WaitHandle]::WaitAny($slots, [TimeSpan]::FromSeconds(30))
        } catch [System.Threading.AbandonedMutexException] {
            # PowerShell may wrap the .NET exception; the slot index is on it.
            $abandoned = $_.Exception
            if ($abandoned -isnot [System.Threading.AbandonedMutexException]) { $abandoned = $abandoned.InnerException }
            $held = $abandoned.MutexIndex
        }
        if ($held -eq [System.Threading.WaitHandle]::WaitTimeout) {
            Write-Host "build-locked: $($slots.Count) dotnet builds are running, waiting..." -ForegroundColor DarkGray
        }
    }

    Write-Host "build-locked: slot $($held + 1)/$($slots.Count), dotnet $($arguments -join ' ')" -ForegroundColor DarkGray
    & dotnet @arguments
    exit $LASTEXITCODE
} finally {
    if ($held -ne [System.Threading.WaitHandle]::WaitTimeout) { $slots[$held].ReleaseMutex() }
    foreach ($slot in $slots) { $slot.Dispose() }
}
