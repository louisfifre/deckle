namespace Deckle.Atelier.Tests;

// A small repository written by the test, close to the one boards 12.8a to
// 12.8c draw: two context files, two skills, two servers, scripts and the
// standard files, plus what the walk must leave out.
internal static class MapSampleRepository
{
    public static void Write(ScratchFolder scratch)
    {
        scratch.File("""
            # Carnet
            @AGENTS.md
            Release with /publier-version. Keep `@README.md` literal.
            """, "CLAUDE.md");
        scratch.File("""
            Build with `scripts/build.ps1`.
            See [the README](README.md).
            """, "AGENTS.md");
        scratch.File("# Carnet\nRun scripts/build.ps1, then read [CONTRIBUTING](CONTRIBUTING.md).\n", "README.md");
        scratch.File("Thanks.\n", "CONTRIBUTING.md");
        scratch.File("MIT\n", "LICENSE");
        scratch.File("Not a standard file here.\n", "src", "CONTRIBUTING.md");

        scratch.File("""
            ---
            name: publier-version
            description: Monte la version et publie une release.
            ---
            Run [release](../../../scripts/release.ps1), after `build.ps1`.
            Search notes with `notes-locales`.
            Back to [CLAUDE.md](../../../CLAUDE.md).
            """, ".claude", "skills", "publier-version", "SKILL.md");
        scratch.File("Then call scripts/capture.ps1.\n", ".claude", "skills", "publier-version", "references", "steps.md");
        scratch.File("Takes a screenshot.\n", ".claude", "skills", "capture-ecran", "SKILL.md");

        scratch.File("""
            {
              "mcpServers": {
                "navigateur": { "command": "npx", "args": ["--key", "secret"] },
                "notes-locales": { "url": "http://localhost:3000/mcp?key=secret" }
              }
            }
            """, ".mcp.json");

        scratch.File("& $PSScriptRoot/build.ps1\n", "scripts", "release.ps1");
        scratch.File("dotnet build\n", "scripts", "build.ps1");
        scratch.File("Write-Host capture\n", "scripts", "capture.ps1");
        scratch.File("import sys\nif __name__ == \"__main__\":\n    sys.exit(0)\n", "tools", "check-links.py");
        scratch.File("def helper():\n    return 1\n", "lib", "helper.py");
        scratch.File("#!/usr/bin/env node\nconsole.log(1)\n", "tools", "run.js");

        scratch.File("A guide, not a thing to use.\n", "docs", "guide.md");
        scratch.File("Always.\n", ".claude", "rules", "style.md");
        scratch.File("---\npaths:\n  - \"src/api/**\"\n---\nOnly for the API.\n", ".claude", "rules", "api.md");
        scratch.File("Read when working in src.\n", "src", "CLAUDE.md");

        scratch.File("[core]\n", ".git", "config");
        scratch.File("# a package\n", "node_modules", "left-pad", "README.md");
        scratch.File("dotnet\n", "bin", "Debug", "copied.ps1");
        scratch.File("dotnet\n", "obj", "generated.ps1");
    }
}
