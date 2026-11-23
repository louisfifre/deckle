using System.Runtime.InteropServices;

namespace Deckle.Install;

// Why the installer refuses a machine, or None. Pure so it is testable without
// a window or the real hardware; the stub feeds it the live values.
public enum MachineRefusal
{
    None,
    UnsupportedArchitecture,
    MissingAvx2,
    WindowsTooOld,
}

public static class MachineRequirements
{
    // First Windows 11 build; Windows 10 never went past 19045.
    public const int MinimumWindowsBuild = 22000;

    // Architecture first: on ARM64 the AVX2 flag of an emulated x64 process says
    // nothing about the machine, so the architecture answer wins.
    public static MachineRefusal Check(Architecture osArchitecture, bool avx2Supported, int windowsBuild)
    {
        if (osArchitecture != Architecture.X64) return MachineRefusal.UnsupportedArchitecture;
        if (!avx2Supported) return MachineRefusal.MissingAvx2;
        if (windowsBuild < MinimumWindowsBuild) return MachineRefusal.WindowsTooOld;
        return MachineRefusal.None;
    }

    // The live machine. OSArchitecture reports the OS, so x64 emulation on ARM is Arm64.
    public static MachineRefusal CheckThisMachine() => Check(
        RuntimeInformation.OSArchitecture,
        System.Runtime.Intrinsics.X86.Avx2.IsSupported,
        Environment.OSVersion.Version.Build);
}
