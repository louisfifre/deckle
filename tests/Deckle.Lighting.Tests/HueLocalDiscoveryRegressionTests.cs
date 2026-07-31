using System.Text.Json;
using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Tests;

[Trait("Category", "regression")]
public sealed class HueLocalDiscoveryRegressionTests
{
    private static readonly JsonElement _example = LoadExample();

    [Fact]
    public void RecognizesDnsRequestPendingStatus()
    {
        uint status = _example.GetProperty("trigger").GetProperty("nativeStartStatus").GetUInt32();

        Assert.True(HueLocalDiscovery.IsRequestPending(status));
    }

    [Fact]
    public void DoesNotConfuseIoPendingWithDnsRequestPending()
    {
        Assert.False(HueLocalDiscovery.IsRequestPending(997));
    }

    [Fact]
    public void IgnoresLateBrowseCallbackAfterContextWasReleased()
    {
        nint context = _example.GetProperty("trigger").GetProperty("lateUnknownContext").GetInt32();

        var exception = Record.Exception(
            () => HueLocalDiscovery.OnBrowseResult(status: 0, context, recordPointer: 0));

        Assert.Null(exception);
    }

    [Fact]
    public void IgnoresLateResolveCallbackAfterContextWasReleased()
    {
        nint context = _example.GetProperty("trigger").GetProperty("lateUnknownContext").GetInt32();

        var exception = Record.Exception(
            () => HueLocalDiscovery.OnResolveResult(status: 0, context, instancePointer: 0));

        Assert.Null(exception);
    }

    private static JsonElement LoadExample()
    {
        var assembly = typeof(HueLocalDiscoveryRegressionTests).Assembly;
        string resourceName = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith(
                "dns-sd-pending-status-lifetime.json",
                StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded bug example '{resourceName}' was not found.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
