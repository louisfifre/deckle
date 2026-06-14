namespace Deckle.Installer;

// ── CliArgs ───────────────────────────────────────────────────────────────────
//
// The installer is a double-click console app first: with no arguments it runs
// the interactive flow and prompts for the folders. Flags exist for the advanced
// / scripted path (and for the uninstaller entry the installer registers, which
// re-invokes the same exe with --uninstall).
//
// Raw flags only — default resolution and prompting live in the flow, so the two
// concerns stay separated (parse is pure, the flow owns the interaction).
internal sealed record CliArgs(
    bool Uninstall,
    bool AssumeYes,
    string? InstallDir,
    string? DataDir)
{
    public static CliArgs Parse(string[] args)
    {
        bool uninstall = false, yes = false;
        string? installDir = null, dataDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--uninstall":
                    uninstall = true;
                    break;
                case "-y":
                case "--yes":
                    yes = true; // accept defaults, no prompts
                    break;
                case "--install-dir":
                    if (i + 1 < args.Length) installDir = args[++i];
                    break;
                case "--data-dir":
                    if (i + 1 < args.Length) dataDir = args[++i];
                    break;
            }
        }

        return new CliArgs(uninstall, yes, installDir, dataDir);
    }
}
