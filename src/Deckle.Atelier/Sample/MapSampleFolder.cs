namespace Deckle.Atelier;

// The project the self-check shows the map on, written under the sample folder
// (SampleFolder) in Projets\carnet, close to the one boards 12.8a to 12.8c
// draw: two context files, two skills, two MCP servers, scripts and the
// standard files of a repository, citing one another so the panel's graph
// has links both ways and more than four to count as "+N". Made from nothing
// and dated, so two runs photograph the same thing. Kept apart from
// SampleFolder so the tools built in parallel each add their own part.
public static class MapSampleFolder
{
    public const string RelativePath = @"Projets\carnet";

    private static readonly DateTime Base = new(2026, 11, 9, 18, 0, 0, DateTimeKind.Local);

    // Writes <sampleRoot>\Projets\carnet, replacing what a previous run left,
    // and returns its path.
    public static string Create(string sampleRoot)
    {
        string root = Path.Combine(sampleRoot, RelativePath);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);

        Write(root, "CLAUDE.md", -1, """
            # Carnet

            Un carnet de notes en Markdown, publié en site statique.

            @AGENTS.md

            ## Publier

            Une version se publie avec /publier-version, jamais à la main.
            Les captures d'écran du site passent par la skill `capture-ecran`.

            ## Règles

            - Écrire en français, phrases courtes.
            - Ne jamais pousser sur main sans build vert.
            - Lire le [README](README.md) avant de toucher à la structure.
            """);

        Write(root, "AGENTS.md", -8, """
            # Agents

            Construire avec `scripts/build.ps1`, puis vérifier les liens.
            Les notes privées se cherchent par le serveur `notes-locales`.
            Voir [CONTRIBUTING](CONTRIBUTING.md) pour les conventions.
            """);

        Write(root, Path.Combine(".claude", "skills", "publier-version", "SKILL.md"), 0, """
            ---
            name: publier-version
            description: Monte la version et publie une release.
            ---

            # Publier une version

            1. Construire avec `build.ps1`.
            2. Lancer [release](../../../scripts/release.ps1).
            3. Chercher les notes de version avec `notes-locales`.
            4. Revenir au [CLAUDE.md](../../../CLAUDE.md) pour les règles.
            5. Prévenir par le serveur `navigateur` que le site a changé.
            """);

        Write(root, Path.Combine(".claude", "skills", "capture-ecran", "SKILL.md"), -19, """
            ---
            name: capture-ecran
            description: Prend une capture du site publié.
            ---

            Ouvrir le site avec mcp__navigateur, puis lancer scripts/capture.ps1.
            """);

        Write(root, ".mcp.json", -41, """
            {
              "mcpServers": {
                "navigateur": { "command": "npx", "args": ["@exemple/navigateur"] },
                "notes-locales": { "command": "notes-locales", "args": ["--dossier", "notes"] }
              }
            }
            """);

        Write(root, Path.Combine("scripts", "build.ps1"), -2, """
            # Construit le site dans out/.
            param([switch]$Propre)
            if ($Propre) { Remove-Item out -Recurse -Force -ErrorAction SilentlyContinue }
            New-Item -ItemType Directory -Force out | Out-Null
            Get-ChildItem notes -Filter *.md | ForEach-Object { Copy-Item $_.FullName out }
            Write-Host "Site construit."
            """);
        Write(root, Path.Combine("scripts", "release.ps1"), 0, """
            # Publie une version : construit, étiquette, pousse.
            param([Parameter(Mandatory)][string]$Version)
            & $PSScriptRoot/build.ps1 -Propre
            git tag "v$Version"
            Write-Host "Version $Version prête."
            """);
        Write(root, Path.Combine("scripts", "capture.ps1"), -19, """
            # Capture la page d'accueil du site.
            Write-Host "Capture enregistrée."
            """);

        Write(root, "README.md", -12, """
            # Carnet

            Mes notes, publiées en site statique.

            Construire : `scripts/build.ps1`. Contribuer : lire [CONTRIBUTING](CONTRIBUTING.md).
            """);
        Write(root, "CONTRIBUTING.md", -250, """
            # Contribuer

            Une note par fichier, en Markdown. Le [README](README.md) dit comment construire.
            """);
        Write(root, "LICENSE", -312, "MIT License\n\nCopyright (c) 2026\n");

        foreach (string folder in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
            Directory.SetLastWriteTime(folder, Base);
        Directory.SetLastWriteTime(root, Base);
        return root;
    }

    private static void Write(string root, string relative, int days, string content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n") + "\n");
        File.SetLastWriteTime(path, Base.AddDays(days));
    }
}
