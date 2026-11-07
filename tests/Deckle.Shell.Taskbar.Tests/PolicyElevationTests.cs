using System.ComponentModel;
using Microsoft.Win32;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// How an elevated reg.exe run turns into the outcome the card reads. The runner
// is a fake: nothing here starts a process or shows a UAC prompt.
[Trait("Category", "unit")]
public sealed class PolicyElevationTests
{
    private static readonly PolicyKey _key = new(RegistryHive.CurrentUser, @"Software\Deckle.Tests\policy");

    private sealed class ScriptedRunner(Func<int> outcome) : IElevatedProcessRunner
    {
        public List<(string FileName, string Arguments)> Calls { get; } = [];

        public int Run(string fileName, string arguments)
        {
            Calls.Add((fileName, arguments));
            return outcome();
        }
    }

    [Fact]
    public void ExitCodeZeroIsApplied() =>
        Assert.Equal(PolicyOutcome.Applied, PolicyElevation.OutcomeOf(0));

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void NonZeroExitCodeIsFailed(int exitCode) =>
        Assert.Equal(PolicyOutcome.Failed, PolicyElevation.OutcomeOf(exitCode));

    [Fact]
    public void DeclinedUacPromptIsPermissionDenied() =>
        Assert.Equal(PolicyOutcome.PermissionDenied,
            PolicyElevation.OutcomeOf(new Win32Exception(PolicyElevation.UacDeclined)));

    public static TheoryData<Exception> OtherFailures => new()
    {
        new Win32Exception(2),
        new InvalidOperationException(),
        new TimeoutException(),
    };

    [Theory]
    [MemberData(nameof(OtherFailures))]
    public void AnyOtherExceptionIsFailed(Exception exception) =>
        Assert.Equal(PolicyOutcome.Failed, PolicyElevation.OutcomeOf(exception));

    [Fact]
    public void SetDwordRunsRegAddOnTheKeyFromSystem32()
    {
        var runner = new ScriptedRunner(() => 0);

        PolicyWriteResult result = new PolicyElevation(runner).SetDword(_key, "Value", 1);

        Assert.Equal(PolicyOutcome.Applied, result.Outcome);
        (string fileName, string arguments) = Assert.Single(runner.Calls);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "reg.exe"), fileName);
        Assert.Equal($"add \"{_key.CommandLineName}\" /v Value /t REG_DWORD /d 1 /f", arguments);
    }

    [Fact]
    public void DeleteValueRunsRegDeleteOnTheKey()
    {
        var runner = new ScriptedRunner(() => 0);

        new PolicyElevation(runner).DeleteValue(_key, "Value");

        Assert.Equal($"delete \"{_key.CommandLineName}\" /v Value /f", Assert.Single(runner.Calls).Arguments);
    }

    [Fact]
    public void ADeclinedPromptNeverThrowsOut()
    {
        var runner = new ScriptedRunner(() => throw new Win32Exception(PolicyElevation.UacDeclined));

        PolicyWriteResult result = new PolicyElevation(runner).SetDword(_key, "Value", 1);

        Assert.Equal(PolicyOutcome.PermissionDenied, result.Outcome);
        Assert.Equal("uac_declined", result.Error);
    }

    [Fact]
    public void AFailingExitCodeIsReportedAsFailed()
    {
        var runner = new ScriptedRunner(() => 1);

        PolicyWriteResult result = new PolicyElevation(runner).DeleteValue(_key, "Value");

        Assert.Equal(PolicyOutcome.Failed, result.Outcome);
        Assert.Equal("exit_1", result.Error);
    }

    [Fact]
    public void ThePolicyKeysSpellTheHiveRegExeExpects()
    {
        Assert.Equal(@"HKCU\Software\X", new PolicyKey(RegistryHive.CurrentUser, @"Software\X").CommandLineName);
        Assert.Equal(@"HKLM\Software\X", new PolicyKey(RegistryHive.LocalMachine, @"Software\X").CommandLineName);
    }
}