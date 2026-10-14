using Deckle.Catalog;

namespace Deckle.Shell.TaskbarCover;

// The page's two manifests: the module switch, first and alone, then the
// approach distance in the Reveal section, hidden while the cover is off.
// Headers, descriptions and option labels resolve from this module's .resw as
// "<LabelKey>.Header" / ".Description" and "<OptionKey>.Content". Every default
// reads from a fresh TaskbarCoverSettings, the single source the resets write
// back.
public partial class TaskbarCoverViewModel
{
    public IReadOnlyList<SettingDescriptor> SwitchManifest =>
    [
        Setting.Toggle(
            "TaskbarCoverPage_Group",
            () => Enabled,
            value => Enabled = value,
            defaultValue: () => new TaskbarCoverSettings().Enabled),
    ];

    // Named levels rather than a pixel slider: the depth each stands for is a
    // multiple of the band thickness (CoverGeometry.ZoneFactor), so one level
    // reads the same on every taskbar size.
    public IReadOnlyList<SettingDescriptor> RevealManifest =>
    [
        Setting.Radio<ApproachDistance>(
            "TaskbarCoverPage_ApproachCard",
            () => Approach,
            value => Approach = value,
            [
                (ApproachDistance.Near,     "TaskbarCoverPage_ApproachNear"),
                (ApproachDistance.Standard, "TaskbarCoverPage_ApproachStandard"),
                (ApproachDistance.Far,      "TaskbarCoverPage_ApproachFar"),
            ],
            visibleWhen: () => Enabled,
            defaultValue: () => new TaskbarCoverSettings().Approach),
    ];
}
