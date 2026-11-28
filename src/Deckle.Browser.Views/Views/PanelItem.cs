using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Browser.Views;

// What the panel shows of one BrowserItem, and what the window keeps beside
// it: the item's web view, made when the item is first selected (never ahead
// of that), and the state the panel binds to.
public sealed class PanelItem(BrowserItem item) : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private bool _isSelected;
    private bool _isHovered;
    private bool _isPlaying;
    private string _title = "";

    public BrowserItem Item { get; } = item;

    /// <summary>The item's web view; null until the item is first selected.</summary>
    public WebView2? View { get; set; }

    /// <summary>True once the view has been given its core (or has been handed
    /// one by the page that opened it), so a second selection never starts it
    /// again.</summary>
    public bool Started { get; set; }

    /// <summary>What the page's card says above the page, if anything.</summary>
    public PageNotice Notice { get; set; }

    /// <summary>The http address that was upgraded to https, while that
    /// navigation may still fail: what "Continue anyway" allows over http.
    /// Kept in memory for the window only; never logged.</summary>
    public Uri? UpgradedFrom { get; set; }

    /// <summary>The address of the top-level page being loaded or shown, the
    /// blocker's "source" for every request the page makes. Memory only.</summary>
    public string PageUrl { get; set; } = "";

    /// <summary>Requests blocked since the page's navigation started.</summary>
    public int BlockedCount { get; set; }

    /// <summary>The id of the script run at document creation for the cosmetic
    /// filters of the current navigation, and the ticket that tells a stale
    /// navigation's script from the current one's.</summary>
    public string? CosmeticScriptId { get; set; }
    public int CosmeticTicket { get; set; }

    /// <summary>The pairing id and the start of the navigation under way, for
    /// the log's start and end of it; null while none is.</summary>
    public string? NavigationWorkId { get; set; }
    public long NavigationStartedAt { get; set; }

    /// <summary>The environment generation the item's web view was made in: a
    /// browser process failure invalidates the whole generation.</summary>
    public int Generation { get; set; }

    /// <summary>Completes at the first completed navigation of the view (or
    /// when a page-opened window is handed over).</summary>
    public TaskCompletionSource Loaded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>What a caller waits on for the next navigation it started.</summary>
    public TaskCompletionSource<bool>? Navigation { get; set; }

    public string Title
    {
        get => _title;
        set
        {
            if (Set(ref _title, value)) Raise(nameof(Tooltip));
        }
    }

    // The tab's line and the tile's tooltip read the title, or the word for a
    // page that has none yet.
    public string Tooltip => _title.Length > 0 ? _title : BrowserText.Get("Tab_Untitled");

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (!Set(ref _icon, value)) return;
            Raise(nameof(IconVisibility));
            Raise(nameof(GlyphVisibility));
        }
    }

    public Visibility IconVisibility => _icon is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GlyphVisibility => _icon is null ? Visibility.Visible : Visibility.Collapsed;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value)) return;
            Raise(nameof(SelectedVisibility));
            Raise(nameof(CloseVisibility));
        }
    }

    // The selected tile shows an accent outline, 2 px.
    public Visibility SelectedVisibility => _isSelected ? Visibility.Visible : Visibility.Collapsed;

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (Set(ref _isHovered, value)) Raise(nameof(CloseVisibility));
        }
    }

    // The close button of a tab's line shows on hover and on the selected one.
    public Visibility CloseVisibility => _isHovered || _isSelected ? Visibility.Visible : Visibility.Collapsed;

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (Set(ref _isPlaying, value)) Raise(nameof(PlayingVisibility));
        }
    }

    public Visibility PlayingVisibility => _isPlaying ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name!);
        return true;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>What a page's card says above the page.</summary>
public enum PageNotice
{
    None,
    NoSecureConnection,
    Crashed,
}
