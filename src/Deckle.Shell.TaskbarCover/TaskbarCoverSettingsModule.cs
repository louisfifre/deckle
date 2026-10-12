using Deckle.Catalog;

namespace Deckle.Shell.TaskbarCover;

public static class TaskbarCoverSettingsModule
{
    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "taskbar-cover",
        PageTag = "Deckle.Shell.TaskbarCover.TaskbarCoverPage, Deckle.Shell.TaskbarCover",
        OwningAssembly = "Deckle.Shell.TaskbarCover",
        Glyph = Glyphs.Taskbar,
        Order = order,
    };
}
