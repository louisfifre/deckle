using Microsoft.Win32;
using Xunit;

namespace Deckle.Input.Trackpad.Tests;

// The act that silences Windows's own three-finger gestures, against a real
// registry key that is never the one the touchpad driver reads:
// HKCU\Software\Deckle.Tests\<guid>, deleted in Dispose. The backup is a real
// file in a temporary folder. What the tests state is what the person gets: the
// six gesture settings read 0 while Deckle drags, and read what they held
// before once the person gives them back, a setting that held nothing holding
// nothing again.
[Trait("Category", "integration")]
[Collection(TrackpadObservabilityCollection.Name)]
public sealed class WindowsGestureNeutralizerTests : IDisposable
{
    // The six settings Windows reads for the native three-finger gestures.
    // Written out here: they are the promise to the driver, not an
    // implementation detail.
    private static readonly string[] GestureValues =
    [
        "ThreeFingerSlideEnabled",
        "ThreeFingerTapEnabled",
        "ThreeFingerUp",
        "ThreeFingerDown",
        "ThreeFingerLeft",
        "ThreeFingerRight",
    ];

    private readonly string _keyPath = @"Software\Deckle.Tests\" + Guid.NewGuid().ToString("N");
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "Deckle.Input.Trackpad.Tests", "gestures-" + Guid.NewGuid().ToString("N"));

    public WindowsGestureNeutralizerTests()
    {
        Directory.CreateDirectory(_folder);
        Registry.CurrentUser.CreateSubKey(_keyPath).Dispose();
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        try { Directory.Delete(_folder, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    private string BackupPath => Path.Combine(_folder, "gesture-backup.json");

    private bool Neutralize() => WindowsGestureNeutralizer.TryNeutralize(_keyPath, () => BackupPath);

    private bool Restore() => WindowsGestureNeutralizer.TryRestore(_keyPath, () => BackupPath);

    private bool Neutralized() => WindowsGestureNeutralizer.AreNeutralized(_keyPath);

    private bool HasBackup() => WindowsGestureNeutralizer.HasBackup(() => BackupPath);

    private void Set(string name, int? value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(_keyPath);
        if (value is int v) key.SetValue(name, v, RegistryValueKind.DWord);
        else key.DeleteValue(name, throwOnMissingValue: false);
    }

    private int? Read(string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_keyPath);
        return key?.GetValue(name) is int v ? v : null;
    }

    // Windows as a person with a typical touchpad has it: some gestures on
    // with their own direction codes, one off, and two never written.
    private Dictionary<string, int?> TypicalWindows()
    {
        var held = new Dictionary<string, int?>
        {
            ["ThreeFingerSlideEnabled"] = 1,
            ["ThreeFingerTapEnabled"] = 0,
            ["ThreeFingerUp"] = 2,
            ["ThreeFingerDown"] = 3,
            ["ThreeFingerLeft"] = null,
            ["ThreeFingerRight"] = null,
        };
        foreach ((string name, int? value) in held) Set(name, value);
        return held;
    }

    [Fact]
    public void TurningTheGesturesOffSetsEachOfTheSixWindowsGesturesToZero()
    {
        TypicalWindows();

        Assert.True(Neutralize());

        foreach (string name in GestureValues) Assert.Equal(0, Read(name));
        Assert.True(Neutralized());
        Assert.True(HasBackup());
    }

    [Fact]
    public void GivingTheGesturesBackPutsEachValueBackAndRemovesTheOnesThatWereAbsent()
    {
        Dictionary<string, int?> held = TypicalWindows();
        Neutralize();

        Assert.True(Restore());

        foreach (string name in GestureValues) Assert.Equal(held[name], Read(name));
        Assert.False(Neutralized());
    }

    [Fact]
    public void GivingTheGesturesBackLeavesNoBackupSoTheNextTurningOffTakesFreshOriginals()
    {
        TypicalWindows();
        Neutralize();
        Restore();
        Assert.False(HasBackup());

        Set("ThreeFingerUp", 4);
        Neutralize();
        Restore();

        Assert.Equal(4, Read("ThreeFingerUp"));
    }

    [Fact]
    public void TurningTheGesturesOffTwiceStillGivesTheFirstOriginalsBack()
    {
        Dictionary<string, int?> held = TypicalWindows();
        Neutralize();

        Assert.True(Neutralize());
        Restore();

        foreach (string name in GestureValues) Assert.Equal(held[name], Read(name));
    }

    [Fact]
    public void TurningTheGesturesOffOnAKeyWindowsNeverWroteStillWorksAndGivesBackAnEmptyKey()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath);

        Assert.True(Neutralize());
        foreach (string name in GestureValues) Assert.Equal(0, Read(name));

        Assert.True(Restore());
        foreach (string name in GestureValues) Assert.Null(Read(name));
    }

    [Fact]
    public void GivingTheGesturesBackWithNothingSavedChangesNothing()
    {
        Dictionary<string, int?> held = TypicalWindows();

        Assert.False(Restore());

        foreach (string name in GestureValues) Assert.Equal(held[name], Read(name));
    }

    [Fact]
    public void ABackupThatCannotBeReadLeavesTheGestureSettingsAsTheyAre()
    {
        TypicalWindows();
        Neutralize();
        File.WriteAllText(BackupPath, "not json");

        Assert.False(Restore());

        foreach (string name in GestureValues) Assert.Equal(0, Read(name));
    }
}
