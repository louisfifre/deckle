$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
$LauncherDir = Join-Path $LibDir 'launcher'
. (Join-Path $LibDir 'script-output.ps1')
. (Join-Path $LauncherDir 'action-log.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$workflow = New-DeckleWorkflowOutput -Category 'build'
$fragments = @(& {
    Write-DeckleWorkflowStep -Output $workflow -Message 'dotnet build (Release x64)'
} 6>&1)
$collector = New-DeckleActionLogCollector -Source Build

Add-DeckleActionLogItem -Collector $collector -InputObject $fragments[0] | Out-Null
Assert-Equal 0 $collector.Records.Count 'an empty breathing line does not create a log record'
Add-DeckleActionLogItem -Collector $collector -InputObject $fragments[1] | Out-Null
Assert-Equal 0 $collector.Records.Count 'a no-newline category remains pending'
Add-DeckleActionLogItem -Collector $collector -InputObject $fragments[2] | Out-Null
Assert-Equal 1 $collector.Records.Count 'the terminating fragment completes one logical line'
Assert-Equal '[build] dotnet build (Release x64)' $collector.Records[0].Message 'incremental fragments retain their complete message'
Assert-Equal 'Category' $collector.Records[0].Segments[0].Role 'the category role survives incremental collection'
Assert-Equal 'Heading' $collector.Records[0].Segments[1].Role 'the heading role survives incremental collection'

$pending = @(& {
    Write-DeckleOutputFragment -Text 'unfinished' -Role Action -NoNewline
} 6>&1)[0]
$pendingCollector = New-DeckleActionLogCollector -Source Build
Add-DeckleActionLogItem -Collector $pendingCollector -InputObject $pending | Out-Null
Assert-Equal 0 $pendingCollector.Records.Count 'an incomplete host fragment is not rendered early'
Complete-DeckleActionLog -Collector $pendingCollector | Out-Null
Assert-Equal 1 $pendingCollector.Records.Count 'completion flushes the last incomplete logical line'
Assert-Equal 'Action' $pendingCollector.Records[0].Segments[0].Role 'completion keeps the pending fragment role'

$duplicates = New-DeckleActionLogCollector -Source Build
Add-DeckleActionLogItem -Collector $duplicates -InputObject 'same line' | Out-Null
Add-DeckleActionLogItem -Collector $duplicates -InputObject 'same line' | Out-Null
Assert-Equal 2 $duplicates.Records.Count 'identical adjacent native-style lines remain distinct'

Write-Host 'action-log.tests.ps1: PASS' -ForegroundColor Green
