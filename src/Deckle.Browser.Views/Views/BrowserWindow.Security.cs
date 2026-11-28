using System.Collections.ObjectModel;
using System.Diagnostics;
using Deckle.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Deckle.Browser.Views;

// ─── Browser window: the shell security layer ────────────────────────────────
//
// The decisions are the domain's (HttpsFirst, SitePermissions, DownloadSafety,
// WebEngine, RegistrableDomain); this file only wires WebView2's events to
// them and puts the questions to the person through WinUI primitives (an
// InfoBar, a ContentDialog, a Flyout), never a web page.
//
// Nothing a person visits is logged: no address, no host, no title.
public sealed partial class BrowserWindow
{
    private readonly HttpsFirst _https = new();
    private readonly SitePermissions _permissions;
    private readonly List<DownloadEntry> _downloads = [];
    private readonly List<(string Host, string Folder)> _hostMappings = [];

    // One question at a time: a ContentDialog cannot be shown twice at once.
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private ContentDialog? _permissionDialog;
    private PermissionDecision? _permissionOverride;
    private bool _engineChecked;
    private bool _environmentLost;
    private CoreWebView2Environment? _watchedEnvironment;

    private static string PermissionsPath => Path.Combine(AppPaths.GetModuleDirectory("browser"), "permissions.json");

    private sealed class DownloadEntry(string fileName, string filePath)
    {
        public string FileName { get; } = fileName;
        public string FilePath { get; } = filePath;
        public CoreWebView2DownloadState State { get; set; } = CoreWebView2DownloadState.InProgress;
    }

    // ── Wiring ───────────────────────────────────────────────────────────────

    // Everything a web view of this window gets: the settings, the virtual
    // hosts a self-check asked for, and the events the security layer answers.
    private void SecurityWire(PanelItem panel, CoreWebView2 core)
    {
        StrictTrackingApplied = WebViewSettings.Apply(core);
        foreach ((string host, string folder) in _hostMappings)
            core.SetVirtualHostNameToFolderMapping(host, folder, CoreWebView2HostResourceAccessKind.Allow);

        core.NavigationStarting += (_, args) => OnNavigationStarting(panel, core, args);
        core.PermissionRequested += (_, args) => OnPermissionRequested(args);
        core.DownloadStarting += (_, args) => OnDownloadStarting(args);
        core.LaunchingExternalUriScheme += (_, args) => OnLaunchingExternalUriScheme(args);
        core.ProcessFailed += (_, args) => OnProcessFailed(panel, args);
    }

    /// <summary>Whether the last web view's strict tracking prevention was in
    /// force when it was made.</summary>
    public bool StrictTrackingApplied { get; private set; }

    // ── Private window ───────────────────────────────────────────────────────

