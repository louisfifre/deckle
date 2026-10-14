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

    // Named levels rather than a pixel slider: each stands for a fixed distance
    // in device-independent pixels (CoverGeometry.ApproachSpanDip), so one
    // level is one hand movement on every monitor. A ComboBox rather than
    // radio buttons (Louis, 2026-10-15): one line on the card's trailing edge,
    // the Windows Settings shape for a short list of named values.
    public IReadOnlyList<SettingDescriptor> RevealManifest =>
    [
        Setting.Choice<ApproachDistance>(
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
