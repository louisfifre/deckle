$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
$FixtureDir = Join-Path $ScriptsDir 'tests\fixtures\script-discovery'
Import-Module (Join-Path $LibDir 'script-discovery.psm1') -Force

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected '$Expected', got '$Actual'" }
}

function Assert-True([bool]$Condition, [string]$Case) {
    if (-not $Condition) { throw "${Case}: condition was false" }
}

function Get-FixtureHeader([string]$RelativePath) {
    return Read-DeckleScriptHeader -Path (Join-Path $FixtureDir $RelativePath)
}

# Discovery reads scripts through the parser only; a script body that writes a
# marker proves nothing ran.
$marker = Join-Path $FixtureDir 'no-header.executed'
if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker -Force }

# Key-value reader shared with front matter.
$pairs = @(Read-DeckleKeyValueLines -Lines @(
    'action: First | -One'
    'Note without a colon is ignored'
    'Action: Second'
    'summary: >'
    '  folded across'
    '    two lines'
    'quoted: "kept inside"'
))
Assert-Equal 4 $pairs.Count 'only key-value lines become pairs'
Assert-Equal 'action' $pairs[0].Key 'keys are read in order'
Assert-Equal 'action' $pairs[1].Key 'a repeated key stays a separate pair and keys are lowercased'
Assert-Equal 'Second' $pairs[1].Value 'the repeated key keeps its own value'
Assert-Equal 'folded across two lines' $pairs[2].Value 'a block scalar folds its lines into one value'
Assert-Equal 'kept inside' $pairs[3].Value 'surrounding quotes are stripped'
$table = ConvertTo-DeckleKeyValueTable -Pairs $pairs
Assert-Equal 'Second' $table['action'] 'the table keeps the last value of a repeated key'

