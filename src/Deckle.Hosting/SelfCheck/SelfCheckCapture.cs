using Deckle.Vision;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Deckle.Hosting.SelfCheck;

// How a self-check photographs a window, shared by every executable of the
// suite so the published Deckle and each separate app take the same image of
// the same view: the window as DWM composed it, or its XAML content when DWM
// gave nothing; a popup left open by a walk closed before the image; a wait
// until the content has loaded.
public static class SelfCheckCapture
{
    // Creates a window through `ensure`, shows it off screen, waits for it,
    // and photographs it as it opens.
    public static async Task OpenAndCaptureAsync(
        SelfCheckReport report,
        InterfaceCaptureRun run,
        string outputFolder,
        string name,
        Func<Window> ensure)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Window window = ensure();
        OffscreenWindow.ShowInactive(window);
        bool loaded = await WaitUntilLoadedAsync(window);
        (int closed, int left) = loaded ? await CloseTransientPopupsAsync(window) : (0, 0);
        report.Surfaces.Add(await CaptureWindowFrameAsync(
            run, window, name, null, null, loaded, clock.ElapsedMilliseconds, closed, left));
        report.Save(outputFolder);
    }

    // The window as a whole. The pixels DWM composed for it (WindowSnapshot) are
    // the truest, backdrop and caption buttons included; a window nobody can see
    // may deliver none, and then its XAML content is rendered instead, which the
    // technique field of the report says.
    public static async Task<SelfCheckSurface> CaptureWindowFrameAsync(
        InterfaceCaptureRun run,
        Window window,
        string name,
        string? page,
        string? label,
        bool loaded,
        long ms,
        int closed = 0,
        int left = 0)
    {
        string technique = "window-capture";
        string? file = null;
        int width = 0, height = 0;
        string? error = null;
        if (loaded)
        {
            nint hwnd = WindowNative.GetWindowHandle(window);
            if (await run.CaptureAsync(hwnd, name, page, label) && run.LastFile is { } captured)
            {
                file = Path.Combine("captures", captured);
            }
            else
            {
                technique = "window-render";
                try
                {
                    string target = Path.Combine(run.Directory, page is null ? $"{name}.png" : $"{name}-{page}.png");
                    ElementImage.Result image = await ElementImage.CaptureWindowContentAsync(window, target);
                    file = Path.Combine("captures", Path.GetFileName(image.Files[0]));
                    width = image.Width;
                    height = image.Height;
                }
                catch (Exception ex)
                {
                    error = $"{ex.GetType().Name}: {ex.Message}";
                }
            }
        }
        return new SelfCheckSurface(name, page, label, loaded, ms, file, closed, left, technique, width, height, null, error);
    }

    // A capture shows the page alone. The walk itself leaves transient UI open:
    // in the compact rail, selecting a child of a group opens the group's
    // flyout, and it stays over the next pages (seen on the Diagnostics image).
    // Every popup open over the window is closed before its image is taken; the
    // count closed is recorded, and one that refuses to close is a failure of
    // that image, so a reviewer never judges a page hidden behind a popup.
    public static async Task<(int Closed, int StillOpen)> CloseTransientPopupsAsync(Window window)
    {
        if (window.Content?.XamlRoot is not { } root) return (0, 0);

        var open = VisualTreeHelper.GetOpenPopupsForXamlRoot(root);
        int closed = 0;
        foreach (Popup popup in open)
        {
            popup.IsOpen = false;
            closed++;
        }
        if (closed == 0) return (0, 0);

        // The close reaches the screen on the next frames.
        await Task.Delay(300);
        return (closed, VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count);
    }

    // The window's content reached Loaded, then two frames and a short settle,
    // as the page walk waits before a capture.
    public static async Task<bool> WaitUntilLoadedAsync(Window window)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (window.Content is FrameworkElement { IsLoaded: true })
            {
                await Task.Delay(600);
                return true;
            }
            await Task.Delay(30);
        }
        return false;
    }
}
