namespace Deckle.Shell.Taskbar;

// A process the native engine is injected into. The engine is one DLL, and each
// target process gets its own copy of it, its own settings document and its own
// pipe, so the taskbar (explorer) and the Start menu (StartMenuExperienceHost)
// are customized independently.
internal sealed record TaskbarTarget(TaskbarTargetKind Kind, string ProcessName, bool Packaged)
{
    /// <summary>The taskbar and the tray: explorer.exe, reached through the window hook on Shell_TrayWnd.</summary>
    public static TaskbarTarget Explorer { get; } = new(TaskbarTargetKind.Explorer, "explorer", Packaged: false);

    /// <summary>
    /// The Start menu: StartMenuExperienceHost.exe, a packaged process whose window sits in a
    /// band EnumWindows does not list, so it is reached through a thread hook and needs the DLL
    /// file and the pipe opened to ALL APPLICATION PACKAGES.
    /// </summary>
    public static TaskbarTarget StartMenu { get; } = new(TaskbarTargetKind.StartMenu, "StartMenuExperienceHost", Packaged: true);

    /// <summary>
    /// The pipe's name without the \\.\pipe\ prefix; the DLL computes the same (util.cpp PipeName).
    /// A side instance (the development console) appends its name, which the DLL reads from
    /// the instance file beside its copy (VersionedNativeCopy.WriteInstance).
    /// </summary>
    public string PipeName(int sessionId, string? instance = null)
    {
        string name = Kind == TaskbarTargetKind.Explorer
            ? $"Deckle.Taskbar.{sessionId}"
            : $"Deckle.Taskbar.{sessionId}.{ProcessName}";
        return string.IsNullOrEmpty(instance) ? name : $"{name}.{instance}";
    }
}

internal enum TaskbarTargetKind
{
    Explorer,
    StartMenu,
}
