using System.Numerics;
using Deckle.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Deckle.App;

// The HUD's processing outline around the field while a run works (Louis,
// 2026-11-14): grey while it transcribes, in colour while it rewrites, the
// same stroke and variants as the HUD. The stroke is baked at the host's size,
// so a resize during a run rebuilds it. Windows' animation preference is
// followed: off, the stroke stands still.
public sealed partial class TranscribeRewriteWindow
{
    private HudComposition.ProcessingStroke? _outline;
    private ProcessingVariant? _outlineVariant;

    private void ShowOutline(ProcessingVariant variant)
    {
        _outlineVariant = variant;
        if (_outline is not null)
        {
            _outline.ApplyVariant(variant, IsDark);
            return;
        }
        BuildOutline(variant);
    }

    private void HideOutline()
    {
        _outlineVariant = null;
        DisposeOutline();
    }

    private void RebuildOutlineForSize()
    {
        if (_outlineVariant is not { } variant) return;
        DisposeOutline();
        BuildOutline(variant);
    }

    private void BuildOutline(ProcessingVariant variant)
    {
        float w = (float)OutlineHost.ActualWidth;
        float h = (float)OutlineHost.ActualHeight;
        if (w <= 0f || h <= 0f) return; // laid out later: SizeChanged builds it

        var compositor = ElementCompositionPreview.GetElementVisual(OutlineHost).Compositor;
        bool animations = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        _outline = HudComposition.CreateProcessingStroke(
            compositor, new Vector2(w, h), IsDark, new HudComposition.ConicArcStrokeConfig(), animations);
        ElementCompositionPreview.SetElementChildVisual(OutlineHost, _outline.Visual);
        _outline.ApplyVariant(variant, IsDark);
    }

    // A missed Dispose leaves the brushes and their animations on the compositor.
    private void DisposeOutline()
    {
        if (_outline is null) return;
        ElementCompositionPreview.SetElementChildVisual(OutlineHost, null);
        _outline.Dispose();
        _outline = null;
    }

    private bool IsDark => RootGrid.ActualTheme == ElementTheme.Dark;
}
