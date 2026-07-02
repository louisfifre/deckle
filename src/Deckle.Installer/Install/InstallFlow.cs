using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;

namespace Deckle.Installer;

// ── InstallFlow ───────────────────────────────────────────────────────────────
//
// Recap → one keystroke → unattended run. Everything is resolved before anything
// is asked (system check, latest release, folder defaults), so the consent screen
// states exactly what will happen — version, download size, both folders. Enter
// is the whole fast path; C reopens the folders; after consent the machine runs
// without questions. The only progress bar is the download's, driven by actual
// bytes, and ticks appear as real work completes — no phantom progress.
//
// What this does NOT do: provision the whisper.cpp native runtime or the speech
// models. Those are the app's first-run wizard's job (auto-download, per user).
// The installer's contract is narrow: place the app, integrate it, launch it.
internal static class InstallFlow
{
    public static async Task<int> RunAsync(CliArgs cli, CancellationToken ct)
    {
        ConsoleUi.Banner("Installer");

        // ── Resolve everything before asking anything ─────────────────────────────
        if (!Environment.Is64BitOperatingSystem)
        {
            ConsoleUi.Error("Deckle requires 64-bit Windows.");
            return 1;
        }

        ReleaseResolver.ResolvedRelease release;
        try
        {
            release = await ReleaseResolver.ResolveLatestAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            ConsoleUi.Error($"Could not reach GitHub to find the latest release — check your connection. ({ex.Message})");
            return 1;
        }

        // A previous install is recognised from its Installed-apps registration —
        // the run then reads as an update and starts from the folders that install
        // actually uses, not from pristine defaults.
        UninstallEntry.ExistingInstall? existing = UninstallEntry.Read();
        string version = BareVersion(release.Tag);

        // A previous install may have moved the data root: the variable, not the
        // hardcoded default, is where the app actually reads — the recap must show
        // that reality, and Enter must preserve it.
        string? existingDataRoot = UserEnvironment.GetDataRoot();
        string? existingInstallDir = existing is null ? null : Path.GetFullPath(existing.InstallDir);

        string? installDirNote = null;
        string installDir = cli.InstallDir is { } requestedInstallDir
            ? Path.GetFullPath(requestedInstallDir)
            : ResolveInitialInstallDir(existingInstallDir, out installDirNote);
        string dataDir = Path.GetFullPath(cli.DataDir ?? existingDataRoot ?? InstallPaths.DefaultDataDir);

        // ── Recap + single-keystroke consent ──────────────────────────────────────
        // The recap re-prints after each folder edit, so what Enter commits to is
        // always the block on screen.
        bool interactive = !cli.AssumeYes && !Console.IsInputRedirected;
        string enterVerb = existing is null ? "installs" : existing.Version == version ? "reinstalls" : "updates";
        bool createStartMenuShortcut = true;
        while (true)
        {
            Recap(release, existing, installDir, installDirNote, dataDir, createStartMenuShortcut);
            if (!interactive) break;
            ConsoleUi.Hint($"Enter {enterVerb} · C folders · S shortcut · Ctrl+C cancels");
            ConsoleKey key = ConsoleUi.WaitKey(ConsoleKey.Enter, ConsoleKey.C, ConsoleKey.S);
            if (key == ConsoleKey.Enter) break;
            if (key == ConsoleKey.S)
            {
                createStartMenuShortcut = !createStartMenuShortcut;
                Console.WriteLine();
                continue;
            }

            Console.WriteLine();
            installDir = Path.GetFullPath(ConsoleUi.PromptPath("App folder", installDir));
            installDirNote = null;
            dataDir = Path.GetFullPath(ConsoleUi.PromptPath("Data folder", dataDir));
            Console.WriteLine();
        }

        // ── Unattended run ────────────────────────────────────────────────────────
        ConsoleUi.Phase(existing is null ? "Installing" : "Updating");
        string tempDir = Path.Combine(Path.GetTempPath(), "Deckle-Installer");
        Directory.CreateDirectory(tempDir);
        string zipPath = Path.Combine(tempDir, $"Deckle-{release.Tag}.zip");

        string expectedSha = ParseSha256Sidecar(await Downloader.GetStringAsync(release.Sha256Url, ct));
        string actualSha = await Downloader.DownloadAsync(release.ZipUrl, zipPath, showProgress: true, ct);
        if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            ConsoleUi.Error($"Checksum mismatch — expected {expectedSha}, got {actualSha}.");
            TryDelete(zipPath);
            return 1;
        }

