<#
.SYNOPSIS
    Show running builds and Deckle instances
.DESCRIPTION
    Lists, read-only, the Deckle instances running on this machine with the
    folder each runs from, then the dotnet builds and tests in progress with
    their project and age, the build scripts waiting for a slot, and the .NET
    build servers left behind. Answers « what holds the build lock » and « is
    the resident Deckle up, and from where » without hand-written process
    queries.
.NOTES
    section: Run
    order: 140
    action: Inspect / processes |
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot

# The primary checkout holds prod\; a worktree's own folder never does, so the
# prod copy is found from the common git directory, not from this checkout.
$commonDir = & git -C $RepoRoot rev-parse --path-format=absolute --git-common-dir 2>$null
$primaryRoot = if ($LASTEXITCODE -eq 0 -and $commonDir) { Split-Path -Parent ([string]$commonDir).Trim() } else { $RepoRoot }
$prodRoot = Join-Path $primaryRoot 'prod'

function Format-Age {
    param([Nullable[datetime]]$Since)

    if ($null -eq $Since) { return '?' }
    $span = (Get-Date) - $Since
    if ($span.TotalMinutes -lt 1) { return '{0}s' -f [int]$span.TotalSeconds }
    if ($span.TotalHours -lt 1) { return '{0}m' -f [int][Math]::Floor($span.TotalMinutes) }
    if ($span.TotalDays -lt 1) { return '{0}h{1:00}' -f [int][Math]::Floor($span.TotalHours), $span.Minutes }
    return '{0}d' -f [int][Math]::Floor($span.TotalDays)
}

# A path under the prod copy, a worktree or the primary checkout reads better
# by the folder that names it than as a full path.
function Get-PlaceName {
    param([string]$Path)

    if (-not $Path) { return '(path unreadable)' }
    $full = [IO.Path]::GetFullPath($Path)
    if ($full.StartsWith($prodRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { return 'prod copy' }
    $match = [regex]::Match($full, '\\worktrees\\[^\\]+\\(?<name>[^\\]+)\\', 'IgnoreCase')
    if ($match.Success) { return "worktree $($match.Groups['name'].Value)" }
    if ($full.StartsWith($primaryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { return 'primary checkout' }
    return $full
}

function Get-ProjectName {
    param([string]$CommandLine)

    $match = [regex]::Match($CommandLine, '(?<path>[^\s"]+\.(csproj|sln|slnx))', 'IgnoreCase')
    if ($match.Success) { return [IO.Path]::GetFileName($match.Groups['path'].Value) }
    return '(no project named)'
}

$names = @('Deckle.exe', 'dotnet.exe', 'MSBuild.exe', 'VBCSCompiler.exe', 'testhost.exe', 'pwsh.exe')
$filter = ($names | ForEach-Object { "Name='$_'" }) -join ' or '
$processes = @(Get-CimInstance Win32_Process -Filter $filter)

$deckle = @()
$builds = @()
$waiting = @()
$servers = @()
$hosts = @()
foreach ($process in $processes) {
    $line = [string]$process.CommandLine
    $row = [pscustomobject]@{
        Id   = $process.ProcessId
        Age  = Format-Age -Since $process.CreationDate
        What = ''
    }
    switch ($process.Name) {
        'Deckle.exe' {
            $mode = if ($line -match '--self-check') { 'self-check' } else { 'running' }
            $row.What = "$mode, $(Get-PlaceName -Path $process.ExecutablePath)"
            $deckle += $row
        }
        'testhost.exe' {
            $row.What = 'test host'
            $hosts += $row
        }
        'VBCSCompiler.exe' { $row.What = 'Roslyn compiler server'; $servers += $row }
        'MSBuild.exe' { $row.What = 'MSBuild node'; $servers += $row }
        'dotnet.exe' {
            if ($line -match 'VBCSCompiler\.dll') { $row.What = 'Roslyn compiler server'; $servers += $row }
            elseif ($line -match 'MSBuild\.dll.*nodemode|/nodemode') { $row.What = 'MSBuild node'; $servers += $row }
            elseif ($line -match 'testhost') { $row.What = 'test host'; $hosts += $row }
            elseif ($line -match '\s(?<verb>build|test|publish|run)\s') {
                $row.What = "dotnet $($Matches['verb']) $(Get-ProjectName -CommandLine $line)"
                $builds += $row
            }
        }
        'pwsh.exe' {
            # Only the repository's build entry points: other shells are noise.
            if ($line -match 'scripts[\\/](?<script>build-locked|build-run|self-check|deploy-prod)\.ps1') {
                $row.What = "$($Matches['script']).ps1 $(Get-ProjectName -CommandLine $line)"
                $waiting += $row
            }
        }
    }
}

function Write-Group {
    param([string]$Title, [object[]]$Rows, [string]$Empty)

    Write-Host ''
    Write-Host $Title -ForegroundColor Cyan
    if ($Rows.Count -eq 0) {
        Write-Host "  $Empty" -ForegroundColor DarkGray
        return
    }
    foreach ($row in ($Rows | Sort-Object Id)) {
        Write-Host ('  {0,7}  {1,6}  {2}' -f $row.Id, $row.Age, $row.What)
    }
}

Write-Host "Repo: $RepoRoot" -ForegroundColor DarkGray
Write-Host '  PID  = process id; age = time since it started.' -ForegroundColor DarkGray
Write-Group -Title 'Deckle instances' -Rows $deckle -Empty 'none running'
Write-Group -Title 'Builds and tests in progress' -Rows $builds -Empty 'none'
Write-Group -Title 'Build scripts (a build-locked.ps1 without a dotnet child waits for a slot)' -Rows $waiting -Empty 'none'
Write-Group -Title 'Test hosts' -Rows $hosts -Empty 'none'
Write-Group -Title '.NET build servers (stopped after five idle minutes by build-server-watchdog.ps1)' -Rows $servers -Empty 'none'
