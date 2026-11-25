using Deckle.App.SelfCheck;
using Deckle.Hosting.SelfCheck;
using WinRT.Interop;

namespace Deckle.App;

// ── Self-check: the icon each window gives the taskbar ───────────────────────
//
// The taskbar showed the generic icon on every Deckle window while the title
// bars showed the mark (ExeIconBugNotes.md): nothing in a run looked at what
// the taskbar is handed. After the walk, each window with a taskbar button is
// asked for its icon, and Deckle.exe for its own; each becomes an image under
// icons/<name>, and one that is missing fails the run. Paste to folder has no
// taskbar button and is left out.
public partial class App
{
    // The size the shell draws a taskbar button's icon at 150 % scale, so the
    // exe's frame is read at a size the taskbar uses.
    private const int ExeIconSize = 48;

    private async Task CaptureTaskbarIconsAsync(SelfCheckReport report, string captures, string outputFolder)
    {
        var windows = new List<(string Name, Func<Microsoft.UI.Xaml.Window> Window)>
        {
            ("settings", EnsureSettingsWindow),
            ("log", EnsureLogWindow),
            ("transcribe-rewrite", EnsureTranscribeRewriteWindow),
        };
        foreach (string name in PublishedHost.WindowNames)
            windows.Add((name, () => PublishedHost.Ensure(name)));
        foreach ((string name, Func<Microsoft.UI.Xaml.Window> window) in windows)
        {
            nint hwnd = WindowNative.GetWindowHandle(window());
            report.Surfaces.Add(await PhotographIconAsync(
                IconImage.WindowIcon(hwnd), owned: false, name, $"The icon the {name} window gives the taskbar",
                captures, outputFolder));
        }

        string exe = Environment.ProcessPath ?? "";
        report.Surfaces.Add(await PhotographIconAsync(
            IconImage.ExeIcon(exe, ExeIconSize), owned: true, "exe", "The icon embedded in Deckle.exe",
            captures, outputFolder));
        report.Save(outputFolder);
    }

    private static async Task<SelfCheckSurface> PhotographIconAsync(
        nint icon, bool owned, string page, string label, string captures, string outputFolder)
    {
        if (icon == 0)
            return new SelfCheckSurface("icons", page, label, true, 0, null, Technique: "icon", Error: "no icon");
        try
        {
            string path = Path.Combine(captures, $"icons-{page}.png");
            int side = await IconImage.SaveAsync(icon, path);
            return new SelfCheckSurface("icons", page, label, true, 0, Path.GetRelativePath(outputFolder, path),
                Technique: "icon", Width: side, Height: side);
        }
        catch (Exception ex)
        {
            return new SelfCheckSurface("icons", page, label, true, 0, null, Technique: "icon",
                Error: $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (owned) IconImage.Destroy(icon);
        }
    }
}
