using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

// ─── ConfirmationService ───────────────────────────────────────────────────
//
// One reusable "are you sure?" gate for irreversible actions, in the same
// ContentDialog shape as the consent dialogs.
//
// Copy-agnostic: the caller passes Title/Body/PrimaryVerb already resolved and
// formatted, so the service owes nothing to any page's .resw. Cancel is the one
// string it owns, because every confirmation shares it.

public sealed record ConfirmationRequest(string Title, string Body, string PrimaryVerb, bool IsDestructive = false);

public static class ConfirmationService
{
    public static async Task<bool> RequestAsync(XamlRoot root, ConfirmationRequest request)
    {
        var dialog = new ContentDialog
        {
            Title = request.Title,
            // A bare string would render on one line and clip; a wrapping
            // TextBlock lets a long body flow.
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
