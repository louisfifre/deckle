using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Deckle.Browser.Views;

// ─── Browser window: the media card ──────────────────────────────────────────
//
// Shown in the panel, above the foot, while any item's page plays sound
// (CoreWebView2.IsDocumentPlayingAudio). It shows the title, the author or
// artist, the position and duration with a progress bar, and previous,
// play/pause, next, mute, picture-in-picture and close; a click on the title
// selects the item that plays.
//
// Where the card reads from: the page itself, by a script run once a second
// (ExecuteScriptAsync) while the card shows, over navigator.mediaSession.metadata
// and the first playing video or audio element. The Windows media sessions
// (GlobalSystemMediaTransportControlsSessionManager) were tried first and are not
// used: see the note at SessionProbe in the self-check's walk for what was seen.
// Play/pause, restart (previous), picture-in-picture and the site's own next
// button are scripts too; mute is the engine's (CoreWebView2.IsMuted).
//
// The title and the artist are what the page says: never logged.
public sealed partial class BrowserWindow
{
    // The element that plays: the first not paused, else the first.
    private const string PickMedia =
        "var els=[].slice.call(document.querySelectorAll('video,audio'));"
        + "var e=els.find(function(x){return !x.paused&&!x.ended})||els[0];";

    private const string ReadMediaScript =
        "(function(){" + PickMedia
        + "var m=navigator.mediaSession&&navigator.mediaSession.metadata;"
        + "return JSON.stringify({title:(m&&m.title)?m.title:document.title,artist:(m&&m.artist)?m.artist:'',"
        + "position:e?e.currentTime:0,duration:(e&&isFinite(e.duration))?e.duration:0,paused:e?e.paused:true,"
        + "hasVideo:!!document.querySelector('video'),hasNext:!!document.querySelector('.ytp-next-button')});})()";

    private const string TogglePlayScript =
        "(function(){" + PickMedia + "if(!e)return;if(e.paused)e.play();else e.pause();})()";

    private const string PauseAllScript =
        "(function(){[].slice.call(document.querySelectorAll('video,audio')).forEach(function(x){x.pause();});})()";

    private const string RestartScript =
        "(function(){" + PickMedia + "if(e)e.currentTime=0;})()";

    private const string NextScript =
        "(function(){var b=document.querySelector('.ytp-next-button');if(b)b.click();})()";

    private const string PictureInPictureScript =
        "(function(){var v=document.querySelector('video');if(!v)return;"
        + "if(document.pictureInPictureElement)document.exitPictureInPicture();"
        + "else v.requestPictureInPicture().catch(function(){});})()";

    private readonly HashSet<Guid> _mediaDismissed = [];
    private DispatcherQueueTimer? _mediaTimer;
    private PanelItem? _mediaItem;
    private bool _mediaPausedByCard;
    private bool _mediaReading;
    private MediaReading? _mediaLast;

    private void WireMedia()
    {
        _mediaTimer = DispatcherQueue.CreateTimer();
        _mediaTimer.Interval = TimeSpan.FromSeconds(1);
        _mediaTimer.IsRepeating = true;
        _mediaTimer.Tick += (_, _) => _ = ReadMediaAsync();
    }

    private void StopMediaTimer() => _mediaTimer?.Stop();

    // A page began or stopped playing sound. The card follows the one that
    // began last; a page paused from the card keeps it until it is closed.
    private void OnPlayingChanged(PanelItem panel)
    {
        if (panel.IsPlaying)
        {
            _mediaDismissed.Remove(panel.Item.Id);
            _mediaItem = panel;
            _mediaPausedByCard = false;
        }
        else if (_mediaItem == panel && !_mediaPausedByCard)
        {
            _mediaItem = AllItems.FirstOrDefault(p => p.IsPlaying);
        }
        UpdateMediaCard();
    }

    // An item closed takes its card with it.
    private void ForgetMedia(PanelItem panel)
    {
        _mediaDismissed.Remove(panel.Item.Id);
        if (_mediaItem != panel) return;
        _mediaItem = AllItems.FirstOrDefault(p => p != panel && p.IsPlaying);
        _mediaPausedByCard = false;
        UpdateMediaCard();
    }

