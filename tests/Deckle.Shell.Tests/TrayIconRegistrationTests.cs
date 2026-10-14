using Deckle.Core;
using Deckle.Shell;
using Xunit;

namespace Deckle.Shell.Tests;

// Contract of TrayIconRegistration: the entry the shell holds carries the
// callback message, the last icon and the last tip, and goes in again, whole,
// after the shell has forgotten it. The fake shell records every message it
// is sent; the Shell32 call itself is not driven here.
public sealed class TrayIconRegistrationTests
{
    private static readonly IntPtr Window        = new(0x10);
    private static readonly IntPtr IdleIcon      = new(0x20);
    private static readonly IntPtr RecordingIcon = new(0x30);

    private const uint AllFields = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;

    [Fact]
    [Trait("Category", "unit")]
    public void TheAddCarriesTheCallbackMessageTheIconAndTheTip()
    {
        var shell = new FakeShell();
        var registration = new TrayIconRegistration(Window, shell);
        registration.Update("Deckle — Ready", IdleIcon);

        Assert.True(registration.Add());

        (uint message, NOTIFYICONDATA data) = Assert.Single(shell.Sent);
        Assert.Equal(NativeMethods.NIM_ADD, message);
        Assert.Equal(Window, data.hWnd);
        Assert.Equal(TrayIconRegistration.IconId, data.uID);
        Assert.Equal(AllFields, data.uFlags);
        Assert.Equal(NativeMethods.WM_TRAY, data.uCallbackMessage);
        Assert.Equal(IdleIcon, data.hIcon);
        Assert.Equal("Deckle — Ready", data.szTip);
    }

    // The App sets the initial status before the icon goes live: that status
    // is kept, not sent to a shell that holds no entry yet.
    [Fact]
    [Trait("Category", "unit")]
    public void AStatusSetBeforeTheAddIsKeptForItNotSentAhead()
    {
        var shell = new FakeShell();
        var registration = new TrayIconRegistration(Window, shell);

        registration.Update("Deckle — Dictation not set up", IdleIcon);

        Assert.Empty(shell.Sent);
        Assert.Equal("Deckle — Dictation not set up", registration.Tip);
        Assert.Equal(IdleIcon, registration.Icon);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void AStatusUpdateAfterTheAddModifiesTheIconAndTheTipOnly()
    {
        var shell = new FakeShell();
        var registration = new TrayIconRegistration(Window, shell);
        registration.Add();

        registration.Update("Deckle — Recording", RecordingIcon);

        (uint message, NOTIFYICONDATA data) = shell.Sent[^1];
        Assert.Equal(NativeMethods.NIM_MODIFY, message);
        Assert.Equal(NativeMethods.NIF_ICON | NativeMethods.NIF_TIP, data.uFlags);
        Assert.Equal(RecordingIcon, data.hIcon);
        Assert.Equal("Deckle — Recording", data.szTip);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void TheTipIsCutToWhatTheShellCanHold()
    {
        var shell = new FakeShell();
        var registration = new TrayIconRegistration(Window, shell);

        registration.Update(new string('x', 200), IdleIcon);

        Assert.Equal(TrayIconRegistration.MaxTipLength, registration.Tip.Length);
    }

    // Explorer restarted on 2026-10-14 (22:56) and the icon never came back:
    // the entry was added once and nothing re-sent it. A re-add is a full
    // NIM_ADD, callback message included, with the status of the moment.
    [Fact]
    [Trait("Category", "regression")]
    public void ATaskbarRecreationAddsTheIconBackWithItsLastStatus()
    {
        var shell = new FakeShell();
        var registration = new TrayIconRegistration(Window, shell);
        registration.Update("Deckle — Ready", IdleIcon);
        registration.Add();
        registration.Update("Deckle — Recording", RecordingIcon);
        shell.Sent.Clear();

        Assert.Equal(TrayIconReaddOutcome.Added, registration.Readd());

        (uint message, NOTIFYICONDATA data) = Assert.Single(shell.Sent);
        Assert.Equal(NativeMethods.NIM_ADD, message);
        Assert.Equal(AllFields, data.uFlags);
        Assert.Equal(NativeMethods.WM_TRAY, data.uCallbackMessage);
        Assert.Equal(RecordingIcon, data.hIcon);
        Assert.Equal("Deckle — Recording", data.szTip);
        Assert.True(registration.IsAdded);
    }

    // The shell refuses an add while its tray window is coming up: each call
    // is one attempt, the caller pauses between them, and the sequence ends
    // after MaxReaddAttempts. The icon counts as gone in between.
    [Fact]
    [Trait("Category", "unit")]
    public void ARefusedReaddIsRetriedThenGivenUp()
    {
        var shell = new FakeShell { Refusing = true };
        var registration = new TrayIconRegistration(Window, shell);
        shell.Refusing = false;
        registration.Add();
        shell.Refusing = true;

        var outcomes = new List<TrayIconReaddOutcome>();
        for (int attempt = 0; attempt < TrayIconRegistration.MaxReaddAttempts; attempt++)
            outcomes.Add(registration.Readd());

        Assert.Equal(
            Enumerable.Repeat(TrayIconReaddOutcome.RetryLater, TrayIconRegistration.MaxReaddAttempts - 1)
                .Append(TrayIconReaddOutcome.GaveUp),
            outcomes);
        Assert.False(registration.IsAdded);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void AReaddAcceptedAfterRefusalsEndsTheSequence()
    {
        var shell = new FakeShell();
        var registration = new TrayIconRegistration(Window, shell);
        registration.Add();
        shell.Refusing = true;
        registration.Readd();
        registration.Readd();
        shell.Refusing = false;

        Assert.Equal(TrayIconReaddOutcome.Added, registration.Readd());
        Assert.True(registration.IsAdded);
        Assert.Equal(3, registration.ReaddAttempts);

        // The next broadcast starts a sequence of its own.
        registration.Readd();
        Assert.Equal(1, registration.ReaddAttempts);
    }

    // A broadcast that did not follow a restart finds the entry still there:
    // the shell refuses a second add but accepts a modify, and the icon is
    // on screen.
    [Fact]
    [Trait("Category", "unit")]
    public void AnEntryTheShellStillHoldsCountsAsAdded()
    {
        var shell = new FakeShell();
        var registration = new TrayIconRegistration(Window, shell);
        registration.Add();
        shell.RefusingAdd = true;

        Assert.Equal(TrayIconReaddOutcome.Added, registration.Readd());
        Assert.Equal(
            [NativeMethods.NIM_ADD, NativeMethods.NIM_ADD, NativeMethods.NIM_MODIFY],
            shell.Sent.Select(call => call.Message));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void TheDeleteIsSentOnlyForAnEntryTheShellHolds()
    {
        var shell = new FakeShell();
        var registration = new TrayIconRegistration(Window, shell);

        registration.Delete();
        Assert.Empty(shell.Sent);

        registration.Add();
        registration.Delete();
        Assert.Equal(NativeMethods.NIM_DELETE, shell.Sent[^1].Message);
        Assert.False(registration.IsAdded);
    }

    private sealed class FakeShell : INotifyIconApi
    {
        public bool Refusing;
        public bool RefusingAdd;
        public List<(uint Message, NOTIFYICONDATA Data)> Sent { get; } = [];

        public bool Notify(uint message, ref NOTIFYICONDATA data)
        {
            Sent.Add((message, data));
            if (Refusing) return false;
            if (RefusingAdd && message == NativeMethods.NIM_ADD) return false;
            return true;
        }

        public int LastError => 0;
    }
}
