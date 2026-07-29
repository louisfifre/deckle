# _menu.psm1 — Interactive arrow-key menu utilities.
#
# Public facade kept at the historical path. Implementation lives under
# scripts/lib/menu/ so the terminal session, shared chrome, list picker, and
# grid picker can evolve independently without growing this file again.

Set-StrictMode -Version Latest

$ModuleDir = $PSScriptRoot
. (Join-Path $ModuleDir 'menu\session.ps1')
. (Join-Path $ModuleDir 'menu\input.ps1')
. (Join-Path $ModuleDir 'menu\chrome.ps1')
. (Join-Path $ModuleDir 'menu\list-picker.ps1')
. (Join-Path $ModuleDir 'menu\grid-picker.ps1')
. (Join-Path $ModuleDir 'menu\grid-status.ps1')
. (Join-Path $ModuleDir 'menu\status-view.ps1')
. (Join-Path $ModuleDir 'menu\text-input.ps1')

Export-ModuleMember -Function Start-MenuSession, Stop-MenuSession, Suspend-MenuSession, Select-Worktree, Select-Action, Select-YesNo, Select-Grid, Show-GridStatus, Show-MenuStatus, Read-MenuText
