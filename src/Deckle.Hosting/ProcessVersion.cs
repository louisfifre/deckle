using System.Diagnostics;

namespace Deckle.Hosting;

// The version of the running build as every executable of the suite shows it:
// the product version of its exe, with the "+commit" suffix the SDK appends
// dropped, so it is the bare number the tag, the changelog and the update
// check use.
public static class ProcessVersion
{
    public static string Display()
    {
        if (Environment.ProcessPath is not { } exe) return "";
        return Trim(FileVersionInfo.GetVersionInfo(exe).ProductVersion);
    }

    // Pure, for a test: "0.141.0+526d0375" is "0.141.0".
    public static string Trim(string? productVersion)
    {
        if (string.IsNullOrWhiteSpace(productVersion)) return "";
        int plus = productVersion.IndexOf('+');
        return plus > 0 ? productVersion[..plus] : productVersion;
    }
}
