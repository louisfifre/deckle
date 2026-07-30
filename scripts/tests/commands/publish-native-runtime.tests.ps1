$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CommandDir = Join-Path $ScriptsDir 'commands'
$LibDir = Join-Path $ScriptsDir 'lib'

$scriptPath = Join-Path $CommandDir 'publish-native-runtime.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    $scriptPath,
    [ref]$tokens,
    [ref]$parseErrors)
if ($parseErrors.Count) {
    throw "publish-native-runtime.ps1 does not parse: $($parseErrors[0].Message)"
}

$commands = @($ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.CommandAst]
}, $true))

foreach ($command in $commands) {
    $name = $command.GetCommandName()
    $text = $command.Extent.Text
    if ($name -ceq 'dotnet') {
        throw "Native publication must not invoke a Deckle build: $text"
    }
    if ($name -ceq 'cmake' -and $text -match '(?i)(^|\s)--build(\s|$)') {
        throw "Native publication must consume the existing whisper.cpp build: $text"
    }
    if ($name -ceq 'ninja' -and $text -notmatch '(?i)--version') {
        throw "Native publication must not invoke Ninja: $text"
    }
    if ($name -ceq 'gh' -and $text -match '(?i)\brelease\s+(create|view|edit|download)\b' -and
        $text -notmatch '(?i)(^|\s)--repo(\s|$)') {
        throw "Every GitHub release operation must name its repository: $text"
    }
    if ($name -ceq 'gh' -and $text -match '(?i)\brelease\s+create\b' -and
        $text -notmatch '(?i)(^|\s)--draft(\s|$)') {
        throw "Native release upload must begin as an explicit draft: $text"
    }
}

Write-Host 'publish-native-runtime.tests.ps1 passed' -ForegroundColor Green
