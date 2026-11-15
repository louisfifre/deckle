namespace Deckle.Projects;

// A group the person creates to sort the projects. It has a title and nothing
// else: no description, no icon, no colour (spec, Home). The order of the
// categories is the order of creation, which is the order of the store's list.
public sealed record ProjectCategory(string Id, string Name)
{
    // The title of a category made with "New category" before the person types
    // another one, and the one it keeps when the box is left empty.
    public const string DefaultName = "New category";
}
