using Deckle.TestSupport;
using Xunit;

namespace Deckle.Shell.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleShellSource.AutostartDisableFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.AutostartDisableSkipped", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellSource.AutostartEnabledDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.AutostartEnableFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.AutostartEnableFailedAclDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.AutostartProbeFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.CursorSignalArmed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.CursorSignalDisarmed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.CursorSignalRegistrationFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.ElevatedStartupDisableFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.ElevatedStartupEnableFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.ElevatedStartupProbeFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.ElevatedStartupTaskUnreadable", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.ElevatedStartupTempNotDeleted", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.HotkeyLayoutChange", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellSource.HotkeyRegistered", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.HotkeyReleaseFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.HotkeyReregisterFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.HotkeyReregisterFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.HotkeySubclassFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.HotkeyUnregistered", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.HotkeyVkResolveFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.MessageOnlyHostCreated", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.MessageOnlyHostDestroyed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.TaskbarRecreated", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellSource.TrayIconFallback", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.TrayIconHostCreated", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.TrayIconHostDestroyed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.TrayIconReaddAttempt", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.TrayIconRestoreFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellSource.WindowSubclassFailed", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleShellSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
