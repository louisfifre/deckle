using System;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;
using WinRT.Interop;
using Deckle.Core;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Modules;
using Deckle.Shell.WindowChrome;
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

        // Update and relocate are single-page flows, not wizards: nothing to
        // navigate between, so no rail.
        Nav.IsPaneVisible = IsWizard;

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

    // Back also decides whether completed rail steps can be clicked, so the
    // rail follows it.
    public void SetBackEnabled(bool enabled)
    {
        BackButton.IsEnabled = enabled;
        ApplyStepStates();
    }

    // The total sits outside the scrolling page, so it stays in view while the
    // person scrolls the cards that change it. Navigation closes it; a page
    // that has a total sets it from OnNavigatedTo and on every change.
    public void SetTotalEstimate(string message)
    {
        TotalEstimateBar.Message = message;
        TotalEstimateBar.IsOpen  = true;
    }

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

    // ── Step rail ──────────────────────────────────────────────────────────
    //
    // Each page announces its step from OnNavigatedTo (SetCurrentStep); the window
    // derives everything else. Steps after the current one are disabled, the
    // current one is selected, completed ones swap their icon for a check mark and
    // stay clickable only while Back is allowed (the same rule as the footer's Back
    // button, so the rail never offers a way back the footer would refuse).

    private SetupStep _currentStep = SetupStep.Modules;
    private readonly List<(SetupStep Step, NavigationViewItem Item)> _rail = new();

    // True while the window itself changes the rail's selection, so
    // SelectionChanged does not read it as a click.
    private bool _railSyncing;

    // Wizard flows (install and in-app re-run) get the rail and the large window;
    // update and relocate keep the narrow card.
    private bool IsWizard => !Context.UpdateMode && !Context.RelocateMode;

    public void SetCurrentStep(SetupStep step)
    {
        _currentStep = step;
        RefreshStepRail();
    }

    // Also called by the Modules page on every selection change, so ticking
    // Dictation adds or removes its step live.
    public void RefreshStepRail()
    {
        if (!IsWizard) return;

        var visible = VisibleSteps();
        if (!visible.SequenceEqual(_rail.Select(r => r.Step)))
        {
            _railSyncing = true;
            try
            {
                Nav.MenuItems.Clear();
                _rail.Clear();
                foreach (SetupStep step in visible)
                {
                    var item = new NavigationViewItem
                    {
                        Content = Loc.Get($"Setup_Step_{step}_Label"),
                        Tag     = step,
                    };
                    _rail.Add((step, item));
                    Nav.MenuItems.Add(item);
                }
            }
            finally
            {
                _railSyncing = false;
            }
        }

        ApplyStepStates();
    }

    // Location exists only where the person picks folders: the install wizard,
    // and the process that resumes it (which shows the step as already done).
    private List<SetupStep> VisibleSteps()
    {
        var steps = new List<SetupStep> { SetupStep.Modules };
        if (Context.InstallMode || Context.ResumesInstall) steps.Add(SetupStep.Location);
        if (Context.SelectedModules.Contains(ModuleIds.Transcription)) steps.Add(SetupStep.Dictation);
        steps.Add(SetupStep.Install);
        steps.Add(SetupStep.Finish);
        return steps;
    }

    private void ApplyStepStates()
    {
        if (_rail.Count == 0) return;

        bool canGoBack = BackButton.IsEnabled;
        _railSyncing = true;
        try
        {
            foreach (var (step, item) in _rail)
            {
                bool done = step < _currentStep;
                item.Icon      = new FontIcon { Glyph = done ? Glyphs.Badge.Success : StepGlyph(step) };
                item.IsEnabled = done ? canGoBack : step == _currentStep;
                if (step == _currentStep) Nav.SelectedItem = item;
            }
        }
        finally
        {
            _railSyncing = false;
        }
    }

    private static string StepGlyph(SetupStep step) => step switch
    {
        SetupStep.Modules   => Glyphs.Setup,
        SetupStep.Location  => Glyphs.Folder,
        SetupStep.Dictation => Glyphs.Microphone,
        SetupStep.Install   => Glyphs.Download,
        _                   => Glyphs.Launch,
    };

    private static Type? PageOf(SetupStep step) => step switch
    {
        SetupStep.Modules   => typeof(ModulesPage),
        SetupStep.Location  => typeof(FoldersPage),
        SetupStep.Dictation => typeof(ChoicesPage),
        _                   => null,
    };

    private void OnStepSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_railSyncing) return;

        // Only a completed step reached while Back is allowed goes anywhere; any
        // other click leaves the selection on the current step.
        if (args.SelectedItem is NavigationViewItem { Tag: SetupStep target }
            && target < _currentStep
            && BackButton.IsEnabled
            && TryGoBackTo(target))
        {
            return;
        }

        ApplyStepStates();
    }

    // Drops the pages between here and the target from the back stack, then
    // goes back once: the intermediate pages are never re-entered, so none of
    // their load work runs for nothing. The page reached announces its step,
    // which moves the rail.
    private bool TryGoBackTo(SetupStep target)
    {
        Type? pageType = PageOf(target);
        if (pageType is null) return false;

        IList<PageStackEntry> stack = Body.BackStack;
        int index = -1;
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i].SourcePageType == pageType) { index = i; break; }
        }
        if (index < 0) return false;

        while (stack.Count - 1 > index) stack.RemoveAt(stack.Count - 1);
        Body.GoBack();
        return true;
    }

    // ── Plumbing ───────────────────────────────────────────────────────────

    private static Visibility ToVisibility(bool visible) =>
        visible ? Visibility.Visible : Visibility.Collapsed;

    // Wizard: 960 DIPs wide, the width the Settings window opens at, so the
    // page column is 960 − 220 rail − 2 × 40 margins = 660. 880 high holds the
    // whole module list without scrolling: 48 title bar + 94 step header +
    // 8 cards × 68 with 7 × 4 spacing + 24 page padding + 56 total band + 72
    // footer = 866. Update and relocate keep the 560×720 narrow card they were
    // tuned in, with the rail hidden.
    private const double WizardWidth = 960, WizardHeight = 880;
    private const double CardWidth   = 560, CardHeight   = 720;

    // The floor keeps a 360-DIP page column beside the rail, where a card,
    // its control wrapped under its header, and the three footer buttons
    // still fit. 400 high is the Settings window's floor: header, total and
    // footer keep a card's height of page between them.
    // The margins are the page padding of SetupWindow.xaml; the rail width is
    // read from the NavigationView itself.
    private const double SideMargins    = 2 * 40;
    private const double MinColumnWidth = 360;
    private const double MinHeight      = 400;

    private void ConfigureWindow()
    {
        // Centred on the window's display work area and capped at 90 % of it,
        // so a small display never gets a window larger than its screen; the
        // page then scrolls in its margin. AppWindow sizes are raw pixels, so
        // DIPs are scaled by GetDpiForWindow / 96.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        if (AppWindow is { } appWindow)
        {
            appWindow.Title = Loc.Get("Setup_WindowTitle");

            IntPtr hwnd  = WindowNative.GetWindowHandle(this);
            double scale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(hwnd));

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                double minWidth = (IsWizard ? Nav.OpenPaneLength : 0) + SideMargins + MinColumnWidth;
                presenter.PreferredMinimumWidth  = (int)Math.Round(minWidth  * scale);
                presenter.PreferredMinimumHeight = (int)Math.Round(MinHeight * scale);
            }

            var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            if (area is not null)
            {
                (double dipWidth, double dipHeight) = IsWizard ? (WizardWidth, WizardHeight) : (CardWidth, CardHeight);
                int w = Math.Min((int)Math.Round(dipWidth  * scale), (int)(area.WorkArea.Width  * 0.9));
                int h = Math.Min((int)Math.Round(dipHeight * scale), (int)(area.WorkArea.Height * 0.9));
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
            }

            // The control stamps its caption padding in raw physical pixels, as
            // in the Settings window; same shared correction.
            CaptionInsetCorrection.Attach(AppTitleBar, appWindow);
        }
    }

    // Navigating runs before the new page's OnNavigatedTo, so a page that keeps
    // a total reopens the band and one that has none never shows the last one.
    private void OnPageNavigating(object sender, NavigatingCancelEventArgs e) =>
        TotalEstimateBar.IsOpen = false;

    // One scroller serves every page, so a page opens at its top, not at the
    // offset the previous one was left at.
    private void OnPageNavigated(object sender, NavigationEventArgs e) =>
        PageScroller.ChangeView(null, 0, null, disableAnimation: true);

    private void OnBackClick(object sender, RoutedEventArgs e)   => BackRequested?.Invoke();
    private void OnNextClick(object sender, RoutedEventArgs e)   => NextRequested?.Invoke();
    private void OnCancelClick(object sender, RoutedEventArgs e) => Complete(false);

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (!_completion.Task.IsCompleted) _completion.TrySetResult(false);
    }
}
