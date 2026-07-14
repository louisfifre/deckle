using System.Diagnostics;
using Deckle.Install;

namespace Deckle.Setup;

internal sealed class UserDataRootSelection : IDataRootSelection
{
    public string? Capture() => UserEnvironment.GetDataRoot();

    public void Select(string target)
    {
        if (PathsEqual(target, InstallPaths.DefaultDataDir)) UserEnvironment.ClearDataRoot();
        else UserEnvironment.SetDataRoot(target);
    }

    public void Restore(string? previous)
    {
        if (previous is null) UserEnvironment.ClearDataRoot();
        else UserEnvironment.SetDataRoot(previous);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}

internal sealed class DeckleDataRootLauncher : IDataRootLauncher
{
    public void Launch(string target, string source)
    {
        string executable = Environment.ProcessPath!;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? "",
        };
        start.Environment[UserEnvironment.DataRootVariable] = target;
        start.ArgumentList.Add("--cleanup-data");
        start.ArgumentList.Add(source);

        if (Process.Start(start) is null)
            throw new InvalidOperationException("The relocated Deckle process could not be started.");
    }
}
