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

    // Marge forfaitaire du chemin de fallback de MeasureFlyout (presenter non
    // capturé au prime cycle). 4 px par côté. Imprécise par nature — elle
    // sur-estimait le chrome réel du presenter (≈ 4-6 DIP), d'où le trou Mica
    // quand elle pilotait la mesure ; le chemin nominal lit désormais la
    // DesiredSize du presenter réel (_primedPresenterSize).
    private const double FlyoutFrameMargin = 4.0;

    private readonly IntPtr _ownerHwnd;

    private Window? _window;
    private Frame? _frame;
    private MenuFlyout? _flyout;
    private MenuFlyoutItem? _ambientItem;
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

    // Cache des DesiredSize capturées pendant le prime cycle, items attachés
    // au visual tree du popup interne. MeasureFlyout() lit ce cache au lieu
    // d'appeler item.Measure() détaché, qui retourne une valeur instable
    // (cf. JOURNAL.md du module — bascule 40 → 32 sur items natifs après un
    // nombre variable d'ouvertures, parce que le template natif tombe à sa
    // MinHeight quand mesuré détaché).
    private readonly System.Collections.Generic.Dictionary<MenuFlyoutItemBase, Windows.Foundation.Size> _primedSizes = new();

    // DesiredSize du MenuFlyoutPresenter réel, capturée au prime cycle. Inclut
    // le padding et la bordure propres du presenter — c'est la taille exacte
    // que le popup visible occupe. MeasureFlyout() la préfère à la somme des
    // items + marge forfaitaire : Full étirant le presenter jusqu'à la fenêtre
    // porteuse, dimensionner la fenêtre sur cette valeur annule l'étirement et
    // supprime le trou Mica (cf. CLAUDE.md, section Placement). null tant que le
    // prime cycle n'a pas tourné, ou si le presenter n'a pas pu être trouvé.
    private Windows.Foundation.Size? _primedPresenterSize;

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
        // Item Ambient construit via le helper réutilisable TraySwitchMenuItem
        // qui applique le Style ToggleSwitchMenuItemStyle (pillule custom
        // dessinée à la main, cf. Themes/TrayMenu.xaml) et encapsule la
        // bascule d'état visuel. L'état est synchronisé avant chaque ouverture
        // dans Show() via TraySwitchMenuItem.SetState. Pour ajouter un autre
        // item togglable : une seule ligne Create + une seule ligne SetState
        // dans Show().
        _ambientItem = TraySwitchMenuItem.Create(
            Loc.Get("TrayMenu_AmbientLight"),
            () =>
            {
                DeckleShellTrayMenuSource.Log.ItemClicked(_ambientItem!.Text);
                Hide("item_click:Ambient");
                OnToggleAmbient?.Invoke();
            });
        _flyout.Items.Add(_ambientItem);

        _flyout.Items.Add(new MenuFlyoutSeparator());
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Logs"),       () => OnShowLogs?.Invoke()));
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Settings"),   () => OnShowSettings?.Invoke()));
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Playground"), () => OnShowPlayground?.Invoke()));

        _flyout.Items.Add(new MenuFlyoutSeparator());
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Restart"), () => OnRestart?.Invoke()));
        _flyout.Items.Add(CreateItem(Loc.Get("TrayMenu_Quit"),    () => OnQuit?.Invoke()));

        ApplyNarrowPadding();

        _flyout.Closed += OnFlyoutClosed;
        _flyout.Opened += OnFlyoutOpened;

        DeckleShellTrayMenuSource.Log.FlyoutBuilt(_flyout.Items.Count);
    }

    // ── Force NarrowPadding à chaque ouverture ────────────────────────────────
    //
    // Le state NarrowPadding (densité compacte 32 DIP, cible Win11 mouse-driven)
    // est appliqué par le framework dès qu'un pointer mouse interagit avec le
    // menu, mais le state se reset à DefaultPadding (40 DIP) entre deux Hide/Show
    // du flyout. Conséquence visible : au premier clic après lancement, les
    // items sont rendus à 40 DIP alors que la fenêtre porteuse est dimensionnée
    // à 32 DIP/item via le cache _primedSizes — le contenu dépasse, le
    // MenuFlyoutPresenter active son ScrollViewer interne, l'utilisateur peut
    // scroller dans un menu qui ne devrait pas l'être. À partir du 2e clic, le
    // framework restaure NarrowPadding (interaction mouse persistée), tout
    // s'aligne.
    //
    // Fix : forcer NarrowPadding sur tous les items dans le handler Opened, au
    // moment où le framework les attache au visual tree du popup. C'est l'instant
    // où GoToState peut effectivement appliquer le state. Aligné avec le pattern
    // Win11 natif desktop : Sound, Defender, Date/Time, Network — tous rendent
    // leur tray menu en densité narrow.
    private void OnFlyoutOpened(object? sender, object e)
    {
        if (_flyout is null) return;
        foreach (var item in _flyout.Items)
        {
            if (item is MenuFlyoutItem mfi)
                VisualStateManager.GoToState(mfi, "NarrowPadding", useTransitions: false);
        }
    }

    private MenuFlyoutItem CreateItem(string text, Action action)
    {
        // MenuFlyoutItem natif pur — aucun Style ni Template override, aucune
        // Height forcée. Le framework gère hover, radius, inset, padding,
        // foreground, DPI scaling et hauteur de cellule intégralement à partir
        // de la DesiredSize naturelle. Le seul item retemplaté du tray menu
        // est l'Ambient Light, faute de slot natif pour greffer un switch à
        // droite (cf. ToggleSwitchMenuItemStyle dans Themes/TrayMenu.xaml).
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) =>
        {
            DeckleShellTrayMenuSource.Log.ItemClicked(text);
            Hide($"item_click:{text}");
            action();
        };
        return item;
    }

    // Neutralise le VisualStateGroup PaddingSizeStates en fixant le Padding de
    // chaque item à la valeur narrow. L'état initial DefaultPadding est un
    // VisualState vide : il laisse LayoutRoot.Padding à sa valeur de
    // TemplateBinding, donc à item.Padding. En fixant item.Padding à narrow, le
    // premier render est déjà compact, sans attendre que le framework bascule en
    // NarrowPadding (bascule qui n'arrivait qu'après le premier frame, d'où le
    // scroll au premier clic : items rendus à 40 DIP dans une fenêtre dimensionnée
    // pour 32). L'état NarrowPadding pose la même valeur — les deux états
    // deviennent équivalents, densité narrow en permanence.
    //
    // Densité narrow assumée comme cible unique : le menu tray s'ouvre au clic
    // droit souris (la branche touch/DefaultPadding du natif ne s'applique pas en
    // pratique sur une app desktop), cohérent avec la doctrine densité Win11 du
    // CLAUDE.md du module.
    private void ApplyNarrowPadding()
    {
        if (_flyout is null) return;
        if (!Application.Current.Resources.TryGetValue(
                "MenuFlyoutItemThemePaddingNarrow", out var narrowObj)
            || narrowObj is not Thickness narrowPadding)
        {
            // Resource non résolue depuis le scope app — on laisse le
            // GoToState(NarrowPadding) du prime cycle et du handler Opened comme
            // filet (le premier clic peut alors rester en DefaultPadding).
            return;
        }

        foreach (var item in _flyout.Items)
            if (item is MenuFlyoutItem mfi)
                mfi.Padding = narrowPadding;
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
            TraySwitchMenuItem.SetState(_ambientItem, ambientOn);
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
        // target (notre frame). Sans Full, le placement Top par défaut place
        // le popup au-dessus du frame, ce qui ajoute un offset vertical par-
        // dessus celui déjà calculé par CalculatePopupWindowPosition — le menu
        // saute d'environ une hauteur de menu vers le haut. La fenêtre porteuse
        // étant déjà positionnée à la coordonnée exacte voulue, Full neutralise
        // cet offset.
        //
        // Contrepartie (doc MS + repro 2026-05-31) : Full étire aussi le
        // presenter jusqu'à remplir la fenêtre porteuse. La taille du menu
        // visible est donc dictée par MeasureFlyout ; le FlyoutFrameMargin de
        // 8 DIP qu'on ajoute se voit en trou Mica en bas (presenter étiré qui
        // ne le consomme pas comme padding). Cf. CLAUDE.md / JOURNAL du module.
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
        var sw = Stopwatch.StartNew();
        _flyout.ShowAt(_frame, new FlyoutShowOptions { ShowMode = FlyoutShowMode.Transient });

        _frame.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            // Capture des DesiredSize items attachés au visual tree du popup,
            // après avoir forcé le state NarrowPadding sur chaque item. Le
            // framework bascule de toute façon en NarrowPadding dès qu'un
            // pointer mouse/pen/keyboard interagit avec le menu (cf. VisualState
            // PaddingSizeStates du DefaultMenuFlyoutItemStyle, generic.xaml du
            // WindowsAppSDK l. 24058) — on précipite ce bascule au prime cycle
            // pour que le cache reflète la taille finale (≈ 32 DIP/item) plutôt
            // que la taille initiale DefaultPadding (≈ 40). Sans cette force,
            // la fenêtre porteuse était dimensionnée à la taille initiale et le
            // popup interne (qui suit le state NarrowPadding) se rendait plus
            // compact, créant un trou Mica visible en bas du popup.
            if (_flyout is not null)
            {
                foreach (var item in _flyout.Items)
                {
                    if (item is MenuFlyoutItem mfi)
                        VisualStateManager.GoToState(mfi, "NarrowPadding", useTransitions: false);
                }
                // Force layout pass pour que les nouvelles valeurs de Padding
                // appliquées par le Storyboard du VisualState soient effectives
                // dans la DesiredSize qu'on s'apprête à capturer.
                _frame!.UpdateLayout();

                _primedSizes.Clear();
                foreach (var item in _flyout.Items)
                    _primedSizes[item] = item.DesiredSize;

                // Capture la taille du presenter réel (remonte depuis le premier
                // item, attaché à ce stade). Sa DesiredSize inclut son padding +
                // sa bordure, donc reflète exactement la card visible — au
                // contraire de la somme des items, qui les ignore et qu'on
                // compensait par une marge forfaitaire imprécise.
                _primedPresenterSize = null;
                if (_flyout.Items.Count > 0)
                {
                    var presenter = FindAncestorPresenter(_flyout.Items[0]);
                    if (presenter is not null)
                    {
                        presenter.UpdateLayout();
                        _primedPresenterSize = presenter.DesiredSize;
                    }
                }
            }

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

    // Remonte le visual tree depuis un descendant jusqu'au MenuFlyoutPresenter
    // qui héberge les items du popup. Retourne null si l'arbre n'est pas encore
    // monté (presenter absent du tree au moment de l'appel).
    private static MenuFlyoutPresenter? FindAncestorPresenter(DependencyObject start)
    {
        DependencyObject? current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is MenuFlyoutPresenter presenter)
                return presenter;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // ── Measure ───────────────────────────────────────────────────────────────
    //
    // Dimensionne la fenêtre porteuse sur la DesiredSize du MenuFlyoutPresenter
    // réel, capturée au prime cycle (_primedPresenterSize). Cette taille inclut
    // le padding et la bordure propres du presenter, donc correspond exactement
    // à la card que le popup peint. Comme Full étire le presenter jusqu'à la
    // fenêtre porteuse, dimensionner la fenêtre sur cette valeur rend
    // l'étirement neutre : ni trou Mica (sur-mesure), ni scroll (sous-mesure).
    //
    // La boucle ci-dessous reste pour le diagnostic par item (events
    // ItemAttachmentChecked / ItemMeasured dans le JSONL) et pour alimenter le
    // fallback. Les DesiredSize y sont lues depuis le cache _primedSizes (items
    // attachés au prime cycle) plutôt que par un item.Measure() détaché, qui
    // retourne une valeur instable (cf. JOURNAL.md du module).
    //
    // Fallback (presenter non capturé au prime, ou prime pas encore tourné) :
    // somme des hauteurs d'items + FlyoutFrameMargin × 2. Chemin historique,
    // imprécis (la marge forfaitaire de 8 DIP sur-estimait le chrome réel du
    // presenter ≈ 4-6 DIP, d'où le trou) — conservé comme garde-fou
    // anti-popup-de-taille-zéro.

    private (int width, int height) MeasureFlyout(double scale)
    {
        if (_flyout is null) return (0, 0);

        double width = 0;
        double height = 0;
        int idx = 0;
        foreach (var item in _flyout.Items)
        {
            string itemText = item switch
            {
                MenuFlyoutItem mi => mi.Text,
                MenuFlyoutSeparator => "<separator>",
                _ => "<unknown>",
            };

            Windows.Foundation.Size desired;
            if (_primedSizes.TryGetValue(item, out var cached))
            {
                desired = cached;
            }
            else
            {
                // Fallback sécurité — le prime cycle n'a pas encore peuplé le
                // cache. Mesure détachée acceptée faute de mieux, le popup
                // affichera au pire la hauteur compressée native.
                item.Measure(new Windows.Foundation.Size(10_000, 10_000));
                desired = item.DesiredSize;
            }
            width = Math.Max(width, desired.Width);
            height += desired.Height;

            DeckleShellTrayMenuSource.Log.ItemMeasured(
                idx, itemText, item.GetType().Name,
                desired.Width, desired.Height);
            idx++;
        }

        double dipW;
        double dipH;
        if (_primedPresenterSize is { } presenterSize
            && presenterSize.Width > 0 && presenterSize.Height > 0)
        {
            // Taille exacte du presenter réel — Full n'a plus rien à étirer.
            dipW = presenterSize.Width;
            dipH = presenterSize.Height;
        }
        else
        {
            // Fallback imprécis : somme des items + marge forfaitaire.
            dipW = width + FlyoutFrameMargin * 2;
            dipH = height + FlyoutFrameMargin * 2;
        }

        // Ceiling plutôt que troncature : on préfère un éventuel sub-pixel de
        // trou (invisible) à une sous-mesure d'un pixel qui réactiverait le
        // scroll du presenter.
        int physW = (int)Math.Ceiling(dipW * scale);
        int physH = (int)Math.Ceiling(dipH * scale);

        DeckleShellTrayMenuSource.Log.FlyoutMeasured(dipW, dipH, physW, physH, scale);

        return (physW, physH);
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
            {
                _flyout.Closed -= OnFlyoutClosed;
                _flyout.Opened -= OnFlyoutOpened;
            }
            _window.Close();
        }

        DeckleShellTrayMenuSource.Log.Disposed();
    }
}
