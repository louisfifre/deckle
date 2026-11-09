using System.IO.Pipes;
using System.Text;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The DLL's side of the pipe played by a NamedPipeClientStream: the same
// connect-then-lines conversation, without explorer.
[Trait("Category", "integration")]
public sealed class TaskbarPipeServerTests : IAsyncDisposable
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private readonly string _name = "Deckle.Taskbar.Tests." + Guid.NewGuid().ToString("N");
    private readonly TaskbarPipeServer _server;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;
    private readonly List<NativeMessage> _received = [];
    private readonly SemaphoreSlim _arrived = new(0);
    private int _consumed;

    public TaskbarPipeServerTests()
    {
        _server = new TaskbarPipeServer(_name);
        _server.MessageReceived += message =>
        {
            lock (_received) _received.Add(message);
            _arrived.Release();
        };
        _run = Task.Run(() => _server.RunAsync(_stop.Token));
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _run; } catch (OperationCanceledException) { }
        await _server.DisposeAsync();
        _stop.Dispose();
    }

    private async Task<NamedPipeClientStream> ConnectAsync()
    {
        var client = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync((int)_wait.TotalMilliseconds);
        return client;
    }

    private static Task WriteAsync(Stream client, string text) =>
        client.WriteAsync(Encoding.UTF8.GetBytes(text)).AsTask();

    private async Task<NativeMessage> NextAsync()
    {
        Assert.True(await _arrived.WaitAsync(_wait), "no message arrived");
        lock (_received) return _received[_consumed++];
    }

    private static async Task<string> ReadLineAsync(Stream client)
    {
        using var reader = new StreamReader(client, JsonLineFraming.Utf8, false, 256, leaveOpen: true);
        return await reader.ReadLineAsync().WaitAsync(_wait) ?? "";
    }

    [Fact]
    public async Task EachLineTheClientSendsArrivesAsOneParsedMessage()
    {
        await using NamedPipeClientStream client = await ConnectAsync();

        await WriteAsync(client, """{"t":"hello","protocol":"1","pid":"7","module":"m"}""" + "\n");
        var hello = Assert.IsType<NativeHello>(await NextAsync());
        await WriteAsync(client, """{"t":"log","level":"info","event":"connected","msg":"m","fields":{}}""" + "\n");
        var record = Assert.IsType<NativeLogRecord>(await NextAsync());

        Assert.Equal(7, hello.ProcessId);
        Assert.Equal("connected", record.Event);
    }

    // The pipe is a byte stream: a message can arrive in pieces, and two can
    // arrive in one read.
    [Fact]
    public async Task ALineSplitAcrossWritesIsJoinedAndTwoLinesInOneWriteAreSeparated()
    {
        await using NamedPipeClientStream client = await ConnectAsync();

        await WriteAsync(client, """{"t":"log","level":"info","ev""");
        await Task.Delay(50);
        await WriteAsync(client, """ent":"one","msg":"","fields":{}}""" + "\n"
            + """{"t":"log","level":"info","event":"two","msg":"","fields":{}}""" + "\n");

        Assert.Equal("one", Assert.IsType<NativeLogRecord>(await NextAsync()).Event);
        Assert.Equal("two", Assert.IsType<NativeLogRecord>(await NextAsync()).Event);
    }

    [Fact]
    public async Task AMultiByteCharacterCutBetweenTwoWritesIsDecodedWhole()
    {
        await using NamedPipeClientStream client = await ConnectAsync();
        byte[] bytes = Encoding.UTF8.GetBytes("""{"t":"log","level":"info","event":"e","msg":"é∶","fields":{}}""" + "\n");
        int cut = Array.IndexOf(bytes, (byte)0xC3) + 1;   // between the two bytes of "é"

        await client.WriteAsync(bytes.AsMemory(0, cut));
        await Task.Delay(50);
        await client.WriteAsync(bytes.AsMemory(cut));

        Assert.Equal("é∶", Assert.IsType<NativeLogRecord>(await NextAsync()).Message);
    }

    [Fact]
    public async Task ACommandSentByTheServerReachesTheClientAsOneLine()
    {
        await using NamedPipeClientStream client = await ConnectAsync();
        await WriteAsync(client, """{"t":"hello","protocol":"1","pid":"1","module":"m"}""" + "\n");
        await NextAsync();   // the server has accepted: it can write

        bool sent = await _server.SendAsync(NativeCommand.Stop);

        Assert.True(sent);
        Assert.Equal(NativeCommand.Stop, await ReadLineAsync(client));
    }

    [Fact]
    public async Task SendingWithNoClientConnectedReportsFalse()
    {
        Assert.False(await _server.SendAsync(NativeCommand.Ping));
    }

    // After an explorer restart a new DLL connects to the same pipe.
    [Fact]
    public async Task ASecondClientCanConnectAfterTheFirstLeft()
    {
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.ClientDisconnected += () => disconnected.TrySetResult();

        NamedPipeClientStream first = await ConnectAsync();
        await WriteAsync(first, """{"t":"hello","protocol":"1","pid":"1","module":"m"}""" + "\n");
        await NextAsync();
        await first.DisposeAsync();
        await disconnected.Task.WaitAsync(_wait);

        await using NamedPipeClientStream second = await ConnectAsync();
        await WriteAsync(second, """{"t":"hello","protocol":"1","pid":"2","module":"m"}""" + "\n");

        Assert.Equal(2, Assert.IsType<NativeHello>(await NextAsync()).ProcessId);
        Assert.Equal(2, _server.Connections);
    }

    [Fact]
    public async Task ALineThatIsNotJsonIsDeliveredAsUnreadableAndTheServerKeepsReading()
    {
        await using NamedPipeClientStream client = await ConnectAsync();

        await WriteAsync(client, "garbage\n");
        Assert.IsType<NativeUnreadableMessage>(await NextAsync());
        await WriteAsync(client, """{"t":"log","level":"info","event":"after","msg":"","fields":{}}""" + "\n");

        Assert.Equal("after", Assert.IsType<NativeLogRecord>(await NextAsync()).Event);
    }
}

