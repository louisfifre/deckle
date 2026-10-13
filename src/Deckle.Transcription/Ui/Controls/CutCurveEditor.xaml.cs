using Microsoft.UI.Xaml.Controls;

namespace Deckle.Transcription;

// The Settings host of HangoverCurveCanvas. It binds to WhisperViewModel, so a
// handle drag writes the persisted SegCurve* values directly. The composer's
// "WhisperCutCurveCard" Editor card owns this control's visibility and reset.
public sealed partial class CutCurveEditor : UserControl
{
    public WhisperViewModel ViewModel { get; }

    public CutCurveEditor(WhisperViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    // The view model and the persisted settings keep the anchors in
    // milliseconds; the canvas plots seconds.
    public static double Seconds(double ms) => ms / 1000.0;
}
