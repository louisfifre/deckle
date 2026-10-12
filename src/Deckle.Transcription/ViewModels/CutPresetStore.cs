using Deckle.Presets;

namespace Deckle.Transcription;

// The store the cut preset family reads and writes through (IPresetStore). It
// joins two places that already exist, and keeps no copy of its own:
//
//   Values       the eight hangover values live in the view model's setters, so a
//                preset write goes through WriteCutValues and persists exactly
//                like a hand edit (each setter pushes to the settings file).
//   Selection    the stored selection id and the user's own values live on the
//   CustomValues persisted EnergySegmenterSettings (CutPreset, CutCustomValues).
//                They are saved on every write: picking a preset can change no
//                value (it was already the live one), so no setter would run and
//                the selection would never reach the disk.
//
// The segmenter and the save are handed in as delegates, not read from the
// settings service here, for one reason: the view model builds a first family
// before Load(), when the persisted values are not yet in its setters. That
// family is bound to a throwaway segmenter, so the family's one-time derivation
// of a missing selection (which compares the live values to the presets) can not
// land in the real file from stale seeds. Load() then builds the real family.
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
