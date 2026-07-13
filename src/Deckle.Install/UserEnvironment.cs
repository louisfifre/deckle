namespace Deckle.Install;

// ── UserEnvironment ───────────────────────────────────────────────────────────
//
// Persists the data-root choice as the user-level DECKLE_DATA_ROOT variable the
// app already honours through AppPaths. Only written when the user picks a folder
// other than the app's own default — otherwise the app's built-in default stands
// and there's no variable to leave behind.
//
// SetEnvironmentVariable(..., User) writes HKCU\Environment and broadcasts
// WM_SETTINGCHANGE, so a newly launched Deckle.exe sees the value without a logoff.
public static class UserEnvironment
{
    public const string DataRootVariable = "DECKLE_DATA_ROOT";

    public static void SetDataRoot(string path) =>
        Environment.SetEnvironmentVariable(DataRootVariable, path, EnvironmentVariableTarget.User);

    public static string? GetDataRoot() =>
        Environment.GetEnvironmentVariable(DataRootVariable, EnvironmentVariableTarget.User);

    public static void ClearDataRoot() =>
        Environment.SetEnvironmentVariable(DataRootVariable, null, EnvironmentVariableTarget.User);
}
