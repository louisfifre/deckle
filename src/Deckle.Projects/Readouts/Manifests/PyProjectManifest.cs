namespace Deckle.Projects;

// pyproject.toml: `[project] version`, or `[tool.poetry] version` for a
// Poetry project. A project that lists "version" under `dynamic` has none.
internal static class PyProjectManifest
{
    public static string? Version(string text) =>
        TomlVersion.Read(text, "version", "project", "tool.poetry");
}