    // The card shows with the panel open (the rail has no room for it).
    private void UpdateMediaCard()
    {
        if (_mediaTimer is null) return;
        bool show = _mediaItem is { } item && !_mediaDismissed.Contains(item.Item.Id) && _pane.IsOpen;
        MediaCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            _mediaTimer.Stop();
            return;
        }
        MediaTitleText.Text = _mediaItem!.Tooltip;
        if (!_mediaTimer.IsRunning) _mediaTimer.Start();
        _ = ReadMediaAsync();
    }

    private async Task ReadMediaAsync()
    {
        if (_mediaReading || _mediaItem?.View?.CoreWebView2 is not { } core) return;
        _mediaReading = true;
        try
        {
            BrowserActivity.MediaRead();
            MediaReading? reading = MediaReading.Parse(await core.ExecuteScriptAsync(ReadMediaScript));
            if (reading is null || _mediaItem?.View?.CoreWebView2 != core) return;
            ShowMedia(reading, core.IsMuted);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ObjectDisposedException)
        {
            // The page is gone or reloading: the next read tries again.
        }
        finally
        {
            _mediaReading = false;
        }
    }

    private void ShowMedia(MediaReading reading, bool muted)
    {
        _mediaLast = reading;
        MediaTitleText.Text = reading.Title.Length > 0 ? reading.Title : _mediaItem?.Tooltip ?? "";
        MediaArtistText.Text = reading.Artist;
        MediaArtistText.Visibility = reading.Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        MediaProgress.Value = reading.Progress;
        MediaPositionText.Text = MediaReading.FormatTime(reading.Position);
        MediaDurationText.Text = reading.Duration > 0 ? MediaReading.FormatTime(reading.Duration) : "";
        MediaPlayPauseIcon.Glyph = reading.Paused ? "" : "";
        string playPause = BrowserText.Get(reading.Paused ? "Media_Play" : "Media_Pause");
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(MediaPlayPauseButton, playPause);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MediaPlayPauseButton, playPause);
        MediaMuteIcon.Glyph = muted ? "" : "";
        string mute = BrowserText.Get(muted ? "Media_Unmute" : "Media_Mute");
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(MediaMuteButton, mute);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MediaMuteButton, mute);
        MediaNextButton.IsEnabled = reading.HasNext;
        MediaPipButton.IsEnabled = reading.HasVideo;
    }

    // Runs a script on the page that plays, then reads the card again.
    private async Task RunMediaScriptAsync(string script)
    {
        if (_mediaItem?.View?.CoreWebView2 is not { } core) return;
        try { await core.ExecuteScriptAsync(script); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ObjectDisposedException)
        {
            return;
        }
        await ReadMediaAsync();
    }

    private void OnMediaTitleClick(object sender, RoutedEventArgs e)
    {
        using var gesture = new GestureScope(BrowserGesture.MediaSelect);
        if (_mediaItem is { } item) SelectItem(item);
        else gesture.Outcome = "refused";
    }

    private void OnMediaPlayPauseClick(object sender, RoutedEventArgs e)
    {
        using var gesture = new GestureScope(BrowserGesture.MediaPlayPause);
        _mediaPausedByCard = _mediaLast is { Paused: false };
        _ = RunMediaScriptAsync(TogglePlayScript);
    }

    // Previous restarts the media: a page has no "previous" of its own to call.
    private void OnMediaPreviousClick(object sender, RoutedEventArgs e)
    {
        using var gesture = new GestureScope(BrowserGesture.MediaPrevious);
        _ = RunMediaScriptAsync(RestartScript);
    }

    // Next presses the site's own next button when it has one (the button is
    // disabled when it has none).
    private void OnMediaNextClick(object sender, RoutedEventArgs e)
    {
        using var gesture = new GestureScope(BrowserGesture.MediaNext);
        _ = RunMediaScriptAsync(NextScript);
    }

    private void OnMediaMuteClick(object sender, RoutedEventArgs e)
    {
        using var gesture = new GestureScope(BrowserGesture.MediaMute);
        if (_mediaItem?.View?.CoreWebView2 is not { } core)
        {
            gesture.Outcome = "refused";
            return;
        }
        core.IsMuted = !core.IsMuted;
        if (_mediaLast is { } last) ShowMedia(last, core.IsMuted);
    }

    // Picture-in-picture is the page's own (a video only); the engine may
    // refuse a request that no key or click of the page made.
    private void OnMediaPipClick(object sender, RoutedEventArgs e)
    {
        using var gesture = new GestureScope(BrowserGesture.MediaPictureInPicture);
        _ = RunMediaScriptAsync(PictureInPictureScript);
    }

    // Close pauses what plays and hides the card, until a page starts again.
    private void OnMediaCloseClick(object sender, RoutedEventArgs e)
    {
        using var gesture = new GestureScope(BrowserGesture.MediaClose);
        if (_mediaItem is not { } item) return;
        _mediaDismissed.Add(item.Item.Id);
        _mediaPausedByCard = false;
        UpdateMediaCard();
        if (item.View?.CoreWebView2 is { } core) _ = core.ExecuteScriptAsync(PauseAllScript);
    }
}
