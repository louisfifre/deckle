namespace Deckle.Projects;

// What a manifest's version value must be to count: text that names a version,
// not a variable that a build resolves later. "${revision}", "$(Version)",
// "@version@" and "$version" are the shapes the supported formats use, and a
// line made of one tells the person nothing the build would not.
internal static class VersionText
{
    public static string? Plain(string? value)
    {
        if (value is null) return null;
        string trimmed = value.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Contains('$') || trimmed.Contains('@') || trimmed.Contains('{')) return null;
        return trimmed;
    }
}
