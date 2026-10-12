// What a module hands its preset family: where the live values, the stored
// selection and the Custom copy live. The module keeps its own persistence
// (its settings POCO and service); the family only reads and writes through
// this surface, so it never holds a parallel store.
//
// Values is the group of settings the presets fix, read from and written to
// the live settings. Selection is the stored preset id, or PresetFamily.Custom;
// null means the file predates stored selections and the family derives it
// once. CustomValues is the user's own values, kept while a preset is selected
// so picking Custom again brings them back; null until the user first edits.

namespace Deckle.Presets;

public interface IPresetStore<T> where T : class
{
    T Values { get; set; }

    string? Selection { get; set; }

    T? CustomValues { get; set; }
}
