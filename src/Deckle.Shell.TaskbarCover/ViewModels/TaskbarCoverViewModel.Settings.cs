using Deckle.Catalog;

namespace Deckle.Shell.TaskbarCover;

public partial class TaskbarCoverViewModel
{
    public IReadOnlyList<SettingDescriptor> SettingsManifest =>
    [
        Setting.Toggle(
            "TaskbarCoverPage_Group",
            () => Enabled,
            value => Enabled = value,
            defaultValue: () => new TaskbarCoverSettings().Enabled),
    ];
}
