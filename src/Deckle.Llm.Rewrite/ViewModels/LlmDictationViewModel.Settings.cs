using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ── LlmDictationViewModel — settings manifest ─────────────────────────────────
//
// The declarative half of the "While you dictate" section: one group whose
// master switch turns batch rewriting on, and whose one child, the batch size,
// shows only while it is on (the composer masks it, never greys it).
//
// Its own section rather than a card in the Active profile one: that section's
// reset is a command that puts the choice back to None, while this group holds
// values whose defaults the composer tracks, each with its own reset. Mixing
// the two would give one wheel two meanings.
//
// Each default reads the POCO initializer (new BatchRewriteSettings()), so a
// reset goes active exactly when a value leaves it. The bounds come from the
// same class, where their why is written.
public partial class LlmDictationViewModel
{
    public IReadOnlyList<SettingDescriptor> BatchRewriting =>
    [
        Setting.Group("LlmBatchRewriteGroup",
            () => Enabled,
            value => Enabled = value,
            children:
            [
                Setting.Magnitude("LlmBatchSizeCard",
                    () => ThresholdChars,
                    value => ThresholdChars = value,
                    new MagnitudeArgs(
                        BatchRewriteSettings.MinThresholdChars,
                        BatchRewriteSettings.MaxThresholdChars,
                        Unit: "characters",
                        Integer: true),
                    defaultValue: () => new BatchRewriteSettings().ThresholdChars),
            ],
            defaultValue: () => new BatchRewriteSettings().Enabled),
    ];
}
