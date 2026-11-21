using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Deckle.Shell.Taskbar;

// Deckle's end of the channel with the injected DLL: a duplex named pipe that
// Deckle serves and the DLL connects to, one JSON document per line (see
// JsonLineFraming, NativeMessage and NativeCommand).
//
// Deckle is the server, so that "Deckle is gone" is simply the pipe closing,
// and the DLL decides on its own what that means (restore and unload).
// One instance at a time: a second client cannot take the place of the first.
// The pipe is open to the current user, which is who explorer runs as. For a
// packaged client it is also open to ALL APPLICATION PACKAGES (read and write),
// because StartMenuExperienceHost runs in an AppContainer that a plain user
// entry does not admit; that entry exists only when allowPackagedClients is set,
// which the host does for a packaged target (TaskbarTarget.Packaged).
//
// Run accepts one client, reads its lines until it disconnects, then waits for
// the next one: after an explorer restart a new DLL connects to the same pipe.
internal sealed class TaskbarPipeServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly bool _allowPackagedClients;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private volatile Stream? _client;
    private int _connections;
    private readonly Func<NamedPipeServerStream> _createPipe;
    private readonly TimeSpan _firstRetry;
    private readonly TimeSpan _retryCeiling;

    /// <param name="pipeName">The pipe's name without the \\.\pipe\ prefix.</param>
    /// <param name="allowPackagedClients">
    /// Also open the pipe to ALL APPLICATION PACKAGES, for a packaged process
    /// such as StartMenuExperienceHost, which a plain user ACL does not admit.
    /// </param>
    public TaskbarPipeServer(string pipeName, bool allowPackagedClients = false)
        : this(pipeName, allowPackagedClients, createPipe: null, firstRetry: null) { }

    /// <param name="createPipe">Makes the pipe instance to serve; null builds the real, access-controlled one. The tests pass one that fails.</param>
    /// <param name="firstRetry">The wait after a first failure; null is one second. It doubles up to thirty seconds.</param>
    internal TaskbarPipeServer(string pipeName, bool allowPackagedClients,
        Func<NamedPipeServerStream>? createPipe, TimeSpan? firstRetry)
    {
        _pipeName = pipeName;
        _allowPackagedClients = allowPackagedClients;
        _createPipe = createPipe ?? CreatePipe;
        _firstRetry = firstRetry ?? TimeSpan.FromSeconds(1);
        _retryCeiling = TimeSpan.FromSeconds(30) > _firstRetry ? TimeSpan.FromSeconds(30) : _firstRetry;
    }

    // The name the DLL computes in util.cpp: one pipe per terminal session and
    // per target process.
    public static string NameFor(TaskbarTarget target, string? instance = null)
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        return target.PipeName(current.SessionId, instance);
    }

    public static string DefaultName() => NameFor(TaskbarTarget.Explorer);

    public bool IsConnected => _client is not null;

    /// <summary>How many clients have connected since the server started.</summary>
    public int Connections => Volatile.Read(ref _connections);

    /// <summary>Raised on the reading task for each line the client sends.</summary>
    public event Action<NativeMessage>? MessageReceived;

    public event Action? ClientConnected;
    public event Action? ClientDisconnected;

    // The loop outlives what goes wrong in it: it is the DLL's only way to reach
    // Deckle, and nobody awaits its task before shutdown, so an exception that
    // escaped would end it in silence and leave the engine without a channel
    // until the next start. A failure is logged once per incident, the loop
    // waits (doubling from FirstRetry up to RetryCeiling) and serves again.
    public async Task RunAsync(CancellationToken cancellation)
    {
        TimeSpan retry = _firstRetry;
        bool failing = false;
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await ServeOneClientAsync(cancellation, onPipeReady: () =>
                {
                    if (!failing) return;
                    failing = false;
                    retry = _firstRetry;
                    DeckleShellTaskbarSource.Log.TaskbarPipeServerRecovered();
                }).ConfigureAwait(false);
                retry = _firstRetry;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // The server stays up for the whole run, so the first failure is the
                // incident and the ones that repeat are not written again.
                if (!failing)
                {
                    failing = true;
                    DeckleShellTaskbarSource.Log.TaskbarPipeServerFailed(exception.GetType().Name, (int)retry.TotalSeconds);
                }
                try { await Task.Delay(retry, cancellation).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, _retryCeiling.Ticks));
            }
        }
    }

    // One pipe instance: created, waited on, served until its client leaves,
    // disposed. onPipeReady runs once the pipe exists, which is what shows the
    // server can listen again after a failure.
    private async Task ServeOneClientAsync(CancellationToken cancellation, Action onPipeReady)
    {
        NamedPipeServerStream pipe = _createPipe();
        try
        {
            onPipeReady();
            await pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);

            Interlocked.Increment(ref _connections);
            _client = pipe;
            ClientConnected?.Invoke();
            try
            {
                using var reader = new StreamReader(pipe, JsonLineFraming.Utf8, false, 4096, leaveOpen: true);
                bool handlerFailing = false;
                while (await reader.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
                {
                    if (line.Length == 0) continue;
                    try
                    {
                        MessageReceived?.Invoke(NativeMessage.Parse(line));
                        handlerFailing = false;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // A handler that throws on one line must not cut the client off:
                        // the line is dropped, the next ones still arrive.
                        if (handlerFailing) continue;
                        handlerFailing = true;
                        DeckleShellTaskbarSource.Log.TaskbarPipeLineFailed(exception.GetType().Name);
                    }
                }
            }
            catch (IOException) { }   // the client went away mid-read
            finally
            {
                _client = null;
                ClientDisconnected?.Invoke();
            }
        }
        finally
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends one command line to the connected client. False when none is
    /// connected or the write failed.
    /// </summary>
    public async Task<bool> SendAsync(string json, CancellationToken cancellation = default)
    {
        byte[] bytes = JsonLineFraming.Frame(json);
        await _writeLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            Stream? client = _client;
            if (client is null) return false;
            // No flush: a pipe write is delivered when it returns, and Flush on a
            // pipe whose client already left throws "pipe is broken".
            await client.WriteAsync(bytes, cancellation).ConfigureAwait(false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User!;
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        if (_allowPackagedClients)
        {
            // ALL APPLICATION PACKAGES: the SID packaged processes carry in
            // addition to their own package SID.
            var allPackages = new SecurityIdentifier("S-1-15-2-1");
            security.AddAccessRule(new PipeAccessRule(allPackages, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        }

        return NamedPipeServerStreamAcl.Create(
            _pipeName, PipeDirection.InOut, maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            // Real buffers: with zero-size ones a write waits for the reader to read it,
            // and a slow DLL would hold the writer.
            inBufferSize: 64 * 1024, outBufferSize: 64 * 1024, security);
    }

    public ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        return ValueTask.CompletedTask;
    }
}



