namespace Deckle.Projects;

// Cargo.toml: `[package] version`, or `[workspace.package] version` for a
// workspace root that declares the version its members inherit. A member that
// says `version.workspace = true` gives no version of its own.
internal static class CargoManifest
{
    public static string? Version(string text) =>
        TomlVersion.Read(text, "version", "package", "workspace.package");
}
