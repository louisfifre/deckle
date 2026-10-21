using System.Diagnostics;

namespace Deckle.Orchestration;

// One app with a window open, as offered by the "Add running app" dialog.
public sealed record RunningApp(
    string DisplayName,
    string WindowTitle,
    string ProcessName,
    string? ExecutablePath);

// A one-shot snapshot of the apps that own a main window. Called on demand
// from the dialog, on a worker thread; never on a timer.
internal static class RunningApps
{
    public static IReadOnlyList<RunningApp> Enumerate()
    {
        int self = Environment.ProcessId;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<RunningApp>();

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == self || process.MainWindowHandle == 0) continue;

                    string name = process.ProcessName;
                    string? path = ProcessNativeMethods.TryGetImagePath(process.Id);

                    // Several processes of one program (browsers, launchers)
                    // are one app to the user.
                    if (!seen.Add(path ?? name)) continue;

                    string title = process.MainWindowTitle;
                    apps.Add(new RunningApp(
                        AppDisplayName.For(path, title, name), title, name, path));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The process exited or refused a query between listing and
                    // reading: it is simply not offered.
                    DeckleOrchestrationSource.Log.ProcessQuerySkipped(
                        "running_apps", process.Id, ex.GetType().Name, ex.Message);
                }
            }
        }

        apps.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName));
        return apps;
    }
}
