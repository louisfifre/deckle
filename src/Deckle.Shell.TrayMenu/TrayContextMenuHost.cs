using System;
using Deckle.Catalog;
using Deckle.Core.Interop;
using Deckle.Shell.TrayMenu.Interop;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Deckle.Shell.TrayMenu;

// ─── Tray context menu — WinUI 3 SecondWindow pattern ─────────────────────────
//
// Hôte qui ponte le tray Win32 (Shell_NotifyIcon) au MenuFlyout WinUI 3 natif.
// Le pattern, inspiré de la librairie open source H.NotifyIcon (MIT), tient en
// une fenêtre WinUI transparente pré-créée à l'init : sur clic droit du tray,
// elle est positionnée au curseur et un MenuFlyout y est ouvert en
// FlyoutShowMode.Transient. Sans cette fenêtre porteuse, un MenuFlyout ne peut
// pas être ancré à un HWND message-only — il lui faut un XamlRoot.
//
// La fenêtre porteuse est invisible : WS_EX_LAYERED + SetLayeredWindowAttributes
// avec alpha=0. Le MenuFlyout est rendu par WinUI dans son propre popup (HWND
// enfant détaché), donc reste pleinement visible. Le rendu Win11 (mica, coins
// arrondis, ombre Fluent, animations natives, ToggleMenuFlyoutItem avec
// checkmark canonique) vient gratuitement avec le contrôle.
//
// Dismiss : Window.Activated → Deactivated couvre le click-outside et la
// perte de focus globale ; le Click de chaque item ferme explicitement. Pas
// d'animation de close pour éviter le hack ré-ouverture-pendant-fermeture
// que H.NotifyIcon doit faire (AreOpenCloseAnimationsEnabled = false).

public sealed class TrayContextMenuHost : IDisposable
{
    // Owner HWND (message-only host du tray). Définit le z-order parent du
    // popup pour que l'activation/désactivation s'enchaîne correctement avec
    // le tray. Stocké en const offset Win32 GWLP_HWNDPARENT (-8).
    private const int GWLP_HWNDPARENT = -8;

    // Marge d'exclusion autour du curseur, en pixels physiques. Évite que le
    // popup vienne recouvrir l'icône tray elle-même (l'API
    // CalculatePopupWindowPosition cherche un emplacement adjacent au rect
    // d'exclusion). 36 px = taille typique d'un slot d'icône tray à 100% DPI.
    private const int CursorExcludeHalfExtent = 18;

    // Bordures internes ajoutées au calcul de taille du flyout (équivalent au
    // padding de la card MenuFlyout). 4 px top + 4 px bottom + 4 px gauche +
    // 4 px droite, scalés par le RasterizationScale du XamlRoot.
    private const double FlyoutFrameMargin = 4.0;

    private readonly IntPtr _ownerHwnd;

    private Window? _window;
    private Frame? _frame;
    private MenuFlyout? _flyout;
    private ToggleMenuFlyoutItem? _ambientItem;
    private IntPtr _hwnd;
    private AppWindow? _appWindow;

    private bool _isVisible;
    private bool _primed;
    private bool _disposed;

    public Action? OnShowLogs        { get; set; }
    public Action? OnShowSettings    { get; set; }
    public Action? OnShowPlayground  { get; set; }
    public Action? OnToggleAmbient   { get; set; }
    public Action? OnRestart         { get; set; }
    public Action? OnQuit            { get; set; }
    public Func<bool>? IsAmbientOn   { get; set; }

    public TrayContextMenuHost(IntPtr ownerHwnd)
    {
        _ownerHwnd = ownerHwnd;
        BuildWindow();
        BuildFlyout();
    }

    // ── Build window ──────────────────────────────────────────────────────────

