namespace Deckle.Shell.Taskbar;

/// <summary>What the taskbar's background is drawn as.</summary>
public enum TaskbarBackground
{
    /// <summary>Explorer's own acrylic; Deckle leaves it alone.</summary>
    Native,

    /// <summary>No fill: the wallpaper shows through.</summary>
    Transparent,

    /// <summary>A solid black fill.</summary>
    Black,
}