        // Windows locks an exe's image while it runs: replacing binaries under a
        // live Deckle would fault mid-extraction and leave a mixed install. Gate
        // here — close-and-retry when someone can answer, a clean error otherwise.
        while (true)
        {
            string[] running = RunningDeckleProcesses(installDir, existingInstallDir);
            if (running.Length == 0) break;
            string names = string.Join(", ", running);
            if (!interactive)
            {
                ConsoleUi.Error($"Deckle is running ({names}) — close it and re-run the installer.");
                TryDelete(zipPath);
                return 1;
            }
            ConsoleUi.Warn($"Deckle is running ({names}) — close it, then press Enter to retry.");
            ConsoleUi.WaitKey(ConsoleKey.Enter);
        }

        Directory.CreateDirectory(installDir);
        CleanInstallFolder(installDir);
        ZipFile.ExtractToDirectory(zipPath, installDir, overwriteFiles: true);
        TryDelete(zipPath);
        string uninstallerPath = CopySelfAsUninstaller(installDir);
        ConsoleUi.Ok($"verified (SHA-256) and unpacked — {Mb(DirectorySize(installDir)):0} MB");

        string appExe = Path.Combine(installDir, "Deckle.exe");
        if (createStartMenuShortcut)
        {
            Shortcut.CreateStartMenu(appExe, "Deckle", "Deckle");
        }
        else
        {
            Shortcut.RemoveStartMenu("Deckle");
        }

        UninstallEntry.Write(installDir, version, uninstallerPath, DirectorySize(installDir));
        ConsoleUi.Ok(createStartMenuShortcut ? "Start Menu · Installed apps" : "Installed apps · no Start Menu shortcut");

        // The variable exists only while the data folder is off the default: set it
        // on a non-default choice, clear it when the user comes back to the default
        // (previously the stale variable silently overrode that choice), and leave
        // no trace on a default-to-default run.
        if (!PathsEqual(dataDir, InstallPaths.DefaultDataDir))
        {
            UserEnvironment.SetDataRoot(dataDir);
            ConsoleUi.Ok($"DECKLE_DATA_ROOT = {dataDir}");
        }
        else if (existingDataRoot is not null)
        {
            UserEnvironment.ClearDataRoot();
            ConsoleUi.Ok("DECKLE_DATA_ROOT cleared — data folder back to the default");
        }

        // Moving the root does not move the files: the app will start fresh at the
        // new location (and re-download models). Say so instead of letting the old
        // folder look silently lost.
        string previousRoot = Path.GetFullPath(existingDataRoot ?? InstallPaths.DefaultDataDir);
        if (!PathsEqual(previousRoot, dataDir) && Directory.Exists(previousRoot))
            ConsoleUi.Info($"data folder changed — existing files stay at {previousRoot} and are not moved");
        if (existingInstallDir is not null && !PathsEqual(existingInstallDir, installDir) && Directory.Exists(existingInstallDir))
            ConsoleUi.Info($"app folder changed — existing files stay at {existingInstallDir} and are not moved");

