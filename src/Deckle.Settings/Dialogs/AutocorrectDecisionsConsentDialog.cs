using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Catalog;
using Deckle.Core;

namespace Deckle.Settings;

// ─── AutocorrectDecisionsConsentDialog ─────────────────────────────────────
//
// Opt-in consent for the per-word autocorrect decision dataset
// (autocorrect.decisions.jsonl). Shown the first time the user flips the toggle
// from off to on. Cancelling reverts the toggle.
//
// Same pattern as ApplicationLogConsentDialog — no "Don't show again", no
// severity icon. This one captures the words the user types on enrolled apps in
// clear text, so the wording is explicit about it. Strings via Loc.Get /
// Resources.resw; the where path is the actual file and stays hardcoded.

internal static class AutocorrectDecisionsConsentDialog
{
    public static async Task<bool> ShowAsync(XamlRoot root)
    {
        string where = Path.Combine(CorpusPaths.GetDirectoryPath(), "autocorrect.decisions.jsonl");

        var body = new StackPanel { Spacing = 12 };

        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = Loc.Get("AutocorrectDecisionsConsent_Body_Intro")
        });

        body.Children.Add(new TextBlock
        {
            Text = Loc.Get("Common_Consent_WhatHeader"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });

        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = Loc.Get("AutocorrectDecisionsConsent_Body_What")
        });

        body.Children.Add(new TextBlock
        {
            Text = Loc.Get("Common_Consent_WhereHeader"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });

        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = where
        });

        body.Children.Add(new TextBlock
        {
            Text = Loc.Get("Common_Consent_RemindHeader"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });

        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = Loc.Get("AutocorrectDecisionsConsent_Body_Remind")
        });

        var dialog = new ContentDialog
        {
            Title = Loc.Get("AutocorrectDecisionsConsent_Title"),
            Content = body,
            PrimaryButtonText = Loc.Get("Common_Enable"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
