$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
Import-Module (Join-Path $LibDir 'resource-inventory.psm1') -Force

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$fixture = Join-Path ([System.IO.Path]::GetTempPath()) ("deckle-resource-test-" + [guid]::NewGuid())
try {
    $app = Join-Path $fixture 'src/Deckle.App'
    $module = Join-Path $fixture 'src/Deckle.Sample'
    New-Item -ItemType Directory -Force (Join-Path $app 'Strings/en-US'), (Join-Path $module 'Strings/en-US') | Out-Null
    Set-Content (Join-Path $app 'Deckle.App.csproj') '<Project />'
    Set-Content (Join-Path $module 'Deckle.Sample.csproj') '<Project />'
    Set-Content (Join-Path $app 'Root.cs') 'var title = Loc.Get("Shared_Title");'
    Set-Content (Join-Path $module 'View.cs') 'var placeholder = Loc.GetFromOptional("Deckle.Sample", "OptionalBox/PlaceholderText");'
    Set-Content (Join-Path $module 'View.xaml') '<TextBlock x:Uid="LocalLabel" />'
    Set-Content (Join-Path $app 'Strings/en-US/Resources.resw') @'
<root><data name="Shared_Title"><value>Root</value></data></root>
'@
    Set-Content (Join-Path $module 'Strings/en-US/Resources.resw') @'
<root>
  <data name="LocalLabel.Text"><value>Local</value></data>
  <data name="OptionalBox.PlaceholderText"><value>Optional</value></data>
  <data name="DescriptorLabel"><value>Sample</value></data>
  <data name="Shared_Title"><value>Different</value></data>
  <data name="Unused_Copy"><value>Unused</value></data>
</root>
'@
    $allowlist = Join-Path $fixture 'allowlist.json'
    Set-Content $allowlist @'
{
  "RequiredKeys": [
    { "Assembly": "Deckle.Sample", "Key": "DescriptorLabel", "Reason": "Fixture for a dynamic cross-assembly contract." }
  ]
}
'@

    $result = Invoke-ResourceInventory -RepoRoot $fixture -AllowlistPath $allowlist
    Assert-Equal 0 @($result.Missing).Count 'missing keys'
    Assert-Equal 2 @($result.PotentiallyUnused).Count 'unused copies'
    Assert-Equal 0 @($result.Divergences).Count 'unused mirror is not required locally'

    # An engine with no map of its own, presented by a Views project that holds
    # its strings: what the engine reads is a use of the Views map, and the
    # mirror check covers it.
    $engine = Join-Path $fixture 'src/Deckle.Engine'
    $views = Join-Path $fixture 'src/Deckle.Engine.Views'
    New-Item -ItemType Directory -Force $engine, (Join-Path $views 'Strings/en-US') | Out-Null
    Set-Content (Join-Path $engine 'Deckle.Engine.csproj') '<Project />'
    Set-Content (Join-Path $views 'Deckle.Engine.Views.csproj') '<Project />'
    Set-Content (Join-Path $engine 'Engine.cs') 'var status = Loc.Get("Engine_Status");'
    Set-Content (Join-Path $app 'Strings/en-US/Resources.resw') @'
<root>
  <data name="Shared_Title"><value>Root</value></data>
  <data name="Engine_Status"><value>Working</value></data>
</root>
'@
    $viewsResw = Join-Path $views 'Strings/en-US/Resources.resw'
    Set-Content $viewsResw @'
<root>
  <data name="Engine_Status"><value>Working</value></data>
  <data name="Engine_Orphan"><value>Orphan</value></data>
</root>
'@
    $paired = Invoke-ResourceInventory -RepoRoot $fixture -AllowlistPath $allowlist
    Assert-Equal 0 @($paired.Missing).Count 'engine and views: missing keys'
    Assert-Equal 3 @($paired.PotentiallyUnused).Count 'engine and views: only the orphan is added'
    Assert-Equal 0 @($paired.PotentiallyUnused | Where-Object Key -eq 'Engine_Status').Count 'a key the engine reads is used'
    Assert-Equal 0 @($paired.Divergences).Count 'engine and views: mirror in step'

    (Get-Content -Raw $viewsResw).Replace('<value>Working</value>', '<value>Drifted</value>') | Set-Content $viewsResw
    $drifted = Invoke-ResourceInventory -RepoRoot $fixture -AllowlistPath $allowlist
    Assert-Equal 1 @($drifted.Divergences).Count 'a drifted copy of a key the engine reads'
    Assert-Equal 'Engine_Status' $drifted.Divergences[0].Key 'drifted key'

    $moduleResw = Join-Path $module 'Strings/en-US/Resources.resw'
    @(Get-Content $moduleResw | Where-Object { $_ -notmatch 'name="DescriptorLabel"' }) |
        Set-Content $moduleResw
    $missingRequired = Invoke-ResourceInventory -RepoRoot $fixture -AllowlistPath $allowlist
    Assert-Equal 1 @($missingRequired.Missing).Count 'missing required key'
    Assert-Equal 'required allowlist' $missingRequired.Missing[0].Reference 'required key source'
} finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}

Write-Host 'resource-inventory.tests.ps1: PASS' -ForegroundColor Green
