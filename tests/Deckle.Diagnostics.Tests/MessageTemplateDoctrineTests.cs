using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleCancellationSource.OperationCancelled", TemplateDoctrine.PreDoctrine },
        { "DeckleDiagnosticsSource.ArchivesPruned", TemplateDoctrine.PreDoctrine },
        { "DeckleDiagnosticsSource.BootReplayOverflow", TemplateDoctrine.PreDoctrine },
        { "DeckleDiagnosticsSource.FlushFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleDiagnosticsSource.FlushTimedOut", TemplateDoctrine.PreDoctrine },
        { "DeckleDiagnosticsSource.MessageTemplateBroken", TemplateDoctrine.PreDoctrine },
        { "DeckleDiagnosticsSource.RotationFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleDiagnosticsSource.RotationRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleDiagnosticsSource.RowsRoutedAgain", TemplateDoctrine.PreDoctrine },
        { "DeckleDiagnosticsSource.RowUnrouted", TemplateDoctrine.PreDoctrine },
        { "DeckleDiagnosticsSource.SettingsLoadError", TemplateDoctrine.PreDoctrine },
        { "DeckleDiagnosticsSource.SettingsLoadWarning", TemplateDoctrine.PreDoctrine },
        { "DeckleDiagnosticsSource.SinkFaulted", TemplateDoctrine.PreDoctrine },
        { "DeckleDiagnosticsSource.SinkRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleHotLoopSource.LoopRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleHotLoopSource.LoopRollup", TemplateDoctrine.PreDoctrine },
        { "DeckleHotLoopSource.LoopStalled", TemplateDoctrine.PreDoctrine },
        { "DeckleNetworkSource.NetworkConnectivityChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleNetworkSource.NetworkQueryFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleNetworkSource.NetworkQueryRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleNetworkSource.NetworkStatusChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleResourceSource.ResourceAcquired", TemplateDoctrine.PreDoctrine },
        { "DeckleResourceSource.ResourceReleased", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsUxSource.FolderActionFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsUxSource.SectionResetDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSettingsUxSource.SettingChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleThemeSource.ThemeChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleThreadingSource.DispatcherEnqueueRejectedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleThreadingSource.MarshalCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleThreadingSource.MarshalQueued", TemplateDoctrine.PreDoctrine },
        { "DeckleThreadingSource.MarshalStalled", TemplateDoctrine.PreDoctrine },
        { "DeckleThreadingSource.MarshalStallRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleThreadingSource.MarshalStallRepeated", TemplateDoctrine.PreDoctrine },
        { "DeckleWindowingSource.OverlaySlotAssigned", TemplateDoctrine.PreDoctrine },
        { "DeckleWindowingSource.PopupAnchored", TemplateDoctrine.PreDoctrine },
        { "DeckleWindowingSource.WindowFailureRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleWindowingSource.WindowFailureRepeated", TemplateDoctrine.PreDoctrine },
        { "DeckleWindowingSource.WindowLoadComplete", TemplateDoctrine.PreDoctrine },
        { "DeckleWindowingSource.WindowPlacementFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleWindowingSource.WindowPositioned", TemplateDoctrine.PreDoctrine },
        { "DeckleWindowingSource.WindowResizeFrame", TemplateDoctrine.PreDoctrine },
        { "DeckleWindowingSource.WindowResizeSettled", TemplateDoctrine.PreDoctrine },
        { "DeckleWindowingSource.WindowShowFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleWindowingSource.WindowZOrderState", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleCancellationSource), typeof(DeckleDiagnosticsSource), typeof(DeckleHotLoopSource), typeof(DeckleNetworkSource), typeof(DeckleResourceSource), typeof(DeckleSettingsUxSource), typeof(DeckleThemeSource), typeof(DeckleThreadingSource), typeof(DeckleWindowingSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
