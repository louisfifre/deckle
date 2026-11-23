using System.ComponentModel;
using System.Net;
using System.Net.Http;

namespace Deckle.Install;

// The sentence a person reads in the stub's message box when the install fails.
// Lives here, below the stub, so the mapping is testable without a window. The
// stub is a NativeAOT Win32 program with no resource loader, so its text is
// inline, as for every other message it shows.
public static class InstallFailureText
{
    // Win32 codes Process.Start raises when Windows refuses to run the wizard.
    private const int ErrorAccessDeniedByPolicy = 1260; // ERROR_ACCESS_DISABLED_BY_POLICY
    private const int ErrorVirusInfected = 225;         // ERROR_VIRUS_INFECTED, also an antivirus block

    // The sentence shown when the installer refuses the machine, with the reason in plain words.
    public static string For(MachineRefusal refusal) => refusal switch
    {
        MachineRefusal.UnsupportedArchitecture =>
            "Deckle does not run on this PC.\n\n" +
            "It needs a 64-bit Intel or AMD processor (x64). ARM PCs are not supported, including with x64 emulation.",
        MachineRefusal.MissingAvx2 =>
            "Deckle does not run on this PC.\n\n" +
            "Its speech recognition needs a processor with AVX2, and this one does not have it.",
        MachineRefusal.WindowsTooOld =>
            "Deckle does not run on this version of Windows.\n\n" +
            "It needs Windows 11. Windows 10 is not supported.",
        _ => string.Empty,
    };

    public static string For(Exception ex) => ex switch
    {
        Win32Exception { NativeErrorCode: ErrorAccessDeniedByPolicy or ErrorVirusInfected } =>
            "Windows blocked Deckle Setup from starting.\n\n" +
            "SmartScreen or your antivirus stopped it because Deckle is not signed yet. " +
            "If you trust this download, allow it (More info, then Run anyway, or an exception in your antivirus) and run Setup again.",
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } =>
            "No published Deckle release was found on GitHub.\n\nTry again later.",
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests } =>
            "GitHub refused the request.\n\nTry again in a little while.",
        HttpRequestException =>
            "Could not reach GitHub to download Deckle.\n\nCheck your internet connection and try again.",
        TimeoutException =>
            "The download stopped receiving data.\n\nCheck your internet connection and try again.",
        _ => "Deckle Setup could not complete:\n\n" + ex.Message,
    };
}
