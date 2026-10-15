using System;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;
using Deckle.Core;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Transcription.Whisper;

namespace Deckle.Setup;

// Shell of the setup wizard: step header, page Frame, and a footer with
// Cancel, Back and Next. The window does not know how to advance; the active
// page handles NextRequested and BackRequested and navigates Body itself.
public sealed partial class SetupWindow : Window
{
    private readonly TaskCompletionSource<bool> _completion = new();

    public SetupContext Context { get; }

    // Resolves true when a page calls Complete(true); false on Cancel,
    // window close, or Complete(false).
    public Task<bool> Completion => _completion.Task;

    // Pages navigate with `Body.Navigate(typeof(Next), setupWindow)`; the next
    // page picks the SetupWindow up from OnNavigatedTo's Parameter.
    public Frame Body => ContentFrame;

    public event Action? NextRequested;
    public event Action? BackRequested;

    public SetupWindow() : this(null) { }

    // The install, update and relocate hosts build their own context and pass
    // it in. The parameterless path serves the in-app wizard: the window seeds
    // the default context.
    public SetupWindow(SetupContext? context)
    {
        InitializeComponent();
        // The module selection is seeded from the recorded presence choice
        // (full catalogue when none), so the install plan is meaningful even
        // if a navigation path skips the Modules page.
        Context = context ?? new SetupContext
        {
            SelectedModel = SpeechModels.DefaultWhisperModel,
            SelectedModules = Deckle.Modules.ModulePresence.Choice
                ?? Deckle.Modules.ModuleRegistry.Modules
                    .Select(m => m.Id)
                    .ToHashSet(StringComparer.Ordinal),
        };

        SystemBackdrop = new MicaBackdrop();

        ConfigureWindow();

        Closed += OnWindowClosed;
        DeckleSetupSource.Log.WindowOpened();

        // Theme switches are traced even on this short-lived window, to
        // correlate an InfoBar or ProgressBar glitch with a switch.
        if (Content is FrameworkElement root)
        {
            _lastTheme = root.ActualTheme;
            root.ActualThemeChanged += OnRootActualThemeChanged;
        }
    }

    // ── Theme tracing ────────────────────────────────────────────────────────
    private ElementTheme _lastTheme;

    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        var to = sender.ActualTheme;
        if (to == _lastTheme) return;
        string source = ThemeRequestSourceProbe.Consume() ?? "system";
        DeckleThemeSource.Log.ThemeChanged(
            "setup", _lastTheme.ToString(), to.ToString(), source);
        _lastTheme = to;
    }

    // ── Public surface for pages ───────────────────────────────────────────

    public void SetStepHeader(string title, string subtitle)
    {
        StepTitle.Text    = title;
        StepSubtitle.Text = subtitle;
    }

    public void SetBackEnabled(bool enabled)  => BackButton.IsEnabled = enabled;
    public void SetNextEnabled(bool enabled)  => NextButton.IsEnabled = enabled;
    public void SetNextLabel(string label)    => NextButton.Content   = label;
    public void SetNextVisible(bool visible)  => NextButton.Visibility   = ToVisibility(visible);
    public void SetCancelVisible(bool visible) => CancelButton.Visibility = ToVisibility(visible);

    public void Complete(bool success)
    {
        DeckleSetupSource.Log.WindowClosing();
        DeckleSetupSource.Log.WindowClosingDetail(success);
        if (!_completion.Task.IsCompleted) _completion.TrySetResult(success);
        Close();
    }

    // ── Plumbing ───────────────────────────────────────────────────────────

    private static Visibility ToVisibility(bool visible) =>
        visible ? Visibility.Visible : Visibility.Collapsed;

    private void ConfigureWindow()
    {
        // 560×720 DIPs, a narrow card shape tuned by hand, centred on the
        // window's display work area. AppWindow.MoveAndResize takes raw
        // pixels, so DIPs are scaled by GetDpiForWindow / 96.
        ExtendsContentIntoTitleBar = true;
        if (AppWindow is { } appWindow)
        {
            appWindow.Title = Loc.Get("Setup_WindowTitle");

            var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            if (area is not null)
            {
                IntPtr hwnd  = WindowNative.GetWindowHandle(this);
                double scale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(hwnd));
                int w = (int)Math.Round(560 * scale);
                int h = (int)Math.Round(720 * scale);
                int x = area.WorkArea.X + (area.WorkArea.Width  - w) / 2;
                int y = area.WorkArea.Y + (area.WorkArea.Height - h) / 2;
                appWindow.MoveAndResize(new RectInt32(x, y, w, h));

                // Emitted after MoveAndResize: this window centres itself
                // explicitly, so its anchor is "Center".
                WindowingProbe.EmitWindowPositioned(hwnd, "setup", "Center");
            }

            if (appWindow.TitleBar is { } titleBar)
            {
                titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
                titleBar.ButtonBackgroundColor         = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            }
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)   => BackRequested?.Invoke();
    private void OnNextClick(object sender, RoutedEventArgs e)   => NextRequested?.Invoke();
    private void OnCancelClick(object sender, RoutedEventArgs e) => Complete(false);

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (!_completion.Task.IsCompleted) _completion.TrySetResult(false);
    }
}
