using Deckle.Atelier;
using Deckle.Atelier.Views;

namespace Deckle.App;

// The map's states in the self-check's walk of the Atelier: the list on a
// sample project (12.8b), an item selected with its panel and graph (12.8a),
// the "always read" box ticked with its total (12.8c), and a folder with
// nothing of the kind (12.14). The project is written under the sample folder
// (MapSampleFolder) once the file tool's states are photographed, so those
// images stay as they were, and before the address bar's text mode, which
// stays open across navigation and would hide the path in these images.
public partial class App
{
    private static async Task WalkAtelierMapAsync(AtelierWindow window, string sample, Func<string, Func<Task>?, Task> capture)
    {
        MapSampleFolder.Create(sample);

        // Adding the known sample folder opens it again at its root.
        await capture("map-list", async () =>
        {
            await window.AddFolderForReviewAsync(sample);
            await window.OpenForReviewAsync(MapSampleFolder.RelativePath);
            await window.ShowToolForReviewAsync("map");
        });
        await capture("map-selected", () => window.SelectForReviewAsync(@".claude\skills\publier-version\SKILL.md"));
        await capture("map-always-read", () =>
        {
            MapReview.SetAlwaysReadOnly(window, true);
            return window.SelectForReviewAsync("CLAUDE.md");
        });
        MapReview.SetAlwaysReadOnly(window, false);

        await capture("map-nothing", async () =>
        {
            await window.AddFolderForReviewAsync(sample);
            await window.OpenForReviewAsync("Documents");
            await window.ShowToolForReviewAsync("map");
        });

        // The walk goes on in the file tool, where the map found it.
        await window.ShowToolForReviewAsync("files");
    }
}