# Front matter null cases.
$scratch = Join-Path ([IO.Path]::GetTempPath()) "script-discovery-$([guid]::NewGuid())"
$null = New-Item -ItemType Directory -Path $scratch
try {
    $noBlock = Join-Path $scratch 'no-block.md'
    Set-Content -LiteralPath $noBlock -Value @('# Title', '', 'name: not front matter') -Encoding utf8NoBOM
    Assert-Equal $null (Get-DeckleMarkdownFrontMatter -Path $noBlock) 'a document without a leading block has no front matter'
    $unclosed = Join-Path $scratch 'unclosed.md'
    Set-Content -LiteralPath $unclosed -Value @('---', 'name: Open', 'body') -Encoding utf8NoBOM
    Assert-Equal $null (Get-DeckleMarkdownFrontMatter -Path $unclosed) 'a block without its closing line has no front matter'
    $empty = Join-Path $scratch 'empty.md'
    Set-Content -LiteralPath $empty -Value @('---', 'plain text', '---') -Encoding utf8NoBOM
    Assert-Equal $null (Get-DeckleMarkdownFrontMatter -Path $empty) 'a block without any field has no front matter'
    Assert-Equal $null (Get-DeckleMarkdownFrontMatter -Path (Join-Path $scratch 'missing.md')) 'a missing file has no front matter'
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
$toolsFrontMatter = Get-DeckleMarkdownFrontMatter -Path (Join-Path $FixtureDir 'tools\README.md')
Assert-Equal 'Tooling' $toolsFrontMatter['name'] 'a folder README names its level'
Assert-Equal '2' $toolsFrontMatter['order'] 'front matter values stay text'

# Header statuses and reasons.
$alpha = Get-FixtureHeader 'alpha.ps1'
Assert-Equal Described $alpha.Status 'a synopsis makes a Header'
Assert-Equal 'Alpha task' $alpha.Label 'the synopsis is the label'
Assert-Equal 'Runs the alpha task over two lines.' $alpha.Description 'description lines join with one space'
Assert-Equal 1 @($alpha.Actions).Count 'a script declares one Action by default'
Assert-Equal 'Alpha task' $alpha.Actions[0].Label 'the default Action takes the synopsis'
Assert-Equal '' $alpha.Actions[0].Arguments 'the default Action has no arguments'
Assert-Equal $null $alpha.Section 'a script without section has none'

$noHeader = Get-FixtureHeader 'no-header.ps1'
Assert-Equal NoHeader $noHeader.Status 'a plain comment is no Header'
Assert-Equal 'no comment-based help block' $noHeader.Reason 'no Header is a choice with a plain reason'

$glued = Get-FixtureHeader 'glued-requires.ps1'
Assert-Equal VoidedHeader $glued.Status 'a block glued under #Requires is voided'
Assert-True ($glued.Reason -match '#Requires') 'the voided reason names the glued #Requires line'

$dotWord = Get-FixtureHeader 'dot-word.ps1'
Assert-Equal VoidedHeader $dotWord.Status 'a dot-word line voids the block'
Assert-True ($dotWord.Reason -match 'dot-word') 'the voided reason names the dot-word cause'

$twoAxes = Get-FixtureHeader 'two-axes.ps1'
Assert-Equal InvalidHeader $twoAxes.Status 'two scalar ValidateSet parameters are invalid'
Assert-True ($twoAxes.Reason -match 'Configuration' -and $twoAxes.Reason -match 'Mode') 'the two-axes reason names both parameters'

# Actions, variants and parameters.
$gamma = Get-FixtureHeader 'gamma.ps1'
Assert-Equal Described $gamma.Status 'action lines keep the Header valid'
Assert-Equal 2 @($gamma.Actions).Count 'each action line declares one Action'
Assert-Equal 'Pack' $gamma.Actions[0].Label 'the first segment is the label'
Assert-Equal '' $gamma.Actions[0].Arguments 'an empty arguments segment stays empty'
Assert-Equal $false $gamma.Actions[0].Danger 'an Action without the danger word is not Danger'
Assert-Equal 'Ship' $gamma.Actions[1].Label 'the second action keeps its label'
Assert-Equal '-Publish' $gamma.Actions[1].Arguments 'the second segment carries the arguments'
Assert-Equal $true $gamma.Actions[1].Danger 'the danger word marks Danger'
Assert-Equal 'Ships the package to the public feed.' $gamma.Actions[1].Confirmation 'a sentence becomes the Confirmation'
Assert-Equal 'Build' $gamma.Section 'section comes from the notes'
Assert-Equal 10 $gamma.Order 'order is an integer'
Assert-Equal 'section,order,action,action' ((@($gamma.Keys) | ForEach-Object Key) -join ',') 'every notes line is kept as an ordered pair'

$beta = Get-FixtureHeader 'beta.ps1'
Assert-Equal Configuration $beta.VariantParameter 'the scalar ValidateSet parameter is the Variant axis'
Assert-Equal 'Release,Debug' ((@($beta.Variants) | ForEach-Object Value) -join ',') 'Variants keep declaration order'
Assert-Equal $true $beta.Variants[0].IsDefault 'the constant default is the default Variant'
Assert-Equal $false $beta.Variants[1].IsDefault 'other values are not default'
$targetParameter = @($beta.Parameters | Where-Object Name -eq 'Target')[0]
Assert-Equal 'string' $targetParameter.TypeName 'parameters keep their type name'
Assert-Equal $true (@($beta.Parameters | Where-Object Name -eq 'Verbose2')[0].IsSwitch) 'switch parameters are flagged'
Assert-Equal 3 @($beta.Parameters).Count 'every parameter is listed'

$arraySet = Get-FixtureHeader 'array-set.ps1'
Assert-Equal Described $arraySet.Status 'an array ValidateSet keeps the Header valid'
Assert-Equal $null $arraySet.VariantParameter 'an array ValidateSet is a Filter, not a Variant axis'
Assert-Equal 0 @($arraySet.Variants).Count 'an array ValidateSet gives no Variants'

# Catalog.
$catalog = Get-DeckleScriptCatalog -Path $FixtureDir
Assert-Equal '' $catalog.RelativePath 'the root level has an empty relative path'
$sections = @($catalog.Sections)
Assert-Equal 3 $sections.Count 'untitled group, Build and Care'
Assert-Equal $null $sections[0].Label 'scripts without section form the first, untitled group'
Assert-Equal 'Build' $sections[1].Label 'a section with an ordered row comes before unordered ones'
Assert-Equal 'Care' $sections[2].Label 'a section without ordered rows comes after'
Assert-Equal 'Alpha task,Array set' ((@($sections[0].Rows) | ForEach-Object Label) -join ',') 'unordered rows sort by label'
Assert-Equal 'Pack,Ship,Beta build' ((@($sections[1].Rows) | ForEach-Object Label) -join ',') 'gamma rows (order 10) come before beta (order 20) in declaration order'
Assert-Equal 1 $sections[1].Rows[1].ActionIndex 'a row keeps its position among the script actions'
Assert-Equal 'gamma.ps1' $sections[1].Rows[0].RelativePath 'rows carry their path relative to the catalog root'
Assert-Equal 'Release,Debug' ((@($sections[1].Rows[2].Variants) | ForEach-Object Value) -join ',') 'beta row carries its Variants'

$accesses = @($catalog.Accesses)
Assert-Equal 'Tooling,zone' ((@($accesses) | ForEach-Object Label) -join ',') 'ordered Accesses come first, a folder without README is labelled by its name'
Assert-Equal 'tools' $accesses[0].RelativePath 'an Access keeps its folder path'
Assert-Equal 'Tools that are rarely used.' $accesses[0].Description 'README front matter describes the level'
Assert-Equal 1 @($accesses[0].Accesses).Count 'a nested folder is a nested Access'
Assert-Equal 'deep' $accesses[0].Accesses[0].Label 'a nested folder without README is labelled by its name'
Assert-Equal 'tools/deep' $accesses[0].Accesses[0].RelativePath 'nested relative paths use forward slashes'
Assert-Equal 'tools/deep/zeta.ps1' $accesses[0].Accesses[0].Sections[0].Rows[0].RelativePath 'nested rows keep their full relative path'

$hiddenNames = (@($catalog.Hidden) | ForEach-Object RelativePath) -join ','
Assert-Equal 'dot-word.ps1,glued-requires.ps1,no-header.ps1,two-axes.ps1' $hiddenNames 'the root level lists the scripts it leaves out'
Assert-Equal 4 @(Get-DeckleCatalogHiddenScripts -Catalog $catalog).Count 'hidden scripts flatten across levels'
Assert-Equal 'InvalidHeader' (@($catalog.Hidden | Where-Object RelativePath -eq 'two-axes.ps1')[0].Status) 'hidden entries keep their status'
Assert-Equal 9 @(Get-DeckleCatalogRows -Catalog $catalog).Count 'every Action Row is reachable depth-first'

Assert-True (-not (Test-Path -LiteralPath $marker)) 'discovery never executes a script'

Write-Host 'script-discovery.tests.ps1: PASS' -ForegroundColor Green
