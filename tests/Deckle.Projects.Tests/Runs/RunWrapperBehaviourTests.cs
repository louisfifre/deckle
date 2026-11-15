using System.Diagnostics;
using Xunit;

namespace Deckle.Projects.Tests;

// The wrapper run for real by PowerShell, hidden and with no input: no terminal
// opens. It is what proves the two files Deckle follows an action by, and that
// the key wait at the end does not hold a redirected shell. Each test skips with
// its reason without pwsh.
[Trait("Category", "integration")]
public sealed class RunWrapperBehaviourTests
{
    private static string? Shell => ShellLocator.Find();

    private sealed record Ran(string RunFolder, int ShellExit, bool TimedOut);

    private static async Task<Ran> RunWrapperAgainst(ScratchFolder scratch, string menuBody, string actionId = "stub.0", string? variant = null)
    {
        string project = scratch.Folder("project");
        string menu = scratch.File(menuBody, "project", "scripts", "menu.ps1");
        string runFolder = scratch.Folder("run");
        string script = RunWrapper.Build(new RunWrapperInput("Stub · project (0000)", runFolder, Shell!, menu, actionId, variant));

        var start = new ProcessStartInfo(Shell!)
        {
            WorkingDirectory = project,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(RunWrapper.Encode(script));

        using Process process = Process.Start(start)!;
        // Nothing is ever typed: a wait for a key must not hold the shell.
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            process.Kill(entireProcessTree: true);
        }
        await Task.WhenAll(output, error);
        return new Ran(runFolder, timedOut ? -99 : process.ExitCode, timedOut);
    }

    [Theory(SkipUnless = nameof(Tools.PwshPresent), SkipType = typeof(Tools), Skip = "pwsh is not on the PATH")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task TheExitCodeOfTheActionIsWrittenAndThePidIsRecorded(int code)
    {
        using var scratch = new ScratchFolder();

        Ran ran = await RunWrapperAgainst(scratch, $"exit {code}");

        Assert.False(ran.TimedOut);
        Assert.Equal(0, ran.ShellExit);
        Assert.Equal(code, int.Parse(File.ReadAllText(Path.Combine(ran.RunFolder, RunWrapper.ResultFile)).Trim()));
        Assert.True(int.Parse(File.ReadAllText(Path.Combine(ran.RunFolder, RunWrapper.StartedFile)).Trim()) > 0);
        Assert.False(File.Exists(Path.Combine(ran.RunFolder, "result.tmp")));
    }

    [Fact(SkipUnless = nameof(Tools.PwshPresent), SkipType = typeof(Tools), Skip = "pwsh is not on the PATH")]
    public async Task AnActionKilledMidRunWritesANonZeroCode()
    {
        using var scratch = new ScratchFolder();

        Ran ran = await RunWrapperAgainst(scratch, "Stop-Process -Id $PID -Force");

        Assert.False(ran.TimedOut);
        int code = int.Parse(File.ReadAllText(Path.Combine(ran.RunFolder, RunWrapper.ResultFile)).Trim());
        Assert.NotEqual(0, code);
    }

    [Fact(SkipUnless = nameof(Tools.PwshPresent), SkipType = typeof(Tools), Skip = "pwsh is not on the PATH")]
    public async Task TheActionIdAndVariantReachTheMenuIntact()
    {
        using var scratch = new ScratchFolder();
        string record = Path.Combine(scratch.Path, "args.txt");
        string body = $"[IO.File]::WriteAllText('{record}', ($args -join '|'))";

        await RunWrapperAgainst(scratch, body, actionId: "it's;odd.0", variant: "De bug");

        Assert.Equal("-Run|it's;odd.0|-Variant|De bug", File.ReadAllText(record));
    }
}
