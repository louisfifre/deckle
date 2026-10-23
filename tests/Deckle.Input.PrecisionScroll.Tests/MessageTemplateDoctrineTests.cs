using Deckle.TestSupport;
using Xunit;

namespace Deckle.Input.PrecisionScroll.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DecklePrecisionScrollSource.EngineStarted", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.EngineStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.EngineStopped", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.EngineStoppedDetail", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.FallbackRecovered", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.FellBackToNativeWheel", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.Inactive", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.InjectionFailed", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.QueueOverloaded", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.RestartScheduledDetail", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.SettingsLoadError", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.SettingsLoadWarning", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.StopSlow", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.TouchpadSettingsUnavailable", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.Unavailable", TemplateDoctrine.PreDoctrine },
        { "DecklePrecisionScrollSource.WorkerFaulted", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DecklePrecisionScrollSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
