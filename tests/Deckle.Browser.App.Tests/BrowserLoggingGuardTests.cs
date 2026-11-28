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

    private static IEnumerable<MethodInfo> EventMethods() =>
        BrowserAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(EventSource).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<EventAttribute>() is not null);
}
