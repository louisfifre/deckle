using System;
using System.IO;

namespace Deckle.Core;

// ── AppPaths ───────────────────────────────────────────────────────────────
//
// Centralized path resolution. Single source of truth for where the app
// reads and writes user data on disk. All mutable per-user state lives
// under <UserDataRoot>:
//
//   • settings.json — the shell's settings file, at the root
//   • secrets.dat   — Deckle.Security vault, DPAPI-sealed single file
//   • backups/      — settings backup snapshots
//   • telemetry/    — JSONL files (latency, microphone), raw-capture
//                     subfolders, and per-profile corpus
//   • diagnostics/  — local logs (setup, errors, app.jsonl); never transmitted
//   • models/       — Whisper ggml-*.bin and the other downloaded models
//   • native/       — libwhisper.dll, ggml*.dll
//   • benchmark/    — optional
//   • modules/      — one folder per module (GetModuleDirectory)
//
// Default <UserDataRoot> = %LOCALAPPDATA%\<AppFolderName>\, the canonical
// per-user data root on Windows (Settings Win11, PowerToys, every
// first-party Microsoft desktop app). The DECKLE_DATA_ROOT env var
// overrides it.
//
// The application binary itself stays read-only and Program Files-friendly:
// it ships with Assets but no models, no native DLLs, no config. The setup
// wizard or scripts/commands/setup/setup-assets.ps1 populates
// <UserDataRoot>\models\ and \native\.
public static class AppPaths
{
    public const string AppFolderName = "Deckle";

    // Serializes settings writes across concurrent app instances.
    public const string SettingsMutexName = $"{AppFolderName}-Settings-Save";

    // Set to a dev folder, or to the custom data root the installer records
    // as a user variable. Empty/unset → default location.
    public const string DataRootEnvVar = "DECKLE_DATA_ROOT";

    // The root the app uses when DECKLE_DATA_ROOT is unset, the one the data
    // folder's reset moves back to. DefaultDataRoot computes it, and the
    // installer reads that same source through InstallPaths.
    public static string DefaultUserDataRoot          { get; }
    public static string UserDataRoot                 { get; }
    public static string SettingsFilePath             { get; }
    public static string SecretsFilePath              { get; }
    public static string SettingsBackupDirectory      { get; }
    public static string TelemetryDirectory           { get; }
    public static string TrackpadTelemetryDirectory   { get; }
    public static string MouseWheelTelemetryDirectory { get; }
    public static string DiagnosticsDirectory         { get; }
    public static string ModelsDirectory              { get; }
    public static string NativeDirectory              { get; }
    public static string BenchmarkDirectory           { get; }
    public static string ModulesDirectory             { get; }

    /// <summary>
    /// Returns the per-module data directory
    /// <c>&lt;UserDataRoot&gt;\modules\&lt;moduleId&gt;\</c>, which keeps each
    /// module's files self-contained.
    /// </summary>
    /// <param name="moduleId">
    /// Stable filesystem-safe identifier of the module. Convention: lowercase,
    /// ASCII, no spaces.
    /// </param>
    public static string GetModuleDirectory(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            throw new ArgumentException("Module id must not be empty.", nameof(moduleId));

        string dir = Path.Combine(ModulesDirectory, moduleId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    static AppPaths()
    {
        DefaultUserDataRoot          = DefaultDataRoot.Resolve();
        UserDataRoot                 = ResolveUserDataRoot();
        SettingsFilePath             = Path.Combine(UserDataRoot, "settings.json");
        SecretsFilePath              = Path.Combine(UserDataRoot, "secrets.dat");
        SettingsBackupDirectory      = Path.Combine(UserDataRoot, "backups");
        TelemetryDirectory           = Path.Combine(UserDataRoot, "telemetry");
        TrackpadTelemetryDirectory   = Path.Combine(TelemetryDirectory, "trackpad");
        MouseWheelTelemetryDirectory = Path.Combine(TelemetryDirectory, "mouse-wheel");
        DiagnosticsDirectory         = Path.Combine(UserDataRoot, "diagnostics");
        ModelsDirectory              = Path.Combine(UserDataRoot, "models");
        NativeDirectory              = Path.Combine(UserDataRoot, "native");
        BenchmarkDirectory           = Path.Combine(UserDataRoot, "benchmark");
        ModulesDirectory             = Path.Combine(UserDataRoot, "modules");

        // UserDataRoot, telemetry, and diagnostics are created eagerly — the
        // locations the app writes to from boot during normal operation.
        // diagnostics/ holds the always-on local setup/error logs, written
        // unconditionally before the user can opt into anything, so the folder
        // must exist at the very first emission. Models, native, and benchmark
        // are populated by the wizard or the user; creating them empty here
        // would mask the "missing dependencies" detection done by
        // NativeRuntime and SpeechModels. Backups dir is created on first write
        // by SettingsBackupService.
        Directory.CreateDirectory(UserDataRoot);
        Directory.CreateDirectory(TelemetryDirectory);
        Directory.CreateDirectory(TrackpadTelemetryDirectory);
        Directory.CreateDirectory(MouseWheelTelemetryDirectory);
        Directory.CreateDirectory(DiagnosticsDirectory);

        TryMigrateLegacySettingsLayout();
    }

    // Moves a legacy <UserDataRoot>\settings\ subfolder (settings.json and
    // backups\) to the root layout. No-op once done; failures are swallowed
    // so a quirky filesystem state doesn't bring the app down at start-up.
    private static void TryMigrateLegacySettingsLayout()
    {
        try
        {
            string legacyDir  = Path.Combine(UserDataRoot, "settings");
            string legacyFile = Path.Combine(legacyDir, "settings.json");

            if (File.Exists(legacyFile) && !File.Exists(SettingsFilePath))
                File.Move(legacyFile, SettingsFilePath);

            string legacyBackups = Path.Combine(legacyDir, "backups");
            if (Directory.Exists(legacyBackups) && !Directory.Exists(SettingsBackupDirectory))
                Directory.Move(legacyBackups, SettingsBackupDirectory);

            if (Directory.Exists(legacyDir) &&
                Directory.GetFileSystemEntries(legacyDir).Length == 0)
                Directory.Delete(legacyDir);
        }
        catch
        {
            // best-effort; the app keeps booting against the new layout
        }
    }

    private static string ResolveUserDataRoot()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable(DataRootEnvVar);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return Path.GetFullPath(overrideRoot);

        return DefaultUserDataRoot;
    }
}
