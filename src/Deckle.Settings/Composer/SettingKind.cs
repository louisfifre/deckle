namespace Deckle.Settings;

// ── SettingKind ──────────────────────────────────────────────────────────────
//
// The control family a SettingDescriptor renders as. Each value has exactly one
// case in SettingsComposer and one factory on Setting. The set grows as real
// settings demand it — the pages inventory shows the next needed kinds are
// Slider, Choice, Path, Number and Text — but a value is added only together
// with its composer case, never speculatively.
public enum SettingKind
{
    Toggle,
    Slider,
    Path,
    Choice,
}
