using System.Text;
using Xunit;

namespace Deckle.Projects.Tests;

// The wrapper is text, so what must survive the trip into PowerShell and
// through wt.exe is checked on the text.
[Trait("Category", "unit")]
public sealed class RunWrapperTests
{
    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("O'Brien", "'O''Brien'")]
    [InlineData("a;b & c", "'a;b & c'")]
    [InlineData("caf\u00E9 \u00FC\u4E2D", "'caf\u00E9 \u00FC\u4E2D'")]
    [InlineData("it\u2019s", "'it\u2019\u2019s'")]
    [InlineData("\u2018x\u201A\u201B", "'\u2018\u2018x\u201A\u201A\u201B\u201B'")]
    [InlineData("", "''")]
    public void QuotingDoublesEveryCharacterPowerShellReadsAsAQuote(string value, string expected)
    {
        Assert.Equal(expected, RunWrapper.Quote(value));
    }

    private static RunWrapperInput Input(string project = @"C:\sample\alpha", string id = "build-run.1", string? variant = null) =>
        new("Build & run \u00B7 alpha (1a2b)", @"C:\data\runs\7-1a2b", @"C:\Program Files\PowerShell\7\pwsh.exe",
            Path.Combine(project, "scripts", "menu.ps1"), id, variant);

    [Fact]
    public void TheScriptWritesItsProcessIdRunsTheMenuThenWritesTheExitCodeAndWaitsForAKey()
    {
        string[] lines = RunWrapper.Build(Input()).Split('\n');

        Assert.Equal("$Host.UI.RawUI.WindowTitle = 'Build & run \u00B7 alpha (1a2b)'", lines[0]);
        Assert.Equal(@"[IO.File]::WriteAllText('C:\data\runs\7-1a2b\started.txt', [string]$PID)", lines[1]);
        Assert.Equal(@"& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -ExecutionPolicy Bypass -File 'C:\sample\alpha\scripts\menu.ps1' -Run 'build-run.1'", lines[2]);
        Assert.StartsWith("$code = $LASTEXITCODE;", lines[3]);
        Assert.Contains(@"result.tmp", lines[4]);
        Assert.Contains(@"Move-Item 'C:\data\runs\7-1a2b\result.tmp' 'C:\data\runs\7-1a2b\result.txt' -Force", lines[4]);
        Assert.Contains("Press any key", lines[5]);
        Assert.StartsWith("try { [void][Console]::ReadKey($true) } catch { }", lines[6]);
        Assert.Equal("exit 0", lines[^1]);
    }

    [Fact]
    public void AVariantIsPassedAndAnActionWithoutOneHasNoVariantArgument()
    {
        Assert.EndsWith("-Run 'self-check.0' -Variant 'Debug'", RunWrapper.Build(Input(id: "self-check.0", variant: "Debug")).Split('\n')[2]);
        Assert.DoesNotContain("-Variant", RunWrapper.Build(Input()));
    }

    [Fact]
    public void ApostrophesSemicolonsAmpersandsAndNonAsciiInThePathAndTheIdArriveIntact()
    {
        string project = "C:\\O'Brien; Caf\u00E9 & Co\u00FC";
        var input = Input(project, id: "a;b'c.0", variant: "it's \"v\"");

        string[] lines = RunWrapper.Build(input).Split('\n');

        Assert.Contains("'C:\\O''Brien; Caf\u00E9 & Co\u00FC\\scripts\\menu.ps1'", lines[2]);
        Assert.Contains("-Run 'a;b''c.0'", lines[2]);
        Assert.Contains("-Variant 'it''s \"v\"'", lines[2]);
    }

    [Fact]
    public void TheScriptIsFiveOnePointOneSafe()
    {
        string script = RunWrapper.Build(Input());

        Assert.DoesNotContain("??", script);
        Assert.DoesNotContain(".Parent", script);
    }

    [Fact]
    public void TheEncodedFormIsBase64OfUtf16AndRoundTrips()
    {
        string script = RunWrapper.Build(Input(@"C:\O'Brien; x"));

        string encoded = RunWrapper.Encode(script);

        Assert.DoesNotContain(';', encoded);
        Assert.Equal(script, Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
    }

    [Fact]
    public void TheMenuScriptOnlyOpensTheMenu()
    {
        Assert.Equal(@"& 'C:\p\pwsh.exe' -NoProfile -ExecutionPolicy Bypass -File 'C:\a\scripts\menu.ps1'",
            RunWrapper.BuildMenu(@"C:\p\pwsh.exe", @"C:\a\scripts\menu.ps1"));
    }
}
