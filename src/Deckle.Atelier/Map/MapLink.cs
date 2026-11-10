namespace Deckle.Atelier;

public enum MapLinkForm
{
    // [text](path) or a reference definition [id]: path.
    MarkdownLink,

    // @path in a file Claude Code or Gemini CLI expands, which loads the
    // target along with it.
    Import,

    // A script's path or file name, a skill's name in backticks or as
    // /name, a server's name in backticks or as mcp__name.
    Mention,
}

// Where a link was read: the file (relative to the folder) and the line,
// counted from 1. A skill's link can be read in one of its supporting files,
// so the file is not always the item's own.
public sealed record MapLinkEvidence(MapLinkForm Form, string RelativePath, int Line);

// One item citing another. A link is counted once per pair of items, however
// many times the text repeats it; Evidence keeps every place.
public sealed record MapLink(string FromId, string ToId, IReadOnlyList<MapLinkEvidence> Evidence);
