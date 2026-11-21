using System.IO.Pipes;
using System.Text;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The server's loop is the DLL's only channel and nobody awaits its task before
// shutdown: an exception that ended it left the engine without a channel, in
// silence. These pin that it logs, waits and serves again.
[Trait("Category", "regression")]
public sealed class TaskbarPipeServerResilienceTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static NamedPipeServerStream RealPipe(string name) => new(
        name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private static async Task<NamedPipeClientStream> ConnectAsync(string name)
    {
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync((int)_wait.TotalMilliseconds);
        return client;
    }

    private static Task WriteLineAsync(Stream client, string json) =>
        client.WriteAsync(Encoding.UTF8.GetBytes(json + "\n")).AsTask();

    private const string Hello = """{"t":"hello","protocol":"1","pid":"7","module":"m"}""";

    [Fact]
    public async Task TheServerServesAgainWhenCreatingThePipeFailsAtFirst()
    {
        string name = "Deckle.Taskbar.Tests." + Guid.NewGuid().ToString("N");
        int attempts = 0;
        var server = new TaskbarPipeServer(name, false,
            createPipe: () => ++attempts <= 2 ? throw new IOException("pipe taken") : RealPipe(name),
            firstRetry: TimeSpan.FromMilliseconds(10));
        var arrived = new TaskCompletionSource<NativeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MessageReceived += message => arrived.TrySetResult(message);
        using var stop = new CancellationTokenSource();
        Task run = Task.Run(() => server.RunAsync(stop.Token));

        await using NamedPipeClientStream client = await ConnectAsync(name);
        await WriteLineAsync(client, Hello);

        Assert.IsType<NativeHello>(await arrived.Task.WaitAsync(_wait));
        Assert.Equal(3, attempts);

        stop.Cancel();
        await run.WaitAsync(_wait);
        await server.DisposeAsync();
    }

    [Fact]
    public async Task AHandlerThatThrowsOnALineDoesNotEndTheLoopOrTheConnection()
    {
        string name = "Deckle.Taskbar.Tests." + Guid.NewGuid().ToString("N");
        var server = new TaskbarPipeServer(name, false,
            createPipe: () => RealPipe(name), firstRetry: TimeSpan.FromMilliseconds(10));
        int seen = 0;
        var second = new TaskCompletionSource<NativeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MessageReceived += message =>
        {
            if (Interlocked.Increment(ref seen) == 1) throw new InvalidOperationException("handler bug");
            second.TrySetResult(message);
        };
        using var stop = new CancellationTokenSource();
        Task run = Task.Run(() => server.RunAsync(stop.Token));

        await using NamedPipeClientStream client = await ConnectAsync(name);
        await WriteLineAsync(client, Hello);
        await WriteLineAsync(client, Hello);

        Assert.IsType<NativeHello>(await second.Task.WaitAsync(_wait));
        Assert.False(run.IsCompleted);

        stop.Cancel();
        await run.WaitAsync(_wait);
        await server.DisposeAsync();
    }

    [Fact]
    public async Task CancellingWhileTheServerWaitsToRetryEndsTheLoopCleanly()
    {
        var server = new TaskbarPipeServer("unused", false,
            createPipe: () => throw new IOException("always"), firstRetry: TimeSpan.FromMinutes(5));
        using var stop = new CancellationTokenSource();
        Task run = Task.Run(() => server.RunAsync(stop.Token));
        await Task.Delay(100);

        stop.Cancel();

        await run.WaitAsync(_wait);
        await server.DisposeAsync();
    }
}
