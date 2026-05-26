using System;
using System.Diagnostics;
using Deckle.Catalog;
using Deckle.Core.Interop;
using Deckle.Diagnostics;
using Deckle.Shell.TrayMenu.Interop;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
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
// arrondis, ombre Fluent, animations natives) vient gratuitement avec le
// contrôle.
//
// Dismiss : Window.Activated → Deactivated couvre le click-outside et la
// perte de focus globale ; le Click de chaque item ferme explicitement. Pas
// d'animation de close pour éviter le hack ré-ouverture-pendant-fermeture
// que H.NotifyIcon doit faire (AreOpenCloseAnimationsEnabled = false).
//
// Observabilité : les événements de positionnement (anchor, monitor, popup
// position, move-and-resize) sont émis via WindowingProbe sur le sub-provider
// transverse DeckleWindowingSource — pas de duplication sur le provider local
// du module. Le sub-provider local Deckle.Shell.TrayMenu trace uniquement ce
// qui est tray-menu-spécifique : prime cycle, mesure des items, dismiss reason.

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
    // 4 px droite, scalés par le DPI du moniteur sous le curseur.
    private const double FlyoutFrameMargin = 4.0;

    // Hauteur fixe (DIP) appliquée à chaque MenuFlyoutItem du tray menu pour
    // matcher la DesiredSize naturelle Win11 au tout premier show et empêcher
    // la compression défectueuse du MenuFlyoutPresenter (qui rabote les items
    // à MinHeight=32 DIP à partir du 2e/3e show sans re-centrer le texte). 40
    // DIP correspond à la valeur visuelle correcte observée immédiatement
    // après restart de l'app (avant que la compression kick in).
    private const double NativeMenuItemHeight = 40.0;

    private readonly IntPtr _ownerHwnd;

    private Window? _window;
    private Frame? _frame;
    private MenuFlyout? _flyout;
    private MenuFlyoutItem? _ambientItem;
    private ToggleSwitch? _ambientSwitch;
    private IntPtr _hwnd;
    private AppWindow? _appWindow;

    private bool _isVisible;
    private bool _primed;
    private bool _disposed;

    // Trackers pour l'event ShowRequested — calcul du delta inter-ouverture
    // utile pour corréler le pipeline de mesure avec une éventuelle inactivité
    // prolongée du visual tree amorcé entre deux ouvertures.
    private long _lastShowTickMs;
    private int _showCount;

    public Action? OnShowLogs        { get; set; }
    public Action? OnShowSettings    { get; set; }
    public Action? OnShowPlayground  { get; set; }
    public Action? OnToggleAmbient   { get; set; }
    public Action? OnRestart         { get; set; }
    public Action? OnQuit            { get; set; }
    public Func<bool>? IsAmbientOn   { get; set; }

    /// <summary>
    /// Optional accessor for the tray icon's screen rect. When provided, the
    /// menu anchors tangent to the icon via <c>CalculatePopupWindowPosition</c>
    /// — placement adapts automatically to the taskbar orientation (left,
    /// right, top, bottom). When null or returning null, falls back to the
    /// cursor position with a 36×36 px exclude rect.
    /// </summary>
    public Func<NativeMethods.RECT?>? GetIconRect { get; set; }

    public TrayContextMenuHost(IntPtr ownerHwnd)
    {
        _ownerHwnd = ownerHwnd;
        BuildWindow();
        BuildFlyout();

        DeckleShellTrayMenuSource.Log.HostConstructed(ownerHwnd.ToInt64());
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
        //
        // MenuFlyoutItem (pas ToggleMenuFlyoutItem) : on évite la sémantique
        // checkmark native qui ferait réserver une colonne à gauche dans le
        // MenuFlyoutPresenter et décalerait tous les autres items du flyout.
        // Le toggle visuel est porté par un ToggleSwitch greffé à droite via
        // ToggleSwitchMenuItemStyle (Themes/TrayMenu.xaml). L'état IsOn du
        // switch est piloté manuellement depuis Show() (cf. plus bas).
        _ambientItem = new MenuFlyoutItem
        {
            Text = Loc.Get("TrayMenu_AmbientLight"),
            Style = (Style)Application.Current.Resources["ToggleSwitchMenuItemStyle"],
            Height = NativeMenuItemHeight,
        };
        _ambientItem.Click += (_, _) =>
        {
            DeckleShellTrayMenuSource.Log.ItemClicked(_ambientItem.Text);
            Hide("item_click:Ambient");
            OnToggleAmbient?.Invoke();
        };
        _flyout.Items.Add(_ambientItem);

        _flyout.Items.Add(new MenuFlyoutSeparator());
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Logs"),       () => OnShowLogs?.Invoke()));
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Settings"),   () => OnShowSettings?.Invoke()));
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Playground"), () => OnShowPlayground?.Invoke()));

        _flyout.Items.Add(new MenuFlyoutSeparator());
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Restart"), () => OnRestart?.Invoke()));
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Quit"),    () => OnQuit?.Invoke()));

        _flyout.Closed += OnFlyoutClosed;

        DeckleShellTrayMenuSource.Log.FlyoutBuilt(_flyout.Items.Count);
    }

    private MenuFlyoutItem CreateItem(string text, Action action)
    {
        // MenuFlyoutItem natif pur — aucun Style ni Template override. Le
        // framework gère hover, radius, inset, padding, foreground et DPI
        // scaling intégralement. Le seul item retemplaté du tray menu est
        // l'Ambient Light, faute de slot natif pour greffer un switch à
        // droite (cf. ToggleSwitchMenuItemStyle dans Themes/TrayMenu.xaml).
        //
        // Height = NativeMenuItemHeight (40 DIP) : matche la DesiredSize
        // naturelle du MenuFlyoutItem Win11 au tout premier show. Sans Height
        // explicite, le MenuFlyoutPresenter compresse les items à
        // MinHeight (32 DIP) à partir du 2e ou 3e show sans re-centrer
        // verticalement le texte — rendu visuel décalé vers le top. Forcer
        // 40 DIP empêche cette compression défectueuse à la source.
        var item = new MenuFlyoutItem { Text = text, Height = NativeMenuItemHeight };
        item.Click += (_, _) =>
        {
            DeckleShellTrayMenuSource.Log.ItemClicked(text);
            Hide($"item_click:{text}");
            action();
        };
        return item;
    }

    // ── Show ──────────────────────────────────────────────────────────────────

    public void Show()
    {
        if (_disposed || _window is null || _frame is null || _flyout is null || _appWindow is null)
            return;

        long nowTickMs = Environment.TickCount64;
        double msSinceLastShow = _showCount == 0 ? 0 : (nowTickMs - _lastShowTickMs);
        _showCount++;
        _lastShowTickMs = nowTickMs;
        DeckleShellTrayMenuSource.Log.ShowRequested(msSinceLastShow, _showCount);

        if (_ambientItem is not null && IsAmbientOn is not null)
        {
            bool ambientOn = IsAmbientOn();
            // Résolution paresseuse du ToggleSwitch interne au template. Le
            // visual tree est constructible une fois le prime cycle exécuté
            // (cf. OnFrameLoaded plus bas) ; au premier Show() le cache est
            // null, on walk le visual tree de l'item pour trouver le switch
            // nommé "StateSwitch", puis on cache la ref pour les Show()
            // suivants. Plus de TemplateBinding IsChecked fragile.
            var sw = _ambientSwitch ??= FindDescendantByName(_ambientItem, "StateSwitch") as ToggleSwitch;
            if (sw is not null) sw.IsOn = ambientOn;
            DeckleShellTrayMenuSource.Log.AmbientStateRead(ambientOn);
        }

        // Anchor + exclude : on préfère le rect réel de l'icône tray (API
        // Shell_NotifyIconGetRect). Cela rend la position automatiquement
        // correcte quelle que soit l'orientation de la taskbar — au-dessus si
        // taskbar en bas, à droite si taskbar à gauche, etc. — sans dépendre
        // du point de clic sur l'icône. Fallback sur la position du curseur
        // si le shell ne sait pas (icône non encore enregistrée, dans
        // l'overflow caché, ou explorer.exe en cours de restart).
        NativeMethods.RECT? iconRect = GetIconRect?.Invoke();
        POINT anchor;
        NativeMethods.RECT exclude;
        int parentRectX, parentRectY, parentRectW, parentRectH;
        if (iconRect is { } icon)
        {
            anchor = new POINT { X = (icon.left + icon.right) / 2, Y = (icon.top + icon.bottom) / 2 };
            exclude = icon;
            parentRectX = icon.left;
            parentRectY = icon.top;
            parentRectW = icon.right - icon.left;
            parentRectH = icon.bottom - icon.top;
        }
        else
        {
            NativeMethods.GetCursorPos(out POINT cursor);
            anchor = cursor;
            exclude = new NativeMethods.RECT
            {
                left   = cursor.X - CursorExcludeHalfExtent,
                top    = cursor.Y - CursorExcludeHalfExtent,
                right  = cursor.X + CursorExcludeHalfExtent,
                bottom = cursor.Y + CursorExcludeHalfExtent,
            };
            // Rect parent dégénéré (0×0 au point d'ancrage) — convention
            // documentée sur DeckleWindowingSource.PopupAnchored pour les
            // popups dont l'app n'a pas de contrôle parent identifié.
            parentRectX = cursor.X;
            parentRectY = cursor.Y;
            parentRectW = 0;
            parentRectH = 0;
        }

        // DPI réel du moniteur sous le point d'ancrage. RasterizationScale du
        // XamlRoot refléterait le DPI du moniteur où la fenêtre porteuse est
        // cachée (typiquement primaire), pas où le tray vit — mismatch sur
        // setup multi-monitor ou écran primaire à 150 %.
        IntPtr monitor = TrayMenuNativeMethods.MonitorFromPoint(
            anchor, TrayMenuNativeMethods.MONITOR_DEFAULTTONEAREST);
        TrayMenuNativeMethods.GetDpiForMonitor(
            monitor, TrayMenuNativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
        double scale = dpiX / 96.0;

        var (width, height) = MeasureFlyout(scale);
        var size = new TrayMenuNativeMethods.SIZE { cx = width, cy = height };

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

        // Émission Windowing canonique : tronc commun WindowPositioned (état
        // effectif du HWND post-MoveAndResize + ShowWindow + SetForegroundWindow)
        // et spécialisé PopupAnchored (rect parent = icône tray ou rect
        // dégénéré au curseur).
        WindowingProbe.EmitWindowPositioned(_hwnd, "tray-popup", "CursorRelative");
        WindowingProbe.EmitPopupAnchored(
            _hwnd, "tray-popup",
            parentRectX, parentRectY, parentRectW, parentRectH);

        // FlyoutPlacementMode.Full : ouvre le menu à l'emplacement exact du
        // target (notre frame). Sans Full, le mode Auto par défaut place le
        // popup adjacent au target (typiquement au-dessus du frame), ce qui
        // double l'offset déjà calculé par CalculatePopupWindowPosition — le
        // menu apparaît alors décalé d'environ une hauteur de menu vers le
        // haut. La fenêtre porteuse étant déjà positionnée à la coordonnée
        // exacte voulue, Full est le mode qui colle.
        _flyout.ShowAt(_frame, new FlyoutShowOptions
        {
            ShowMode = FlyoutShowMode.Transient,
            Placement = FlyoutPlacementMode.Full,
        });
        DeckleShellTrayMenuSource.Log.FlyoutShownAt();
    }

    // ── Hide ──────────────────────────────────────────────────────────────────

    // Le paramètre `reason` qualifie l'origine du dismiss côté call site —
    // "deactivated" (perte d'activation), "flyout_closed" (Flyout.Closed),
    // "item_click:<libellé>" (sélection d'item). Tracé sur l'event Hidden pour
    // distinguer les chemins de fermeture dans le JSONL.
    private void Hide(string reason)
    {
        if (!_isVisible) return;
        _isVisible = false;
        DeckleShellTrayMenuSource.Log.Hidden(reason);
        _flyout?.Hide();
        if (_hwnd != IntPtr.Zero)
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
    }

    // ── Lifecycle handlers ────────────────────────────────────────────────────

    private void OnFrameLoaded(object sender, RoutedEventArgs e)
    {
        DeckleShellTrayMenuSource.Log.FrameLoaded(_primed);

        if (_primed || _flyout is null || _frame is null) return;
        _primed = true;

        // WS_POPUPWINDOW post-Loaded : efface la caption héritée
        // d'OverlappedPresenter même quand SetBorderAndTitleBar(false, false)
        // a été appelé. Sans ça, le HWND garde un WS_CAPTION résiduel visible
        // au DWM, ce qui interfère avec le rendu rounded corners.
        IntPtr styleBefore = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE);
        IntPtr newStyle = new((long)TrayMenuNativeMethods.WS_POPUPWINDOW);
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE, newStyle);
        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
                | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
        DeckleShellTrayMenuSource.Log.PrimeCycleStarted(
            styleBefore.ToInt64(), newStyle.ToInt64());

        // Prime measure : amorcer le visual tree pour que les MenuFlyoutItem
        // natifs aient leur ControlTemplate appliqué et leur DesiredSize
        // mesurable au premier vrai Show(). Un cycle ShowAt + Hide synchrone
        // est insuffisant — observation app.jsonl du 2026-05-25 : show_count=1
        // mesure desired_w/h=0 pour tous les items natifs. Cause : le Hide
        // synchrone immédiat coupe l'amorce avant que le layout pass de WinUI
        // ait tourné sur les items du MenuFlyoutPresenter. Fix : différer le
        // Hide via DispatcherQueue.TryEnqueue(Low) — le priority Low insère le
        // callback après que le layout pass et le render frame initial du popup
        // aient eu lieu. À ce moment-là chaque item a son DesiredSize correct,
        // le visual tree reste "réchauffé" pour la durée de vie du process.
        //
        // **Note sur la convergence presenter** : le MenuFlyoutPresenter natif
        // a une compression défectueuse qui kick in à partir du 2e ou 3e show —
        // il rend les MenuFlyoutItem natifs à MinHeight (32 DIP) au lieu de
        // leur DesiredSize naturelle (40 DIP), sans re-centrer le texte
        // verticalement, ce qui donne un rendu visuellement compressé. La
        // parade adoptée vit côté création des items (Height="40" forcée dans
        // BuildFlyout), pas dans le prime cycle — empêcher la compression à la
        // source est plus robuste que d'essayer de la pré-déclencher.
        var sw = Stopwatch.StartNew();
        _flyout.ShowAt(_frame, new FlyoutShowOptions { ShowMode = FlyoutShowMode.Transient });

        _frame.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _flyout?.Hide();
            sw.Stop();
            DeckleShellTrayMenuSource.Log.PrimeCycleCompleted(sw.Elapsed.TotalMilliseconds);
        });
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        DeckleShellTrayMenuSource.Log.WindowActivated(
            args.WindowActivationState.ToString(), _isVisible);

        if (args.WindowActivationState == WindowActivationState.Deactivated && _isVisible)
            Hide("deactivated");
    }

    private void OnFlyoutClosed(object? sender, object e)
    {
        DeckleShellTrayMenuSource.Log.FlyoutClosed(_isVisible);

        if (_isVisible) Hide("flyout_closed");
    }

    // ── Measure ───────────────────────────────────────────────────────────────
    //
    // Itère les items du flyout après amorce du visual tree (cycle ShowAt/Hide
    // dans OnFrameLoaded — compense microsoft-ui-xaml#7374), mesure chacun et
    // somme. Pas de Height ni Padding hardcodé : forcer Height=32 réduisait
    // seulement le ContentRoot des items, mais le MenuFlyoutPresenter
    // continuait à allouer la hauteur native par cellule — le hover ne
    // couvrait alors qu'une partie de la cellule et le texte n'était pas
    // centré dans l'espace alloué. On laisse le rendu natif WinUI 3 Win11
    // décider de tout, ce qui colle au comportement de la WinUI 3 Gallery
    // pour ce type de menu. Largeur naturelle, déterminée par le libellé le
    // plus long plus le ToggleSwitch éventuel — pas de MinWidth forcé, le
    // menu colle au contenu pour rester compact. Surplus FlyoutFrameMargin
    // × 2 couvre la card du MenuFlyout. Conversion en pixels physiques via
    // le scale du moniteur sous le point d'ancrage.
    //
    // L'event ItemMeasured trace la DesiredSize de chaque item — si on observe
    // desired_w/h=0 sur un ou plusieurs items, c'est le signal direct que
    // Measure tourne hors visual tree amorcé. L'event FlyoutMeasured trace
    // les agrégats avant et après conversion physique.

    private (int width, int height) MeasureFlyout(double scale)
    {
        if (_flyout is null) return (0, 0);

        double width = 0;
        double height = 0;
        int idx = 0;
        foreach (var item in _flyout.Items)
        {
            item.Measure(new Windows.Foundation.Size(10_000, 10_000));
            width = Math.Max(width, item.DesiredSize.Width);
            height += item.DesiredSize.Height;

            string itemText = item switch
            {
                MenuFlyoutItem mi => mi.Text,
                MenuFlyoutSeparator => "<separator>",
                _ => "<unknown>",
            };
            DeckleShellTrayMenuSource.Log.ItemMeasured(
                idx, itemText, item.GetType().Name,
                item.DesiredSize.Width, item.DesiredSize.Height);
            idx++;
        }

        double dipW = width + FlyoutFrameMargin * 2;
        double dipH = height + FlyoutFrameMargin * 2;
        int physW = (int)(dipW * scale);
        int physH = (int)(dipH * scale);

        DeckleShellTrayMenuSource.Log.FlyoutMeasured(dipW, dipH, physW, physH, scale);

        return (physW, physH);
    }

    // ── Visual tree helpers ───────────────────────────────────────────────────

    // Walk récursif du visual tree pour récupérer un descendant par son x:Name.
    // Utilisé pour résoudre le ToggleSwitch interne au template Ambient depuis
    // le code C# — un FrameworkElement nommé dans un ControlTemplate est isolé
    // dans le scope du template, FindName() sur le parent ne le trouve pas.
    // VisualTreeHelper traverse récursivement et reste indépendant du scope.
    // Retourne null si le template n'a pas encore été appliqué (item pas dans
    // le visual tree) ou si le nom n'existe pas.
    private static DependencyObject? FindDescendantByName(DependencyObject parent, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe && fe.Name == name)
                return child;
            var found = FindDescendantByName(child, name);
            if (found is not null) return found;
        }
        return null;
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

        DeckleShellTrayMenuSource.Log.Disposed();
    }
}
