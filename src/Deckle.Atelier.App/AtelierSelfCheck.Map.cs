using Deckle.Atelier;
using Deckle.Atelier.Views;
using Deckle.FolderMap;

namespace Deckle.Atelier.App;

// The map's states in the self-check's walk of the Atelier: the list on a
// sample project (12.8b), an item selected with its panel and graph (12.8a),
// the "always read" box ticked with its total (12.8c), a filter that leaves no
// row, the list by sections with an item that has no link (12.8a), the large
// graph with an item chosen and the list it gives back (12.9), and a folder
// with nothing of the kind (12.14). The project is written under the sample folder
// (MapSampleFolder) once the file tool's states are photographed, so those
// images stay as they were, and before the address bar's text mode, which
// stays open across navigation and would hide the path in these images.
internal static partial class AtelierSelfCheck
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
        // A filter that leaves no row: "always read" keeps only context
        // files, and their box is unticked (spec, Open 33).
        await capture("map-no-match", () =>
        {
            MapReview.SetKindShown(window, MapItemKind.ContextFile, false);
            return Task.CompletedTask;
        });
        MapReview.SetKindShown(window, MapItemKind.ContextFile, true);
        MapReview.SetAlwaysReadOnly(window, false);

        // By sections (12.8a), with an item that has no link selected, whose
        // panel says so where the graph would be (spec, Open 33).
        await capture("map-sections", () =>
        {
            MapReview.SetBySections(window, true);
            return window.SelectForReviewAsync("LICENSE");
        });
        // The same filters by sections: "always read" leaves only the context
        // files' section, the others emptied and gone, and the total row.
        await capture("map-sections-always-read", () =>
        {
            MapReview.SetAlwaysReadOnly(window, true);
            return window.SelectForReviewAsync("CLAUDE.md");
        });
        MapReview.SetAlwaysReadOnly(window, false);
        MapReview.SetBySections(window, false);

        // The large graph (12.9) with the skill chosen, it and its neighbours
        // standing out, then the list it gives back, on the same row.
        await capture("map-large-graph", async () =>
        {
            await window.SelectForReviewAsync(@".claude\skills\publier-version\SKILL.md");
            MapReview.SetLargeGraph(window, true);
        });
        await capture("map-large-graph-left", () =>
        {
            MapReview.SetLargeGraph(window, false);
            return Task.CompletedTask;
        });

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
