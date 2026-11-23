using System.Runtime.InteropServices;

namespace Deckle.Shell.Taskbar;

// Whether this machine can host the native engine, which patches Explorer's own
// XAML and so exists only for the x64 build of Windows 11 (build 22000 and up).
// Windows 10 and ARM64 are refused, not degraded: the engine is never started
// and the page says why. The decision is a pure function of two facts so the
// rule is tested without a machine of each kind.
public enum TaskbarSupport
{
    Supported,
    WindowsTooOld,
    UnsupportedArchitecture,
}

public static class TaskbarPlatformGate
{
    public const int FirstSupportedBuild = 22000;

    // The architecture is judged first: on ARM64 the person cannot fix it by
    // updating Windows, so it is the more useful word.
    public static TaskbarSupport Evaluate(bool isWindowsBuildSupported, Architecture osArchitecture)
    {
        if (osArchitecture == Architecture.Arm64) return TaskbarSupport.UnsupportedArchitecture;
        return isWindowsBuildSupported ? TaskbarSupport.Supported : TaskbarSupport.WindowsTooOld;
    }

    private static readonly Lazy<TaskbarSupport> _current = new(() => Evaluate(
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, FirstSupportedBuild),
        RuntimeInformation.OSArchitecture));

    public static TaskbarSupport Current => _current.Value;

    // The .resw key of the line the page shows; null when supported.
    public static string? MessageKey(TaskbarSupport support) => support switch
    {
        TaskbarSupport.WindowsTooOld => "TaskbarPage_UnsupportedWindows",
        TaskbarSupport.UnsupportedArchitecture => "TaskbarPage_UnsupportedArchitecture",
        _ => null,
    };

    // The reason as the log spells it; null when supported.
    public static string? LogReason(TaskbarSupport support) => support switch
    {
        TaskbarSupport.WindowsTooOld => "windows_before_11",
        TaskbarSupport.UnsupportedArchitecture => "arm64",
        _ => null,
    };
}
