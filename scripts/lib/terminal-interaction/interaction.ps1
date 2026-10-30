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
        'Execute' {
            # The Execution View is installed first, so the runner paints
            # into a View that already owns the top of the stack.
            $state = New-TerminalViewState -View $Decision.View
            if ($Decision.Mode -eq 'Replace') {
                $ViewStack[$ViewStack.Count - 1] = $state
            } else {
                $ViewStack.Add($state)
            }
            return $false
        }
        default { throw "Unknown transition decision '$($Decision.Kind)'." }
    }
}

function Get-TerminalStateFrame {
    param(
        [Parameter(Mandatory)][System.Collections.Generic.List[object]]$ViewStack,
        [Parameter(Mandatory)][object]$State,
        [Parameter(Mandatory)][object]$HostState,
        [AllowEmptyCollection()][string[]]$Logo = @()
    )

    $metrics = Get-TerminalHostMetrics
    $ownerActionMenu = Get-TerminalOwnerActionMenu -ViewStack $ViewStack -View $State.View
    return Get-TerminalInteractionFrame `
        -View $State.View `
        -OwnerActionMenu $ownerActionMenu `
        -Width $metrics.Width `
        -Height $metrics.Height `
        -FocusedTargetId $State.FocusedTargetId `
        -SupportsUnicode ($HostState.UnicodeOutput -eq 'Supported') `
        -ColorCapability $HostState.Color `
        -BodyOffset $State.BodyOffset `
        -JournalOffset $State.JournalOffset `
        -Logo $Logo
}

function Invoke-TerminalExecution {
    param(
        [Parameter(Mandatory)][System.Collections.Generic.List[object]]$ViewStack,
        [Parameter(Mandatory)][object]$Decision,
        [Parameter(Mandatory)][object]$HostState,
        [AllowNull()][object]$PreviousFrame,
        [AllowEmptyCollection()][string[]]$Logo = @()
    )

    # The run owns the terminal until it ends: no input is read, the Journal
    # follows its latest page, and keys pressed meanwhile are dropped so they
    # never activate a target of the finished View.
    $state = $ViewStack[$ViewStack.Count - 1]
    $lastFrame = $PreviousFrame
    $paint = {
        param($view)
        $state.View = $view
        $state.JournalOffset = [int]::MaxValue
        $frame = Get-TerminalStateFrame -ViewStack $ViewStack -State $state -HostState $HostState -Logo $Logo
        Write-TerminalInteractionFrame -Frame $frame -HostState $HostState -PreviousFrame $lastFrame
        $lastFrame = $frame
    }.GetNewClosure()
    & $paint $state.View
    $final = & $Decision.Runner $paint
    if ($null -ne $final) {
        $state.View = $final
        $state.JournalOffset = [int]::MaxValue
    }
    $state.FocusedTargetId = $null
    Clear-TerminalHostInput -State $HostState
    return $lastFrame
}

