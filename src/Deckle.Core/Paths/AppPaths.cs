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
//   • recovery/     — the sealed raw text of a dictation in progress,
//                     present only while a take runs or after a crash
//   • telemetry/    — JSONL files (latency, microphone), raw-capture
//                     subfolders, and per-profile corpus
//   • diagnostics/  — local logs (setup, errors, trace.jsonl); never transmitted
//   • diagnostics/dumps/ — minidumps of fatal runtime errors, newest few kept
//   • models/       — Whisper ggml-*.bin and the other downloaded models
//   • native/       — whisper.dll, ggml*.dll and the C++ runtime (NativeRuntime)
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
// wizard or scripts/setup/setup-assets.ps1 populates
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
    // Holds dictation.dat while a take runs; created by the store on its first write.
    public static string RecoveryDirectory            { get; }
    public static string DictationRecoveryFilePath    { get; }
    public static string TelemetryDirectory           { get; }
    public static string TrackpadTelemetryDirectory   { get; }
    public static string MouseWheelTelemetryDirectory { get; }
    public static string DiagnosticsDirectory         { get; }
    // Minidumps createdump writes on a fatal runtime error; created by the
    // host's entry point before it arms the capture, not here.
    public static string CrashDumpsDirectory          { get; }
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
        if (!ModuleDirectoryName.IsSingleSegment(moduleId))
            throw new ArgumentException("Module id must be a single folder name.", nameof(moduleId));

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
        RecoveryDirectory            = Path.Combine(UserDataRoot, "recovery");
        DictationRecoveryFilePath    = Path.Combine(RecoveryDirectory, "dictation.dat");
        TelemetryDirectory           = Path.Combine(UserDataRoot, "telemetry");
        TrackpadTelemetryDirectory   = Path.Combine(TelemetryDirectory, "trackpad");
        MouseWheelTelemetryDirectory = Path.Combine(TelemetryDirectory, "mouse-wheel");
        DiagnosticsDirectory         = Path.Combine(UserDataRoot, "diagnostics");
        CrashDumpsDirectory          = Path.Combine(DiagnosticsDirectory, "dumps");
        ModelsDirectory            = Path.Combine(UserDataRoot, "models");
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

        LegacyLayoutMigrationFailure = MigrateLegacySettingsLayout(
            UserDataRoot, SettingsFilePath, SettingsBackupDirectory);
    }

    // Why the legacy settings layout could not be moved, or null when it was
    // moved or there was nothing to move. This class runs in a static
    // constructor, before any log sink exists and below the module that could
    // write one, so it keeps the failure for the host to report once its sinks
    // are installed instead of swallowing it.
    public static Exception? LegacyLayoutMigrationFailure { get; }

    // Moves a legacy <UserDataRoot>\settings\ subfolder (settings.json and
    // backups\) to the root layout. No-op once done. A failure does not stop
    // the app from booting against the new layout; it is returned so it can be
    // reported, because settings left in the old place read as defaults.
    internal static Exception? MigrateLegacySettingsLayout(
        string userDataRoot,
        string settingsFilePath,
        string settingsBackupDirectory)
    {
        try
        {
            string legacyDir  = Path.Combine(userDataRoot, "settings");
            string legacyFile = Path.Combine(legacyDir, "settings.json");

            if (File.Exists(legacyFile) && !File.Exists(settingsFilePath))
                File.Move(legacyFile, settingsFilePath);

            string legacyBackups = Path.Combine(legacyDir, "backups");
            if (Directory.Exists(legacyBackups) && !Directory.Exists(settingsBackupDirectory))
                Directory.Move(legacyBackups, settingsBackupDirectory);

            if (Directory.Exists(legacyDir) &&
                Directory.GetFileSystemEntries(legacyDir).Length == 0)
                Directory.Delete(legacyDir);

            return null;
        }
        catch (Exception ex)
        {
            return ex;
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
