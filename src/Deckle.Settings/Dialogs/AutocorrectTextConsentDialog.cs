using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Catalog;
using Deckle.Core;

namespace Deckle.Settings;

// ─── AutocorrectTextConsentDialog ──────────────────────────────────────────
//
// Opt-in consent for the typed-sentence corpus (autocorrect.text.jsonl). Shown
// the first time the user flips the nested toggle from off to on. Cancelling
// reverts it.
//
// Same pattern as AutocorrectDecisionsConsentDialog, but this is the heaviest
// text capture in the app — a verbatim record of everything typed at the keyboard
// in any editable field (never password fields), enrolled or not — so the wording
// is the most explicit of the consent dialogs.
// Strings via Loc.Get / Resources.resw; the where path is the actual file.

internal static class AutocorrectTextConsentDialog
{
    public static async Task<bool> ShowAsync(XamlRoot root)
    {
        // One consent envelope, two files: the typed-sentence corpus and, on
        // enrolled apps only, the typing stream (runs segmented at backward
        // repairs). Both paths shown so the where stays the actual files.
        string directory = CorpusPaths.GetDirectoryPath();
        string where = Path.Combine(directory, "autocorrect.text.jsonl")
            + "\n" + Path.Combine(directory, "autocorrect.stream.jsonl");

        var body = new StackPanel { Spacing = 12 };

        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = Loc.Get("AutocorrectTextConsent_Body_Intro")
        });

        body.Children.Add(new TextBlock
        {
            Text = Loc.Get("Common_Consent_WhatHeader"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });

        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = Loc.Get("AutocorrectTextConsent_Body_What")
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
            Text = Loc.Get("AutocorrectTextConsent_Body_Remind")
        });

        var dialog = new ContentDialog
        {
            Title = Loc.Get("AutocorrectTextConsent_Title"),
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
