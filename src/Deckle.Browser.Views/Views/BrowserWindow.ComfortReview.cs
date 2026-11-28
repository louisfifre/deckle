using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Browser.Views;

// ─── Browser window: what the comfort's walk presses and reads ───────────────
//
// As BrowserWindow.Gestures: a button is invoked through its own automation
// peer, a text box takes its text (its peer offers no value pattern); what no
// peer can do is said at the method. Keys are never sent: the window is shown
// with WS_EX_NOACTIVATE and never takes the keyboard, and a key sent to the
// system would reach whatever the maintainer is typing into.
public sealed partial class BrowserWindow
{
    // ── Suggestions ──────────────────────────────────────────────────────

    /// <summary>The titles the address field's list shows now, as the list holds them.</summary>
    public IReadOnlyList<string> SuggestionTitlesForReview =>
        AddressBox.ItemsSource is IEnumerable<SuggestionLine> lines ? [.. lines.Select(l => l.Title)] : [];

    /// <summary>The domains the address field's list shows now.</summary>
    public IReadOnlyList<string> SuggestionDomainsForReview =>
        AddressBox.ItemsSource is IEnumerable<SuggestionLine> lines ? [.. lines.Select(l => l.Domain)] : [];

    /// <summary>The suggestion source the field's typing calls, with a text: the titles it returns.
    /// Nothing is requested from the network for it, which the walk reads from the request count.</summary>
    public IReadOnlyList<string> SuggestForReview(string typed) =>
        [.. _history.Suggest(typed).Select(s => s.Title.Length > 0 ? s.Title : s.Domain)];

    /// <summary>How many web requests the pages made since the process began (any page of any window).</summary>
    public long RequestsSeenForReview => BrowserActivity.RequestsTotal;

    /// <summary>How many pages the history holds.</summary>
    public int HistoryCountForReview => _history.Count;

    /// <summary>Opens the list under the field, finds the line of the suggestion and presses it through its own
    /// peer when the line offers an invoke; otherwise the field's own query-submitted handler is run with the
    /// suggestion, as a click on a line ends in. Returns how it was pressed, or null when there is no such line.</summary>
    public async Task<string?> ChooseSuggestionForReviewAsync(int index)
    {
        if (AddressBox.ItemsSource is not IEnumerable<SuggestionLine> lines || lines.ElementAtOrDefault(index) is not { } line) return null;
        AddressBox.IsSuggestionListOpen = true;
        ListViewItem? container = null;
        for (int waited = 0; waited < 2000 && container is null; waited += 50)
        {
            await Task.Delay(50);
            foreach (Microsoft.UI.Xaml.Controls.Primitives.Popup popup in OpenPopups())
            {
                ListView? list = FindAll<ListView>(popup.Child).FirstOrDefault();
                container = list?.ContainerFromIndex(index) as ListViewItem;
                if (container is not null) break;
            }
        }
        await Task.Delay(100);

        if (container is not null && PeerOf(container)?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
        {
            invoke.Invoke();
            return "the list line's own invoke";
        }
        CommitAddress(line);
        return container is null
            ? "handler (the list drew no line to press)"
            : "handler (the list line offers no invoke)";
    }

    /// <summary>Calls a DevTools protocol method on the shown page and returns its JSON result: what the walk
    /// uses to start sound the way a click would (userGesture), which no key or pointer can do here.</summary>
    public async Task<string> CallDevToolsForReviewAsync(string method, string parametersJson)
    {
        PanelItem panel = _shown ?? throw new InvalidOperationException("No page is shown.");
        await panel.Loaded.Task;
        return await panel.View!.CoreWebView2.CallDevToolsProtocolMethodAsync(method, parametersJson);
    }

    // ── Find ─────────────────────────────────────────────────────────────

    /// <summary>What Ctrl+F runs: the bar opens. Not a key.</summary>
    public void OpenFindForReview() => OpenFind();

    public bool FindBarShownForReview => FindBar.Visibility == Visibility.Visible;

    /// <summary>The bar's text box takes the text, as typing sets it; the search follows as typing makes it.</summary>
    public void TypeFindForReview(string text) => FindBox.Text = text;

    public int FindMatchesForReview => _findMatches;

    public int FindActiveForReview => _findActive;

    public string FindCountTextForReview => FindCountText.Text;

    public bool ClickFindNextForReview() => InvokeByAutomation(FindNextButton);

    public bool ClickFindPreviousForReview() => InvokeByAutomation(FindPreviousButton);

    public bool ClickFindCloseForReview() => InvokeByAutomation(FindCloseButton);

    // ── Shortcuts ────────────────────────────────────────────────────────

    /// <summary>Runs a shortcut's command as its keys would. Not a key.</summary>
    public void RunShortcutForReview(ShortcutCommand command) => RunShortcut(command);

    public bool AddressFocusedForReview => AddressFocused;

    // ── The media card ───────────────────────────────────────────────────

    public bool MediaCardShownForReview => MediaCard.Visibility == Visibility.Visible;

    public string MediaTitleForReview => MediaTitleText.Text;

    public string MediaArtistForReview => MediaArtistText.Text;

    /// <summary>Whether the media the card read last was paused (true when it read nothing).</summary>
    public bool MediaPausedForReview => _mediaLast?.Paused ?? true;

    public bool MediaReadForReview => _mediaLast is not null;

    public bool MediaMutedForReview => _mediaItem?.View?.CoreWebView2?.IsMuted ?? false;

    /// <summary>The item the card follows.</summary>
    public Guid? MediaItemForReview => _mediaItem?.Item.Id;

    public bool ClickMediaPlayPauseForReview() => InvokeByAutomation(MediaPlayPauseButton);

    public bool ClickMediaMuteForReview() => InvokeByAutomation(MediaMuteButton);

    public bool ClickMediaCloseForReview() => InvokeByAutomation(MediaCloseButton);

    public bool ClickMediaTitleForReview() => InvokeByAutomation(MediaTitleButton);

    public bool ClickMediaPreviousForReview() => InvokeByAutomation(MediaPreviousButton);

    public bool ClickMediaNextForReview() => InvokeByAutomation(MediaNextButton);

    public bool ClickMediaPipForReview() => InvokeByAutomation(MediaPipButton);
}
