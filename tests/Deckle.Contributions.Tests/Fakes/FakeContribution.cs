using Deckle.Modules;
using Microsoft.UI.Xaml;

namespace Deckle.Contributions.Tests.Fakes;

// A contribution a test composes by hand. Its windows are inert stand-ins:
// the host never touches a window's members, it hands the object to the
// services and to the contribution's own delegates, so identity is all a test
// needs of one.
internal sealed class FakeContribution : IContribution
{
    public required string Id { get; init; }

    public ModuleDescriptor? Descriptor { get; init; }

    public IReadOnlyList<ContributedWindow> Windows { get; init; } = [];

    public IReadOnlyList<ContributedWalk> SelfCheckWalks { get; init; } = [];

    public Action<IContributionContext>? OnCompose { get; init; }

    public int ComposeCalls { get; private set; }

    public void Compose(IContributionContext context)
    {
        ComposeCalls++;
        OnCompose?.Invoke(context);
    }

    public static ModuleDescriptor Module(string id, int order = 0, params string[] dependsOn) => new()
    {
        Id = id,
        Glyph = "",
        Order = order,
        DependsOn = dependsOn,
    };

    // A window object with no WinUI behind it: WinUI cannot be started in a
    // test process, and the host only passes the window around.
    public static Window NewWindow() =>
        (Window)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Window));

    // Window overrides Equals over its native pointer, which a stand-in lacks:
    // windows are compared by reference only.
    public static void AssertSame(IEnumerable<Window> expected, IEnumerable<Window> actual)
    {
        Window[] e = expected.ToArray(), a = actual.ToArray();
        Xunit.Assert.Equal(e.Length, a.Length);
        for (int i = 0; i < e.Length; i++) Xunit.Assert.Same(e[i], a[i]);
    }

    // A window that builds a fresh stand-in on each Create, counting its
    // builds, and records what was shown.
    public sealed class Probe
    {
        public int Created { get; private set; }
        public List<Window> Built { get; } = [];
        public List<Window> Shown { get; } = [];
        public List<(Window Window, bool State)> RecordingStates { get; } = [];
        public List<Window> Closed { get; } = [];

        public ContributedWindow Window(
            string name, string? trayLabelKey = null, bool developerOnly = false, bool withRecordingState = false) => new()
        {
            Name = name,
            Create = () =>
            {
                Created++;
                Window window = NewWindow();
                Built.Add(window);
                return window;
            },
            Show = Shown.Add,
            TrayLabelKey = trayLabelKey,
            DeveloperOnly = developerOnly,
            SetRecordingState = withRecordingState ? (w, s) => RecordingStates.Add((w, s)) : null,
            OnClosed = Closed.Add,
        };
    }
}
