# Stateful interaction loop. Repository work remains behind the intent handler.

function New-TerminalViewState {
    param([Parameter(Mandatory)][object]$View)

    return [pscustomobject][ordered]@{
        View = $View
        FocusedTargetId = $null
        BodyOffset = 0
        JournalOffset = if ($View.Kind -eq 'Execution') { [int]::MaxValue } else { 0 }
    }
}

function Set-TerminalDecision {
    param(
        [Parameter(Mandatory)][System.Collections.Generic.List[object]]$ViewStack,
        [object]$Decision
    )

    if ($null -eq $Decision -or $Decision.Kind -eq 'Stay') { return $false }
    switch ($Decision.Kind) {
        'OpenView' {
            $ViewStack.Add((New-TerminalViewState -View $Decision.View))
            return $false
        }
        'ReplaceView' {
            $ViewStack[$ViewStack.Count - 1] = New-TerminalViewState -View $Decision.View
            return $false
        }
        'UpdateView' {
            $ViewStack[$ViewStack.Count - 1].View = $Decision.View
            return $false
        }
        'Back' {
            if ($ViewStack.Count -gt 1) { $ViewStack.RemoveAt($ViewStack.Count - 1) }
            return $false
        }
        'Exit' { return $true }
        default { throw "Unknown transition decision '$($Decision.Kind)'." }
    }
}

function Move-TerminalToOwningActionMenu {
    param([Parameter(Mandatory)][System.Collections.Generic.List[object]]$ViewStack)

    $current = $ViewStack[$ViewStack.Count - 1].View
    if ($current.Kind -eq 'ActionMenu') { return }
    for ($index = $ViewStack.Count - 1; $index -ge 0; $index--) {
        if ($ViewStack[$index].View.ViewId -eq $current.OwnerActionMenuId) {
            while ($ViewStack.Count -gt $index + 1) { $ViewStack.RemoveAt($ViewStack.Count - 1) }
            return
        }
    }
    while ($ViewStack.Count -gt 1) { $ViewStack.RemoveAt($ViewStack.Count - 1) }
}

function Test-TerminalControlC {
    param([Parameter(Mandatory)][ConsoleKeyInfo]$KeyInfo)

    return $KeyInfo.Key -eq [ConsoleKey]::C -and ($KeyInfo.Modifiers -band [ConsoleModifiers]::Control)
}

