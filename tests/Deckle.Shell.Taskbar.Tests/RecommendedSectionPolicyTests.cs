using System.ComponentModel;
using Deckle.TestSupport;
using Microsoft.Win32;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The policy engine against a real registry tree, never the policy keys Start
// reads: the "user" and "machine" keys are both under
// HKCU\Software\Deckle.Tests\<guid>, handed to the constructor and deleted in
// Dispose. The elevated route is a fake runner that plays reg.exe on the
// temporary user key, or declines like a UAC prompt answered No.
[Trait("Category", "integration")]
[Collection(TaskbarObservabilityCollection.Name)]
public sealed class RecommendedSectionPolicyTests : IDisposable
{
    private readonly string _testRoot = @"Software\Deckle.Tests\" + Guid.NewGuid().ToString("N");
    private readonly PolicyKey _userKey;
    private readonly PolicyKey _machineKey;

    public RecommendedSectionPolicyTests()
    {
        _userKey = new PolicyKey(RegistryHive.CurrentUser, _testRoot + @"\User\Explorer");
        _machineKey = new PolicyKey(RegistryHive.CurrentUser, _testRoot + @"\Machine\Explorer");
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_testRoot, throwOnMissingSubKey: false);

    // Plays reg.exe: "add" writes the user key, "delete" deletes the value,
    // unless told to decline (UAC No), to exit with a code, or to write
    // nowhere (an elevation under another account's hive).
    private sealed class FakeRunner(PolicyKey userKey) : IElevatedProcessRunner
    {
        public int Calls { get; private set; }
        public bool Declines { get; set; }
        public int ExitCode { get; set; }
        public bool WritesElsewhere { get; set; }

        public int Run(string fileName, string arguments)
        {
            Calls++;
            if (Declines) throw new Win32Exception(PolicyElevation.UacDeclined);
            Assert.Contains($"\"{userKey.CommandLineName}\"", arguments);
            if (ExitCode != 0 || WritesElsewhere) return ExitCode;
            if (arguments.StartsWith("add ", StringComparison.Ordinal))
                userKey.WriteDword(RecommendedSectionPolicy.ValueName, RecommendedSectionPolicy.Hidden);
            else
                userKey.DeleteValue(RecommendedSectionPolicy.ValueName);
            return 0;
        }
    }

    private (RecommendedSectionPolicy Policy, FakeRunner Runner) Create(bool isPrivileged = false)
    {
        var runner = new FakeRunner(_userKey);
        return (new RecommendedSectionPolicy(_userKey, _machineKey, runner, isPrivileged), runner);
    }

    private uint? UserValue => _userKey.ReadDword(RecommendedSectionPolicy.ValueName);

    [Fact]
    public void HideThroughElevationWritesTheUserPolicy()
    {
        (RecommendedSectionPolicy policy, FakeRunner runner) = Create();

        PolicyOutcome outcome = policy.Hide();

        Assert.Equal(PolicyOutcome.Applied, outcome);
        Assert.Equal(RecommendedSectionPolicy.Hidden, UserValue);
        Assert.Equal(1, runner.Calls);
        Assert.True(policy.IsHidden());
    }

