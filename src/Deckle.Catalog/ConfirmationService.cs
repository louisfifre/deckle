using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

// ─── ConfirmationService ───────────────────────────────────────────────────
//
// One reusable "are you sure?" gate for destructive commands. Any page that
// guards an irreversible action (restore-over, wipe, reset-to-default) builds
// a ConfirmationRequest and awaits RequestAsync — same ContentDialog shape and
// theme as the modules' consent dialogs, so the look stays consistent.
//
// The service is deliberately copy-agnostic: the caller passes the already-
// resolved, already-formatted Title/Body/PrimaryVerb (its own Loc keys, its
// own placeholders). The service never resolves a key, so it owes nothing to
// any one page's resw and stays reusable by all of them. Cancel is the one
// string it owns, because every confirmation shares the same Cancel verb.

public sealed record ConfirmationRequest(string Title, string Body, string PrimaryVerb, bool IsDestructive = false);

public static class ConfirmationService
{
    public static async Task<bool> RequestAsync(XamlRoot root, ConfirmationRequest request)
    {
        var dialog = new ContentDialog
        {
            Title = request.Title,
            // Wrapped in a TextBlock — like the consent dialogs' bodies — so a
            // long, caller-supplied body line flows instead of clipping the
            // dialog. A bare string would render single-line.
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = request.Body
            },
            PrimaryButtonText = request.PrimaryVerb,
            CloseButtonText = Loc.Get("Common_Cancel"),
            // A destructive action defaults to the SAFE button: Enter commits
            // nothing, the user has to reach for the verb on purpose. A merely
            // confirming (non-destructive) action keeps Primary as default.
            DefaultButton = request.IsDestructive
                ? ContentDialogButton.Close
                : ContentDialogButton.Primary,
            XamlRoot = root
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
