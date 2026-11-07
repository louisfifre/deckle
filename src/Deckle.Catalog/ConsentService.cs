using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

// ─── ConsentService ────────────────────────────────────────────────────────
//
// The one consent dialog. Every opt-in that starts a recording of the person's
// data (text corpus, audio, typed text, traces, telemetry) asks through here,
// so the rules of a consent live in one place:
//
//   * the shape is fixed: an intro, then "What is recorded", "Where it is
//     stored", "How to stop";
//   * the safe button is the default. Enter, like Escape, grants nothing: the
//     person has to reach for "Enable" on purpose. Windows documents the safe,
//     non-destructive button as the one every dialog must offer, and the
//     default button as an optional emphasis; a consent that records data
//     emphasizes the refusal.
//
// Copy-agnostic like ConfirmationService: the caller passes its own title and
// paragraphs already resolved; the section headers and the two verbs are the
// strings every consent shares.

public sealed record ConsentRequest(string Title, string Intro, string What, string Where, string Remind)
{
    // A consent grants nothing by default. Kept as a member, not a literal in
    // the service, so a test can pin the rule without showing a dialog.
    public ContentDialogButton DefaultButton => ContentDialogButton.Close;

    // The body, top to bottom. A null header is the intro, which has none.
    // The Where text is a filesystem path and is shown as given.
    public IReadOnlyList<ConsentSection> Sections { get; } = new[]
    {
        new ConsentSection(null, Intro),
        new ConsentSection("Common_Consent_WhatHeader", What),
        new ConsentSection("Common_Consent_WhereHeader", Where),
        new ConsentSection("Common_Consent_RemindHeader", Remind),
    };
}

public sealed record ConsentSection(string? HeaderKey, string Text);

public static class ConsentService
{
    public static async Task<bool> RequestAsync(XamlRoot root, ConsentRequest request)
    {
        var body = new StackPanel { Spacing = 12 };

        foreach (ConsentSection section in request.Sections)
        {
            if (section.HeaderKey is { } headerKey)
            {
                body.Children.Add(new TextBlock
                {
                    Text = Loc.Get(headerKey),
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
                });
            }

            body.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = section.Text
            });
        }

        var dialog = new ContentDialog
        {
            Title = request.Title,
            Content = body,
            PrimaryButtonText = Loc.Get("Common_Enable"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = request.DefaultButton,
            XamlRoot = root
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
