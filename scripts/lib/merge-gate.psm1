# The decisions of the merge gate (scripts/merge-branch.ps1), kept apart from
# the runs so its tests can feed them plain values: which apps a change
# reaches, which checks proved unstable on the same tree, and the three-state
# table that ends every gate run.

# The five executables of the suite, as scripts/self-check.ps1 names them.
$script:GateApps = [ordered]@{
    deckle   = 'Deckle.App'
    folders  = 'Deckle.Folders.App'
    mail     = 'Deckle.Mail.App'
    projects = 'Deckle.Projects.App'
    browser  = 'Deckle.Browser.App'
}

# The project names an app compiles in: its own and every project its
# ProjectReference items reach, followed transitively.
function Get-GateProjectClosure {
    param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string]$Project)

    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $queue = [System.Collections.Generic.Queue[string]]::new()
    $queue.Enqueue((Join-Path $RepoRoot "src\$Project\$Project.csproj"))
    while ($queue.Count -gt 0) {
        $path = [IO.Path]::GetFullPath($queue.Dequeue())
        $name = [IO.Path]::GetFileNameWithoutExtension($path)
        if (-not $seen.Add($name) -or -not (Test-Path -LiteralPath $path)) { continue }
        $text = Get-Content -Raw -LiteralPath $path
        foreach ($match in [regex]::Matches($text, '<ProjectReference\s+Include="([^"]+)"')) {
            $queue.Enqueue((Join-Path (Split-Path -Parent $path) $match.Groups[1].Value))
        }
    }
    return $seen
}

# Which apps the changed paths reach, each with the first path that reached it.
# A file under src/<Project>/ reaches every app whose closure holds <Project>;
# a project no app references reaches the published Deckle, the host that
# composes the modules. A build file at the root of the repository or of src/
# reaches all five. Anything else (tests, scripts, docs) reaches none: the
# suites cover it, and no window changes.
function Get-GateTouchedApps {
    param([Parameter(Mandatory)][string]$RepoRoot, [string[]]$ChangedPaths = @())

    $closures = [ordered]@{}
    foreach ($app in $script:GateApps.Keys) {
        $closures[$app] = Get-GateProjectClosure -RepoRoot $RepoRoot -Project $script:GateApps[$app]
    }
    $touched = [ordered]@{}
    foreach ($raw in $ChangedPaths) {
        $path = $raw -replace '\\', '/'
        $reached = @()
        if ($path -match '^(src/)?[^/]+\.(props|targets)$' -or $path -match '^(global\.json|nuget\.config)$') {
            $reached = @($script:GateApps.Keys)
        }
        elseif ($path -match '^src/([^/]+)/') {
            $project = $Matches[1]
            $reached = @($closures.Keys | Where-Object { $closures[$_].Contains($project) })
            if ($reached.Count -eq 0) { $reached = @('deckle') }
        }
        foreach ($app in $reached) {
            if (-not $touched.Contains($app)) { $touched[$app] = $path }
        }
    }
    return @($script:GateApps.Keys | Where-Object { $touched.Contains($_) } | ForEach-Object {
            [pscustomobject]@{ App = $_; Because = $touched[$_] }
        })
}

# The checks that proved unstable on this tree: a check is unstable when it
# failed in one run of the tree and passed in another. A run records the steps
# it reached (Ran) and the checks that failed (Failed, each "<step>: <name>"),
# so a step a run never reached counts as neither passed nor failed there.
function Get-GateUnstableChecks {
    param(
        [Parameter(Mandatory)][string]$Tree,
        [object[]]$History = @(),
        [Parameter(Mandatory)][object]$Current
    )

    $runs = @(@($History | Where-Object { $_ -and $_.tree -eq $Tree }) + $Current)
    $unstable = [System.Collections.Generic.List[string]]::new()
    $names = @($runs | ForEach-Object { @($_.failed) } | Where-Object { $_ } | Sort-Object -Unique)
    foreach ($name in $names) {
        $step = ($name -split ': ', 2)[0]
        $passedSomewhere = @($runs | Where-Object { @($_.ran) -contains $step -and @($_.failed) -notcontains $name }).Count -gt 0
        if ($passedSomewhere) { $unstable.Add($name) }
    }
    return @($unstable)
}

# The table that ends a gate run, in Markdown so an agent pastes it as is into
# its report. Each row is a check with its proof and one of three states:
# green, red, never seen.
function Format-GateTable {
    param([Parameter(Mandatory)][object[]]$Rows)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('| Check | How it was checked | State |')
    $lines.Add('| --- | --- | --- |')
    foreach ($row in $Rows) {
        if ($row.State -notin 'green', 'red', 'never seen') { throw "A gate row's state is green, red or never seen, not '$($row.State)'." }
        $lines.Add("| $($row.Check) | $($row.Proof -replace '\|', '/') | $($row.State) |")
    }
    return @($lines)
}

Export-ModuleMember -Function Get-GateTouchedApps, Get-GateUnstableChecks, Format-GateTable
