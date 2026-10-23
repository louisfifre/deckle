using Deckle.TestSupport;
using Xunit;

namespace Deckle.Settings.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleSettingsSource.BackupCreatedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.BackupFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.BackupListFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.BackupSkippedSourceMissingDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.CleanupFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.DataSizeFilesSkippedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.InjectFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.MigrationDispatchedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.MigrationDispatchSkipped", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.ModuleFolderMigratedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.ModuleFolderRenameFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.ModuleRenameDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.NavCompletedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.NavFailedFrameRejectedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.NavFailedThrew", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.NavFailedTypeNotFoundDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.NavImpossibleNoTagDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.NavSelectionChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.NavSelectionIgnored", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.NavSkippedAlreadyCurrent", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.NavStackTrace", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.NavStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.NavTiming", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.PageReady", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.PerModuleMigrationFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.RenamedRootKeyDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.RestoreDeclined", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleSettingsSource.RestoredFromBackupDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.RestoreFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.RestoreSkippedSnapshotMissingDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.SearchEntrySkipped", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.SearchExecuted", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.SearchFocusReleased", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.SearchNavigatedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.SearchPresentationChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.SettingsLoadComplete", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.SettingsLoaded", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.SettingsLoadError", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.SettingsLoadWarning", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.SettingsModuleParentMissing", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.SettingsModuleRegistered", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.SettingsModuleUnregistered", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.SetupWindowOpenFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsSource.TitleBarLayout", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleSettingsSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
