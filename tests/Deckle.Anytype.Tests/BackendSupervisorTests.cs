using System.IO;
using System.Net;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Behavior of EnsureRunningAsync at its two deterministic gates: no binary on
// disk, and a backend already serving. The spawn/watch/restart paths need a
// real child process and are covered by the live proof runner, not here.
public sealed class BackendSupervisorTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EnsureRunning_reports_not_provisioned_when_the_binary_is_absent()
    {
        var spec = new BackendProcessSpec(
            Path.Combine(Path.GetTempPath(), $"deckle-absent-{Guid.NewGuid():N}.exe"),
            "serve");
        using var supervisor = new BackendSupervisor(spec, new BackendHealthProbe("http://127.0.0.1:1"));

        Assert.Equal(BackendStartOutcome.NotProvisioned, await supervisor.EnsureRunningAsync(Ct));
    }

    [Fact]
    public async Task EnsureRunning_reports_already_running_when_health_answers()
    {
        // A stand-in health endpoint answering 200, and a spec whose binary
        // exists on disk but matches no live process — the warm path returns
        // without spawning or adopting anything.
        using var listener = new HttpListener();
        int port = 34801 + (Environment.CurrentManagedThreadId % 100);
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var serving = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = 200;
            context.Response.Close();
        }, Ct);

        string fakeExe = Path.Combine(Path.GetTempPath(), $"deckle-fake-{Guid.NewGuid():N}.exe");
        await File.WriteAllBytesAsync(fakeExe, [], Ct);
        try
        {
            using var supervisor = new BackendSupervisor(
                new BackendProcessSpec(fakeExe, "serve"),
                new BackendHealthProbe($"http://127.0.0.1:{port}"));

            Assert.Equal(BackendStartOutcome.AlreadyRunning, await supervisor.EnsureRunningAsync(Ct));
            await serving.WaitAsync(Ct);
        }
        finally
        {
            listener.Stop();
            File.Delete(fakeExe);
        }
    }
}