function Get-TerminalEscapeDecision {
    param(
        [Parameter(Mandatory)][object]$View,
        [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$ViewDepth
    )

    if ($View.Kind -eq 'Execution' -and $View.State -eq 'Running') {
        return [pscustomobject]@{ Kind = 'Stay' }
    }
    if ($ViewDepth -gt 1) {
        return [pscustomobject]@{ Kind = 'Back' }
    }
    if ($View.Kind -eq 'ActionMenu' -and $null -eq $View.BackTarget) {
        return [pscustomobject]@{ Kind = 'Exit' }
    }
    return [pscustomobject]@{ Kind = 'Stay' }
}

function Test-TerminalControlC {
    param([Parameter(Mandatory)][ConsoleKeyInfo]$KeyInfo)

    return $KeyInfo.Key -eq [ConsoleKey]::C -and ($KeyInfo.Modifiers -band [ConsoleModifiers]::Control)
}

function Get-TerminalOwnerActionMenu {
    param(
        [Parameter(Mandatory)][System.Collections.Generic.List[object]]$ViewStack,
        [Parameter(Mandatory)][object]$View
    )

    if ($View.Kind -eq 'ActionMenu') { return $View }
    for ($index = $ViewStack.Count - 1; $index -ge 0; $index--) {
        $candidate = $ViewStack[$index].View
        if ($candidate.Kind -eq 'ActionMenu' -and $candidate.ViewId -eq $View.OwnerActionMenuId) {
            return $candidate
        }
    }
    throw "Owning Action Menu '$($View.OwnerActionMenuId)' for View '$($View.ViewId)' is absent from the interaction stack."
}

function Start-TerminalInteraction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$RootView,
        [Parameter(Mandatory)][scriptblock]$IntentHandler,
        [ValidateSet('Terminal', 'TrueColor')][string]$Palette = 'Terminal',
        # Lines drawn above the Header of every frame when the terminal
        # affords them.
        [AllowEmptyCollection()][string[]]$Logo = @()
    )

    $viewStack = [System.Collections.Generic.List[object]]::new()
    $viewStack.Add((New-TerminalViewState -View $RootView))
    $hostState = Start-TerminalHost -Palette $Palette
    try {
        $exitRequested = $false
        $previousFrame = $null
        while (-not $exitRequested) {
            $metrics = Get-TerminalHostMetrics
            $currentState = $viewStack[$viewStack.Count - 1]
            $ownerActionMenu = Get-TerminalOwnerActionMenu -ViewStack $viewStack -View $currentState.View
            $frame = Get-TerminalInteractionFrame `
                -View $currentState.View `
                -OwnerActionMenu $ownerActionMenu `
                -Width $metrics.Width `
                -Height $metrics.Height `
                -FocusedTargetId $currentState.FocusedTargetId `
                -SupportsUnicode ($hostState.UnicodeOutput -eq 'Supported') `
                -ColorCapability $hostState.Color `
                -BodyOffset $currentState.BodyOffset `
                -JournalOffset $currentState.JournalOffset `
                -Logo $Logo

            $initialFocus = Get-TerminalInitialFocus -Frame $frame
            $hasCurrentFocus = $false
            if ($currentState.FocusedTargetId) {
                $hasCurrentFocus = @($frame.Targets | Where-Object { $_.TargetId -eq $currentState.FocusedTargetId -and $_.Target.Enabled }).Count -gt 0
            }
            if (-not $hasCurrentFocus -and $initialFocus) {
                $currentState.FocusedTargetId = $initialFocus
                $frame = Get-TerminalInteractionFrame `
                    -View $currentState.View `
                    -OwnerActionMenu $ownerActionMenu `
                    -Width $metrics.Width `
                    -Height $metrics.Height `
                    -FocusedTargetId $currentState.FocusedTargetId `
                    -SupportsUnicode ($hostState.UnicodeOutput -eq 'Supported') `
                    -ColorCapability $hostState.Color `
                    -BodyOffset $currentState.BodyOffset `
                    -JournalOffset $currentState.JournalOffset `
                    -Logo $Logo
            }
            Write-TerminalInteractionFrame -Frame $frame -HostState $hostState -PreviousFrame $previousFrame
            $previousFrame = $frame

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
                    $decision = Get-TerminalEscapeDecision `
                        -View $currentState.View `
                        -ViewDepth $viewStack.Count
                    $exitRequested = Set-TerminalDecision -ViewStack $viewStack -Decision $decision
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
                            $nextOffset = Move-TerminalJournalPage -Frame $frame -CurrentOffset $normalizedOffset -Direction $direction
                            $currentState.JournalOffset = $nextOffset
                        } else {
                            $lastOffset = [Math]::Max(0, $frame.BodyLineCount - $frame.BodyPageSize)
                            $normalizedOffset = [Math]::Min($currentState.BodyOffset, $lastOffset)
                            $nextOffset = Move-TerminalBodyPage -Frame $frame -CurrentOffset $normalizedOffset -Direction $direction
                            $currentState.BodyOffset = $nextOffset
                        }
                        $currentState.FocusedTargetId = Get-TerminalPagingFocusTargetId `
                            -Direction $direction `
                            -Offset $nextOffset `
                            -LastOffset $lastOffset
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
                    if ($null -ne $decision -and $decision.Kind -eq 'Execute') {
                        $previousFrame = Invoke-TerminalExecution -ViewStack $viewStack -Decision $decision -HostState $hostState -PreviousFrame $previousFrame -Logo $Logo
                    }
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
