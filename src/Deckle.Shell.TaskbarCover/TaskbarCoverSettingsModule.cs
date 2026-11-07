using Deckle.Catalog;

namespace Deckle.Shell.TaskbarCover;

public static class TaskbarCoverSettingsModule
{
    // The Taskbar module's id, spelled out: this module cannot reference
    // Deckle.Shell.Taskbar, so the two meet on the string. A ParentId naming no
    // registered module leaves the cover out of the rail.
    private const string TaskbarModuleId = "taskbar";

    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "taskbar-cover",
        ParentId = TaskbarModuleId,
        PageTag = "Deckle.Shell.TaskbarCover.TaskbarCoverPage, Deckle.Shell.TaskbarCover",
        OwningAssembly = "Deckle.Shell.TaskbarCover",
        Glyph = Glyphs.Taskbar,
        Order = order,
    };
}
