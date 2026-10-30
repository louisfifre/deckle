# script-discovery.psm1 - Public facade for reading a scripts folder into a catalog.

Set-StrictMode -Version 2.0

$ModuleRoot = $PSScriptRoot
. (Join-Path $ModuleRoot 'script-discovery\key-value-lines.ps1')
. (Join-Path $ModuleRoot 'script-discovery\front-matter.ps1')
. (Join-Path $ModuleRoot 'script-discovery\header.ps1')
. (Join-Path $ModuleRoot 'script-discovery\catalog.ps1')

Export-ModuleMember -Function `
    Read-KeyValueLines, `
    ConvertTo-KeyValueTable, `
    Get-MarkdownFrontMatter, `
    Read-ScriptHeader, `
    Get-ScriptCatalog, `
    Get-CatalogHiddenScripts, `
    Get-CatalogRows
