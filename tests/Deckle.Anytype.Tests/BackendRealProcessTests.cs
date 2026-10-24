using System.Diagnostics;
using System.IO;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The replacement policy is pinned elsewhere against fakes. These tests run the
// same code on real Windows processes, because Process.Kill, HasExited and the
// MainModule read of BackendProcessHost.Open are exactly what a fake cannot
// vouch for. What stays out of reach is the Windows denial for an elevated
// serve: the test host is not elevated against anything.
[Trait("Category", "integration")]
public sealed class BackendRealProcessTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Generous for a kill that the kernel completes in milliseconds, so a loaded
    // machine does not turn a pass into a flake.
    private static readonly TimeSpan ExitBudget = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait("Category", "regression")]
    public async Task Terminating_a_live_process_ends_it_and_reports_no_refusal()
    {
        using var dummy = DummyServe.Start();

        bool ended = dummy.Process.TryTerminate(out string exType, out string exMessage);

        Assert.True(ended);
        Assert.Equal("", exType);
        Assert.Equal("", exMessage);
        await dummy.Process.WaitForExitAsync(Ct).WaitAsync(ExitBudget, Ct);
        Assert.True(dummy.Process.HasExited);
    }

    // Exiting between the caller's check and the kill is the outcome wanted, not
    // a refusal.
    [Fact]
    public async Task Terminating_a_process_that_already_exited_is_not_a_refusal()
    {
        using var dummy = DummyServe.Start();
        dummy.Kill();
        await dummy.Process.WaitForExitAsync(Ct).WaitAsync(ExitBudget, Ct);
        Assert.True(dummy.Process.HasExited);

        bool ended = dummy.Process.TryTerminate(out string exType, out string exMessage);

        Assert.True(ended);
        Assert.Equal("", exType);
        Assert.Equal("", exMessage);
    }

    // The whole chain on a real process: Open reads the path from the live
    // process, the identity is proven against the catalog, the process is ended,
    // and the wait sees it gone. Nothing is provisioned, so nothing is spawned.
    [Fact]
    [Trait("Category", "regression")]
    public async Task A_replace_request_ends_a_real_trusted_process_and_spawns_nothing_when_not_provisioned()
    {
        using var dummy = DummyServe.Start();
        var provider = new FakeBackendProvider(dummy.ExecutablePath) { ActiveSpec = null };
        BackendReconciler reconciler = Create(provider);

        BackendReconciliationResult result = await reconciler.ReconcileAsync(
            BackendTriggers.StartFailed, Ct, replaceProcessId: dummy.Process.Id);

        Assert.Equal(BackendReplaceOutcome.Ended, result.Replacement?.Outcome);
        Assert.True(dummy.Process.HasExited);
        Assert.Equal(BackendStartOutcome.NotProvisioned, result.Outcome);
        Assert.Null(result.Process);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task A_replace_request_leaves_a_real_process_running_when_its_path_is_not_trusted()
    {
        using var dummy = DummyServe.Start();
        // A trusted path that is not the dummy's: the dummy is a stranger.
        string trusted = Path.Combine(dummy.Directory, "trusted-" + Path.GetFileName(dummy.ExecutablePath));
        var provider = new FakeBackendProvider(trusted) { ActiveSpec = null };
        BackendReconciler reconciler = Create(provider);

        BackendReconciliationResult result = await reconciler.ReconcileAsync(
            BackendTriggers.StartFailed, Ct, replaceProcessId: dummy.Process.Id);

        Assert.Equal(BackendReplaceOutcome.Declined, result.Replacement?.Outcome);
        Assert.False(dummy.Process.HasExited);
        Assert.Equal(BackendStartOutcome.NotProvisioned, result.Outcome);
    }

    // Real host and real clock: the termination wait must let real time pass,
    // which the controlled test clock does not.
    private static BackendReconciler Create(FakeBackendProvider provider) => new(
        provider,
        new BackendProcessHost(),
        new ScriptedBackendListener(),
        new FakeBackendHealth(),
        new ImmediateBackendCoordinator(),
        new BackendTime());

    // A harmless process that only waits, started through the production
    // BackendProcessHost.Spawn. FindRunning looks processes up by file name, so
    // the stock ping.exe is copied under a name no other process on the machine
    // carries; a bare ping.exe could be somebody else's. It lives about two
    // minutes at most if a test dies before Dispose.
    private sealed class DummyServe : IDisposable
    {
        public string Directory { get; }
        public string ExecutablePath { get; }
        public IBackendProcess Process { get; }

        private DummyServe(string directory, string executablePath, IBackendProcess process)
        {
            Directory = directory;
            ExecutablePath = executablePath;
            Process = process;
        }

        public static DummyServe Start()
        {
            string directory = Path.Combine(
                Path.GetTempPath(), "deckle-real-process-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            string path = Path.Combine(
                directory, "deckle-wait-" + Guid.NewGuid().ToString("N")[..8] + ".exe");
            try
            {
                File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), path);
                IBackendProcess process = new BackendProcessHost().Spawn(
                        new BackendProcessSpec(path, "-n 120 127.0.0.1"), inRestart: false)
                    ?? throw new InvalidOperationException("the dummy process did not start");
                return new DummyServe(directory, path, process);
            }
            catch
            {
                TryDelete(directory);
                throw;
            }
        }

        public void Kill()
        {
            try
            {
                using var live = System.Diagnostics.Process.GetProcessById(Process.Id);
                live.Kill();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                or System.ComponentModel.Win32Exception)
            {
                // Already gone: that is what cleanup wants.
            }
        }

        public void Dispose()
        {
            Kill();
            try { Process.WaitForExitAsync(CancellationToken.None).Wait(ExitBudget); }
            catch (Exception) { /* best effort: the file delete below tolerates a lock */ }
            Process.Dispose();
            TryDelete(Directory);
        }

        private static void TryDelete(string directory)
        {
            // Windows keeps an exe locked for a moment after its process ended,
            // so the first attempts can fail; a folder left behind in the end is
            // harmless, a failing test is not.
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    System.IO.Directory.Delete(directory, recursive: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}
