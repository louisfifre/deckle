using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Llm.Rewrite;

// ─── Active profile section of LlmPage ─────────────────────────────────────
//
// One ComboBox: "(None)" then every defined profile. The choice persists
// (LlmSettings.ActiveProfileId/Name) and applies to every dictation while
// rewriting is enabled; "(None)" keeps the transcript as spoken.
//
// The list depends on the defined profiles — Reload() must be called by the
// host after any mutation of the Profiles section so newly-added / renamed
// profiles show up here.

public sealed partial class LlmActiveProfileSection : UserControl
{
    // Sentinel shown first in the combo to mean "plain transcription".
    // Stored as null in settings — not as this string.
    private const string NoneSentinel = "(None)";

    private bool _loading;

    public LlmActiveProfileSection()
    {
        InitializeComponent();
    }

    public void Reload()
    {
        _loading = true;
        var s = LlmSettingsService.Instance.Current;

        // Match the saved choice first by stable id (survives renames), then
        // by name for pre-migration configs. Nothing resolves → (None).
        ActiveProfileCombo.Items.Clear();
        ActiveProfileCombo.Items.Add(new ComboBoxItem { Content = NoneSentinel });
        for (int i = 0; i < s.Profiles.Count; i++)
            ActiveProfileCombo.Items.Add(new ComboBoxItem { Content = s.Profiles[i].Name });
        int index = ResolveProfileIndex(s, s.ActiveProfileId, s.ActiveProfileName);
        ActiveProfileCombo.SelectedIndex = index >= 0 ? index + 1 : 0;

        _loading = false;
    }

    private static int ResolveProfileIndex(LlmSettings s, string? id, string? name)
    {
        if (!string.IsNullOrEmpty(id))
        {
            for (int i = 0; i < s.Profiles.Count; i++)
                if (s.Profiles[i].Id == id) return i;
        }
        if (!string.IsNullOrWhiteSpace(name))
        {
            for (int i = 0; i < s.Profiles.Count; i++)
                if (string.Equals(s.Profiles[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
        }
        return -1;
    }

    private void ActiveProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ActiveProfileCombo.SelectedIndex < 0) return;
        var s = LlmSettingsService.Instance.Current;
        // Index-based: item 0 is (None), item i+1 is Profiles[i]. A profile
        // literally named "(None)" is never confused with the sentinel.
        int profileIndex = ActiveProfileCombo.SelectedIndex - 1;
        RewriteProfile? profile = profileIndex >= 0 && profileIndex < s.Profiles.Count
            ? s.Profiles[profileIndex]
            : null;
        s.ActiveProfileId = profile?.Id;
        s.ActiveProfileName = profile?.Name;
        LlmSettingsService.Instance.Save();
    }

    // Scope: the active profile choice only. The Profiles list itself stays
    // untouched — resetting the choice should not wipe user-authored profiles.
    //
    // No confirmation: this reset is REVERSIBLE — it only puts the choice back
    // to the POCO default (null, plain transcription), destroying nothing the
    // user can't re-pick in a click.
    private void ResetSection_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new LlmSettings();
        var s = LlmSettingsService.Instance.Current;
        s.ActiveProfileId   = defaults.ActiveProfileId;
        s.ActiveProfileName = defaults.ActiveProfileName;
        LlmSettingsService.Instance.Save();
        Reload();
    }
}
