# apply-update.ps1 - Swap a staged build into the prod copy, then start it.
#
# Ships in every build beside Deckle.exe (Prod\apply-update.ps1), so the
# helper always matches the app that starts it. scripts/deploy-prod.ps1 copies
# the staged build's own copy to the prod copy root, and Deckle starts it from
# there (src/Deckle.App/ProdUpdate.cs) when current\Deckle.exe starts while
# next\ holds a staged build. It never runs from inside current\: it renames
# that folder.
#
# Written for Windows PowerShell 5.1, which every Windows 11 ships: the local
# copy must update on a machine where PowerShell 7 is absent or broken. ASCII
# only for the same reason, since 5.1 reads a file without BOM as ANSI.
#
# The swap is three renames, each instant on one volume:
#   previous\ removed, current\ -> previous\, next\ -> current\
# A failure after the first rename puts current\ back, so a start always finds
# a runnable current\. Whatever happens, the script ends by starting
# current\Deckle.exe, the new build or the one in place.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Root,
    # The arguments the outgoing start received, handed on to the new one.
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$AppArguments
)

$ErrorActionPreference = 'Stop'

$current  = Join-Path $Root 'current'
$next     = Join-Path $Root 'next'
$previous = Join-Path $Root 'previous'
$logPath  = Join-Path $Root 'apply-update.log'

function Write-Note([string]$Line) {
    try {
        # Capped: the copy may update daily for years.
        if ((Test-Path -LiteralPath $logPath) -and (Get-Item -LiteralPath $logPath).Length -gt 256KB) {
            Move-Item -LiteralPath $logPath -Destination "$logPath.old" -Force
        }
        Add-Content -LiteralPath $logPath -Value ('{0} | pid={1} | {2}' -f (Get-Date).ToString('o'), $PID, $Line)
    } catch { }
}

# Every Deckle that runs from current\: the start that handed off, and the
# outgoing instance of a restart, which spawned that start before it exited.
function Get-CurrentDeckle {
    $prefix = $current.TrimEnd('\') + '\'
    @(Get-Process -Name Deckle -ErrorAction SilentlyContinue | Where-Object {
        $path = try { $_.Path } catch { $null }
        $path -and $path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
    })
}

# An antivirus or the indexer can hold a freshly closed file for a moment.
function Invoke-WithRetry([scriptblock]$Action) {
    for ($attempt = 1; ; $attempt++) {
        try { & $Action; return }
        catch {
            if ($attempt -ge 10) { throw }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Join-Arguments([string[]]$Values) {
    # Start-Process joins its arguments with spaces and quotes nothing.
    (@($Values) | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ' '
}

Write-Note "apply started | root=$Root"

$deadline = (Get-Date).AddSeconds(60)
do {
    $running = Get-CurrentDeckle
    if ($running.Count -eq 0) { break }
    Start-Sleep -Milliseconds 200
} while ((Get-Date) -lt $deadline)

if ($running.Count -gt 0) {
    # Still running after a minute: nothing is swapped and no second instance
    # is started; the next start of current\ tries again.
    Write-Note ('apply skipped | reason=deckle_still_running | pids={0}' -f (($running | ForEach-Object { $_.Id }) -join ','))
    exit 1
}

$swapped = $false
try {
    if (Test-Path -LiteralPath $previous) {
        Invoke-WithRetry { Remove-Item -LiteralPath $previous -Recurse -Force }
    }
    Invoke-WithRetry { Rename-Item -LiteralPath $current -NewName 'previous' }
    try {
        Invoke-WithRetry { Rename-Item -LiteralPath $next -NewName 'current' }
        $swapped = $true
    } catch {
        Rename-Item -LiteralPath $previous -NewName 'current'
        throw
    }
} catch {
    Write-Note ("apply failed | ex_message={0} | outcome=runs_current" -f $_.Exception.Message)
}

$exe = Join-Path $current 'Deckle.exe'
$version = try { (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion } catch { '' }
if ($swapped) {
    Write-Note "apply completed | version=$version"
} else {
    # next\ is still there, so the start below would hand off again, and again:
    # this start runs the build in place, and the next one retries the swap.
    # ProdUpdate reads and removes the marker, so no successor inherits it.
    $env:DECKLE_PROD_NO_UPDATE = '1'
}

$start = @{ FilePath = $exe; WorkingDirectory = $current }
if (@($AppArguments).Count -gt 0) { $start.ArgumentList = Join-Arguments $AppArguments }
Start-Process @start
