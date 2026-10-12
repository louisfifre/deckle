// A built-in preset: a stable lowercase ASCII id (never "custom", which is
// reserved for the user's own values), the .resw key whose "<LabelKey>.Content"
// entry names it in the consumer module's resources (the same key a
// ComboBoxItem x:Uid would carry), and the values it writes.
//
// T is a record class so two value sets compare by value and so the store can
// hold "no Custom values yet" as null.

namespace Deckle.Presets;

public sealed record Preset<T>(string Id, string LabelKey, T Values) where T : class;
