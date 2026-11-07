using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace Deckle.Anytype;

internal interface IGatewayProcess : IDisposable
{
    int Id { get; }
    DateTimeOffset? StartedAt { get; }
    bool HasExited { get; }
    int? ExitCode { get; }
    Task WaitForExitAsync(CancellationToken ct);
    bool TryKill(out string exType, out string exMessage);
}

// The Windows side of gateway supervision, behind one seam so the decisions
// in GatewaySupervisor can be tested without real processes.
internal interface IGatewayProcesses
{
    BackendListenerSnapshot InspectListener();
    // Null when the process is gone or Windows refuses to name its image.
    string? ExecutablePathOf(int processId);
    IReadOnlyList<int> FindGatewayProcessIds();
    IGatewayProcess? Open(int processId);
    IGatewayProcess Spawn(string executablePath);
    // Sets the stop event the gateway process created for itself. False when
    // the event does not exist (the process is gone, or never got that far).
    bool RequestStop(int processId);
    // GatewayKeyFingerprint.Of the API key Deckle would give a gateway now, null
    // when no key can be read.
    string? CurrentKeyFingerprint();
    // What the listening gateway says it holds on its health path, null when it
    // does not answer or answers no fingerprint.
    Task<string?> ReadHeldKeyFingerprintAsync(CancellationToken ct);
}

internal sealed class GatewayProcesses : IGatewayProcesses
{
    private static readonly HttpClient Health = new() { Timeout = TimeSpan.FromSeconds(3) };

    private readonly BackendListenerOwner _listener = new(GatewayContract.Port);

    public string? CurrentKeyFingerprint()
    {
        try
        {
            return GatewayKeyFingerprint.Of(AnytypeCredentials.Load().ApiKey);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Deckle.Security.SecretVaultException)
        {
            return null;
        }
    }

    public async Task<string?> ReadHeldKeyFingerprintAsync(CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = await Health
                .GetAsync($"http://127.0.0.1:{GatewayContract.Port}{GatewayContract.HealthPath}", ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            using JsonDocument body = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
                .ConfigureAwait(false);
            return body.RootElement.TryGetProperty(GatewayContract.KeyFingerprintProperty, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
            && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public BackendListenerSnapshot InspectListener() => _listener.Inspect();

    public string? ExecutablePathOf(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            string? path = process.MainModule?.FileName;
            return path is null ? null : Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public IReadOnlyList<int> FindGatewayProcessIds()
    {
        var ids = new List<int>();
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GatewayContract.ExecutableName)))
        {
            ids.Add(process.Id);
            process.Dispose();
        }
        return ids;
    }

    public IGatewayProcess? Open(int processId)
    {
        try
        {
            return new GatewayProcess(Process.GetProcessById(processId));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // Started without a console and without a window. Deckle never puts it in a
    // job, so the gateway outlives the Deckle that started it, as the backend does
    // (ADR-0002).
    public IGatewayProcess Spawn(string executablePath)
    {
        Process process = Process.Start(new ProcessStartInfo
        {
            FileName         = executablePath,
            UseShellExecute  = false,
            CreateNoWindow   = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
        }) ?? throw new InvalidOperationException("Process.Start returned no process.");
        return new GatewayProcess(process);
    }

    public bool RequestStop(int processId)
    {
        if (!EventWaitHandle.TryOpenExisting(GatewayContract.StopEventName(processId), out EventWaitHandle? stop))
            return false;
        using (stop)
            return stop.Set();
    }
}

internal sealed class GatewayProcess(Process process) : IGatewayProcess
{
    private readonly Process _process = process;

    public int Id { get; } = process.Id;

    public DateTimeOffset? StartedAt
    {
        get
        {
            try { return _process.StartTime; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                return null;
            }
        }
    }

    // An unreadable handle reads as exited, so the supervisor never waits on a
    // process it cannot observe.
    public bool HasExited
    {
        get
        {
            try { return _process.HasExited; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                return true;
            }
        }
    }

    public int? ExitCode
    {
        get
        {
            try { return _process.ExitCode; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                return null;
            }
        }
    }

    public Task WaitForExitAsync(CancellationToken ct) => _process.WaitForExitAsync(ct);

    public bool TryKill(out string exType, out string exMessage)
    {
        exType = "";
        exMessage = "";
        try
        {
            _process.Kill();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            if (HasExited) return true;
            exType = ex.GetType().Name;
            exMessage = ex.Message;
            return false;
        }
    }

    public void Dispose() => _process.Dispose();
}
