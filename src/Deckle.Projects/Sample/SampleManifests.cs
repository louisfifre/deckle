namespace Deckle.Projects;

// The text of the synthetic files the sample projects hold: a script menu stub
// and the manifests that give a project several versions. Nothing here comes
// from a real project.
internal static class SampleManifests
{
    // A stand-in for scripts/menu.ps1: answers -List with the rows the real
    // launcher prints (id, label, script, arguments, variant, tab-separated) and
    // -Run with a one-line journal. Runs under Windows PowerShell 5.1 and 7.
    public static string MenuStub(params (string Id, string Label, string Script, string Variant)[] rows)
    {
        string table = string.Join(",\n", rows.Select(r => $"    @('{r.Id}', '{r.Label}', '{r.Script}', '', '{r.Variant}')"));
        return $$"""
            # Synthetic script menu for the Deckle Projects self-check and tests.
            # It answers -List like the real launcher and -Run with a short journal.
            param([switch]$List, [string]$Run, [string]$Variant)
            $rows = @(
            {{table}}
            )
            if ($List) {
                foreach ($row in $rows) { [Console]::WriteLine($row -join "`t") }
                return
            }
            if ($Run) {
                Write-Host "sample run of $Run $Variant"
                exit 0
            }
            Write-Host 'Sample menu.'
            """.ReplaceLineEndings("\n") + "\n";
    }

    public static string CargoManifest(string name, string version) => $"""
        [package]
        name = "{name}"
        version = "{version}"
        edition = "2021"
        """.ReplaceLineEndings("\n") + "\n";

    public static string GradleProperties(string modId, string version) => $"""
        # Synthetic mod template properties.
        mod_id={modId}
        mod_version={version}
        """.ReplaceLineEndings("\n") + "\n";

    public static string PackageJson(string name, string version) =>
        $"{{\n  \"name\": \"{name}\",\n  \"version\": \"{version}\"\n}}\n";

    public static string SkillFile(string name, string description) => $"""
        ---
        name: {name}
        description: {description}
        ---

        Steps for {name}, written for a sample.
        """.ReplaceLineEndings("\n") + "\n";
}
