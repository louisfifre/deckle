using Deckle.Catalog;

namespace Deckle.Shell.Taskbar;

// The taskbar module's nav identity for the Settings shell. Its own page is the
// customization page; the rail shows it as General once a child nests under
// this node. The cover module nests there by naming this id as its ParentId,
// so ModuleId is a contract: change it and the cover leaves the rail.
public static class TaskbarSettingsModule
{
    public const string ModuleId = "taskbar";

    public const string ResourceLibrary = "Deckle.Shell.Taskbar";

    public const string PageTag = "Deckle.Shell.Taskbar.TaskbarPage, Deckle.Shell.Taskbar";

    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = ModuleId,
        PageTag = PageTag,
        OwningAssembly = ResourceLibrary,
        Glyph = Glyphs.Taskbar,
        Order = order,
    };
}
