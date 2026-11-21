using Deckle.Core;

namespace Deckle.Projects;

// A place the window can show: Home, a category's page, a project's page. A
// record with value equality, so the navigation history (NavigationHistory of
// Deckle Folders, which holds any class with Equals) tells "the place already
// shown" from a new one, and Forget can drop every place that names a project
// or a category that was removed.
//
// BeginRename is how the person asked to arrive, not where they are: "Rename" in
// a card's menu opens the project's page with its name already a text box
// (spec), a one-shot of that first show. The history therefore records Place,
// which drops it, so Back and Forward reopen the page as a page and the same
// project is never two entries.
public abstract record ProjectsTarget
{
    private ProjectsTarget() { }

    // The place to record in the history: this target without its arrival.
    public ProjectsTarget Place => this is Project { BeginRename: true } project
        ? project with { BeginRename = false }
        : this;

    // True when showing this target should open with the name in edit.
    public bool BeginsRename => this is Project { BeginRename: true };

    public sealed record Home : ProjectsTarget;

    public sealed record Category(string CategoryId) : ProjectsTarget;

    public sealed record Project(string ProjectId, bool BeginRename = false) : ProjectsTarget;

    // For NavigationHistory.Forget: the places that name this project.
    public static Func<ProjectsTarget, bool> NamesProject(string projectId) =>
        target => target is Project project && project.ProjectId == projectId;

    // The places that name this category.
    public static Func<ProjectsTarget, bool> NamesCategory(string categoryId) =>
        target => target is Category category && category.CategoryId == categoryId;
}
