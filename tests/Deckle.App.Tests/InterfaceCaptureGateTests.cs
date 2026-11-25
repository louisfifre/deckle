using Deckle.Settings;
using Xunit;

namespace Deckle.App.Tests;

// The gate in front of the interface capture: Developer mode only, read at
// each start, one capture at a time.
[Trait("Category", "unit")]
public sealed class InterfaceCaptureGateTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "captures");

    private sealed class Probe
    {
        public bool Mode;
        public List<string> Calls { get; } = [];
        public Func<Task<InterfaceCaptureOutcome>> Run { get; set; }
        public InterfaceCaptureOutcome Outcome { get; set; } = new(Folder, 3, 0);

        public Probe(bool mode)
        {
            Mode = mode;
            Run = () =>
            {
                Calls.Add("run");
                return Task.FromResult(Outcome);
            };
        }

        public InterfaceCaptureGate Gate() => new(
            () => Mode,
            () => Run(),
            () => Calls.Add("settings"),
            folder => Calls.Add("folder:" + folder));
    }

    [Fact]
    public async Task WithDeveloperModeOffNoDoorStartsACapture()
    {
        var probe = new Probe(false);
        var gate = probe.Gate();

        Assert.Null(await gate.FromSettingsAsync());
        await gate.FromTrayAsync();

        Assert.Empty(probe.Calls);
    }

    [Fact]
    public async Task DeveloperModeIsReadAtEachStart()
    {
        var probe = new Probe(true);
        var gate = probe.Gate();
        probe.Mode = false;

        Assert.Null(await gate.FromSettingsAsync());
        await gate.FromTrayAsync();

        Assert.Empty(probe.Calls);
    }

    [Fact]
    public async Task WithDeveloperModeOnTheSettingsDoorRunsOneCapture()
    {
        var probe = new Probe(true);

        InterfaceCaptureOutcome? outcome = await probe.Gate().FromSettingsAsync();

        Assert.Same(probe.Outcome, outcome);
        Assert.Equal(["run"], probe.Calls);
    }

    [Fact]
    public async Task WithDeveloperModeOnTheTrayDoorOpensSettingsThenCapturesThenOpensTheFolder()
    {
        var probe = new Probe(true);

        await probe.Gate().FromTrayAsync();

        Assert.Equal(["settings", "run", "folder:" + Folder], probe.Calls);
    }

    [Fact]
    public async Task TheTrayDoorOpensNoFolderWhenNothingWasCaptured()
    {
        var probe = new Probe(true) { Outcome = new InterfaceCaptureOutcome(Folder, 0, 1) };

        await probe.Gate().FromTrayAsync();

        Assert.Equal(["settings", "run"], probe.Calls);
    }

    [Fact]
    public async Task ASecondStartWhileACaptureRunsIsRefused()
    {
        var probe = new Probe(true);
        var hold = new TaskCompletionSource<InterfaceCaptureOutcome>();
        probe.Run = () =>
        {
            probe.Calls.Add("run");
            return hold.Task;
        };
        var gate = probe.Gate();

        Task<InterfaceCaptureOutcome?> first = gate.FromSettingsAsync();
        Assert.Null(await gate.FromSettingsAsync());
        await gate.FromTrayAsync();
        Assert.Equal(["run"], probe.Calls);

        hold.SetResult(probe.Outcome);
        Assert.Same(probe.Outcome, await first);

        probe.Run = () =>
        {
            probe.Calls.Add("run");
            return Task.FromResult(probe.Outcome);
        };
        Assert.NotNull(await gate.FromSettingsAsync());
        Assert.Equal(["run", "run"], probe.Calls);
    }
}
