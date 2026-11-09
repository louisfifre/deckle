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
// The pipe is open to the current user only, which is who explorer runs as.
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

    /// <param name="pipeName">The pipe's name without the \\.\pipe\ prefix.</param>
    /// <param name="allowPackagedClients">
    /// Also open the pipe to ALL APPLICATION PACKAGES, for a packaged process
    /// such as StartMenuExperienceHost, which a plain user ACL does not admit.
    /// </param>
    public TaskbarPipeServer(string pipeName, bool allowPackagedClients = false)
    {
        _pipeName = pipeName;
        _allowPackagedClients = allowPackagedClients;
    }

    // The name the DLL computes in util.cpp: one pipe per terminal session and
    // per target process.
    public static string NameFor(TaskbarTarget target)
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        return target.PipeName(current.SessionId);
    }

    public static string DefaultName() => NameFor(TaskbarTarget.Explorer);

    public bool IsConnected => _client is not null;

    /// <summary>How many clients have connected since the server started.</summary>
    public int Connections => Volatile.Read(ref _connections);

    /// <summary>Raised on the reading task for each line the client sends.</summary>
    public event Action<NativeMessage>? MessageReceived;

    public event Action? ClientConnected;
    public event Action? ClientDisconnected;

    public async Task RunAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            NamedPipeServerStream pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);

                Interlocked.Increment(ref _connections);
                _client = pipe;
                ClientConnected?.Invoke();
                try
                {
                    using var reader = new StreamReader(pipe, JsonLineFraming.Utf8, false, 4096, leaveOpen: true);
                    while (await reader.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
                    {
                        if (line.Length == 0) continue;
                        MessageReceived?.Invoke(NativeMessage.Parse(line));
                    }
                }
                catch (IOException) { }   // the client went away mid-read
                finally
                {
                    _client = null;
                    ClientDisconnected?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
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



