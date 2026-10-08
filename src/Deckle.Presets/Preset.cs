// A built-in preset: a stable lowercase ASCII id (never "custom", which is
// reserved for "the current values match no preset"), the .resw key whose
// "<LabelKey>.Content" entry names it in the consumer module's resources (the
// same key a ComboBoxItem x:Uid would carry), and the values it writes.

namespace Deckle.Presets;

public sealed record Preset<T>(string Id, string LabelKey, T Values) where T : notnull;