    private void OnPrivateWindowAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        new BrowserWindow(true).ShowAndActivate();
    }

    /// <summary>Whether this is a private window.</summary>
    public bool IsPrivate => _isPrivate;

    /// <summary>Whether the shown page's web view runs in the engine's in-private
    /// mode (null while it has none).</summary>
    public bool? WebViewIsPrivate => ShownCore?.Profile.IsInPrivateModeEnabled;

    // ── HTTPS first ──────────────────────────────────────────────────────────

    private void OnNavigationStarting(PanelItem panel, CoreWebView2 core, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? uri)) return;

        if (_https.Upgrade(uri) is { } secure)
        {
            args.Cancel = true;
            panel.UpgradedFrom = uri;
            string target = secure.AbsoluteUri;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (panel.View?.CoreWebView2 is { } live) live.Navigate(target);
            });
            return;
        }

        // A navigation to anywhere but the upgraded address ends what the notice
        // was about.
        bool sameSite = panel.UpgradedFrom is { } from
            && uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.IdnHost, from.IdnHost, StringComparison.OrdinalIgnoreCase);
        if (!sameSite)
        {
            panel.UpgradedFrom = null;
            if (panel.Notice != PageNotice.None)
            {
                panel.Notice = PageNotice.None;
                if (_shown == panel) UpdateNotice();
            }
        }
    }

    private static bool IsConnectionFailure(CoreWebView2WebErrorStatus status) => status is
        CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect
        or CoreWebView2WebErrorStatus.CertificateExpired
        or CoreWebView2WebErrorStatus.ClientCertificateContainsErrors
        or CoreWebView2WebErrorStatus.CertificateRevoked
        or CoreWebView2WebErrorStatus.CertificateIsInvalid
        or CoreWebView2WebErrorStatus.ServerUnreachable
        or CoreWebView2WebErrorStatus.Timeout
        or CoreWebView2WebErrorStatus.ConnectionAborted
        or CoreWebView2WebErrorStatus.ConnectionReset
        or CoreWebView2WebErrorStatus.Disconnected
        or CoreWebView2WebErrorStatus.CannotConnect
        or CoreWebView2WebErrorStatus.HostNameNotResolved;

    private void OnNavigationCompleted(PanelItem panel, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.IsSuccess)
        {
            panel.UpgradedFrom = null;
            if (panel.Notice != PageNotice.None)
            {
                panel.Notice = PageNotice.None;
                if (_shown == panel) UpdateNotice();
            }
            return;
        }

        if (panel.UpgradedFrom is not null && IsConnectionFailure(args.WebErrorStatus))
        {
            panel.Notice = PageNotice.NoSecureConnection;
            if (_shown == panel) UpdateNotice();
        }
    }

    // The notice above the shown page, if it has one.
    private void UpdateNotice()
    {
        switch (_shown?.Notice ?? PageNotice.None)
        {
            case PageNotice.NoSecureConnection:
                NoticeBar.Message = BrowserText.Get("Notice_NoHttps");
                PageNoticeButton.Content = BrowserText.Get("Notice_ContinueAnyway");
                NoticeBar.IsOpen = true;
                break;
            case PageNotice.Crashed:
                NoticeBar.Message = BrowserText.Get("Notice_Crashed");
                PageNoticeButton.Content = BrowserText.Get("Notice_Reload");
                NoticeBar.IsOpen = true;
                break;
            default:
                NoticeBar.IsOpen = false;
                break;
        }
    }

    private void OnPageNoticeAction(object sender, RoutedEventArgs e)
    {
        if (_shown is not { } panel) return;
        switch (panel.Notice)
        {
            case PageNotice.NoSecureConnection when panel.UpgradedFrom is { } from:
                _https.AllowHttp(from.IdnHost);
                panel.Notice = PageNotice.None;
                UpdateNotice();
                panel.UpgradedFrom = null;
                ShownCore?.Navigate(from.AbsoluteUri);
                break;
            case PageNotice.Crashed:
                ReloadAfterCrash(panel);
                break;
        }
    }

    // ── A page that crashes ──────────────────────────────────────────────────

    private void OnProcessFailed(PanelItem panel, CoreWebView2ProcessFailedEventArgs args)
    {
        DeckleBrowserViewsSource.Log.ProcessFailed(args.ProcessFailedKind.ToString());
        switch (args.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                panel.Notice = PageNotice.Crashed;
                break;
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                // Every web view of the generation died with it.
                _environmentLost = true;
                foreach (PanelItem each in AllItems.Where(p => p.View is not null && p.Generation == _environmentGeneration))
                    each.Notice = PageNotice.Crashed;
                break;
            default:
                return;
        }
        if (_shown is not null) UpdateNotice();
    }

    // A render crash reloads the page in place. A browser process failure makes
    // the environment again and every web view with it: the others start when
    // they are next shown.
    private void ReloadAfterCrash(PanelItem panel)
    {
        panel.Notice = PageNotice.None;
        if (!_environmentLost)
        {
            UpdateNotice();
            panel.View?.CoreWebView2?.Reload();
            return;
        }

        _environmentLost = false;
        _environmentTask = null;
        _environmentGeneration++;
        DetachEnvironment();
        _environment = null;
        foreach (PanelItem each in AllItems.Where(p => p.View is not null && p.Generation < _environmentGeneration).ToList())
        {
            each.Notice = PageNotice.None;
            each.Started = false;
            DisposeView(each);
        }
        _engineChecked = false;
        ApplySelection();
    }

    // ── Web engine ───────────────────────────────────────────────────────────

    // The first time the window has an environment: its version is a machine
    // fact and is logged; a version below the minimum is said at the top, and a
    // newer one installed later is too.
    private void OnEnvironmentReady(CoreWebView2Environment environment)
    {
        if (_engineChecked) return;
        _engineChecked = true;

        string version = environment.BrowserVersionString;
        DeckleBrowserViewsSource.Log.EngineVersion(version);
        EngineVersion = version;
        if (WebEngine.IsOutOfDate(version)) ShowEngineBar("Engine_OutOfDate");

        DetachEnvironment();
        _watchedEnvironment = environment;
        environment.NewBrowserVersionAvailable += OnNewBrowserVersionAvailable;
    }

    private void DetachEnvironment()
    {
        if (_watchedEnvironment is null) return;
        _watchedEnvironment.NewBrowserVersionAvailable -= OnNewBrowserVersionAvailable;
        _watchedEnvironment = null;
    }

    private void OnNewBrowserVersionAvailable(CoreWebView2Environment sender, object args) =>
        DispatcherQueue.TryEnqueue(() => ShowEngineBar("Engine_Newer"));

    // A closed bar gives its margin back to the page.
    private void OnEngineBarClosed(InfoBar sender, InfoBarClosedEventArgs args) => sender.Visibility = Visibility.Collapsed;

    private void ShowEngineBar(string key)
    {
        EngineBar.Message = BrowserText.Get(key);
        EngineBar.Visibility = Visibility.Visible;
        EngineBar.IsOpen = true;
    }

    /// <summary>The web engine's version string, once the window has started a
    /// web view. A machine fact.</summary>
    public string EngineVersion { get; private set; } = "";

    // ── Permissions ──────────────────────────────────────────────────────────

    private static PermissionKind KindOf(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Camera => PermissionKind.Camera,
        CoreWebView2PermissionKind.Microphone => PermissionKind.Microphone,
        CoreWebView2PermissionKind.Geolocation => PermissionKind.Geolocation,
        CoreWebView2PermissionKind.Notifications => PermissionKind.Notifications,
        CoreWebView2PermissionKind.ClipboardRead => PermissionKind.ClipboardRead,
        _ => PermissionKind.Other,
    };

    private static string OriginOf(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

    private void OnPermissionRequested(CoreWebView2PermissionRequestedEventArgs args)
    {
        // Never kept in the engine's profile: the answers are ours, in
        // permissions.json.
        args.SavesInProfile = false;
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? uri))
        {
            args.State = CoreWebView2PermissionState.Deny;
            return;
        }

        PermissionKind kind = KindOf(args.PermissionKind);
        string origin = OriginOf(uri);
        if (_permissions.Get(origin, kind) is { } stored)
        {
            args.State = StateOf(stored);
            return;
        }

        Windows.Foundation.Deferral deferral = args.GetDeferral();
        _ = AnswerPermissionAsync(args, origin, kind, uri.IdnHost, deferral);
    }

    private static CoreWebView2PermissionState StateOf(PermissionDecision decision) =>
        decision == PermissionDecision.Allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;

    private async Task AnswerPermissionAsync(
        CoreWebView2PermissionRequestedEventArgs args, string origin, PermissionKind kind, string host, Windows.Foundation.Deferral deferral)
    {
        try
        {
            args.State = StateOf(await AskPermissionAsync(origin, kind, host));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            args.State = CoreWebView2PermissionState.Deny;
        }
        finally
        {
            deferral.Complete();
        }
    }

    // The question, in a ContentDialog naming the site's registrable domain and
    // the permission in words. Allow and Block are stored; a dialog dismissed
    // any other way blocks this once and is not stored, so the site asks again.
    private async Task<PermissionDecision> AskPermissionAsync(string origin, PermissionKind kind, string host)
    {
        await _dialogGate.WaitAsync();
        try
        {
            // An earlier question may have answered this one.
            if (_permissions.Get(origin, kind) is { } already) return already;

            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = BrowserText.Get("Permission_Title"),
                Content = string.Format(BrowserText.Get("Permission_Question"),
                    RegistrableDomain.Of(host), BrowserText.Get("Permission_Do_" + kind)),
                PrimaryButtonText = BrowserText.Get("Permission_Allow"),
                SecondaryButtonText = BrowserText.Get("Permission_Block"),
                DefaultButton = ContentDialogButton.Secondary,
            };
            _permissionOverride = null;
            _permissionDialog = dialog;
            ContentDialogResult result = await dialog.ShowAsync();
            _permissionDialog = null;

            PermissionDecision? answer = _permissionOverride ?? result switch
            {
                ContentDialogResult.Primary => PermissionDecision.Allow,
                ContentDialogResult.Secondary => PermissionDecision.Block,
                _ => null,
            };
            _permissionOverride = null;
            if (answer is not { } decision) return PermissionDecision.Block;

            _permissions.Set(origin, kind, decision);
            SavePermissions();
            return decision;
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    private void SavePermissions()
    {
        if (_isPrivate) return;
        try { _permissions.Save(PermissionsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleBrowserViewsSource.Log.SessionSaveFailed(ex.GetType().Name);
        }
    }

    // ── Site settings ────────────────────────────────────────────────────────

    private void OnSiteSettingsOpening(object sender, object e) => FillSiteSettings();

    private void FillSiteSettings()
    {
        SiteSettingsPanel.Children.Clear();
        IReadOnlyList<StoredPermission> stored = _current is { } uri && SiteName.Display(uri).Length > 0
            ? _permissions.ForOrigin(OriginOf(uri))
            : [];

        SiteSettingsPanel.Children.Add(new TextBlock
        {
            Text = SiteName.Display(_current),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        if (stored.Count == 0)
        {
            SiteSettingsPanel.Children.Add(new TextBlock
            {
                Text = BrowserText.Get("SiteSettings_Empty"),
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (StoredPermission permission in stored)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = $"{BrowserText.Get("Permission_Name_" + permission.Kind)}: "
                    + BrowserText.Get(permission.Decision == PermissionDecision.Allow ? "Permission_Allowed" : "Permission_Blocked"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var remove = new Button { Content = BrowserText.Get("Permission_Remove"), Tag = permission };
            remove.Click += OnRemovePermissionClick;
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            SiteSettingsPanel.Children.Add(row);
        }
    }

    private void OnRemovePermissionClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not StoredPermission permission) return;
        _permissions.Remove(permission.Origin, permission.Kind);
        SavePermissions();
        FillSiteSettings();
    }

    // ── Downloads ────────────────────────────────────────────────────────────

    private void OnDownloadStarting(CoreWebView2DownloadStartingEventArgs args)
    {
        // No default dialog: where to save is asked here.
        args.Handled = true;
        Windows.Foundation.Deferral deferral = args.GetDeferral();
        _ = DecideDownloadAsync(args, deferral);
    }

    private async Task DecideDownloadAsync(CoreWebView2DownloadStartingEventArgs args, Windows.Foundation.Deferral deferral)
    {
        try
        {
            string suggested = Path.GetFileName(args.ResultFilePath);

            if (DownloadSafety.IsExecutable(suggested) && !await ConfirmAsync(
                    BrowserText.Get("Download_ExecutableTitle"), BrowserText.Get("Download_ExecutableBody"),
                    BrowserText.Get("Download_Confirm"), BrowserText.Get("Download_Cancel")))
            {
                args.Cancel = true;
                return;
            }

            string extension = Path.GetExtension(suggested);
            var picker = new FileSavePicker
            {
                SuggestedFileName = Path.GetFileNameWithoutExtension(suggested),
                SuggestedStartLocation = PickerLocationId.Downloads,
            };
            if (extension.Length > 0) picker.FileTypeChoices.Add(extension.TrimStart('.').ToUpperInvariant(), [extension]);
            else picker.FileTypeChoices.Add(BrowserText.Get("Download_AllFiles"), ["."]);
            InitializeWithWindow.Initialize(picker, _hwnd);

            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                args.Cancel = true;
                return;
            }

            args.ResultFilePath = file.Path;
            var entry = new DownloadEntry(file.Name, file.Path);
            _downloads.Insert(0, entry);
            CoreWebView2DownloadOperation operation = args.DownloadOperation;
            operation.StateChanged += (_, _) => entry.State = operation.State;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException
                                       or UnauthorizedAccessException)
        {
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnDownloadsOpening(object sender, object e)
    {
        DownloadsPanel.Children.Clear();
        if (_downloads.Count == 0)
        {
            DownloadsPanel.Children.Add(new TextBlock
            {
                Text = BrowserText.Get("Downloads_Empty"),
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            return;
        }

        foreach (DownloadEntry entry in _downloads)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = entry.FileName, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock
            {
                Text = BrowserText.Get(entry.State switch
                {
                    CoreWebView2DownloadState.Completed => "Download_Completed",
                    CoreWebView2DownloadState.Interrupted => "Download_Failed",
                    _ => "Download_InProgress",
                }),
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            });
            row.Children.Add(text);
            var open = new Button { Content = BrowserText.Get("Download_OpenFolder"), Tag = entry };
            open.Click += OnOpenDownloadFolderClick;
            Grid.SetColumn(open, 1);
            row.Children.Add(open);
            DownloadsPanel.Children.Add(row);
        }
    }

    // Shows the file in Explorer; a file is never opened by the browser.
    private void OnOpenDownloadFolderClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not DownloadEntry entry) return;
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add($"/select,{entry.FilePath}");
        try { Process.Start(start)?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    // ── Other apps ───────────────────────────────────────────────────────────

    private void OnLaunchingExternalUriScheme(CoreWebView2LaunchingExternalUriSchemeEventArgs args)
    {
        // Nothing starts on its own: the page's request is cancelled and the
        // person is asked, every time.
        args.Cancel = true;
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? uri)) _ = AskExternalAsync(uri);
    }

    private async Task AskExternalAsync(Uri uri)
    {
        try
        {
            string name = uri.Scheme;
            IReadOnlyList<Windows.ApplicationModel.AppInfo> handlers = await Launcher.FindUriSchemeHandlersAsync(uri.Scheme);
            if (handlers.Count > 0 && handlers[0].DisplayInfo.DisplayName is { Length: > 0 } display) name = display;

            if (await ConfirmAsync(null, string.Format(BrowserText.Get("External_Question"), name),
                    BrowserText.Get("External_Open"), BrowserText.Get("External_Cancel")))
                await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Nothing opened; nothing to say.
        }
    }

    // A yes or no in a ContentDialog, one at a time.
    private async Task<bool> ConfirmAsync(string? title, string body, string yes, string no)
    {
        await _dialogGate.WaitAsync();
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = title,
                Content = body,
                PrimaryButtonText = yes,
                CloseButtonText = no,
                DefaultButton = ContentDialogButton.Close,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    // ── What the self-check uses ─────────────────────────────────────────────

    /// <summary>Maps a host name to a folder for every web view of the window
    /// (made already or later), so local pages load over https.</summary>
    public void MapVirtualHost(string host, string folder)
    {
        _hostMappings.Add((host, folder));
        foreach (PanelItem panel in AllItems)
            panel.View?.CoreWebView2?.SetVirtualHostNameToFolderMapping(host, folder, CoreWebView2HostResourceAccessKind.Allow);
    }

    /// <summary>Waits until a permission question is on screen. False on timeout.</summary>
    public async Task<bool> WaitForPermissionQuestionAsync(TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (_permissionDialog is null || !_permissionDialog.IsLoaded)
        {
            if (clock.Elapsed > timeout) return false;
            await Task.Delay(50);
        }
        return true;
    }

    /// <summary>Answers the question on screen, as the buttons would.</summary>
    public void AnswerPermission(PermissionDecision decision)
    {
        _permissionOverride = decision;
        _permissionDialog?.Hide();
    }

    /// <summary>The stored answer for an origin and a kind, if any.</summary>
    public PermissionDecision? StoredPermissionOf(string origin, PermissionKind kind) => _permissions.Get(origin, kind);

    /// <summary>Opens the downloads flyout, as the foot's button does.</summary>
    public void ShowDownloads() => DownloadsFlyout.ShowAt(DownloadsButton);

    /// <summary>Closes the downloads flyout.</summary>
    public void HideDownloads() => DownloadsFlyout.Hide();

    /// <summary>How many downloads this run holds.</summary>
    public int DownloadCount => _downloads.Count;
}
