using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Deckle.Anytype;

// The delete's second validation, carried between two processes. The gateway
// runs the MCP gestures but cannot show a toast; the resident app can. The
// gateway asks over a named pipe restricted to the current user, the app asks
// the person and answers. One request and one answer per connection, each a
// single JSON line.
//
// The trust boundary stays the one DeletionApproval states: a checkpoint, not a
// lock. Any process of the same user could answer, as it could click the toast.

// The gateway's side: an IDeletionApproval that asks the resident app. When no
// app listens (Deckle closed, or an older Deckle without the relay) the delete
// fails closed with Unavailable, the answer the person already gets when toasts
// cannot be shown.
public sealed class PipeDeletionApproval(string pipeName, TimeSpan connectTimeout) : IDeletionApproval
{
    public PipeDeletionApproval(string pipeName) : this(pipeName, TimeSpan.FromSeconds(2)) { }

    public async Task<DeletionApprovalOutcome> RequestAsync(DeletionCandidate candidate, CancellationToken ct)
    {
        using var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        string step = "connect";
        try
        {
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connect.CancelAfter(connectTimeout);
                try
                {
                    await pipe.ConnectAsync(connect.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    DeckleAnytypeSource.Log.DeletionApprovalRelayFailed("gateway", step, "Timeout", "");
                    return DeletionApprovalOutcome.Unavailable;
                }
            }

            step = "request";
            await DeletionApprovalWire.WriteLineAsync(pipe, JsonSerializer.Serialize(candidate), ct).ConfigureAwait(false);

            step = "answer";
            string? line = await DeletionApprovalWire.ReadLineAsync(pipe, ct).ConfigureAwait(false);
            return line is not null && Enum.TryParse(line, out DeletionApprovalOutcome outcome)
                ? outcome
                : DeletionApprovalOutcome.Unavailable;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DeckleAnytypeSource.Log.DeletionApprovalRelayFailed("gateway", step, ex.GetType().Name, ex.Message);
            return DeletionApprovalOutcome.Unavailable;
        }
    }
}

// The resident app's side: listens on the pipe and hands each request to the
// approval that can reach the person (the toast).
public sealed class DeletionApprovalPipeServer : IAsyncDisposable
{
    // A delete waits on a person; a few concurrent asks is already unusual.
    private const int MaxConnections = 4;

    private readonly string _pipeName;
    private readonly IDeletionApproval _approval;
    private readonly CancellationTokenSource _cts = new();
    private Task? _run;

    public DeletionApprovalPipeServer(string pipeName, IDeletionApproval approval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(approval);
        _pipeName = pipeName;
        _approval = approval;
    }

    public void Start()
    {
        if (_run is not null) return;
        DeckleAnytypeSource.Log.DeletionApprovalRelayListening();
        _run = Task.WhenAll(Enumerable.Range(0, MaxConnections).Select(_ => Task.Run(() => ServeAsync(_cts.Token))));
    }

    private async Task ServeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string step = "listen";
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _pipeName, PipeDirection.InOut, MaxConnections,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);

                step = "request";
                string? line = await DeletionApprovalWire.ReadLineAsync(pipe, ct).ConfigureAwait(false);
                DeletionCandidate? candidate = line is null ? null : JsonSerializer.Deserialize<DeletionCandidate>(line);
                if (candidate is null)
                    continue;

                step = "ask";
                DeletionApprovalOutcome outcome = await _approval.RequestAsync(candidate, ct).ConfigureAwait(false);

                step = "answer";
                await DeletionApprovalWire.WriteLineAsync(pipe, outcome.ToString(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // A gateway that hung up, or a malformed request: this connection
                // ends, the listener goes on.
                DeckleAnytypeSource.Log.DeletionApprovalRelayFailed("app", step, ex.GetType().Name, ex.Message);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_run is not null)
        {
            try { await _run.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            DeckleAnytypeSource.Log.DeletionApprovalRelayStopped();
        }
        _cts.Dispose();
    }
}

internal static class DeletionApprovalWire
{
    // A request is a name, a type key and a count: far below this bound.
    private const int MaxLineBytes = 64 * 1024;

    public static async Task WriteLineAsync(Stream stream, string line, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    // Reads up to the newline. Null when the other side closed first.
    public static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        byte[] one = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(one, ct).ConfigureAwait(false);
            if (read == 0)
                return null;
            if (one[0] == (byte)'\n')
                return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            if (buffer.Length >= MaxLineBytes)
                throw new IOException("The approval message exceeds its size bound.");
            buffer.WriteByte(one[0]);
        }
    }
}
