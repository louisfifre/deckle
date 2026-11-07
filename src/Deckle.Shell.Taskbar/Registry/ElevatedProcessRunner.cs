using System.Diagnostics;

namespace Deckle.Shell.Taskbar;

// The ElevatedStartupService pattern (src/Deckle.Shell): ShellExecute with the
// "runas" verb, which shows the UAC prompt before the program starts. Start
// blocks until the prompt is answered; a declined prompt throws a
// Win32Exception whose NativeErrorCode is 1223 (ERROR_CANCELLED), which
// PolicyElevation maps. .NET runs ShellExecute on an STA thread of its own when
// the caller is MTA, so this works from a timer or thread-pool thread.
//
// Hidden window: the elevated program is a console tool, and SW_HIDE is passed
// through to it. ElevatedStartupService says a runas window cannot be hidden
// and accepts the flash; whether SW_HIDE suppresses reg.exe's console here is
// unverified on the machine, and a brief flash is the worst case.
public sealed class ElevatedProcessRunner : IElevatedProcessRunner
{
    // reg.exe answers in milliseconds; this only bounds a hung process, so a
    // pass never blocks forever. The UAC prompt itself is not timed: Start
    // returns once it is answered.
    private static readonly TimeSpan _exitTimeout = TimeSpan.FromSeconds(30);

    public int Run(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName        = fileName,
                Arguments       = arguments,
                UseShellExecute = true,
                Verb            = "runas",
                WindowStyle     = ProcessWindowStyle.Hidden,
            },
        };
        if (!process.Start())
            throw new InvalidOperationException("ShellExecute started no process.");
        if (!process.WaitForExit(_exitTimeout))
            throw new TimeoutException($"The elevated process did not exit within {_exitTimeout.TotalSeconds} s.");
        return process.ExitCode;
    }
}
