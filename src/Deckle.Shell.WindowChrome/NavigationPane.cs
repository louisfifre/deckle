using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Shell.WindowChrome;

// ─── Navigation pane ──────────────────────────────────────────────────────────
//
// One behaviour for the navigation of every Deckle window, as
// specs/deckle-navigation-pane.md writes it: in a window wide enough for the
// rail, the navigation sits beside the page, as the rail or open, and the user's
// choice stays; in a narrow window, nothing shows and the navigation button
// opens it over the page.
//
// Why the mode is set here rather than left to PaneDisplayMode="Auto". Auto
// shows the rail between its two thresholds, but in that band it opens the pane
// OVER the page (LeftCompact) and closes it on every choice, so the user could
// never keep it open there. Left, by contrast, is the rail when closed and the
// open pane beside the page when open, at any width: it is the docked mode this
// behaviour needs, and LeftMinimal is the narrow one.
//
// The window keeps the title bar's button: its PaneToggleRequested calls
// Toggle. The NavigationView's own toggle stays off, as each window's XAML has it.
public sealed class NavigationPane
{
    // WinUI's own CompactModeThresholdWidth default, the width from which
    // NavigationView itself starts showing the rail beside the page.
    public const double DefaultDockedFromWidth = 641;

    // The choice of each kind of window, kept while Deckle runs, so a window
    // closed and opened again finds its navigation as the user left it (spec,
    // Opening a window). UI thread only.
    private static readonly Dictionary<string, bool> Choices = [];

    private readonly NavigationView _nav;
    private readonly string _windowKey;
    private readonly double _dockedFromWidth;
    private readonly NavigationPaneState _state;
    private bool _applying;

    private NavigationPane(NavigationView nav, string windowKey, bool openAtFirst, double dockedFromWidth)
    {
        _nav = nav;
        _windowKey = windowKey;
        _dockedFromWidth = dockedFromWidth;
        bool openByChoice = Choices.TryGetValue(windowKey, out bool kept) ? kept : openAtFirst;
        // Docked until the first measure says otherwise: the first layout pass
        // then corrects it, before anything is drawn.
        _state = new NavigationPaneState(openByChoice, docked: true);

        nav.SizeChanged += OnSizeChanged;
        nav.PaneClosed += OnPaneClosed;
        Apply();
    }

    /// <summary>Gives a window's NavigationView the shared behaviour.
    /// <paramref name="windowKey"/> names the kind of window, so its choice
    /// survives the window being closed and opened again.
    /// <paramref name="openAtFirst"/> is what the window's spec says for the
    /// first opening.</summary>
    public static NavigationPane Attach(
        NavigationView nav, string windowKey, bool openAtFirst = true, double dockedFromWidth = DefaultDockedFromWidth) =>
        new(nav, windowKey, openAtFirst, dockedFromWidth);

    /// <summary>Raised after the pane opened or closed, by the user or by a
    /// change of width.</summary>
    public event Action? Changed;

    /// <summary>True while the pane shows its names, beside the page or over it.</summary>
    public bool IsOpen => _state.PaneOpen;

    /// <summary>True while the pane covers the page: a narrow window.</summary>
    public bool IsOverPage => !_state.Docked;

    /// <summary>The title bar's navigation button.</summary>
    public void Toggle()
    {
        _state.Toggle();
        Remember();
        Apply();
    }

    /// <summary>Opens or closes the pane as the navigation button would.</summary>
    public void SetOpen(bool open)
    {
        if (open == _state.PaneOpen) return;
        _state.SetOpen(open);
        Remember();
        Apply();
    }

    /// <summary>A place was chosen: a pane over the page gets out of the way,
    /// a docked one stays as it is.</summary>
    public void CloseIfOverPage()
    {
        if (_state.Docked || !_state.OpenOverPage) return;
        _state.SetOpen(false);
        Apply();
    }

    private void Remember()
    {
        if (_state.Docked) Choices[_windowKey] = _state.OpenByChoice;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_state.SetDocked(args.NewSize.Width >= _dockedFromWidth)) Apply();
    }

    // In LeftMinimal the control closes its pane by itself on a choice or a
    // click on the page. The state follows, so the next press opens it again.
    private void OnPaneClosed(NavigationView sender, object args)
    {
        if (_applying) return;
        _state.ClosedOverPage();
        Changed?.Invoke();
    }

    // The mode first, then the open state: NavigationView may close its pane
    // while it changes mode (a guard: its documentation does not say), so the
    // state is written after, and PaneClosed is ignored during the change.
    private void Apply()
    {
        _applying = true;
        try
        {
            NavigationViewPaneDisplayMode mode = _state.Docked
                ? NavigationViewPaneDisplayMode.Left
                : NavigationViewPaneDisplayMode.LeftMinimal;
            if (_nav.PaneDisplayMode != mode) _nav.PaneDisplayMode = mode;
            if (_nav.IsPaneOpen != _state.PaneOpen) _nav.IsPaneOpen = _state.PaneOpen;
        }
        finally
        {
            _applying = false;
        }
        Changed?.Invoke();
    }
}
