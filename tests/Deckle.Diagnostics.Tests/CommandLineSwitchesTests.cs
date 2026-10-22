using System;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "regression")]
public sealed class CommandLineSwitchesTests
{
    [Fact]
    public void ThePathGivenToASwitchNeverReachesTheRunIdentityLine()
    {
        string names = CommandLineSwitches.Names(
            [@"--cleanup-data", @"C:\Users\someone\AppData\Local\Deckle", "--settings", "audio"]);

        Assert.Equal("--cleanup-data,--settings", names);
        Assert.DoesNotContain("someone", names);
    }

    [Fact]
    public void AValueJoinedToASwitchWithAnEqualsSignIsCutOff()
    {
        Assert.Equal("--data-root", CommandLineSwitches.Names(["--data-root=D:\\private\\folder"]));
    }

    [Fact]
    public void NoSwitchReadsAsNone()
    {
        Assert.Equal("none", CommandLineSwitches.Names(Array.Empty<string>()));
        Assert.Equal("none", CommandLineSwitches.Names(["just-a-value"]));
    }
}