    [Fact]
    public void HideFromAnElevatedProcessWritesDirectly()
    {
        (RecommendedSectionPolicy policy, FakeRunner runner) = Create(isPrivileged: true);

        Assert.Equal(PolicyOutcome.Applied, policy.Hide());
        Assert.Equal(RecommendedSectionPolicy.Hidden, UserValue);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public void ADeclinedPromptIsPermissionDeniedAndWritesNothing()
    {
        (RecommendedSectionPolicy policy, FakeRunner runner) = Create();
        runner.Declines = true;

        Assert.Equal(PolicyOutcome.PermissionDenied, policy.Hide());
        Assert.Null(UserValue);
        Assert.False(policy.IsHidden());
    }

    [Fact]
    public void AFailingExitCodeIsFailed()
    {
        (RecommendedSectionPolicy policy, FakeRunner runner) = Create();
        runner.ExitCode = 1;

        Assert.Equal(PolicyOutcome.Failed, policy.Hide());
    }

    [Fact]
    public void ASuccessThatMissedTheUserHiveIsFailed()
    {
        (RecommendedSectionPolicy policy, FakeRunner runner) = Create();
        runner.WritesElsewhere = true;

        Assert.Equal(PolicyOutcome.Failed, policy.Hide());
        Assert.Null(UserValue);
    }

    [Fact]
    public void HidingTwiceAsksOnce()
    {
        (RecommendedSectionPolicy policy, FakeRunner runner) = Create();
        policy.Hide();

        Assert.Equal(PolicyOutcome.Applied, policy.Hide());
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public void ShowDeletesTheUserPolicyByTheSameRoute()
    {
        (RecommendedSectionPolicy policy, FakeRunner runner) = Create();
        policy.Hide();

        Assert.Equal(PolicyOutcome.Applied, policy.Show());
        Assert.Null(UserValue);
        Assert.False(policy.IsHidden());
        Assert.Equal(2, runner.Calls);
    }

    [Fact]
    public void ShowFromAnElevatedProcessDeletesDirectly()
    {
        (RecommendedSectionPolicy policy, FakeRunner runner) = Create(isPrivileged: true);
        policy.Hide();

        Assert.Equal(PolicyOutcome.Applied, policy.Show());
        Assert.Null(UserValue);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public void ShowWithNothingToRemoveDoesNotPrompt()
    {
        (RecommendedSectionPolicy policy, FakeRunner runner) = Create();

        Assert.Equal(PolicyOutcome.Applied, policy.Show());
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public void ADeclinedShowLeavesThePolicyInPlace()
    {
        (RecommendedSectionPolicy policy, FakeRunner runner) = Create();
        policy.Hide();
        runner.Declines = true;

        Assert.Equal(PolicyOutcome.PermissionDenied, policy.Show());
        Assert.Equal(RecommendedSectionPolicy.Hidden, UserValue);
        Assert.True(policy.IsHidden());
    }

    // Deckle never writes HKLM: a hide it finds there is already done, and a
    // show it cannot do is refused without asking Windows for anything.
    [Fact]
    public void AMachinePolicyIsReadNeverWritten()
    {
        _machineKey.WriteDword(RecommendedSectionPolicy.ValueName, RecommendedSectionPolicy.Hidden);
        (RecommendedSectionPolicy policy, FakeRunner runner) = Create();

        Assert.True(policy.IsHidden());
        Assert.True(policy.IsHeldByMachine());
        Assert.Equal(PolicyOutcome.Applied, policy.Hide());
        Assert.Equal(PolicyOutcome.HeldByMachine, policy.Show());

        Assert.Equal(0, runner.Calls);
        Assert.Null(UserValue);
        Assert.Equal(RecommendedSectionPolicy.Hidden, _machineKey.ReadDword(RecommendedSectionPolicy.ValueName));
    }

    [Fact]
    public void AMachinePolicyHidingTooKeepsTheUserValueOnShow()
    {
        _machineKey.WriteDword(RecommendedSectionPolicy.ValueName, RecommendedSectionPolicy.Hidden);
        _userKey.WriteDword(RecommendedSectionPolicy.ValueName, RecommendedSectionPolicy.Hidden);
        (RecommendedSectionPolicy policy, FakeRunner runner) = Create();

        Assert.Equal(PolicyOutcome.HeldByMachine, policy.Show());

        Assert.Equal(0, runner.Calls);
        Assert.Equal(RecommendedSectionPolicy.Hidden, UserValue);
    }

    [Fact]
    public void AnAbsentPolicyReadsAsShown()
    {
        (RecommendedSectionPolicy policy, _) = Create();

        Assert.False(policy.IsHidden());
        Assert.False(policy.IsHeldByMachine());
    }

    [Fact]
    public async Task TheAsyncVersionsRunTheSameCalls()
    {
        (RecommendedSectionPolicy policy, FakeRunner runner) = Create();

        Assert.Equal(PolicyOutcome.Applied, await policy.HideAsync());
        Assert.True(policy.IsHidden());
        Assert.Equal(PolicyOutcome.Applied, await policy.ShowAsync());
        Assert.False(policy.IsHidden());
        Assert.Equal(2, runner.Calls);
    }

    [Fact]
    [Trait("Category", "observability")]
    public void AHideThatWroteLogsItsMilestoneAndADeclinedOneLogsTheWarning()
    {
        using var listener = new TestEventListener("Deckle-Taskbar");
        (RecommendedSectionPolicy policy, FakeRunner runner) = Create();

        policy.Hide();
        runner.Declines = true;
        policy.Show();

        Assert.Contains(listener.Events, e => e.EventId == DeckleShellTaskbarSource.EvtRecommendedSectionHidden);
        var failed = Assert.Single(listener.Events, e => e.EventId == DeckleShellTaskbarSource.EvtRecommendedPolicyFailed);
        Assert.Equal(new[] { "show", "elevated", "PermissionDenied", "uac_declined" }, failed.Payload!.Select(p => p?.ToString()).ToArray());
    }
}