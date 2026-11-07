namespace Deckle.Shell.Taskbar;

/// <summary>
/// Runs a program as administrator and returns its exit code. The UAC prompt
/// comes from here; a declined prompt surfaces as the exception
/// <see cref="ElevatedProcessRunner"/> documents. Tests replace it with a fake
/// that never elevates.
/// </summary>
public interface IElevatedProcessRunner
{
    int Run(string fileName, string arguments);
}
