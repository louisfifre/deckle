namespace Deckle.Shell.Taskbar;

/// <summary>How wide the Start menu is.</summary>
public enum StartMenuWidth
{
    /// <summary>Windows' own width, left alone.</summary>
    Native,

    /// <summary>As wide as the screen the menu opens on, with Windows' gap on each side.</summary>
    Fill,

    /// <summary>The width the person typed (TaskbarSettings.StartMenuCustomWidth), held to the screen.</summary>
    Custom,
}
