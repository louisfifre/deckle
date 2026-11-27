namespace Deckle.Projects;

// How Home groups the projects that are in no category.
//
// The "Without a category" fold exists to set those projects apart from the
// categories above it, closed by default so the categories lead (spec, Home).
// With no category on Home there is nothing to set them apart from: the fold
// would be the whole page, closed, and the person's projects would sit behind
// a header they never asked for. So the fold is drawn only while Home has a
// category; without one, the projects show as Home's own grid, open, with no
// header and no hint about dragging onto a category that does not exist.
public static class HomeLayout
{
    public static bool FoldsLooseProjects(int categoryCount) => categoryCount > 0;
}
