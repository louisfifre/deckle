namespace Deckle.Shell.Taskbar;

/// <summary>Why a part is not doing what the person asked, as the page says it.</summary>
public enum TaskbarProblem
{
    /// <summary>The engine looked for the element on this build of Windows and did not find it.</summary>
    NotApplied,

    /// <summary>Explorer keeps undoing the value as fast as the engine sets it.</summary>
    Contested,

    /// <summary>The engine could not apply the part, or could not be started at all.</summary>
    Error,
}
