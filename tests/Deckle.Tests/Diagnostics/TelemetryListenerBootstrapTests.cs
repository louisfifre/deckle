using Deckle.Chrono;
using Deckle.Diagnostics.Telemetry;
using Xunit;

namespace Deckle.Tests.Diagnostics;

[Trait("Category", "regression")]
public sealed class TelemetryListenerBootstrapTests
{
    [Fact]
    public void ApplicationLogRespectsRuntimeDropFilter()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "telemetry-listener-" + Guid.NewGuid().ToString("N"));
        string appLog = Path.Combine(root, "app.jsonl");

        TelemetryListenerBootstrap.ShutDown();
        try
        {
            TelemetryListenerBootstrap.Configure(root, validationSubdirectory: false);
            TelemetryListenerBootstrap.ConfigureGates(name => name == "ApplicationLogToDisk");
            TelemetryListenerBootstrap.ConfigureApplicationLogProviderLevelDropFilter(
                (provider, _) => provider == "Deckle.Chrono");

            DeckleChronoSource.Log.PilotEmitted("dropped-by-filter");

            Assert.False(File.Exists(appLog));

            TelemetryListenerBootstrap.ConfigureApplicationLogProviderLevelDropFilter((_, _) => false);
            DeckleChronoSource.Log.PilotEmitted("written-after-filter");

            Assert.True(File.Exists(appLog));
            string jsonl = File.ReadAllText(appLog);
            Assert.Contains("written-after-filter", jsonl);
            Assert.DoesNotContain("dropped-by-filter", jsonl);
        }
        finally
        {
            TelemetryListenerBootstrap.ShutDown();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
