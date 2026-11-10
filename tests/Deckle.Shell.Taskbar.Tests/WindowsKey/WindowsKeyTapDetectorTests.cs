using Deckle.Input;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The key sequences that make a Windows key press a lone tap, a shortcut or
// noise. The detector takes events and returns actions, so each sequence is
// written as the events a keyboard would raise.
[Trait("Category", "unit")]
public sealed class WindowsKeyTapDetectorTests
{
    private const ushort LeftWindows = 0x5B;
    private const ushort RightWindows = 0x5C;
    private const ushort KeyE = 0x45;
    private const ushort Shift = 0x10;

    private readonly WindowsKeyTapDetector _detector = new() { Enabled = true };

    private static KeyboardKeyEvent Key(ushort virtualKey, bool down, bool injected = false) =>
        new(virtualKey, ScanCode: 0, IsKeyDown: down, IsExtended: false, IsInjected: injected, TimestampMs: 0);

    private WindowsKeyAction Down(ushort virtualKey, bool injected = false) => _detector.OnKey(Key(virtualKey, true, injected));

    private WindowsKeyAction Up(ushort virtualKey, bool injected = false) => _detector.OnKey(Key(virtualKey, false, injected));

    [Fact]
    public void ALoneTapSuppressesStartOnTheWayDownThenFocusesTheTaskbarOnTheWayUp()
    {
        Assert.Equal(WindowsKeyAction.SuppressStart, Down(LeftWindows));
        Assert.Equal(WindowsKeyAction.FocusTaskbar, Up(LeftWindows));
    }

    [Fact]
    public void TheRightWindowsKeyIsTheSameAsTheLeftOne()
    {
        Assert.Equal(WindowsKeyAction.SuppressStart, Down(RightWindows));
        Assert.Equal(WindowsKeyAction.FocusTaskbar, Up(RightWindows));
    }

    [Fact]
    public void AShortcutSuppressesStartAndNothingMore()
    {
        Assert.Equal(WindowsKeyAction.SuppressStart, Down(LeftWindows));
        Assert.Equal(WindowsKeyAction.None, Down(KeyE));
        Assert.Equal(WindowsKeyAction.None, Up(KeyE));
        Assert.Equal(WindowsKeyAction.None, Up(LeftWindows));
    }

    // Windows + Shift + S: a modifier is a key like any other.
    [Fact]
    public void AModifierPressedWithTheWindowsKeyMakesItAShortcut()
    {
        Assert.Equal(WindowsKeyAction.SuppressStart, Down(LeftWindows));
        Assert.Equal(WindowsKeyAction.None, Down(Shift));
        Assert.Equal(WindowsKeyAction.None, Up(Shift));
        Assert.Equal(WindowsKeyAction.None, Up(LeftWindows));
    }

    [Fact]
    public void AKeyReleasedAloneInsideTheWindowsPressDoesNotMakeItAShortcut()
    {
        // A key held before the Windows key, released while it is down.
        Assert.Equal(WindowsKeyAction.SuppressStart, Down(LeftWindows));
        Assert.Equal(WindowsKeyAction.None, Up(KeyE));
        Assert.Equal(WindowsKeyAction.FocusTaskbar, Up(LeftWindows));
    }

    [Fact]
    public void AKeyRepeatingTheWindowsKeyAnswersOnlyOnce()
    {
        Assert.Equal(WindowsKeyAction.SuppressStart, Down(LeftWindows));
        Assert.Equal(WindowsKeyAction.None, Down(LeftWindows));
        Assert.Equal(WindowsKeyAction.None, Down(LeftWindows));
        Assert.Equal(WindowsKeyAction.FocusTaskbar, Up(LeftWindows));
    }

    [Fact]
    public void TwoWindowsKeysHeldTogetherAreOnePress()
    {
        Assert.Equal(WindowsKeyAction.SuppressStart, Down(LeftWindows));
        Assert.Equal(WindowsKeyAction.None, Down(RightWindows));
        Assert.Equal(WindowsKeyAction.None, Up(LeftWindows));
        Assert.Equal(WindowsKeyAction.FocusTaskbar, Up(RightWindows));
    }

    [Fact]
    public void KeysWithoutTheWindowsKeyAreNotTheirBusiness()
    {
        Assert.Equal(WindowsKeyAction.None, Down(KeyE));
        Assert.Equal(WindowsKeyAction.None, Up(KeyE));
    }

    [Fact]
    public void AWindowsKeyReleaseWithNoPressIsIgnored()
    {
        Assert.Equal(WindowsKeyAction.None, Up(LeftWindows));
    }

    // The tracker's own throwaway key and Windows + T arrive as injected events;
    // reading them would loop.
    [Fact]
    public void InjectedEventsAreIgnored()
    {
        Assert.Equal(WindowsKeyAction.None, Down(LeftWindows, injected: true));
        Assert.Equal(WindowsKeyAction.None, Down(0xFF, injected: true));
        Assert.Equal(WindowsKeyAction.None, Up(0xFF, injected: true));
        Assert.Equal(WindowsKeyAction.None, Up(LeftWindows, injected: true));
    }

    [Fact]
    public void InjectedKeysInsideAPhysicalPressDoNotMakeItAShortcut()
    {
        Assert.Equal(WindowsKeyAction.SuppressStart, Down(LeftWindows));
        Assert.Equal(WindowsKeyAction.None, Down(0xFF, injected: true));
        Assert.Equal(WindowsKeyAction.None, Up(0xFF, injected: true));
        Assert.Equal(WindowsKeyAction.FocusTaskbar, Up(LeftWindows));
    }

    [Fact]
    public void NothingIsDoneWhileDisabled()
    {
        var detector = new WindowsKeyTapDetector();

        Assert.Equal(WindowsKeyAction.None, detector.OnKey(Key(LeftWindows, true)));
        Assert.Equal(WindowsKeyAction.None, detector.OnKey(Key(LeftWindows, false)));
    }

    [Fact]
    public void TurningItOffInTheMiddleOfAPressForgetsThePress()
    {
        Assert.Equal(WindowsKeyAction.SuppressStart, Down(LeftWindows));

        _detector.Enabled = false;
        _detector.Enabled = true;

        Assert.Equal(WindowsKeyAction.None, Up(LeftWindows));
    }

    // Win+L: the lock screen takes the Windows key's release, so the tracker
    // never sees it. The next press must still be answered.
    [Fact]
    public void AWindowsKeyPressedAfterAShortcutWhoseReleaseWasLostIsANewPress()
    {
        Assert.Equal(WindowsKeyAction.SuppressStart, Down(LeftWindows));
        Assert.Equal(WindowsKeyAction.None, Down(0x4C)); // L, then the lock screen

        Assert.Equal(WindowsKeyAction.SuppressStart, Down(LeftWindows));
        Assert.Equal(WindowsKeyAction.FocusTaskbar, Up(LeftWindows));
    }

    [Fact]
    public void TwoLoneTapsInARowAreBothHandled()
    {
        for (int tap = 0; tap < 2; tap++)
        {
            Assert.Equal(WindowsKeyAction.SuppressStart, Down(LeftWindows));
            Assert.Equal(WindowsKeyAction.FocusTaskbar, Up(LeftWindows));
        }
    }
}
