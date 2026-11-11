using System.Diagnostics.Tracing;
using System.IO.Pipes;
using System.Text;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The stop as Deckle's host sees it, with the DLL's side of the pipe played by a
// NamedPipeClientStream. The DLL no longer unloads once XAML Diagnostics
// answered in its target: it parks there for the next Deckle (native
// residency.h), so "parked" is the word that confirms a stop.
[Trait("Category", "integration")]
[Collection(TaskbarObservabilityCollection.Name)]
public sealed class TaskbarEngineHostStopTests : IAsyncDisposable
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private readonly string _name = "Deckle.Taskbar.Tests." + Guid.NewGuid().ToString("N");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "deckle-host-tests-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskbarEngineHost _host;
    private readonly Task _run;
    private readonly TaskCompletionSource _hello = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskbarEngineHostStopTests()
    {
        var server = new TaskbarPipeServer(_name);
        _host = new TaskbarEngineHost("unused.dll", new VersionedNativeCopy(_root), TaskbarTarget.Explorer, server);
        _host.MessageReceived += message => { if (message is NativeHello) _hello.TrySetResult(); };
        _run = Task.Run(() => server.RunAsync(_stop.Token));
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _run; } catch (OperationCanceledException) { }
        await _host.DisposeAsync();
        _stop.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    // Connects as the DLL does, says hello, and on the stop command answers
    // with `lastWords` before it closes the pipe.
    private async Task<Task> PlayDllAsync(params string[] lastWords)
    {
        var client = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync((int)_wait.TotalMilliseconds);
        await WriteAsync(client, """{"t":"hello","protocol":"1","pid":"7","module":"m"}""");
        await _hello.Task.WaitAsync(_wait);

        return Task.Run(async () =>
        {
            using var reader = new StreamReader(client, JsonLineFraming.Utf8, false, 256, leaveOpen: true);
            string? command = await reader.ReadLineAsync().WaitAsync(_wait);
            Assert.Equal(NativeCommand.Stop, command);
            foreach (string word in lastWords)
                await WriteAsync(client, $$$"""{"t":"log","level":"info","event":"{{{word}}}","msg":"m","fields":{}}""");
            await client.DisposeAsync();
        });
    }

    private static Task WriteAsync(Stream client, string line) =>
        client.WriteAsync(Encoding.UTF8.GetBytes(line + "\n")).AsTask();

    private static string? DisconnectOutcome(TestEventListener listener) =>
        listener.Events
            .Where(e => e.EventId == DeckleShellTaskbarSource.EvtNativeEngineDisconnected)
            .Select(e => e.Payload?[0]?.ToString())
            .LastOrDefault();

    [Fact]
    public async Task AnEngineThatParksConfirmsTheStopAndDisconnectsAsParked()
    {
        using var listener = new TestEventListener("Deckle-Taskbar");
        Task dll = await PlayDllAsync("stopping", "restored", "parked");

        bool stopped = await _host.StopEngineAsync();
        await dll;

        Assert.True(stopped);
        Assert.Equal("parked", DisconnectOutcome(listener));
    }

    // An engine that never reached XAML Diagnostics still leaves its target.
    [Fact]
    public async Task AnEngineThatUnloadsConfirmsTheStopAndDisconnectsAsUnloaded()
    {
        using var listener = new TestEventListener("Deckle-Taskbar");
        Task dll = await PlayDllAsync("stopping", "unloading");

        bool stopped = await _host.StopEngineAsync();
        await dll;

        Assert.True(stopped);
        Assert.Equal("unloaded", DisconnectOutcome(listener));
    }

    [Fact]
    public async Task AnEngineThatLeavesWithoutAWordDoesNotConfirmTheStop()
    {
        using var listener = new TestEventListener("Deckle-Taskbar");
        Task dll = await PlayDllAsync();

        bool stopped = await _host.StopEngineAsync();
        await dll;

        Assert.False(stopped);
        Assert.Equal("lost", DisconnectOutcome(listener));
    }

    // The build an engine runs is the versioned folder its DLL sits in.
    [Theory]
    [InlineData(@"C:\data\modules\taskbar\native\0.1.0.0-0c94048c\Deckle.Shell.Taskbar.Native.dll", "0.1.0.0-0c94048c")]
    [InlineData("", "")]
    [InlineData("Deckle.Shell.Taskbar.Native.dll", "")]
    public void TheBuildOfAnEngineIsTheFolderOfItsDll(string path, string expected)
    {
        Assert.Equal(expected, TaskbarEngineHost.BuildOf(path));
    }
}