    private void BuildWindow()
    {
        _window = new Window();
        _frame = new Frame
        {
            // Background transparent : le frame ne peint rien, la fenêtre
            // entière reste invisible grâce au layered alpha=0.
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        _window.Content = _frame;

        _hwnd = WindowNative.GetWindowHandle(_window);
        _appWindow = _window.AppWindow;

        // Owner = HWND du message-only host du tray. Positionne le popup dans
        // le z-order / activation stack du tray, exactement comme un menu
        // contextuel natif Win32. Sans owner, le popup serait une fenêtre
        // top-level autonome — l'activation et le dismiss ne s'enchaîneraient
        // plus correctement avec le tray.
        NativeMethods.SetWindowLongPtr(_hwnd, GWLP_HWNDPARENT, _ownerHwnd);

        // Coins arrondis Win11 : DWM clippe le HWND au niveau du compositeur.
        // S'applique à toute la fenêtre porteuse, mais comme elle est
        // invisible (alpha=0), l'effet visible n'apparaît qu'au popup
        // MenuFlyout qui suit sa propre forme arrondie.
        uint rounded = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref rounded, sizeof(uint));

        // WS_EX_LAYERED + alpha=0 : fenêtre porteuse complètement invisible.
        // Le MenuFlyout est rendu dans un popup WinUI séparé (HWND enfant),
        // donc visible normalement. Pas de WS_EX_TRANSPARENT — la fenêtre
        // doit recevoir le focus pour que le dismiss par Activated marche.
        IntPtr exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        IntPtr newExStyle = new(exStyle.ToInt64() | (long)NativeMethods.WS_EX_LAYERED);
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, newExStyle);
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 0, NativeMethods.LWA_ALPHA);

        if (_appWindow is not null)
        {
            _appWindow.IsShownInSwitchers = false;
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsResizable = false;
                presenter.IsAlwaysOnTop = true;
                presenter.SetBorderAndTitleBar(false, false);
            }
        }

        _window.Activated += OnWindowActivated;
        _frame.Loaded += OnFrameLoaded;
    }

    // ── Build flyout ──────────────────────────────────────────────────────────

    private void BuildFlyout()
    {
        _flyout = new MenuFlyout
        {
            // Animations désactivées : H.NotifyIcon documente un hack
            // ré-ouverture-pendant-close-animation pour éviter que masquer
            // la fenêtre porteuse mid-animation coupe la transition. En
            // les coupant on évite le hack entièrement — le menu apparaît
            // et disparaît instantanément, ce qui colle au tempo d'un clic
            // tray Windows natif (TrackPopupMenu est tout aussi sec).
            AreOpenCloseAnimationsEnabled = false,
        };

        // Ambient Light en tête : c'est la commande à toggle la plus fréquente
        // pour Louis (allumer/éteindre les LEDs sans naviguer dans Settings).
        // Les commandes d'ouverture de fenêtre viennent ensuite, séparées des
        // commandes de cycle de vie (Restart, Quit) par un séparateur final.
        _ambientItem = new ToggleMenuFlyoutItem { Text = Loc.Get("TrayMenu_AmbientLight") };
        _ambientItem.Click += (_, _) => { Hide(); OnToggleAmbient?.Invoke(); };
        _flyout.Items.Add(_ambientItem);

        _flyout.Items.Add(new MenuFlyoutSeparator());
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Logs"),       () => OnShowLogs?.Invoke()));
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Settings"),   () => OnShowSettings?.Invoke()));
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Playground"), () => OnShowPlayground?.Invoke()));

        _flyout.Items.Add(new MenuFlyoutSeparator());
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Restart"), () => OnRestart?.Invoke()));
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Quit"),    () => OnQuit?.Invoke()));

        _flyout.Closed += OnFlyoutClosed;
    }

    private MenuFlyoutItem CreateItem(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => { Hide(); action(); };
        return item;
    }

    // ── Show ──────────────────────────────────────────────────────────────────

    public void Show()
    {
        if (_disposed || _window is null || _frame is null || _flyout is null || _appWindow is null)
            return;

        if (_ambientItem is not null && IsAmbientOn is not null)
            _ambientItem.IsChecked = IsAmbientOn();

        NativeMethods.GetCursorPos(out POINT cursor);

        var (width, height) = MeasureFlyout();
        var anchor = cursor;
        var size = new TrayMenuNativeMethods.SIZE { cx = width, cy = height };

        NativeMethods.RECT exclude = new()
        {
            left   = cursor.X - CursorExcludeHalfExtent,
            top    = cursor.Y - CursorExcludeHalfExtent,
            right  = cursor.X + CursorExcludeHalfExtent,
            bottom = cursor.Y + CursorExcludeHalfExtent,
        };

        NativeMethods.RECT popup = default;
        TrayMenuNativeMethods.CalculatePopupWindowPosition(
            ref anchor, ref size,
            NativeMethods.TPM_BOTTOMALIGN | TrayMenuNativeMethods.TPM_WORKAREA,
            ref exclude, ref popup);

        _appWindow.MoveAndResize(new Windows.Graphics.RectInt32
        {
            X = popup.left,
            Y = popup.top,
            Width = popup.right - popup.left,
            Height = popup.bottom - popup.top,
        });

        _isVisible = true;
        NativeMethods.ShowWindow(_hwnd, TrayMenuNativeMethods.SW_SHOWNORMAL);
        NativeMethods.SetForegroundWindow(_hwnd);

        _flyout.ShowAt(_frame, new FlyoutShowOptions
        {
            ShowMode = FlyoutShowMode.Transient,
        });
    }

    // ── Hide ──────────────────────────────────────────────────────────────────

    private void Hide()
    {
        if (!_isVisible) return;
        _isVisible = false;
        _flyout?.Hide();
        if (_hwnd != IntPtr.Zero)
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
    }

    // ── Lifecycle handlers ────────────────────────────────────────────────────

    private void OnFrameLoaded(object sender, RoutedEventArgs e)
    {
        if (_primed || _flyout is null || _frame is null) return;
        _primed = true;

        // WS_POPUPWINDOW post-Loaded : efface la caption héritée
        // d'OverlappedPresenter même quand SetBorderAndTitleBar(false, false)
        // a été appelé. Sans ça, le HWND garde un WS_CAPTION résiduel visible
        // au DWM, ce qui interfère avec le rendu rounded corners.
        IntPtr newStyle = new((long)TrayMenuNativeMethods.WS_POPUPWINDOW);
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE, newStyle);
        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
                | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);

        // Prime measure : le premier ShowAt mesure mal les items
        // (microsoft-ui-xaml#7374 — 40 px observés au lieu de 32 px). Un
        // cycle ShowAt/Hide invisible amorce le visual tree pour que les
        // mesures subséquentes soient correctes dès la première vraie ouverture.
        _flyout.ShowAt(_frame, new FlyoutShowOptions { ShowMode = FlyoutShowMode.Transient });
        _flyout.Hide();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && _isVisible)
            Hide();
    }

    private void OnFlyoutClosed(object? sender, object e)
    {
        if (_isVisible) Hide();
    }

    // ── Measure ───────────────────────────────────────────────────────────────
    //
    // Itère les items du flyout, force une hauteur et un padding canonique
    // (compense le défaut de mesure WinUI documenté en microsoft-ui-xaml#7374),
    // mesure chacun et somme les dimensions. Convertit en pixels physiques via
    // RasterizationScale du XamlRoot. Le surplus FlyoutFrameMargin × 2 couvre
    // la card du MenuFlyout (border + padding interne).

    private (int width, int height) MeasureFlyout()
    {
        if (_flyout is null) return (0, 0);

        double width = 0;
        double height = 0;
        foreach (var item in _flyout.Items)
        {
            item.Height = 32;
            item.Padding = new Thickness(11, 0, 11, 0);
            item.Measure(new Windows.Foundation.Size(10_000, 10_000));
            width = Math.Max(width, item.DesiredSize.Width);
            height += item.DesiredSize.Height;
        }

        double scale = _flyout.XamlRoot?.RasterizationScale ?? 1.0;
        return (
            (int)((width  + FlyoutFrameMargin * 2) * scale),
            (int)((height + FlyoutFrameMargin * 2) * scale));
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_window is not null)
        {
            _window.Activated -= OnWindowActivated;
            if (_frame is not null)
                _frame.Loaded -= OnFrameLoaded;
            if (_flyout is not null)
                _flyout.Closed -= OnFlyoutClosed;
            _window.Close();
        }
    }
}
