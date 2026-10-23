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
    public void SettingChangedKeepsAllowlistedValues()
    {
        using var listener = new TestEventListener("Deckle-SettingsUx");

        DeckleSettingsUxSource.Log.SettingChanged("Theme", "Dark");
        DeckleSettingsUxSource.Log.SettingChanged("Hotkeys.Record", "Control+0x1E");

        Assert.Equal("Dark", listener.Events[0].Payload?[1]);
        Assert.Equal("Control+0x1E", listener.Events[1].Payload?[1]);
    }
}
