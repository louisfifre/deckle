using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

public class DeckleSettingsUxSourceTests
{
    // The folder setters passed the chosen path as the value, so the log
    // carried C:\Users\<name>\... for anyone reading a shared log.
    [Fact]
    [Trait("Category", "regression")]
    public void SettingChangedNeverLogsADirectoryPath()
    {
        using var listener = new TestEventListener("Deckle-SettingsUx");
        string path = @"C:\Users\someone\Documents\Deckle backups";

        DeckleSettingsUxSource.Log.SettingChanged("Paths.BackupDirectory", $"\"{path}\"");
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.StorageDirectory", $"\"{path}\"");
        DeckleSettingsUxSource.Log.SettingChanged("Some.FutureSetting", path);

        Assert.Equal(3, listener.Events.Count);
        Assert.All(listener.Events, ev => Assert.Equal("(changed)", ev.Payload?[1]));
    }

    [Fact]
    [Trait("Category", "observability")]
    public void SettingChangedKeepsOnlyTheAudioDevice()
    {
        using var listener = new TestEventListener("Deckle-SettingsUx");

        DeckleSettingsUxSource.Log.SettingChanged("Audio input device", "system default");
        DeckleSettingsUxSource.Log.SettingChanged("Theme", "Dark");

        Assert.Equal("system default", listener.Events[0].Payload?[1]);
        Assert.Equal("(changed)", listener.Events[1].Payload?[1]);
    }

    // The composer's line reaches log.jsonl only as an Informational
    // milestone; Verbose would stay in the trace.
    [Fact]
    [Trait("Category", "observability")]
    public void SettingWrittenIsAMilestoneCarryingBothValues()
    {
        using var listener = new TestEventListener("Deckle-SettingsUx");

        DeckleSettingsUxSource.Log.SettingWritten("Deckle.Settings", "GeneralDeveloperModeCard", "off", "on");

        var ev = Assert.Single(listener.Events);
        Assert.Equal(EventLevel.Informational, ev.Level);
        Assert.Equal(ObservationTags.Milestone, ev.Tags & ObservationTags.Milestone);
        Assert.Equal(new object?[] { "Deckle.Settings", "GeneralDeveloperModeCard", "off", "on" }, ev.Payload);
    }
}
