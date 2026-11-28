using System.Diagnostics.Tracing;
using System.Reflection;
using Deckle.Browser.Views;
using Xunit;

namespace Deckle.Browser.App.Tests;

// Nothing a person visits reaches the log: no address, no page title, no host,
// no domain. A provider's event parameters are named after what they carry, so
// a name that says url, uri, title, host or domain is a line that would carry
// a visited page. This reads every EventSource of the browser assemblies.
[Trait("Category", "observability")]
public sealed class BrowserLoggingGuardTests
{
    private static readonly string[] Forbidden = ["url", "uri", "title", "host", "domain"];

    private static readonly Assembly[] BrowserAssemblies =
    [
        typeof(Deckle.Browser.BrowserSession).Assembly,
        typeof(DeckleBrowserViewsSource).Assembly,
        typeof(DeckleBrowserAppSource).Assembly,
    ];

    [Fact]
    public void TheGuardSeesTheBrowserProviders()
    {
        Assert.NotEmpty(EventMethods());
    }

    [Fact]
    public void NoEventParameterOfTheBrowserAssembliesNamesAnAddressATitleAHostOrADomain()
    {
        string[] offenders =
        [
            .. EventMethods()
                .SelectMany(m => m.GetParameters().Select(p => (Method: m, Parameter: p)))
                .Where(x => Forbidden.Any(word => x.Parameter.Name!.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .Select(x => $"{x.Method.DeclaringType!.Name}.{x.Method.Name}({x.Parameter.Name})"),
        ];

        Assert.Empty(offenders);
    }

    // The events of a gesture, a navigation and the periodic summary carry what
    // a machine can say of them and nothing a person typed or visited: the
    // pairing id, a kind or an outcome, the engine's error status name, and
    // numbers. A string parameter outside that list is a new thing to justify.
    [Fact]
    public void TheGestureNavigationAndSummaryEventsCarryKindsAndNumbersOnly()
    {
        string[] names = ["GestureStarted", "GestureEnded", "NavigationStarted", "NavigationEnded", "ActivitySummary"];
        string[] allowedWords = ["gesture_kind", "outcome", "web_error", "work_id"];

        MethodInfo[] events = [.. EventMethods().Where(m => names.Contains(m.Name))];
        Assert.Equal(names.Length, events.Length);

        string[] strangers =
        [
            .. events
                .SelectMany(m => m.GetParameters().Select(p => (Method: m, Parameter: p)))
                .Where(x => x.Parameter.ParameterType == typeof(string) && !allowedWords.Contains(x.Parameter.Name!))
                .Select(x => $"{x.Method.Name}({x.Parameter.Name})"),
        ];
        Assert.Empty(strangers);

        // Every start and end of lasting work pairs by the same id; every end has its duration.
        foreach (string name in new[] { "GestureStarted", "GestureEnded", "NavigationStarted", "NavigationEnded" })
            Assert.Contains(events.Single(m => m.Name == name).GetParameters(), p => p.Name == "work_id");
        foreach (string name in new[] { "GestureEnded", "NavigationEnded" })
            Assert.Contains(events.Single(m => m.Name == name).GetParameters(), p => p.Name == "duration_ms");
    }

    private static IEnumerable<MethodInfo> EventMethods() =>
        BrowserAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(EventSource).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<EventAttribute>() is not null);
}
