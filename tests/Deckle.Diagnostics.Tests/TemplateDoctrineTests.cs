using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// The check every suite runs on its providers' templates. Its verdicts are
// pinned here on literal templates, so a wrong verdict cannot hide behind a
// suite's list of known breaches.
[Trait("Category", "unit")]
public sealed class TemplateDoctrineTests
{
    [Theory]
    [InlineData(EventLevel.Informational, "Deckle is ready", 2)]
    [InlineData(EventLevel.Verbose, "model={0} | load_ms={1}", 2)]
    [InlineData(EventLevel.Verbose, "elapsed={0}ms", 1)]
    [InlineData(EventLevel.Verbose, "ratio={0:F2}", 1)]
    [InlineData(EventLevel.Warning, "The model could not load\nreason={0} | ex_type={1}", 2)]
    [InlineData(EventLevel.Error, "Cannot relaunch Deckle", 0)]
    [InlineData(EventLevel.Critical, "The app is stopping\nreason={0}", 1)]
    public void ATemplateShapedForItsLevelPasses(EventLevel level, string message, int payloadCount)
    {
        Assert.Null(TemplateDoctrine.Breach(level, message, payloadCount));
    }

    [Theory]
    [InlineData(EventLevel.Informational, "Shutdown requested | work_id={0}", 1)]
    [InlineData(EventLevel.Informational, "Model {0} loaded", 1)]
    [InlineData(EventLevel.Informational, "Deckle is ready\nsession={0}", 1)]
    [InlineData(EventLevel.Verbose, "spawn new process | exe={0}", 1)]
    [InlineData(EventLevel.Verbose, "reason=resident_mutex_abandoned", 0)]
    [InlineData(EventLevel.Verbose, "Model={0}", 1)]
    [InlineData(EventLevel.Verbose, "path={0} file", 1)]
    [InlineData(EventLevel.Warning, "Shutdown step {0} failed | {1}: {2}", 3)]
    [InlineData(EventLevel.Warning, "The model could not load | reason={0}", 1)]
    [InlineData(EventLevel.Warning, "The model could not load", 1)]
    [InlineData(EventLevel.Error, "\nreason={0}", 1)]
    [InlineData(EventLevel.Error, "Cannot relaunch\nDeckle", 0)]
    public void ATemplateShapedForAnotherLevelIsABreach(EventLevel level, string message, int payloadCount)
    {
        Assert.NotNull(TemplateDoctrine.Breach(level, message, payloadCount));
    }

    [Fact]
    public void AnEventWithoutAMessageIsABreach()
    {
        Assert.NotNull(TemplateDoctrine.Breach(EventLevel.Informational, null, 0));
    }

    [Fact]
    public void AnUnlistedBreachIsReportedAsALineReadyForTheList()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations(
            [typeof(FakeSource)], new Dictionary<string, string>());

        string failure = Assert.Single(failures);
        Assert.StartsWith("{ \"FakeSource.Unshaped\", \"" + TemplateDoctrine.ReasonToWrite + "\" },", failure);
    }

    [Fact]
    public void AListedBreachPasses()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations(
            [typeof(FakeSource)],
            new Dictionary<string, string> { ["FakeSource.Unshaped"] = "a reason" });

        Assert.Empty(failures);
    }

    [Fact]
    public void AListedTemplateThatNowFollowsTheDoctrineFailsAsStale()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations(
            [typeof(FakeSource)],
            new Dictionary<string, string>
            {
                ["FakeSource.Unshaped"] = "a reason",
                ["FakeSource.Shaped"] = "a reason",
            });

        string failure = Assert.Single(failures);
        Assert.Contains("stale: FakeSource.Shaped", failure);
    }

    [Fact]
    public void AListedNameThatMatchesNoEventFailsAsStale()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations(
            [typeof(FakeSource)],
            new Dictionary<string, string>
            {
                ["FakeSource.Unshaped"] = "a reason",
                ["FakeSource.Renamed"] = "a reason",
            });

        string failure = Assert.Single(failures);
        Assert.Contains("stale: FakeSource.Renamed", failure);
    }

    // MicrophoneUnavailable(string, uint) wrote its code as text in v0.88.0:
    // the call bound to WriteEvent(string, long).
    [Theory]
    [InlineData(nameof(BindingSource.Widened))]
    [InlineData(nameof(BindingSource.MixedIntAndLong))]
    public void ASignatureThatWidensIntoATypedOverloadIsABreach(string name)
    {
        Assert.NotNull(TemplateDoctrine.WidenedBinding(typeof(BindingSource).GetMethod(name)!));
    }

    [Theory]
    [InlineData(nameof(BindingSource.Exact))]
    [InlineData(nameof(BindingSource.Empty))]
    [InlineData(nameof(BindingSource.ThroughTheParamsOverload))]
    [InlineData(nameof(BindingSource.ABoolAlone))]
    public void ASignatureThatReachesWriteEventAsDeclaredPasses(string name)
    {
        Assert.Null(TemplateDoctrine.WidenedBinding(typeof(BindingSource).GetMethod(name)!));
    }

    [Fact]
    public void AWidenedBindingIsReportedEvenWithoutATemplateBreach()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations(
            [typeof(BindingSource)], new Dictionary<string, string>());

        Assert.Equal(2, failures.Count);
        Assert.All(failures, f => Assert.StartsWith("binding: BindingSource.", f));
    }

    // Only the [Event] attributes are read, so the methods need no EventSource.
    private sealed class FakeSource
    {
        [Event(1, Level = EventLevel.Informational, Message = "The work started")]
        public void Shaped(string work_id) { }

        [Event(2, Level = EventLevel.Informational, Message = "The work started | work_id={0}")]
        public void Unshaped(string work_id) { }
    }

    private sealed class BindingSource
    {
        [Event(1, Level = EventLevel.Informational, Message = "A")]
        public void Widened(string phase, uint code) { }

        [Event(2, Level = EventLevel.Informational, Message = "B")]
        public void MixedIntAndLong(int count, long duration_ms) { }

        [Event(3, Level = EventLevel.Informational, Message = "C")]
        public void Exact(string phase, int count) { }

        [Event(4, Level = EventLevel.Informational, Message = "D")]
        public void Empty() { }

        [Event(5, Level = EventLevel.Informational, Message = "E")]
        public void ThroughTheParamsOverload(string phase, uint code, string name) { }

        [Event(6, Level = EventLevel.Informational, Message = "F")]
        public void ABoolAlone(bool on) { }
    }
}
