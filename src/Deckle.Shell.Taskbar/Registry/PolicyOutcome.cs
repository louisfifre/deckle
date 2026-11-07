namespace Deckle.Shell.Taskbar;

/// <summary>
/// How a change to the Recommended section policy ended. The card reads it to
/// say why the switch went back.
/// </summary>
public enum PolicyOutcome
{
    /// <summary>The policy holds the state asked for.</summary>
    Applied,

    /// <summary>The person declined the Windows prompt, or the process may not write the key.</summary>
    PermissionDenied,

    /// <summary>
    /// The machine policy (HKLM) hides the section. Someone else set it, and
    /// Deckle never writes that hive, so it cannot give the section back.
    /// </summary>
    HeldByMachine,

    /// <summary>Anything else: reg.exe failed, the value did not land in the user's hive, a hung process.</summary>
    Failed,
}