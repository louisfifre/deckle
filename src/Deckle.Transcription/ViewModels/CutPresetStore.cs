using Deckle.Presets;

namespace Deckle.Transcription;

// The cut preset family's store. It keeps no copy of its own: the eight values
// go through the view model's setters, so a preset write persists like a hand
// edit; the selection and the custom values live on the persisted
// EnergySegmenterSettings. Those two save on every write: picking a preset
// whose values are already live runs no setter, so the selection would never
// reach the disk.
//
// The segmenter and the save are delegates because the view model builds a
// first family before Load(), bound to a throwaway segmenter, so the family's
// one-time derivation of a missing selection cannot land in the real file from
// stale seeds.
internal sealed class CutPresetStore : IPresetStore<CutCurveValues>
{
    private readonly WhisperViewModel _viewModel;
    private readonly Func<EnergySegmenterSettings> _segmenter;
    private readonly Action _save;

    public CutPresetStore(WhisperViewModel viewModel, Func<EnergySegmenterSettings> segmenter, Action save)
    {
        _viewModel = viewModel;
        _segmenter = segmenter;
        _save = save;
    }

    public CutCurveValues Values
    {
        get => _viewModel.ReadCutValues();
        set => _viewModel.WriteCutValues(value);
    }

    public string? Selection
    {
        get => _segmenter().CutPreset;
        set
        {
            _segmenter().CutPreset = value;
            _save();
        }
    }

    public CutCurveValues? CustomValues
    {
        get => _segmenter().CutCustomValues;
        set
        {
            _segmenter().CutCustomValues = value;
            _save();
        }
    }
}
