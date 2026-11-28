using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace Deckle.Browser.Views;

// ─── Browser window: find in page ────────────────────────────────────────────
//
// Ctrl+F opens a small bar at the top right of the page card: a text box, the
// "n of m" count, previous and next, and close. The search is the engine's own
// (CoreWebView2.Find, CoreWebView2FindOptions from the environment: both are
// in the WebView2 1.0.3719.77 SDK in stable form), with the engine's own find
// bar suppressed so only ours shows. Typing searches as you type; Enter is the
// next match, Shift+Enter the previous, Escape closes the bar and clears the
// highlights.
//
// What was searched for is typed text: it is never logged, only counted.
public sealed partial class BrowserWindow
{
    private CoreWebView2Find? _find;
    private int _findMatches;
    private int _findActive = -1;

    private void OpenFind()
    {
        using var gesture = new GestureScope(BrowserGesture.FindOpen);
        if (ShownCore is not { } core)
        {
            gesture.Outcome = "refused";
            return;
        }
        FindBar.Visibility = Visibility.Visible;
        AttachFind(core);
        FindBox.Focus(FocusState.Programmatic);
        FindBox.SelectAll();
        if (FindBox.Text.Length > 0) _ = StartFindAsync(FindBox.Text);
    }

    private void AttachFind(CoreWebView2 core)
    {
        if (_find is not null) return;
        _find = core.Find;
        _find.MatchCountChanged += OnFindMatchCountChanged;
        _find.ActiveMatchIndexChanged += OnFindActiveChanged;
    }

    // The bar closes, the highlights go, the focus returns to the page.
    private void CloseFind()
    {
        if (FindBar.Visibility != Visibility.Visible) return;
        FindBar.Visibility = Visibility.Collapsed;
        DetachFind(stop: true);
        _shown?.View?.Focus(FocusState.Programmatic);
    }

    private void DetachFind(bool stop)
    {
        if (_find is { } find)
        {
            find.MatchCountChanged -= OnFindMatchCountChanged;
            find.ActiveMatchIndexChanged -= OnFindActiveChanged;
            if (stop)
            {
                try { find.Stop(); }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { /* the page is gone */ }
            }
        }
        _find = null;
        _findMatches = 0;
        _findActive = -1;
        FindCountText.Text = "";
    }

    private async Task StartFindAsync(string term)
    {
        BrowserActivity.FindSearched();
        if (_find is null || _environment is null) return;
        if (term.Length == 0)
        {
            _find.Stop();
            _findMatches = 0;
            _findActive = -1;
            FindCountText.Text = "";
            return;
        }
        try
        {
            CoreWebView2FindOptions options = _environment.CreateFindOptions();
            options.FindTerm = term;
            options.IsCaseSensitive = false;
            options.ShouldHighlightAllMatches = true;
            options.SuppressDefaultFindDialog = true;
            await _find.StartAsync(options);
            ShowFindCount();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or NotImplementedException)
        {
            // An engine without the find session: the bar says no match.
            FindCountText.Text = BrowserText.Get("Find_None");
        }
    }

    private void OnFindMatchCountChanged(CoreWebView2Find sender, object args) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            _findMatches = sender.MatchCount;
            ShowFindCount();
        });

    private void OnFindActiveChanged(CoreWebView2Find sender, object args) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            _findActive = sender.ActiveMatchIndex;
            ShowFindCount();
        });

    private void ShowFindCount()
    {
        if (_find is null || FindBox.Text.Length == 0) return;
        FindCountText.Text = _findMatches > 0
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, BrowserText.Get("Find_Count"), Math.Max(_findActive, 1), _findMatches)
            : BrowserText.Get("Find_None");
    }

    private void OnFindTextChanged(object sender, TextChangedEventArgs e) => _ = StartFindAsync(FindBox.Text);

    private void OnFindKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                if (IsDown(VirtualKey.Shift)) FindPrevious();
                else FindNext();
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                FindClose();
                break;
        }
    }

    private void OnFindNextClick(object sender, RoutedEventArgs e) => FindNext();

    private void OnFindPreviousClick(object sender, RoutedEventArgs e) => FindPrevious();

    private void OnFindCloseClick(object sender, RoutedEventArgs e) => FindClose();

    private void FindNext()
    {
        using var gesture = new GestureScope(BrowserGesture.FindNext);
        _find?.FindNext();
    }

    private void FindPrevious()
    {
        using var gesture = new GestureScope(BrowserGesture.FindPrevious);
        _find?.FindPrevious();
    }

    private void FindClose()
    {
        using var gesture = new GestureScope(BrowserGesture.FindClose);
        CloseFind();
    }
}
