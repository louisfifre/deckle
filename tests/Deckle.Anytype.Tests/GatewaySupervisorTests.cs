using Deckle.Anytype;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Anytype.Tests;

// The supervisor's decisions against a scripted process table. The payload and
// its staging use a real temporary folder.
[Trait("Category", "integration")]
public sealed class GatewaySupervisorTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "deckle-gateway-sup-" + Guid.NewGuid().ToString("N"));
    private readonly FakeGatewayProcesses _processes = new();

    private string Payload => Path.Combine(_root, "payload");
    private string Versions => Path.Combine(_root, "versions");

    public GatewaySupervisorTests()
    {
        Directory.CreateDirectory(Payload);
        File.WriteAllText(Path.Combine(Payload, GatewayContract.ExecutableName), "exe");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string StagedExecutable =>
        Path.Combine(Versions, GatewayPayload.Fingerprint(Payload)!, GatewayContract.ExecutableName);

    private GatewaySupervisor Supervisor() => new(
        Payload, Versions, _processes,
        listenTimeout: TimeSpan.FromSeconds(1),
        stopTimeout: TimeSpan.FromSeconds(1),
        delay: (_, ct) => Task.Delay(1, ct));

    [Fact]
    public async Task A_gateway_of_the_same_build_is_kept_without_any_restart()
    {
        _processes.Running(77, StagedExecutable, listening: true);

        await using (GatewaySupervisor supervisor = Supervisor())
        {
            supervisor.Start();
            await Until(() => _processes.Opened.Contains(77));
        }

        Assert.Empty(_processes.Spawned);
        Assert.Empty(_processes.StopRequests);
        Assert.False(_processes.Process(77).HasExited);
    }

    [Fact]
    public async Task A_gateway_of_another_build_is_drained_then_replaced()
    {
        _processes.Running(77, Path.Combine(Versions, "older", GatewayContract.ExecutableName), listening: true);

        await using (GatewaySupervisor supervisor = Supervisor())
        {
            supervisor.Start();
            await Until(() => _processes.Spawned.Count == 1);
        }

        Assert.Equal([77], _processes.StopRequests);
        Assert.Equal(StagedExecutable, _processes.Spawned[0], ignoreCase: true);
    }

    [Fact]
    public async Task A_foreign_program_on_the_port_is_never_stopped()
    {
        _processes.Running(77, @"C:\Elsewhere\Other.exe", listening: true);

        GatewaySupervisor supervisor = Supervisor();
        supervisor.Start();
        await supervisor.Completion.WaitAsync(Patience);
        await supervisor.DisposeAsync();

        Assert.Empty(_processes.Spawned);
        Assert.Empty(_processes.StopRequests);
    }

    [Fact]
    public async Task A_free_port_gets_this_build_and_a_crash_restarts_it()
    {
        await using GatewaySupervisor supervisor = Supervisor();
        supervisor.Start();
        await Until(() => _processes.Spawned.Count == 1);

        _processes.Process(_processes.LastSpawnedId).Exit(GatewayContract.ExitCodes.Faulted);
        await Until(() => _processes.Spawned.Count == 2);

        Assert.All(_processes.Spawned, path => Assert.Equal(StagedExecutable, path, ignoreCase: true));
    }

    [Fact]
    public async Task Missing_credentials_stop_the_restarts()
    {
        GatewaySupervisor supervisor = Supervisor();
        supervisor.Start();
        await Until(() => _processes.Spawned.Count == 1);

        _processes.Process(_processes.LastSpawnedId).Exit(GatewayContract.ExitCodes.NotProvisioned);
        await supervisor.Completion.WaitAsync(Patience);
        await supervisor.DisposeAsync();

        Assert.Single(_processes.Spawned);
    }

    [Fact]
    public async Task Disposing_the_supervisor_leaves_the_gateway_running()
    {
        GatewaySupervisor supervisor = Supervisor();
        supervisor.Start();
        await Until(() => _processes.Spawned.Count == 1);

        await supervisor.DisposeAsync();

        Assert.False(_processes.Process(_processes.LastSpawnedId).HasExited);
        Assert.Empty(_processes.StopRequests);
    }

    private static bool IsOpen => UserIncidentRegistry.Instance.IsOpen(UserIncidents.AnytypeGateway);

    [Fact]
    public async Task A_gateway_that_keeps_crashing_is_given_up_and_shown_as_a_user_incident()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeGateway);
        GatewaySupervisor supervisor = Supervisor();
        supervisor.Start();

        // The first start and four restarts all die at once: the ladder is spent.
        for (int spawned = 1; spawned <= 5; spawned++)
        {
            await Until(() => _processes.Spawned.Count == spawned);
            _processes.Process(_processes.LastSpawnedId).Exit(GatewayContract.ExitCodes.Faulted);
        }
        await supervisor.Completion.WaitAsync(Patience);
        await supervisor.DisposeAsync();

        Assert.Equal(
            "gave_up",
            UserIncidentRegistry.Instance.Snapshot().Single(i => i.Id == UserIncidents.AnytypeGateway).Reason);
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeGateway);
    }

    [Fact]
    public async Task A_gateway_that_stops_for_missing_credentials_is_no_user_incident()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeGateway);
        GatewaySupervisor supervisor = Supervisor();
        supervisor.Start();
        await Until(() => _processes.Spawned.Count == 1);

        _processes.Process(_processes.LastSpawnedId).Exit(GatewayContract.ExitCodes.NotProvisioned);
        await supervisor.Completion.WaitAsync(Patience);
        await supervisor.DisposeAsync();

        Assert.False(IsOpen);
    }

    [Fact]
    public async Task A_gateway_that_serves_again_closes_the_incident_an_earlier_give_up_left()
    {
        UserIncidentRegistry.Instance.Open(UserIncidents.AnytypeGateway, "gave_up");
        _processes.Running(77, StagedExecutable, listening: true);

        await using (GatewaySupervisor supervisor = Supervisor())
        {
            supervisor.Start();
            await Until(() => !IsOpen);
        }

        Assert.False(IsOpen);
    }

    private static async Task Until(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The supervisor did not reach the expected state.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeGatewayProcesses : IGatewayProcesses
    {
        private readonly object _gate = new();
        private readonly Dictionary<int, FakeGatewayProcess> _processes = new();
        private readonly Dictionary<int, string> _paths = new();
        private int _listenerOwner;
        private int _nextId = 1000;

        public List<string> Spawned { get; } = new();
        public List<int> StopRequests { get; } = new();
        public List<int> Opened { get; } = new();
        public int LastSpawnedId { get; private set; }

        public void Running(int id, string path, bool listening)
        {
            lock (_gate)
            {
                _processes[id] = new FakeGatewayProcess(id, this);
                _paths[id] = path;
                if (listening) _listenerOwner = id;
            }
        }

        public FakeGatewayProcess Process(int id) { lock (_gate) return _processes[id]; }

        public void Exited(int id)
        {
            lock (_gate)
            {
                if (_listenerOwner == id) _listenerOwner = 0;
            }
        }

        public BackendListenerSnapshot InspectListener()
        {
            lock (_gate)
                return _listenerOwner == 0
                    ? new(BackendListenerState.Unbound)
                    : new(BackendListenerState.Owned, _listenerOwner);
        }

        public string? ExecutablePathOf(int processId)
        {
            lock (_gate)
                return _processes.TryGetValue(processId, out var p) && !p.HasExited ? _paths[processId] : null;
        }

        public IReadOnlyList<int> FindGatewayProcessIds()
        {
            lock (_gate)
                return _processes.Values.Where(p => !p.HasExited).Select(p => p.Id).ToList();
        }

        public IGatewayProcess? Open(int processId)
        {
            lock (_gate)
            {
                Opened.Add(processId);
                return _processes.TryGetValue(processId, out var p) && !p.HasExited ? p : null;
            }
        }

        public IGatewayProcess Spawn(string executablePath)
        {
            lock (_gate)
            {
                int id = _nextId++;
                Running(id, executablePath, listening: true);
                Spawned.Add(executablePath);
                LastSpawnedId = id;
                return _processes[id];
            }
        }

        public bool RequestStop(int processId)
        {
            FakeGatewayProcess process;
            lock (_gate)
            {
                StopRequests.Add(processId);
                process = _processes[processId];
            }
            process.Exit(GatewayContract.ExitCodes.Stopped);
            return true;
        }
    }

    private sealed class FakeGatewayProcess(int id, FakeGatewayProcesses table) : IGatewayProcess
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Id { get; } = id;
        public DateTimeOffset? StartedAt { get; } = DateTimeOffset.Now;
        public bool HasExited => _exit.Task.IsCompleted;
        public int? ExitCode { get; private set; }

        public void Exit(int code)
        {
            ExitCode = code;
            table.Exited(Id);
            _exit.TrySetResult();
        }

        public Task WaitForExitAsync(CancellationToken ct) => _exit.Task.WaitAsync(ct);

        public bool TryKill(out string exType, out string exMessage)
        {
            exType = exMessage = "";
            Exit(-1);
            return true;
        }

        // The supervisor disposes its handle; the fake process lives on.
        public void Dispose() { }
    }
}