        Process.Start(new ProcessStartInfo(appExe) { UseShellExecute = true, WorkingDirectory = installDir });
        Console.WriteLine();
        ConsoleUi.Success(existing is null ? "Deckle is installed and running." : "Deckle is up to date and running.");
        // The provisioning note only concerns a first install — an update keeps the
        // data folder, so the app comes back already provisioned.
        if (existing is null)
            ConsoleUi.Info("First launch downloads the speech runtime and a model — follow the setup window.");
        return 0;
    }

    // The consent screen: what will happen (install, update or reinstall), how
    // heavy the download is, where the two folders land, and why the data folder
    // is the one worth moving.
    private static void Recap(
        ReleaseResolver.ResolvedRelease release,
        UninstallEntry.ExistingInstall? existing,
        string installDir,
        string? installDirNote,
        string dataDir,
        bool createStartMenuShortcut)
    {
        string terms = release.ZipSize > 0
            ? $"{Mb(release.ZipSize):0} MB download, no admin"
            : "no admin";
        string headline = existing is null
            ? $"Deckle {release.Tag} — ready to install ({terms})"
            : existing.Version == BareVersion(release.Tag)
                ? $"Deckle {release.Tag} — ready to reinstall ({terms})"
                : $"Deckle v{existing.Version} → {release.Tag} — ready to update ({terms})";
        ConsoleUi.Headline(headline);
        Console.WriteLine();
        ConsoleUi.Row("App", installDir);
        if (!string.IsNullOrWhiteSpace(installDirNote)) ConsoleUi.RowNote(installDirNote);
        ConsoleUi.Row("Data", dataDir);
        ConsoleUi.RowNote("speech models live here and can reach ~3 GB");
        ConsoleUi.Row("Shortcut", createStartMenuShortcut ? "Start Menu" : "none");
        Console.WriteLine();
    }

    // "v0.7.1" → "0.7.1" — the tag with its leading v dropped, as the registry and
    // the recap compare it.
    private static string BareVersion(string tag) => tag.StartsWith('v') ? tag[1..] : tag;

    private static string ResolveInitialInstallDir(string? existingInstallDir, out string? note)
    {
        note = null;
        if (existingInstallDir is null) return Path.GetFullPath(InstallPaths.DefaultInstallDir);

        if (!Directory.Exists(existingInstallDir))
        {
            note = "previous app folder was not found; using the default";
            return Path.GetFullPath(InstallPaths.DefaultInstallDir);
        }

        return existingInstallDir;
    }

    // Empties the install folder before extraction, so files a newer version
    // renamed or dropped never linger beside the fresh payload. Safe by
    // construction: user data lives in the separate data folder. The one spared
    // entry is this very exe when the installed stub is the process running the
    // update — a process cannot delete its own image; that stub then simply stays
    // in place as the registered uninstaller.
    private static void CleanInstallFolder(string installDir)
    {
        // Only a folder that actually holds Deckle binaries gets cleaned: stale
        // files only exist over a previous install, and the guard keeps a mistyped
        // custom folder (Documents, a drive root) from being emptied.
        if (!File.Exists(Path.Combine(installDir, "Deckle.exe"))) return;

        string? self = Environment.ProcessPath;
        foreach (string entry in Directory.EnumerateFileSystemEntries(installDir))
        {
            if (self is not null && PathsEqual(entry, self)) continue;
            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
            else File.Delete(entry);
        }
    }

    // The .sha256 sidecar is `<hex> *<filename>` (sha256sum -c format). Take the hex.
    private static string ParseSha256Sidecar(string content)
    {
        string first = content.Trim().Split(' ', '\t', '\n', '\r')[0];
        return first.ToLowerInvariant();
    }

    // Copies the running installer into the install folder as the uninstaller. When
    // the installer is re-run from inside the folder, source and dest coincide —
    // skip the copy rather than fault on a same-file copy.
    private static string CopySelfAsUninstaller(string installDir)
    {
        string self = Environment.ProcessPath
            ?? throw new InvalidOperationException("cannot resolve the installer's own path.");
        string dest = Path.Combine(installDir, "Deckle-Installer.exe");
        if (!PathsEqual(self, dest)) File.Copy(self, dest, overwrite: true);
        return dest;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    private static string[] RunningDeckleProcesses(string installDir, string? existingInstallDir)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in InstallFoldersToCheck(installDir, existingInstallDir))
        {
            foreach (string name in RunningProcesses.FromFolder(folder))
                names.Add(name);
        }

        return names.ToArray();
    }

    private static IEnumerable<string> InstallFoldersToCheck(string installDir, string? existingInstallDir)
    {
        yield return installDir;
        if (existingInstallDir is not null && !PathsEqual(existingInstallDir, installDir))
            yield return existingInstallDir;
    }

    private static long DirectorySize(string dir) =>
        new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    private static double Mb(long bytes) => bytes / (1024.0 * 1024.0);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
