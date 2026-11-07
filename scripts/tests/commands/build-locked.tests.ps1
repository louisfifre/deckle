$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Script = Join-Path $ScriptsDir 'build-locked.ps1'

# The script takes the machine-wide build lock and runs dotnet as soon as it
# is called, so these cases ask PowerShell's static binder how a command line
# would bind, without running it.
function Get-Binding([string]$Arguments) {
    $ast = [System.Management.Automation.Language.Parser]::ParseInput("& '$Script' $Arguments", [ref]$null, [ref]$null)
    $command = $ast.Find({ $args[0] -is [System.Management.Automation.Language.CommandAst] }, $true)
    [System.Management.Automation.Language.StaticParameterBinder]::BindCommand($command, $true)
}

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected '$Expected', got '$Actual'" }
}

# Regression (BugExamples/build-locked-positional-dotnet-args.json): an extra
# dotnet argument right after -Project bound to -Configuration and failed.
$binding = Get-Binding '-Project Deckle.Tests.sln /v:q /bl:artifacts/build.binlog'
Assert-Equal $false $binding.BoundParameters.ContainsKey('Configuration') 'a slash switch after -Project never binds to -Configuration'
Assert-Equal 'Deckle.Tests.sln' $binding.BoundParameters['Project'].ConstantValue 'the project is the named one'
Assert-Equal $true $binding.BoundParameters.ContainsKey('DotnetArgs') 'the slash switches reach dotnet'

# A named configuration still binds where it belongs.
$binding = Get-Binding '-Test -Project x.csproj -Configuration Release /v:q'
Assert-Equal 'Release' $binding.BoundParameters['Configuration'].ConstantValue 'a named configuration binds to -Configuration'
Assert-Equal $true $binding.BoundParameters.ContainsKey('Test') 'the -Test switch binds'

Write-Host 'build-locked.tests.ps1: passed' -ForegroundColor Green
