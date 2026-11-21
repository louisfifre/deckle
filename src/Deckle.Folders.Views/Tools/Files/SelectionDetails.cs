using Microsoft.UI.Xaml;

namespace Deckle.Folders.Views;

// What the details panel holds for several items selected at once (spec,
// Open 33: Windows' conventions). As File Explorer's details pane does: the
// number of items as the title, what they are above it, then the size of the
// files among them and the folder they are in. The words are SelectionSummary's
// rule, the sentences these resources.
internal static class SelectionDetails
{
    private static readonly Lazy<SelectionTexts> Texts = new(() => new SelectionTexts(
        FoldersText.Get("Files_Selection_Items_One_Format"),
        FoldersText.Get("Files_Selection_Items_Many_Format"),
        FoldersText.Get("Files_Selection_Folders_One_Format"),
        FoldersText.Get("Files_Selection_Folders_Many_Format"),
        FoldersText.Get("Files_Selection_Files_One_Format"),
        FoldersText.Get("Files_Selection_Files_Many_Format"),
        FoldersText.Get("Files_Summary_Separator"),
        FoldersText.Get("Files_Selection_Size_WithoutFolders_Format")));

    // The items all sit in the folder the list shows.
    public static UIElement For(IReadOnlyList<FolderEntry> entries, string folder)
    {
        SelectionWording wording = SelectionSummary.Of(entries).Describe(Texts.Value, FoldersText.Culture);
        return new DetailsSheet(wording.Kinds, wording.Title)
            .Section(
                FoldersText.Get("Details_Section"),
                (FoldersText.Get("Details_Size"), wording.Size),
                (FoldersText.Get("Details_Location"), folder))
            .Element;
    }
}
