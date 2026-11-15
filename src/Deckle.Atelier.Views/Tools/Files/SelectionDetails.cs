using Microsoft.UI.Xaml;

namespace Deckle.Atelier.Views;

// What the details panel holds for several items selected at once (spec,
// Open 33: Windows' conventions). As File Explorer's details pane does: the
// number of items as the title, what they are above it, then the size of the
// files among them and the folder they are in. The words are SelectionSummary's
// rule, the sentences these resources.
internal static class SelectionDetails
{
    private static readonly Lazy<SelectionTexts> Texts = new(() => new SelectionTexts(
        AtelierText.Get("Files_Selection_Items_One_Format"),
        AtelierText.Get("Files_Selection_Items_Many_Format"),
        AtelierText.Get("Files_Selection_Folders_One_Format"),
        AtelierText.Get("Files_Selection_Folders_Many_Format"),
        AtelierText.Get("Files_Selection_Files_One_Format"),
        AtelierText.Get("Files_Selection_Files_Many_Format"),
        AtelierText.Get("Files_Summary_Separator"),
        AtelierText.Get("Files_Selection_Size_WithoutFolders_Format")));

    // The items all sit in the folder the list shows.
    public static UIElement For(IReadOnlyList<FolderEntry> entries, string folder)
    {
        SelectionWording wording = SelectionSummary.Of(entries).Describe(Texts.Value, AtelierText.Culture);
        return new DetailsSheet(wording.Kinds, wording.Title)
            .Section(
                AtelierText.Get("Details_Section"),
                (AtelierText.Get("Details_Size"), wording.Size),
                (AtelierText.Get("Details_Location"), folder))
            .Element;
    }
}
