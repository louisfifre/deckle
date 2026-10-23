using Deckle.TestSupport;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleShellTaskbarCoverSource.BandCreateFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.BandCreateFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.CoverHidden", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.CoverRestacked", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.CoverShown", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.CoverSuppressed", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellTaskbarCoverSource.CoverSuppressedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.CoverUnsuppressed", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellTaskbarCoverSource.CoverUnsuppressedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.HostStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.HostStartFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.LayoutRebuilt", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellTaskbarCoverSource.LayoutRebuiltDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.ReorderHookFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.SessionNotifyFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.SettingsLoadError", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.SettingsLoadWarning", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.SystemResumed", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellTaskbarCoverSource.SystemResumedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTaskbarCoverSource.SystemSuspended", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellTaskbarCoverSource.SystemSuspendedDetail", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleShellTaskbarCoverSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
