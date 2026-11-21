using Deckle.Atelier;
using Deckle.Atelier.Views;

namespace Deckle.Atelier.App;

// The self-check's image of the gallery's previews (spec, Behaviours: "it
// also shows PDFs and Markdown files large"), called from WalkAtelierAsync
// while the gallery is shown: the sample's real PDF shows its first page and
// its Markdown file the beginning of its text. The image fails when either
// tile still shows its type icon. The other PDFs of the sample hold zeroes,
// so the same image shows that a PDF Windows cannot read keeps its icon.
// Nothing is opened. It leaves the gallery shown, as it found it.
internal static partial class AtelierSelfCheck
{
    private static Task WalkAtelierPreviewsAsync(
        AtelierWindow window, Func<string, Func<Task>?, Task> capture) =>
        capture("files-gallery-previews", async () =>
        {
            await FilesReview.ShowDisplayAsync(window, FileListDisplay.Gallery);
            await FilesReview.RequirePreviewsAsync(window, SampleFolder.PdfFileName, SampleFolder.MarkdownFileName);
        });
}
