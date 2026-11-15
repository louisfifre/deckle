namespace Deckle.Projects;

// The shape of projects.json. Plain classes with settable properties so
// System.Text.Json reads a file written by a later version (unknown fields are
// ignored) and an earlier one (missing fields keep their defaults).
//
// The order of Categories is the order of creation; the order of Projects is
// the whole list's order, and a project's place in its category is its
// position among the projects of that category. Nothing else records a place.
internal sealed class StoredFile
{
    public int Version { get; set; } = 1;
    public List<StoredCategory> Categories { get; set; } = [];
    public List<StoredProject> Projects { get; set; } = [];
}

internal sealed class StoredCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

internal sealed class StoredProject
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Name { get; set; }
    public string? CategoryId { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public List<StoredPin> Pinned { get; set; } = [];
    public StoredActionRef? Main { get; set; }
}

internal sealed class StoredPin
{
    public string Id { get; set; } = "";
    public string? Variant { get; set; }
    public string Label { get; set; } = "";
    public string Script { get; set; } = "";
}

internal sealed class StoredActionRef
{
    public string Id { get; set; } = "";
    public string? Variant { get; set; }
}
