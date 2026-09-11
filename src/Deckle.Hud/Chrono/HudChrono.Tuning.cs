using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Deckle.Composition;
using Deckle.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Deckle.Hud;

public sealed partial class HudChrono
{
    private void OnMaterialTuningChanged()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(OnMaterialTuningChanged);
            return;
        }
        RebuildCountdownVisual();
        RebuildTunedStroke();
    }

    private void OnGeometryTuningChanged()
    {
        if (DispatcherQueue.HasThreadAccess) RebuildTunedStroke();
        else DispatcherQueue.TryEnqueue(RebuildTunedStroke);
    }

    private void RebuildTunedStroke()
    {
        if (_processingStroke is not { } previous || _currentVariant is not { } variant) return;
        var config = HudGeometryTuning.ApplyTo(HudMaterialTuning.ApplyTo(previous.Config));
        if (config.Equals(previous.Config)) return;

        var compositor = ElementCompositionPreview.GetElementVisual(ProcessingSurfaceHost).Compositor;
        float width = (float)ProcessingSurfaceHost.ActualWidth;
        float height = (float)ProcessingSurfaceHost.ActualHeight;
        if (width <= 0 || height <= 0) return;
        var size = new Vector2(width, height);
        // Bake before taking the audio gate. The producer can keep updating the
        // previous stroke while Win2D prepares the new material on the UI thread.
        var replacement = variant == ProcessingVariant.Recording
            ? HudComposition.CreateRecordingStroke(compositor, size, config, _animationsEnabled)
            : HudComposition.CreateProcessingStroke(compositor, size, config, _animationsEnabled);
        try
        {
            replacement.ApplyVariant(variant, ChronoRoot.ActualTheme == ElementTheme.Dark);
            if (_revealsActive) TearDownReveals();
            lock (_strokeSync)
            {
                if (variant == ProcessingVariant.Recording) replacement.UpdateLevel(_smoothedLevel);
                ElementCompositionPreview.SetElementChildVisual(ProcessingSurfaceHost, replacement.Visual);
                _processingStroke = replacement;
            }
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        // The swap completed under the gate, so no producer can still hold the
        // previous stroke. Release outside the gate; digit reveals were detached.
        int ageMs = (int)Stopwatch.GetElapsedTime(_processingStrokeAcquiredTicks).TotalMilliseconds;
        DeckleResourceSource.Log.ResourceReleased("composition-visual", _processingStrokeHandle, ageMs, "hud-chrono-stroke");
        previous.Dispose();
        _processingStrokeHandle = RuntimeHelpers.GetHashCode(replacement.Visual);
        _processingStrokeAcquiredTicks = Stopwatch.GetTimestamp();
        DeckleResourceSource.Log.ResourceAcquired("composition-visual", _processingStrokeHandle, 0, "hud-chrono-stroke");
        if (_revealsActive)
        {
            EnsureReveals();
            HookRendering();
            UpdateReveals();
        }
    }
}
