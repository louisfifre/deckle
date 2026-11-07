using Deckle.Catalog;

namespace Deckle.Shell.TaskbarCover;

// The page's two manifests: the module switch, first and alone, then the
// approach distance in the Reveal section, hidden while the cover is off.
// Headers and descriptions resolve from this module's .resw as
// "<LabelKey>.Header" / ".Description". Every default reads from a fresh
// TaskbarCoverSettings, the single source the resets write back.
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

    // A magnitude in device-independent pixels, a field with a slider
    // (2026-11-07), where three named levels sat in a ComboBox since
    // 2026-10-15: the maintainer asked for a distance to sweep while the
    // preview under the card shows it on the screen. The unit is "DIP", not
    // "px": "px" read as physical pixels (the maintainer's report on the first
    // build of this card). The description defines DIP once, and the
    // preview's readout gives the figure in this display's own pixels. The slider's
    // every detent reaches ApproachDistancePreviewed; the setting itself is
    // written at the release.
    public IReadOnlyList<SettingDescriptor> RevealManifest =>
    [
        Setting.Magnitude(
            "TaskbarCoverPage_ApproachCard",
            () => ApproachDistanceDip,
            value => ApproachDistanceDip = ApproachDistance.Clamp((int)Math.Round(value)),
            new MagnitudeArgs(
                ApproachDistance.MinimumDip,
                ApproachDistance.MaximumDip,
                Unit: ApproachDistance.UnitLabel,
                Integer: true,
                Preview: value => ApproachDistancePreviewed?.Invoke(ApproachDistance.Clamp((int)Math.Round(value)))),
            visibleWhen: () => Enabled,
            defaultValue: () => new TaskbarCoverSettings().ApproachDistanceDip),
    ];
}
