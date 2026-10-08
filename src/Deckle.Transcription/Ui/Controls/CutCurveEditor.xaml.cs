using Microsoft.UI.Xaml.Controls;

namespace Deckle.Transcription;

// The Settings host of the one shared curve editor. The Playground binds the
// same HangoverCurveCanvas to its own draft view model; here it binds to
// WhisperViewModel, so a handle drag writes the persisted SegCurve* values
// directly. Only this binding shell differs: nothing of the canvas is copied,
// and nothing here depends on the Playground.
//
// The composer builds it inside the "WhisperCutCurveCard" Editor card and owns
// that card's visibility (Custom cut preset only) and reset; this control only
// syncs itself to the view model through x:Bind.
public sealed partial class CutCurveEditor : UserControl
{
    public WhisperViewModel ViewModel { get; }

    public CutCurveEditor(WhisperViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    // The view model keeps the hangover anchors in milliseconds (the unit the
    // Number cards and the persisted settings use); the canvas plots seconds.
    public static double Seconds(double ms) => ms / 1000.0;
}
