using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Diagnostics;

namespace Deckle.Settings;

// ── Shortcuts page ────────────────────────────────────────────────────────────
//
// Every global key combination Deckle registers, on one page of their own: the
// shortcuts span every module, so they belong to none of the module pages, and
// on General they buried the app-wide settings under seven cards. Three
// sections, grouped by what the action drives. The values live in
// GeneralViewModel, the same owner as before the split; this page holds its own
// instance and reloads it on each visit, like General does, so neither page
// pushes stale values over the other's edits.
public sealed partial class HotkeysPage : Page
{
    public GeneralViewModel ViewModel { get; } = new();

    // Every composer is created before the first LoadAndSync so its subscription
    // catches Load()'s PropertyChanged, and held in a field so it lives as long
    // as the cached page.
    private readonly SettingsComposer _dictationComposer;
    private readonly SettingsComposer _inputComposer;
    private readonly SettingsComposer _displayComposer;

    public HotkeysPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        _dictationComposer = ComposeSection(DictationSection, DictationHost, ViewModel.DictationHotkeys);
        _inputComposer = ComposeSection(InputSection, InputHost, ViewModel.InputHotkeys);
        _displayComposer = ComposeSection(DisplaySection, DisplayHost, ViewModel.DisplayHotkeys);
        // No load here: OnNavigatedTo follows the constructor on the first visit
        // and runs LoadAndSync then.
    }

    // The App rebinds the global hotkeys itself when the saved settings change,
    // so a card only writes its value. Each card hides when the App did not build
    // its action.
    private SettingsComposer ComposeSection(
        SettingsSection section, Panel host, IReadOnlyList<SettingDescriptor> settings)
    {
        var composer = new SettingsComposer(host, ViewModel);
        // Subscribed before Compose so its closing RefreshAll lands the initial
        // gate too.
        composer.DirtyChanged += (_, _) =>
        {
            section.IsResetEnabled = composer.IsDirty();
            GateResetAll();
        };
        composer.Compose(settings);
        return composer;
    }

    // NavigationCacheMode.Required reuses the page instance — the constructor
    // only fires once. Without this override, navigating away then back would
    // show stale shortcuts, and a hotkey edit (which writes every shortcut this
    // instance holds) would overwrite a change made elsewhere.
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadAndSync();
    }

    // The composed sections need no sync pass: each composer subscribes to the
    // VM, so the reload's PropertyChanged re-syncs their controls. Only the
    // hotkeys reload: Load() would also probe autostart (schtasks.exe on the UI
    // thread) and list the backups, neither of which this page shows.
    private void LoadAndSync()
    {
        ViewModel.LoadHotkeys();
        // A section whose every card is hidden would leave its title over
        // nothing, so it hides whole.
        DictationSection.Visibility = SectionVisibility(GeneralViewModel.DictationHotkeyIds);
        InputSection.Visibility = SectionVisibility(GeneralViewModel.InputHotkeyIds);
        DisplaySection.Visibility = SectionVisibility(GeneralViewModel.DisplayHotkeyIds);
        // Load() raises no PropertyChanged on a clean profile, so no composer
        // DirtyChanged fires and this call settles the gate.
        GateResetAll();
    }

    private static Visibility SectionVisibility(IReadOnlyList<string> ids)
        => GeneralViewModel.AnyHotkeyAvailable(ids) ? Visibility.Visible : Visibility.Collapsed;

    // ── Whole-page "Reset all" ────────────────────────────────────────────────
    //
    // The header block confirms by itself, with the generic wording: nothing
    // user-authored is lost here. The composers are null only while the
    // constructor is still composing, when a DirtyChanged can already fire.
    private void GateResetAll()
    {
        Header.IsResetAllEnabled =
            (_dictationComposer?.IsDirty() ?? false) ||
            (_inputComposer?.IsDirty() ?? false) ||
            (_displayComposer?.IsDirty() ?? false);
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _dictationComposer.ResetAll();
        _inputComposer.ResetAll();
        _displayComposer.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Shortcuts (all)");
    }

    // ── Reset per section ───────────────────────────────────────────────────
    //
    // Each value goes back to its default through its own setter, which pushes
    // the settings the App rebinds from, so a handler only triggers the reset
    // and logs it.

    private void ResetDictation_Click(object sender, RoutedEventArgs e)
    {
        _dictationComposer.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Shortcuts: dictation");
    }

    private void ResetInput_Click(object sender, RoutedEventArgs e)
    {
        _inputComposer.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Shortcuts: input");
    }

    private void ResetDisplay_Click(object sender, RoutedEventArgs e)
    {
        _displayComposer.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Shortcuts: display and lights");
    }
}
