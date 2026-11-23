using System.Runtime.InteropServices;
using Deckle.Install;
using Xunit;

namespace Deckle.Install.Tests;

[Trait("Category", "unit")]
public sealed class MachineRequirementsTests
{
    [Fact]
    public void SupportedMachineIsNotRefused() =>
        Assert.Equal(MachineRefusal.None, MachineRequirements.Check(Architecture.X64, true, 22000));

    [Theory]
    [InlineData(Architecture.Arm64)]
    [InlineData(Architecture.X86)]
    public void NonX64SystemIsRefusedEvenWhenAvx2AndBuildPass(Architecture arch) =>
        Assert.Equal(MachineRefusal.UnsupportedArchitecture, MachineRequirements.Check(arch, true, 26100));

    [Fact]
    public void MissingAvx2IsRefused() =>
        Assert.Equal(MachineRefusal.MissingAvx2, MachineRequirements.Check(Architecture.X64, false, 26100));

    [Theory]
    [InlineData(19045)]
    [InlineData(21999)]
    public void Windows10IsRefused(int build) =>
        Assert.Equal(MachineRefusal.WindowsTooOld, MachineRequirements.Check(Architecture.X64, true, build));

    [Fact]
    public void ArchitectureOutranksTheOtherReasons() =>
        Assert.Equal(MachineRefusal.UnsupportedArchitecture, MachineRequirements.Check(Architecture.Arm64, false, 19045));

    [Theory]
    [InlineData(MachineRefusal.UnsupportedArchitecture, "ARM")]
    [InlineData(MachineRefusal.MissingAvx2, "AVX2")]
    [InlineData(MachineRefusal.WindowsTooOld, "Windows 11")]
    public void EachRefusalSaysWhy(MachineRefusal refusal, string expected) =>
        Assert.Contains(expected, InstallFailureText.For(refusal));
}