function Start-TerminalInteraction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$RootView,
        [Parameter(Mandatory)][scriptblock]$IntentHandler
    )

    $viewStack = [System.Collections.Generic.List[object]]::new()
    $viewStack.Add((New-TerminalViewState -View $RootView))
    $hostState = Start-TerminalHost
    try {
        $exitRequested = $false
        while (-not $exitRequested) {
            $metrics = Get-TerminalHostMetrics
            $currentState = $viewStack[$viewStack.Count - 1]
            $frame = Get-TerminalInteractionFrame `
                -View $currentState.View `
                -Width $metrics.Width `
                -Height $metrics.Height `
                -FocusedTargetId $currentState.FocusedTargetId `
                -SupportsUnicode ($hostState.UnicodeOutput -eq 'Supported') `
                -BodyOffset $currentState.BodyOffset `
                -JournalOffset $currentState.JournalOffset

            $initialFocus = Get-TerminalInitialFocus -Frame $frame
            $hasCurrentFocus = $false
            if ($currentState.FocusedTargetId) {
                $hasCurrentFocus = @($frame.Targets | Where-Object { $_.TargetId -eq $currentState.FocusedTargetId -and $_.Target.Enabled }).Count -gt 0
            }
            if (-not $hasCurrentFocus -and $initialFocus) {
                $currentState.FocusedTargetId = $initialFocus
                $frame = Get-TerminalInteractionFrame `
                    -View $currentState.View `
                    -Width $metrics.Width `
                    -Height $metrics.Height `
                    -FocusedTargetId $currentState.FocusedTargetId `
                    -SupportsUnicode ($hostState.UnicodeOutput -eq 'Supported') `
                    -BodyOffset $currentState.BodyOffset `
                    -JournalOffset $currentState.JournalOffset
            }
            Write-TerminalInteractionFrame -Frame $frame -HostState $hostState

            $inputEvent = Read-TerminalHostEvent -State $hostState
            $kind = $inputEvent.Kind.ToString()
            if ($kind -eq 'Resize') { continue }
            if ($kind -eq 'Wheel') {
                if ($currentState.View.Kind -eq 'Execution' -and $frame.JournalPageSize -gt 0) {
                    $lastOffset = [Math]::Max(0, $frame.JournalLineCount - $frame.JournalPageSize)
                    $normalizedOffset = [Math]::Min($currentState.JournalOffset, $lastOffset)
                    $direction = if ($inputEvent.WheelDelta -gt 0) { 'Previous' } else { 'Next' }
                    $currentState.JournalOffset = Move-TerminalJournalPage -Frame $frame -CurrentOffset $normalizedOffset -Direction $direction
                } elseif ($frame.BodyPageSize -gt 0 -and $frame.BodyLineCount -gt $frame.BodyPageSize) {
                    $lastOffset = [Math]::Max(0, $frame.BodyLineCount - $frame.BodyPageSize)
                    $normalizedOffset = [Math]::Min($currentState.BodyOffset, $lastOffset)
                    $direction = if ($inputEvent.WheelDelta -gt 0) { 'Previous' } else { 'Next' }
                    $currentState.BodyOffset = Move-TerminalBodyPage -Frame $frame -CurrentOffset $normalizedOffset -Direction $direction
                }
                continue
            }
            if ($kind -ne 'Key') { continue }

            $key = $inputEvent.KeyInfo
            if (Test-TerminalControlC -KeyInfo $key) {
                $exitRequested = $true
                continue
            }
            switch ($key.Key.ToString()) {
                'UpArrow' {
                    $currentState.FocusedTargetId = Move-TerminalFocus -Frame $frame -CurrentTargetId $currentState.FocusedTargetId -Direction Up
                }
                'DownArrow' {
                    $currentState.FocusedTargetId = Move-TerminalFocus -Frame $frame -CurrentTargetId $currentState.FocusedTargetId -Direction Down
                }
                'LeftArrow' {
                    $currentState.FocusedTargetId = Move-TerminalFocus -Frame $frame -CurrentTargetId $currentState.FocusedTargetId -Direction Left
                }
                'RightArrow' {
                    $currentState.FocusedTargetId = Move-TerminalFocus -Frame $frame -CurrentTargetId $currentState.FocusedTargetId -Direction Right
                }
                'PageUp' {
                    if ($currentState.View.Kind -eq 'Execution') {
                        $lastOffset = [Math]::Max(0, $frame.JournalLineCount - $frame.JournalPageSize)
                        $normalizedOffset = [Math]::Min($currentState.JournalOffset, $lastOffset)
                        $currentState.JournalOffset = Move-TerminalJournalPage -Frame $frame -CurrentOffset $normalizedOffset -Direction Previous
                    } elseif ($frame.BodyPageSize -gt 0) {
                        $lastOffset = [Math]::Max(0, $frame.BodyLineCount - $frame.BodyPageSize)
                        $normalizedOffset = [Math]::Min($currentState.BodyOffset, $lastOffset)
                        $currentState.BodyOffset = Move-TerminalBodyPage -Frame $frame -CurrentOffset $normalizedOffset -Direction Previous
                    }
                }
                'PageDown' {
                    if ($currentState.View.Kind -eq 'Execution') {
                        $lastOffset = [Math]::Max(0, $frame.JournalLineCount - $frame.JournalPageSize)
                        $normalizedOffset = [Math]::Min($currentState.JournalOffset, $lastOffset)
                        $currentState.JournalOffset = Move-TerminalJournalPage -Frame $frame -CurrentOffset $normalizedOffset -Direction Next
                    } elseif ($frame.BodyPageSize -gt 0) {
                        $lastOffset = [Math]::Max(0, $frame.BodyLineCount - $frame.BodyPageSize)
                        $normalizedOffset = [Math]::Min($currentState.BodyOffset, $lastOffset)
                        $currentState.BodyOffset = Move-TerminalBodyPage -Frame $frame -CurrentOffset $normalizedOffset -Direction Next
                    }
                }
                'Home' {
                    if ($currentState.View.Kind -eq 'Execution') { $currentState.JournalOffset = 0 }
                    else { $currentState.BodyOffset = 0 }
                }
                'End' {
                    if ($currentState.View.Kind -eq 'Execution') {
                        $currentState.JournalOffset = Move-TerminalJournalPage -Frame $frame -CurrentOffset $currentState.JournalOffset -Direction Last
                    } else {
                        $currentState.BodyOffset = Move-TerminalBodyPage -Frame $frame -CurrentOffset $currentState.BodyOffset -Direction Last
                    }
                }
                'Backspace' {
                    if ($currentState.View.Kind -ne 'Execution' -or $currentState.View.State -ne 'Running') {
                        if ($null -ne $currentState.View.BackTarget -and $currentState.View.BackTarget.Enabled -and $viewStack.Count -gt 1) {
                            $viewStack.RemoveAt($viewStack.Count - 1)
                        }
                    }
                }
                'Escape' {
                    if ($currentState.View.Kind -ne 'Execution' -or $currentState.View.State -ne 'Running') {
                        Move-TerminalToOwningActionMenu -ViewStack $viewStack
                    }
                }
                'Enter' {
                    $target = Get-TerminalFocusedTarget -Frame $frame -FocusedTargetId $currentState.FocusedTargetId
                    if ($null -eq $target -or -not $target.Enabled) { continue }
                    $navigationCommand = $null
                    if ($target.IntentKind -eq 'Navigation' -and $null -ne $target.Payload) {
                        $commandProperty = $target.Payload.PSObject.Properties['Command']
                        if ($null -ne $commandProperty) { $navigationCommand = [string]$commandProperty.Value }
                    }
                    if ($navigationCommand -eq 'Page') {
                        $direction = $target.Payload.PageDirection
                        if ($currentState.View.Kind -eq 'Execution') {
                            $lastOffset = [Math]::Max(0, $frame.JournalLineCount - $frame.JournalPageSize)
                            $normalizedOffset = [Math]::Min($currentState.JournalOffset, $lastOffset)
                            $currentState.JournalOffset = Move-TerminalJournalPage -Frame $frame -CurrentOffset $normalizedOffset -Direction $direction
                        } else {
                            $lastOffset = [Math]::Max(0, $frame.BodyLineCount - $frame.BodyPageSize)
                            $normalizedOffset = [Math]::Min($currentState.BodyOffset, $lastOffset)
                            $currentState.BodyOffset = Move-TerminalBodyPage -Frame $frame -CurrentOffset $normalizedOffset -Direction $direction
                        }
                        continue
                    }
                    if ($navigationCommand -eq 'Back') {
                        if ($viewStack.Count -gt 1) { $viewStack.RemoveAt($viewStack.Count - 1) }
                        continue
                    }
                    $request = [pscustomobject][ordered]@{
                        TargetId = $target.TargetId
                        IntentKind = $target.IntentKind
                        Payload = $target.Payload
                        SourceViewId = $currentState.View.ViewId
                        Activation = 'Enter'
                    }
                    $decision = & $IntentHandler $request $currentState.View
                    $exitRequested = Set-TerminalDecision -ViewStack $viewStack -Decision $decision
                }
                'Spacebar' {
                    $target = Get-TerminalFocusedTarget -Frame $frame -FocusedTargetId $currentState.FocusedTargetId
                    if ($null -eq $target -or -not $target.Enabled -or $target.SelectionMode -ne 'Multiple') { continue }
                    $request = [pscustomobject][ordered]@{
                        TargetId = $target.TargetId
                        IntentKind = $target.IntentKind
                        Payload = $target.Payload
                        SourceViewId = $currentState.View.ViewId
                        Activation = 'Space'
                    }
                    $decision = & $IntentHandler $request $currentState.View
                    $exitRequested = Set-TerminalDecision -ViewStack $viewStack -Decision $decision
                }
            }
        }
    } finally {
        Stop-TerminalHost -State $hostState
    }
}
