using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Localization;
using Deckle.Shell;
using Deckle.Shell.WindowChrome;
using Deckle.Transcription;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;

namespace Deckle.App;

// The Transcribe and rewrite window (specs/deckle-transcribe-and-rewrite.md):
// a text pasted or transcribed from a file, rewritten on demand by the profile
// shown. The App hands in the engines as callbacks, as it does for LogWindow,
// so the window never builds a service of its own. The commands live in
// TranscribeRewriteWindow.Commands.cs, the field in .Editor.cs and the HUD
// outline around it in .Outline.cs.
public sealed partial class TranscribeRewriteWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly TranscribeRewriteHistory _history = new();

    // Null while speech is not set up: the file button then says so.
    public Func<string, Action<TranscribedUtterance>, Task<string?>>? TranscribeFile { get; set; }

    // Blocking, as RewriteService.Rewrite is: the window calls it off the UI thread.
    public Func<string, RewriteProfile, GenerationResult>? Rewrite { get; set; }

    public Action? OnShowRewriteSettingsRequested { get; set; }

    public TranscribeRewriteWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        string? icon = IconAssets.ResolveImagePath(recording: false);
        if (icon is not null)
            AppTitleBarIcon.ImageSource = new BitmapImage(new Uri(icon));
        IconAssets.ApplyToWindow(AppWindow);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // Standard, not Tall (Louis, 2026-11-14): the bar holds nothing but the
        // title, and tall caption buttons reached down over the toolbar row.
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        CaptionInsetCorrection.Attach(AppTitleBar, AppWindow);
        SystemBackdrop = new MicaBackdrop();
        Title = Loc.Get("TranscribeRewrite_WindowTitle");

        // Small at first (Louis, 2026-11-14): the person enlarges it, and the
        // placement store gives it back at that size from then on.
        double dpiScale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(_hwnd));
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min((int)(640 * dpiScale), workArea.Width),
            Math.Min((int)(480 * dpiScale), workArea.Height)));

        // The minimum keeps both rows of buttons on one line each.
        var presenter = OverlappedPresenter.Create();
        presenter.PreferredMinimumWidth  = (int)(520 * dpiScale);
        presenter.PreferredMinimumHeight = (int)(320 * dpiScale);
        AppWindow.SetPresenter(presenter);

        LabelIconButtons();
        LoadProfiles();
        LlmSettingsService.Instance.Changed += OnLlmSettingsChanged;
        OutlineHost.SizeChanged += (_, _) => RebuildOutlineForSize();
        Closed += (_, _) =>
        {
            LlmSettingsService.Instance.Changed -= OnLlmSettingsChanged;
            DisposeOutline();
        };

        UpdateCommands();
    }

    public void ShowAndActivate()
    {
        if (AppWindow.Presenter is OverlappedPresenter op &&
            op.State == OverlappedPresenterState.Minimized)
        {
            op.Restore();
        }

        AppWindow.Show();
        Activate();
        // Activate() does not always bring the window to front from a tray
        // callback. SetForegroundWindow is allowed: the tray host is same-process.
        NativeMethods.SetForegroundWindow(_hwnd);
        WindowingProbe.EmitWindowPositioned(_hwnd, "transcribe-rewrite", "Center");
    }

    // An icon alone carries its name in a tooltip and for a screen reader.
    private void LabelIconButtons()
    {
        Label(UndoButton, "TranscribeRewrite_Undo");
        Label(RedoButton, "TranscribeRewrite_Redo");
        Label(RewriteSettingsButton, "TranscribeRewrite_RewriteSettings");

        static void Label(Button button, string key)
        {
            string text = Loc.Get(key);
            ToolTipService.SetToolTip(button, text);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text);
        }
    }

    // The profiles of the Rewriting page, by name. The one shown stays shown
    // across a settings change while it still exists.
    private void LoadProfiles()
    {
        string? selectedId = (ProfileBox.SelectedItem as RewriteProfile)?.Id;
        List<RewriteProfile> profiles = LlmSettingsService.Instance.Current.Profiles.ToList();
        ProfileBox.ItemsSource = profiles;
        ProfileBox.SelectedItem =
            profiles.FirstOrDefault(p => p.Id == (selectedId ?? s_lastProfileId))
            ?? profiles.FirstOrDefault();
    }

    // The last profile used, for the next opening of the window in this run.
    private static string? s_lastProfileId;

    private void OnLlmSettingsChanged()
        => DispatcherQueue.TryEnqueue(LoadProfiles);
}
