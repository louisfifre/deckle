$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'resource-inventory.psm1') -Force

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
