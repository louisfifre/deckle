// A preset family: the built-in presets of one group of settings in one
// module. It tells which preset the current values match (else Custom),
// applies a preset, and hands a selector its options.
//
// T is the record of the settings group. Value equality is the contract:
// a preset matches when its Values equal the current values. Doubles compare
// exactly, so a preset must hold the very literals Apply writes.
//
// Custom is derived, never stored: it only means "the current values match
// no preset". Apply writes through the consumer's own read/write delegates,
// so persistence stays the module's own; the family keeps no parallel store.

namespace Deckle.Presets;

public static class PresetFamily
{
    /// <summary>The reserved selection id meaning the current values match no preset.</summary>
    public const string Custom = "custom";
}

public sealed class PresetFamily<T> where T : notnull
{
    private readonly Func<T> _read;
    private readonly Action<T> _write;

    public PresetFamily(IReadOnlyList<Preset<T>> presets, Func<T> read, Action<T> write)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);

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
        _read = read;
        _write = write;
    }

    /// <summary>The built-in presets, in declared order.</summary>
    public IReadOnlyList<Preset<T>> Presets { get; }

    /// <summary>The selection id: the matching preset's id, else <see cref="PresetFamily.Custom"/>.</summary>
    public string Selection => Match()?.Id ?? PresetFamily.Custom;

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

    /// <summary>The first preset whose values equal the current values, else null.</summary>
    public Preset<T>? Match()
    {
        var current = _read();
        foreach (var preset in Presets)
        {
            if (EqualityComparer<T>.Default.Equals(preset.Values, current))
            {
                return preset;
            }
        }

        return null;
    }

    /// <summary>
    /// Writes a known preset's values and returns true; Custom or an unknown
    /// id writes nothing and returns false.
    /// </summary>
    public bool Apply(string id)
    {
        var preset = Find(id);
        if (preset is null)
        {
            return false;
        }

        _write(preset.Values);
        return true;
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
}
