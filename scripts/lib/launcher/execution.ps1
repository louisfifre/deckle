# Execution of a discovered Action: the script runs in this process under the
# repository root, every stream it emits is captured into the Journal through
# the action log, and the Execution View follows the run.
#
# Running in-process rather than in a child pwsh keeps the host messages
# (roles, colours) that the scripts emit through the output library, which a
# redirected child would flatten to text. The price is that a script cannot
# be cancelled mid-run: the launcher reads no input until it ends.

. (Join-Path $PSScriptRoot 'action-log.ps1')

$script:JournalPaintInterval = [timespan]::FromMilliseconds(80)

function Get-LauncherInvocation {
    param(
        [Parameter(Mandatory)][object]$Action,
        [Parameter(Mandatory)][string]$ScriptsRoot
    )

    $path = Join-Path $ScriptsRoot ($Action.ScriptRelativePath -replace '/', [IO.Path]::DirectorySeparatorChar)
    # The arguments come verbatim from the script's own Header, written by
    # the maintainer, so they are read as PowerShell syntax on purpose.
    $text = "& '$($path -replace "'", "''")'"
    if (-not [string]::IsNullOrWhiteSpace($Action.Arguments)) { $text += " $($Action.Arguments)" }
    if ($Action.Variant) { $text += " -$($Action.VariantParameter) '$($Action.Variant -replace "'", "''")'" }
    return [pscustomobject]@{ Path = $path; CommandText = $text }
}

function ConvertTo-LauncherJournalLine {
    param([Parameter(Mandatory)][object]$Record)

    $role = switch ($Record.Level) {
        'Error' { 'Error' }
        'Warning' { 'Warning' }
        'Step' { 'Section' }
        'Summary' { 'PanelTitle' }
        default {
            $segmentRoles = @($Record.Segments | ForEach-Object Role | Where-Object { $_ } | Select-Object -Unique)
            if ($segmentRoles.Count -eq 1 -and $segmentRoles[0] -eq 'Muted') { 'Supporting' }
            elseif ($segmentRoles.Count -eq 1 -and $segmentRoles[0] -eq 'Success') { 'Success' }
            else { 'Body' }
        }
    }
    return [pscustomobject]@{ Text = $Record.Message; PresentationRole = $role }
}

function New-LauncherTrackingSteps {
    param([Parameter(Mandatory)][ValidateSet('Starting', 'Running', 'Completed', 'Failed')][string]$Phase)

    $run = switch ($Phase) { 'Starting' { 'Pending' } 'Running' { 'Running' } default { 'Completed' } }
    $conclude = switch ($Phase) { 'Completed' { 'Completed' } 'Failed' { 'Failed' } default { 'Pending' } }
    return @(
        [pscustomobject]@{ Label = 'Start the script'; State = if ($Phase -eq 'Starting') { 'Running' } else { 'Completed' } }
        [pscustomobject]@{ Label = 'Run'; State = $run }
        [pscustomobject]@{ Label = 'Conclude'; State = $conclude }
    )
}

function New-LauncherExecutionRecord {
    param(
        [Parameter(Mandatory)][object]$Action,
        [Parameter(Mandatory)][string]$OwnerActionMenuId,
        [Parameter(Mandatory)][int]$Sequence
    )

    $variantLabel = if ($Action.Variant) { " / $($Action.Variant)" } else { '' }
    return [pscustomobject][ordered]@{
        PSTypeName = 'DeckleScripts.Launcher.ExecutionRecord'
        Sequence = $Sequence
        ViewId = "execution.$($Action.ActionId).$Sequence"
        Action = $Action
        Context = "$($Action.Label)$variantLabel"
        OwnerActionMenuId = $OwnerActionMenuId
        StartedAt = Get-Date
        EndedAt = $null
        State = 'Running'
        Phase = 'Starting'
        Result = $null
        Journal = [System.Collections.Generic.List[object]]::new()
    }
}

function ConvertTo-LauncherExecutionView {
    param(
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.Launcher.ExecutionRecord')][object]$Record,
        [Parameter(Mandatory)][string]$Banner,
        # The Logs reopen a run under the root menu, not under the folder
        # menu that started it, so the owner is a choice of the caller.
        [string]$OwnerActionMenuId = $Record.OwnerActionMenuId,
        [object]$BackTarget = (New-LauncherBackTarget)
    )

    return New-TerminalExecutionView `
        -ViewId $Record.ViewId `
        -Banner $Banner `
        -Context $Record.Context `
        -State $Record.State `
        -JournalLines @($Record.Journal) `
        -TrackingSteps (New-LauncherTrackingSteps -Phase $Record.Phase) `
        -Result $Record.Result `
        -BackTarget $BackTarget `
        -OwnerActionMenuId $OwnerActionMenuId
}

function Get-LauncherExecutionResult {
    param(
        [AllowNull()][string]$Failure,
        [int]$ExitCode,
        [AllowNull()][string]$ReportedResult
    )

    if ($Failure) { return [pscustomobject]@{ State = 'Failed'; Result = "Failed: $Failure" } }
    if ($ExitCode -ne 0) { return [pscustomobject]@{ State = 'Failed'; Result = "Failed with exit code $ExitCode" } }
    # The script's own summary is the canonical conclusion when it gives one.
    switch ($ReportedResult) {
        'Failed' { return [pscustomobject]@{ State = 'Failed'; Result = 'Failed' } }
        'Partial' { return [pscustomobject]@{ State = 'Completed'; Result = 'Partial' } }
        'Skipped' { return [pscustomobject]@{ State = 'Completed'; Result = 'Skipped' } }
        default { return [pscustomobject]@{ State = 'Completed'; Result = 'Success' } }
    }
}

function Invoke-LauncherExecution {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.Launcher.ExecutionRecord')][object]$Record,
        [Parameter(Mandatory)][string]$ScriptsRoot,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        # Called with the record after each change worth painting.
        [scriptblock]$Publish = {}
    )

    $invocation = Get-LauncherInvocation -Action $Record.Action -ScriptsRoot $ScriptsRoot
    $collector = New-ActionLogCollector -Source $Record.Action.ScriptRelativePath
    $Record.Journal.Add([pscustomobject]@{ Text = "[launcher] $($invocation.CommandText)"; PresentationRole = 'Supporting' })
    $Record.Phase = 'Running'
    & $Publish $Record

    $lastPaint = [datetime]::UtcNow
    $failure = $null
    $exitCode = 0
    if (-not (Test-Path -LiteralPath $invocation.Path -PathType Leaf)) {
        $failure = "Script not found: $($invocation.Path)"
    } else {
        Push-Location -LiteralPath $RepositoryRoot
        try {
            $global:LASTEXITCODE = 0
            $block = [scriptblock]::Create($invocation.CommandText)
            & $block *>&1 | ForEach-Object {
                foreach ($entry in @(Add-ActionLogItem -Collector $collector -InputObject $_)) {
                    $Record.Journal.Add((ConvertTo-LauncherJournalLine -Record $entry))
                }
                # Painting every line would slow a chatty script; the Journal
                # follows at a human pace and catches up at the end.
                if (([datetime]::UtcNow - $lastPaint) -gt $script:JournalPaintInterval) {
                    & $Publish $Record
                    $lastPaint = [datetime]::UtcNow
                }
            }
            if ($null -ne $LASTEXITCODE) { $exitCode = [int]$LASTEXITCODE }
        } catch {
            $failure = $_.Exception.Message
        } finally {
            Pop-Location
        }
    }
    foreach ($entry in @(Complete-ActionLog -Collector $collector)) {
        $Record.Journal.Add((ConvertTo-LauncherJournalLine -Record $entry))
    }
    if ($failure) {
        $Record.Journal.Add([pscustomobject]@{ Text = "[launcher] $failure"; PresentationRole = 'Error' })
    }

    $conclusion = Get-LauncherExecutionResult -Failure $failure -ExitCode $exitCode -ReportedResult $collector.ReportedResult
    $Record.State = $conclusion.State
    $Record.Result = $conclusion.Result
    $Record.Phase = $conclusion.State
    $Record.EndedAt = Get-Date
    & $Publish $Record
    return $Record
}
