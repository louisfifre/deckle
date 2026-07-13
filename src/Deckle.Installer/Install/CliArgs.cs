namespace Deckle.Installer;

// ── CliArgs ───────────────────────────────────────────────────────────────────
//
// The stub is a double-click GUI app first: with no arguments it downloads the
// latest Deckle and hands off to the first-run wizard. The only flags are for the
// uninstaller path the wizard registers — it re-invokes this same exe with
// --uninstall — and -y/--yes for the quiet uninstall the Installed-apps
// QuietUninstallString drives.
//
// Folder choice used to live here (--install-dir / --data-dir); it moved into the
// WinUI wizard, so the stub no longer parses it.
internal sealed record CliArgs(bool Uninstall, bool AssumeYes)
{
    public static CliArgs Parse(string[] args)
    {
        bool uninstall = false, yes = false;

        foreach (string arg in args)
        {
            switch (arg.ToLowerInvariant())
            {
                case "--uninstall":
                    uninstall = true;
                    break;
                case "-y":
                case "--yes":
                    yes = true; // skip the uninstall prompts, keep data
                    break;
            }
        }

        return new CliArgs(uninstall, yes);
    }
}
