// A preset family: the built-in presets of one group of settings in one
// module, plus the user's own values under the Custom selection.
//
// The rule (Louis, 2026-10-12): real time. Any hand edit saves at once and
// switches the selection to Custom; picking a preset writes its values;
// picking Custom again brings the hand-made values back. So the selection
// is stored, and so are the Custom values: the family writes both through
// the store the module hands it.
//
// Reentrance: applying a preset writes the values through the module's own
// setters, which report every write as an edit. NotifyEdited ignores those
// reports while IsApplying is true, so the consumer needs no guard of its own.
//
// Migration: a store whose Selection is null (a file written before the
// selection existed) gets it derived once from its values — the preset they
// equal, else Custom with those values kept.

namespace Deckle.Presets;

public static class PresetFamily
{
    /// <summary>The reserved selection id meaning "the user's own values".</summary>
    public const string Custom = "custom";
}

public sealed class PresetFamily<T> where T : class
{
    private readonly IPresetStore<T> _store;

    public PresetFamily(IReadOnlyList<Preset<T>> presets, string defaultId, IPresetStore<T> store)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(defaultId);
        ArgumentNullException.ThrowIfNull(store);

        if (presets.Count == 0)
        {
            throw new ArgumentException("A preset family needs at least one preset.", nameof(presets));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in presets)
        {
            if (string.IsNullOrWhiteSpace(preset.Id))
            {
                throw new ArgumentException("A preset id cannot be empty.", nameof(presets));
            }

            if (preset.Id == PresetFamily.Custom)
            {
                throw new ArgumentException($"The id \"{PresetFamily.Custom}\" is reserved.", nameof(presets));
            }

            if (!ids.Add(preset.Id))
            {
                throw new ArgumentException($"The preset id \"{preset.Id}\" is declared twice.", nameof(presets));
            }
        }

        Presets = presets;
        _store = store;
        Default = Find(defaultId)
            ?? throw new ArgumentException($"The default id \"{defaultId}\" names no preset.", nameof(defaultId));

        if (_store.Selection is null || (_store.Selection != PresetFamily.Custom && Find(_store.Selection) is null))
        {
            DeriveSelectionFromValues();
        }
    }

    /// <summary>The built-in presets, in declared order.</summary>
    public IReadOnlyList<Preset<T>> Presets { get; }

    /// <summary>The preset a reset returns to.</summary>
    public Preset<T> Default { get; }

    /// <summary>The stored selection: a preset id or <see cref="PresetFamily.Custom"/>.</summary>
    public string Selection => _store.Selection ?? PresetFamily.Custom;

    /// <summary>True while <see cref="Select"/> writes a preset's values.</summary>
    public bool IsApplying { get; private set; }

    public Preset<T>? Find(string id)
    {
        foreach (var preset in Presets)
        {
            if (string.Equals(preset.Id, id, StringComparison.Ordinal))
            {
                return preset;
            }
        }

        return null;
    }

    /// <summary>
    /// Selects a preset and writes its values, or selects Custom and writes
    /// the kept Custom values back (the live values stay when none were kept
    /// yet). Returns false, writing nothing, for an unknown id.
    /// </summary>
    public bool Select(string id)
    {
        if (id == PresetFamily.Custom)
        {
            // The selection is written before the values: a consumer whose
            // value setters read Selection back (to mirror it into a selector)
            // then sees the new one, not the one being left.
            IsApplying = true;
            try
            {
                _store.Selection = PresetFamily.Custom;
                if (_store.CustomValues is { } custom)
                {
                    _store.Values = custom;
                }
                else
                {
                    _store.CustomValues = _store.Values;
                }
            }
            finally
            {
                IsApplying = false;
            }

            return true;
        }

        var preset = Find(id);
        if (preset is null)
        {
            return false;
        }

        IsApplying = true;
        try
        {
            _store.Selection = preset.Id;
            _store.Values = preset.Values;
        }
        finally
        {
            IsApplying = false;
        }

        return true;
    }

    /// <summary>
    /// Called by the consumer after each hand edit of a value the presets fix:
    /// the live values become the Custom values and the selection turns to
    /// Custom. Ignored while a preset is being applied.
    /// </summary>
    public void NotifyEdited()
    {
        if (IsApplying)
        {
            return;
        }

        _store.CustomValues = _store.Values;
        _store.Selection = PresetFamily.Custom;
    }

    /// <summary>
    /// The selector options: each preset as (Id, LabelKey) in declared order,
    /// then Custom last with the given label key.
    /// </summary>
    public IReadOnlyList<(string Value, string LabelKey)> SelectorOptions(string customLabelKey)
    {
        var options = new List<(string Value, string LabelKey)>(Presets.Count + 1);
        foreach (var preset in Presets)
        {
            options.Add((preset.Id, preset.LabelKey));
        }

        options.Add((PresetFamily.Custom, customLabelKey));
        return options;
    }

    private void DeriveSelectionFromValues()
    {
        var current = _store.Values;
        foreach (var preset in Presets)
        {
            if (EqualityComparer<T>.Default.Equals(preset.Values, current))
            {
                _store.Selection = preset.Id;
                return;
            }
        }

        _store.CustomValues = current;
        _store.Selection = PresetFamily.Custom;
    }
}
