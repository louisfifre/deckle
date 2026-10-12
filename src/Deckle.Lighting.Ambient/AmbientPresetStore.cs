using Deckle.Presets;

namespace Deckle.Lighting.Ambient;

// Hands the preset family the Ambient settings: the eight tuning values as
// one record, the stored selection and the kept Custom values. Reads the
// current settings object on every access, so a Reload or Replace on the
// service is followed. Saving stays the caller's act, as for any other edit.
internal sealed class AmbientPresetStore(Func<AmbientSettings> current) : IPresetStore<AmbientTuning>
{
    public AmbientTuning Values
    {
        get => AmbientTuning.From(current());
        set => value.ApplyTo(current());
    }

    public string? Selection
    {
        get => current().Preset;
        set => current().Preset = value;
    }

    public AmbientTuning? CustomValues
    {
        get => current().CustomTuning;
        set => current().CustomTuning = value;
    }
}
