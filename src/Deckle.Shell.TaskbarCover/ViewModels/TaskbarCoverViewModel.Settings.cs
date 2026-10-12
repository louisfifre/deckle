using Deckle.Catalog;

namespace Deckle.Shell.TaskbarCover;

public partial class TaskbarCoverViewModel
{
    // One card: the switch. The approach distances are constants in
    // CoverGeometry until the named levels (near, standard, far) are decided.
    public IReadOnlyList<SettingDescriptor> SettingsManifest =>
    [
        Setting.Toggle(
            "TaskbarCoverPage_Group",
            () => Enabled,
            value => Enabled = value,
            glyph: Glyphs.Taskbar,
            defaultValue: () => new TaskbarCoverSettings().Enabled),
    ];
}
