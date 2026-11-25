$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Script = Join-Path $ScriptsDir 'merge-branch.ps1'
Import-Module (Join-Path $ScriptsDir 'lib\merge-gate.psm1') -Force

function Assert-True([bool]$Condition, [string]$Case) {
    if (-not $Condition) { throw "${Case}: condition was false" }
}

$root = Join-Path ([IO.Path]::GetTempPath()) "deckle-merge-gate-$([guid]::NewGuid())"
# The fixture repositories read neither the machine's global nor its system git
# configuration, whose hooks may refuse a commit by another author.
$savedEnv = @{}
foreach ($pair in @{ GIT_CONFIG_GLOBAL = 'NUL'; GIT_CONFIG_NOSYSTEM = '1'; GIT_AUTHOR_NAME = 'Someone'; GIT_AUTHOR_EMAIL = 'someone@example.com';
        GIT_COMMITTER_NAME = 'Someone'; GIT_COMMITTER_EMAIL = 'someone@example.com' }.GetEnumerator()) {
    $savedEnv[$pair.Key] = [Environment]::GetEnvironmentVariable($pair.Key)
    [Environment]::SetEnvironmentVariable($pair.Key, $pair.Value)
}
try {
    # --- Which apps a change reaches ----------------------------------------
    # A small src/ tree: Core under every app, Views under Folders only, and a
    # Lone project no app references.
    $repo = Join-Path $root 'apps'
    function New-Project([string]$Name, [string[]]$References = @()) {
        $dir = Join-Path $repo "src\$Name"
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $items = ($References | ForEach-Object { "    <ProjectReference Include=`"..\$_\$_.csproj`" />" }) -join "`n"
        Set-Content -LiteralPath (Join-Path $dir "$Name.csproj") -Value "<Project>`n  <ItemGroup>`n$items`n  </ItemGroup>`n</Project>"
    }
    New-Project 'Deckle.Core'
    New-Project 'Deckle.Folders.Views' @('Deckle.Core')
    New-Project 'Deckle.Lone'
    New-Project 'Deckle.App' @('Deckle.Core')
    New-Project 'Deckle.Folders.App' @('Deckle.Folders.Views')
    New-Project 'Deckle.Mail.App' @('Deckle.Core')
    New-Project 'Deckle.Projects.App' @('Deckle.Core')

    $apps = @(Get-GateTouchedApps -RepoRoot $repo -ChangedPaths @('src/Deckle.Folders.Views/Groups.cs'))
    Assert-True ($apps.Count -eq 1 -and $apps[0].App -eq 'folders') 'a views project reaches the one app that references it, through a project between'
    Assert-True ($apps[0].Because -eq 'src/Deckle.Folders.Views/Groups.cs') 'the app names the path that reached it'

    $apps = @(Get-GateTouchedApps -RepoRoot $repo -ChangedPaths @('src\Deckle.Core\Clock.cs'))
    Assert-True (($apps.App -join ',') -eq 'deckle,folders,mail,projects') 'a project under every app reaches all four, read with either slash'

    $apps = @(Get-GateTouchedApps -RepoRoot $repo -ChangedPaths @('src/Deckle.Lone/Thing.cs'))
    Assert-True (($apps.App -join ',') -eq 'deckle') 'a project no app references reaches the published Deckle'

    $apps = @(Get-GateTouchedApps -RepoRoot $repo -ChangedPaths @('Directory.Packages.props'))
    Assert-True ($apps.Count -eq 4) 'a build file at the root reaches all four apps'

    $apps = @(Get-GateTouchedApps -RepoRoot $repo -ChangedPaths @('scripts/self-check.ps1', 'tests/Deckle.Core.Tests/ClockTests.cs', 'AGENTS.md'))
    Assert-True ($apps.Count -eq 0) 'scripts, tests and docs reach no app'

    $apps = @(Get-GateTouchedApps -RepoRoot $repo -ChangedPaths @('src/Deckle.Folders.Views/A.cs', 'src/Deckle.Core/B.cs'))
    Assert-True ((($apps | Where-Object App -eq 'folders').Because) -eq 'src/Deckle.Folders.Views/A.cs') 'an app keeps the first path that reached it'

    # --- Unstable checks -----------------------------------------------------
    $redRun = [pscustomobject]@{ tree = 't1'; ran = @('tests'); failed = @('tests: Suite.Flaky') }
    $greenRun = [pscustomobject]@{ tree = 't1'; ran = @('tests', 'scripts', 'self-check projects'); failed = @() }
    $unstable = @(Get-GateUnstableChecks -Tree 't1' -History @($redRun) -Current $greenRun)
    Assert-True (($unstable -join ',') -eq 'tests: Suite.Flaky') 'a test red before and green now on the same tree is unstable'

    $unstable = @(Get-GateUnstableChecks -Tree 't1' -History @($greenRun) -Current $redRun)
    Assert-True (($unstable -join ',') -eq 'tests: Suite.Flaky') 'a test green before and red now on the same tree is unstable'

    $unstable = @(Get-GateUnstableChecks -Tree 't1' -History @($redRun) -Current $redRun)
    Assert-True ($unstable.Count -eq 0) 'a test red twice is red, not unstable'

    $otherTree = [pscustomobject]@{ tree = 't0'; ran = @('tests'); failed = @('tests: Suite.Flaky') }
    $unstable = @(Get-GateUnstableChecks -Tree 't1' -History @($otherTree) -Current $greenRun)
    Assert-True ($unstable.Count -eq 0) 'a red on another tree is history, not instability: the tree changed'

    $stoppedEarly = [pscustomobject]@{ tree = 't1'; ran = @('tests'); failed = @('tests: Suite.A') }
    $reachedFurther = [pscustomobject]@{ tree = 't1'; ran = @('tests', 'self-check projects'); failed = @('tests: Suite.A', 'self-check projects: the walk') }
    $unstable = @(Get-GateUnstableChecks -Tree 't1' -History @($stoppedEarly) -Current $reachedFurther)
    Assert-True ($unstable.Count -eq 0) 'a step a run never reached counts as neither passed nor failed there'

    # --- The table -----------------------------------------------------------
    $table = @(Format-GateTable -Rows @(
            [pscustomobject]@{ Check = 'Build'; Proof = '12 s'; State = 'green' },
            [pscustomobject]@{ Check = 'Tests'; Proof = 'a | b'; State = 'red' },
            [pscustomobject]@{ Check = 'Self-check'; Proof = 'not run'; State = 'never seen' }))
    Assert-True ($table[0] -eq '| Check | How it was checked | State |') 'the table has its three columns'
    Assert-True ($table.Count -eq 5 -and $table[4] -eq '| Self-check | not run | never seen |') 'one line per check, after the header'
    Assert-True ($table[3] -eq '| Tests | a / b | red |') 'a pipe in a proof does not break the table'
    $threw = $false
    try { Format-GateTable -Rows @([pscustomobject]@{ Check = 'x'; Proof = 'y'; State = 'flaky' }) | Out-Null } catch { $threw = $true }
    Assert-True $threw 'a state other than green, red or never seen is refused'

    # --- Refusals before anything is built -----------------------------------
    # A repository with origin, its main checkout and a branch in a worktree of
    # its own, the scripts copied in: every refusal below happens before a build.
    $origin = Join-Path $root 'origin.git'
    $main = Join-Path $root 'main'
    $work = Join-Path $root 'work'
    & git init --quiet --bare --initial-branch=main $origin
    & git clone --quiet $origin $main 2>$null
    & git -C $main checkout --quiet -b main 2>$null
    New-Item -ItemType Directory -Force -Path (Join-Path $main 'scripts\lib') | Out-Null
    Copy-Item -LiteralPath $Script -Destination (Join-Path $main 'scripts')
    Copy-Item -LiteralPath (Join-Path $ScriptsDir 'lib\merge-gate.psm1') -Destination (Join-Path $main 'scripts\lib')
    & git -C $main add -A
    & git -C $main commit --quiet -m 'start'
    & git -C $main push --quiet origin main 2>$null
    & git -C $main worktree add --quiet -b feat/thing $work 2>$null
    $gate = Join-Path $work 'scripts\merge-branch.ps1'
    function Invoke-Gate([string[]]$Arguments) {
        $text = @(& pwsh -NoProfile -File $gate @Arguments 2>&1 6>&1) -join "`n"
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = $text }
    }

    $result = Invoke-Gate @('-Target', $main, '-NoMerge')
    Assert-True ($result.ExitCode -eq 2 -and $result.Text -match 'main is where branches land') 'main itself is not a branch to merge'

    $result = Invoke-Gate @('-MainCheckout', $main)
    Assert-True ($result.ExitCode -eq 2 -and $result.Text -match '-Summary') 'a merge without its summary is refused before any build'

    Set-Content -LiteralPath (Join-Path $work 'note.txt') -Value 'draft'
    $result = Invoke-Gate @('-NoMerge')
    Assert-True ($result.ExitCode -eq 2 -and $result.Text -match 'uncommitted') 'a worktree with uncommitted changes is refused: the gate judges a commit'
    & git -C $work add -A
    & git -C $work commit --quiet -m 'work'

    # origin's main moves on: the branch no longer holds it.
    Set-Content -LiteralPath (Join-Path $main 'other.txt') -Value 'other'
    & git -C $main add -A
    & git -C $main commit --quiet -m 'other'
    & git -C $main push --quiet origin main 2>$null
    $result = Invoke-Gate @('-MainCheckout', $main, '-Summary', 'a thing')
    Assert-True ($result.ExitCode -eq 2 -and $result.Text -match 'does not contain origin/main') 'a branch that misses origin''s main is refused before any build'
    Assert-True ($result.Text -notmatch 'building') 'the refusal comes before the build'

    $result = Invoke-Gate @('-Branch', 'feat/none', '-NoMerge')
    Assert-True ($result.ExitCode -eq 2 -and $result.Text -match 'No worktree holds feat/none') 'a branch no worktree holds is refused'

    Write-Host 'merge-branch.tests.ps1: PASS' -ForegroundColor Green
}
finally {
    foreach ($key in $savedEnv.Keys) { [Environment]::SetEnvironmentVariable($key, $savedEnv[$key]) }
    if (Test-Path -LiteralPath $root) {
        & git -C (Join-Path $root 'main') worktree remove --force (Join-Path $root 'work') 2>$null
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}
