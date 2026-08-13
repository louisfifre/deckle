# terminal-interaction.psm1 - Public facade for reusable terminal interactions.

Set-StrictMode -Version 2.0

$ModuleRoot = $PSScriptRoot
. (Join-Path $ModuleRoot 'terminal-interaction\contracts.ps1')
. (Join-Path $ModuleRoot 'terminal-interaction\preparation.ps1')
. (Join-Path $ModuleRoot 'terminal-interaction\theme.ps1')
. (Join-Path $ModuleRoot 'terminal-interaction\layout.ps1')
. (Join-Path $ModuleRoot 'terminal-interaction\preparation-layout.ps1')
. (Join-Path $ModuleRoot 'terminal-interaction\rendering.ps1')
. (Join-Path $ModuleRoot 'terminal-interaction\navigation.ps1')
. (Join-Path $ModuleRoot 'terminal-interaction\host.ps1')
. (Join-Path $ModuleRoot 'terminal-interaction\interaction.ps1')

Export-ModuleMember -Function `
    New-TerminalTarget, `
    New-TerminalActionRow, `
    New-TerminalSection, `
    New-TerminalSelectionOption, `
    New-TerminalSelector, `
    New-TerminalEffectiveScope, `
    New-TerminalReview, `
    New-TerminalActionMenuView, `
    New-TerminalPreparationView, `
    New-TerminalContentView, `
    New-TerminalExecutionView, `
    Get-TerminalInteractionFrame, `
    ConvertTo-TerminalFrameText, `
    Move-TerminalFocus, `
    Get-TerminalPresentationStyle, `
    Start-TerminalInteraction
