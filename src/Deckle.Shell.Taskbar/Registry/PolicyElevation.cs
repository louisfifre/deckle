using System.ComponentModel;

namespace Deckle.Shell.Taskbar;

/// <summary>
/// What an elevated policy write ended in: the outcome the card reads, and the
/// label the log gets ("none", "exit_N", "uac_declined" or an exception type).
/// </summary>
public readonly record struct PolicyWriteResult(PolicyOutcome Outcome, string Error);

// Writes a policy value as administrator through reg.exe, for a process that is
// not elevated itself (brief-csharp-socle B9): HKCU\Software\Policies grants the
// user ReadKey only, so even a per-user policy needs the admin token (brief A4).
//
// The elevated reg.exe runs under the same user's admin token (split token), so
// its HKCU is the user's hive. If a standard user ever elevates with another
// account's credentials (over-the-shoulder), HKCU is that admin's hive and the
// write lands there; RecommendedSectionPolicy reads the value back after an
// exit code 0 to catch it.
//
// Nothing throws out of here: every outcome is a PolicyWriteResult, so the page
// never sees an exception.
public sealed class PolicyElevation(IElevatedProcessRunner runner)
{
    /// <summary>ERROR_CANCELLED: the user answered No to the UAC prompt.</summary>
    public const int UacDeclined = 1223;

    // reg.exe from System32 by full path, so no other reg.exe on the search
    // path or in the working directory runs with the admin token.
    private static string RegExePath => System.IO.Path.Combine(Environment.SystemDirectory, "reg.exe");

    public PolicyWriteResult SetDword(PolicyKey key, string valueName, uint value) =>
        Run($"add \"{key.CommandLineName}\" /v {valueName} /t REG_DWORD /d {value} /f");

    public PolicyWriteResult DeleteValue(PolicyKey key, string valueName) =>
        Run($"delete \"{key.CommandLineName}\" /v {valueName} /f");

    private PolicyWriteResult Run(string arguments)
    {
        try
        {
            int exitCode = runner.Run(RegExePath, arguments);
            return new PolicyWriteResult(OutcomeOf(exitCode), exitCode == 0 ? "none" : $"exit_{exitCode}");
        }
        catch (Exception ex)
        {
            return new PolicyWriteResult(OutcomeOf(ex), ErrorOf(ex));
        }
    }

    /// <summary>
    /// reg.exe exits 0 on success and 1 on any failure; an elevated reg.exe
    /// has the rights it needs, so a non-zero code is an unexpected failure,
    /// not a refusal.
    /// </summary>
    public static PolicyOutcome OutcomeOf(int exitCode) =>
        exitCode == 0 ? PolicyOutcome.Applied : PolicyOutcome.Failed;

    /// <summary>
    /// A declined UAC prompt is Windows refusing permission; anything else
    /// (reg.exe missing, ShellExecute failing, a hung process) is a failure.
    /// </summary>
    public static PolicyOutcome OutcomeOf(Exception exception) =>
        exception is Win32Exception { NativeErrorCode: UacDeclined }
            ? PolicyOutcome.PermissionDenied
            : PolicyOutcome.Failed;

    private static string ErrorOf(Exception exception) =>
        exception is Win32Exception { NativeErrorCode: UacDeclined }
            ? "uac_declined"
            : exception.GetType().Name;
}