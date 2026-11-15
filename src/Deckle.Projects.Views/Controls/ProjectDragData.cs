using Windows.ApplicationModel.DataTransfer;

namespace Deckle.Projects.Views;

// The contract between what is dragged (a project's card) and what accepts it
// (a category's section, the "Without a category" fold). The project's id
// travels in the DataPackage under a private format name: it means something
// only to this window, so a drop on a text editor or on Explorer finds nothing
// it can use.
internal static class ProjectDragData
{
    public const string Format = "Deckle.Projects.ProjectId";

    // Called from DragStarting of the dragged element.
    public static void Put(DataPackage data, string projectId)
    {
        data.SetData(Format, projectId);
        data.RequestedOperation = DataPackageOperation.Move;
    }

    public static bool IsProject(DataPackageView data) => data.Contains(Format);

    // The id dropped, or null when the package carries none.
    public static async Task<string?> TakeAsync(DataPackageView data)
    {
        if (!data.Contains(Format)) return null;
        return await data.GetDataAsync(Format) as string;
    }
}
