using System.Text.Json;
using Deckle.Orchestration;
using Xunit;

namespace Deckle.Orchestration.Tests;

[Trait("Category", "unit")]
public sealed class AppRulesTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void HdrByMonitorStaysCaseInsensitiveAfterJsonRoundTrip()
    {
        var rules = new AppRules();
        rules.HdrByMonitor[@"\\.\DISPLAY1"] = HdrRule.On;

        string json = JsonSerializer.Serialize(rules, Options);
        var restored = JsonSerializer.Deserialize<AppRules>(json, Options)!;

        Assert.Equal(HdrRule.On, restored.HdrByMonitor[@"\\.\display1"]);
    }

    [Fact]
    public void HdrRulesRoundTripPerMonitorAsNames()
    {
        var entry = new AppEntry();
        entry.Rules.HdrByMonitor[@"\\.\DISPLAY1"] = HdrRule.On;
        entry.Rules.HdrByMonitor[@"\\.\DISPLAY2"] = HdrRule.Off;
        entry.Rules.HdrByMonitor[@"\\.\DISPLAY3"] = HdrRule.Leave;

        string json = JsonSerializer.Serialize(entry, Options);
        var restored = JsonSerializer.Deserialize<AppEntry>(json, Options)!;

        Assert.Contains("\"On\"", json);
        Assert.Contains("\"Off\"", json);
        Assert.Equal(HdrRule.On, restored.Rules.HdrByMonitor[@"\\.\DISPLAY1"]);
        Assert.Equal(HdrRule.Off, restored.Rules.HdrByMonitor[@"\\.\DISPLAY2"]);
        Assert.Equal(HdrRule.Leave, restored.Rules.HdrByMonitor[@"\\.\DISPLAY3"]);
    }

    [Fact]
    public void EntryRoundTripWritesEnumsAsNames()
    {
        var entry = new AppEntry { DisplayName = "Hollow", Kind = AppKind.Game, Source = AppSource.Steam };

        string json = JsonSerializer.Serialize(entry, Options);
        var restored = JsonSerializer.Deserialize<AppEntry>(json, Options)!;

        Assert.Contains("\"kind\":\"Game\"", json);
        Assert.Equal(AppKind.Game, restored.Kind);
        Assert.Equal(AppSource.Steam, restored.Source);
        Assert.Equal(entry.Id, restored.Id);
    }
}
