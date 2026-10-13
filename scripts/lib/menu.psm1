# menu.psm1 — Interactive arrow-key menu utilities.
#
# Module facade: the implementation lives under scripts/lib/menu/, one file per
# concern (terminal session, shared chrome, list picker, grid picker).

Set-StrictMode -Version Latest

$ModuleDir = $PSScriptRoot
. (Join-Path $ModuleDir 'menu\session.ps1')
. (Join-Path $ModuleDir 'menu\input.ps1')
. (Join-Path $ModuleDir 'menu\chrome.ps1')
. (Join-Path $ModuleDir 'menu\grid-layout.ps1')
. (Join-Path $ModuleDir 'menu\list-picker.ps1')
. (Join-Path $ModuleDir 'menu\grid-picker.ps1')
. (Join-Path $ModuleDir 'menu\grid-status.ps1')
. (Join-Path $ModuleDir 'menu\status-view.ps1')
. (Join-Path $ModuleDir 'menu\text-input.ps1')

Export-ModuleMember -Function Start-MenuSession, Stop-MenuSession, Select-Worktree, Select-Action, Select-YesNo, Select-Grid, New-GridStatusView, Update-GridStatusView, Close-GridStatusView, Show-GridStatus, Show-MenuStatus, Read-MenuText
