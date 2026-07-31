$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
. (Join-Path $LibDir 'script-output.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$output = New-DeckleWorkflowOutput -Category 'build'
Assert-Equal '        ' $output.Indent 'workflow details align with the category message'

$step = @(& { Write-DeckleWorkflowStep -Output $output -Message 'Compile Deckle' } 6>&1)
Assert-Equal 3 $step.Count 'a workflow step keeps its breathing line and two visual segments'
Assert-Equal '[build] ' ([string]$step[1].MessageData.Message) 'category is emitted as its own segment'
Assert-Equal (Get-DeckleOutputColor -Role Category) $step[1].MessageData.ForegroundColor 'category uses the shared semantic color'
Assert-Equal 'Category' $step[1].MessageData.DeckleRole 'category keeps its semantic role independently from color'
Assert-Equal $true ($step[1].Tags -contains 'Deckle.Output') 'semantic output is tagged for launcher collection'
Assert-Equal 'Compile Deckle' ([string]$step[2].MessageData.Message) 'step information is emitted separately'
Assert-Equal (Get-DeckleOutputColor -Role Heading) $step[2].MessageData.ForegroundColor 'step title uses the shared heading color'
Assert-Equal 'Heading' $step[2].MessageData.DeckleRole 'step title keeps its semantic role independently from color'

$action = @(& { Write-DeckleWorkflowAction -Output $output -Message 'Killing Deckle PID 25628' } 6>&1)
Assert-Equal (Get-DeckleOutputColor -Role Action) $action[1].MessageData.ForegroundColor 'an operation in progress uses the action color'

$result = @(& { Write-DeckleWorkflowResult -Output $output -Message 'Build succeeded' } 6>&1)
Assert-Equal (Get-DeckleOutputColor -Role Success) $result[1].MessageData.ForegroundColor 'a confirmed positive outcome uses the success color'

$warning = @(& { Write-DeckleWorkflowMessage -Output $output -Message 'Needs attention' -Role Warning } 6>&1)
Assert-Equal '        ' ([string]$warning[0].MessageData.Message) 'message indentation remains a body segment'
Assert-Equal (Get-DeckleOutputColor -Role Warning) $warning[1].MessageData.ForegroundColor 'important states use their semantic color'

Write-Host 'script-output.tests.ps1: PASS' -ForegroundColor Green
