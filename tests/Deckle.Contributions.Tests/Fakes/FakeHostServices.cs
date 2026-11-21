using Microsoft.UI.Xaml;

namespace Deckle.Contributions.Tests.Fakes;

// The application's window mechanics, recorded instead of performed.
internal sealed class FakeHostServices : IContributionHostServices
{
    // By reference: Window overrides Equals and GetHashCode over its native
    // pointer, which the stand-in windows of a test do not have.
    private readonly Dictionary<Window, Action> _closed = new(ReferenceEqualityComparer.Instance);

    public bool DeveloperModeEnabled { get; set; }

    public HashSet<string> AbsentModules { get; } = [];

    public List<string> Opened { get; } = [];
    public List<string> Placed { get; } = [];
    public List<Window> Themed { get; } = [];
    public List<string> SettingsPages { get; } = [];

    public Window CreateOrLog(string name, Func<Window> create) => create();

    public void Place(Window window, string name) => Placed.Add(name);

    public void ApplyTheme(Window window) => Themed.Add(window);

    public void WhenClosed(Window window, Action closed) => _closed[window] = closed;

    public void WindowOpened(string name) => Opened.Add(name);

    public string ModuleDirectory(string moduleId) => Path.Combine("data", "modules", moduleId);

    public bool IsModulePresent(string moduleId) => !AbsentModules.Contains(moduleId);

    public void OpenSettingsPage(string pageTag) => SettingsPages.Add(pageTag);

    // The person closes the window.
    public void Close(Window window) => _closed[window]();
}
