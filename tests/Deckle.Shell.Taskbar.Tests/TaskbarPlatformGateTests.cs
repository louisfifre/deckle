using System.Runtime.InteropServices;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

[Trait("Category", "unit")]
public sealed class TaskbarPlatformGateTests
{
    [Theory]
    [InlineData(true, Architecture.X64, TaskbarSupport.Supported)]
    [InlineData(false, Architecture.X64, TaskbarSupport.WindowsTooOld)]
    [InlineData(true, Architecture.Arm64, TaskbarSupport.UnsupportedArchitecture)]
    [InlineData(false, Architecture.Arm64, TaskbarSupport.UnsupportedArchitecture)]
    [InlineData(false, Architecture.X86, TaskbarSupport.WindowsTooOld)]
    public void OnlyRecentWindowsOnX64HostsTheEngine(bool buildSupported, Architecture architecture, TaskbarSupport expected) =>
        Assert.Equal(expected, TaskbarPlatformGate.Evaluate(buildSupported, architecture));

    [Fact]
    public void EachRefusalHasALineForThePageAndAReasonForTheLog()
    {
        Assert.Null(TaskbarPlatformGate.MessageKey(TaskbarSupport.Supported));
        Assert.Null(TaskbarPlatformGate.LogReason(TaskbarSupport.Supported));
        foreach (TaskbarSupport refused in new[] { TaskbarSupport.WindowsTooOld, TaskbarSupport.UnsupportedArchitecture })
        {
            Assert.False(string.IsNullOrEmpty(TaskbarPlatformGate.MessageKey(refused)));
            Assert.False(string.IsNullOrEmpty(TaskbarPlatformGate.LogReason(refused)));
        }
    }
}
