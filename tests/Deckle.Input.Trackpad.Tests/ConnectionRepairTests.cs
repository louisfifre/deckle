using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Deckle.Input.Trackpad;
using Xunit;

namespace Deckle.Input.Trackpad.Tests;

// The repair runs elevated from a file that any process of the user can
// rewrite, so the launcher must refuse a file that is not the embedded script.
// These tests run the generated launcher in a real, non-elevated Windows
// PowerShell against a temp script: the elevation itself is not exercised, the
// hash check and the exit codes are.
public class ConnectionRepairTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"deckle-repair-test-{Guid.NewGuid():N}");

    public ConnectionRepairTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Sha256Of(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private string WriteScript(string name, string text)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    private static int RunLauncher(string scriptPath, string expectedSha256)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = ConnectionRepair.BuildArguments(scriptPath, expectedSha256),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using Process process = Process.Start(psi)!;
        process.StandardInput.Close();   // Read-Host sees end of input at once
        process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "launcher did not exit");
        return process.ExitCode;
    }

    [Fact]
    [Trait("Category", "unit")]
    public void TheCommandLineCarriesAnEncodedLauncherNotTheScriptPath()
    {
        string args = ConnectionRepair.BuildArguments(@"C:\x\it's.ps1", new string('A', 64));

        Assert.StartsWith("-NoProfile -EncodedCommand ", args);
        Assert.DoesNotContain("-File", args);

        string encoded = args["-NoProfile -EncodedCommand ".Length..];
        string launcher = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        Assert.Contains(@"'C:\x\it''s.ps1'", launcher);
        Assert.Contains(new string('A', 64), launcher);
    }

    [Theory]
    [Trait("Category", "unit")]
    [InlineData(1, "script_failed")]
    [InlineData(3, "script_modified")]
    [InlineData(4, "script_unreadable")]
    [InlineData(-1073741510, "script_interrupted")]
    public void AnExitCodeNamesWhyTheRepairDidNotComplete(int code, string reason)
        => Assert.Equal(reason, ConnectionRepair.FailureReason(code));

    [Fact]
    [Trait("Category", "integration")]
    public void ATextMatchingItsHashRunsAndKeepsTheScriptsExitCode()
    {
        string marker = Path.Combine(_dir, "ran.txt");
        string text = $"Set-Content -Path '{marker}' -Value ok\nexit 7\n";
        string script = WriteScript("ok.ps1", text);

        // The hash covers the file bytes, BOM included.
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(script)));
        Assert.NotEqual(Sha256Of(text), hash);

        Assert.Equal(7, RunLauncher(script, hash));
        Assert.True(File.Exists(marker));
    }

    [Fact]
    [Trait("Category", "integration")]
    public void AModifiedFileIsRefusedAndNeverRuns()
    {
        string marker = Path.Combine(_dir, "ran.txt");
        string script = WriteScript("tampered.ps1", $"Set-Content -Path '{marker}' -Value ok\n");
        string shippedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(script)));

        File.AppendAllText(script, "# one more line\n");

        Assert.Equal(ConnectionRepair.ExitScriptModified, RunLauncher(script, shippedHash));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    [Trait("Category", "integration")]
    public void AMissingFileIsRefusedWithItsOwnExitCode()
        => Assert.Equal(
            ConnectionRepair.ExitScriptUnreadable,
            RunLauncher(Path.Combine(_dir, "absent.ps1"), new string('0', 64)));

    // The launcher runs the shipped script as a script block, so it must parse
    // as one; this also catches a syntax slip in the script itself.
    [Fact]
    [Trait("Category", "integration")]
    public void TheShippedScriptParsesWithoutErrors()
    {
        using Stream resource = typeof(ConnectionRepair).Assembly.GetManifestResourceStream(
            "Deckle.Input.Trackpad.Acts.repair-trackpad-connection.ps1")!;
        using var reader = new StreamReader(resource, Encoding.UTF8);
        string path = WriteScript("shipped.ps1", reader.ReadToEnd());

        const string parse =
            "$e = $null; $null = [System.Management.Automation.Language.Parser]::ParseFile($env:PARSE_TARGET, [ref]$null, [ref]$e); " +
            "if ($e) { $e | ForEach-Object { $_.Message }; exit 1 }";
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (string a in new[] { "-NoProfile", "-Command", parse }) psi.ArgumentList.Add(a);
        psi.Environment["PARSE_TARGET"] = path;
        using Process process = Process.Start(psi)!;
        string output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(60_000));
        Assert.True(process.ExitCode == 0, output);
    }
}
