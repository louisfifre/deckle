using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Input.PrecisionScroll;

public partial class PrecisionScrollViewModel
{
    [ObservableProperty]
    public partial double DistancePerDetentMm { get; set; }

    [ObservableProperty]
    public partial double InitialStepDurationMs { get; set; }

    [ObservableProperty]
    public partial double QuietPeriodScale { get; set; }

    private void LoadTuning(PrecisionScrollTuning tuning)
    {
        DistancePerDetentMm = tuning.DistancePerDetentMm;
        InitialStepDurationMs = tuning.InitialStepDurationMs;
        QuietPeriodScale = tuning.QuietPeriodScale;
    }

    private PrecisionScrollTuning CreateTuning() => new()
    {
        DistancePerDetentMm = DistancePerDetentMm,
        InitialStepDurationMs = InitialStepDurationMs,
        QuietPeriodScale = QuietPeriodScale,
    };

    partial void OnDistancePerDetentMmChanged(double value) => SaveTuning();
    partial void OnInitialStepDurationMsChanged(double value) => SaveTuning();
    partial void OnQuietPeriodScaleChanged(double value) => SaveTuning();

    private void SaveTuning()
    {
        if (!_isSyncing)
            Save();
    }
}
