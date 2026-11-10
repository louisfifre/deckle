using Deckle.Input;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The runtime against a keyboard host and an injector that only record: which
// sequence the setting turns into which sends, and what the host sees of it.
[Trait("Category", "unit")]
public sealed class WindowsKeyTaskbarFocusTests
{
    private const ushort LeftWindows = 0x5B;
    private const ushort KeyE = 0x45;

    private readonly FakeHost _host = new();
    private readonly RecordingInjector _injector = new();
    private readonly WindowsKeyTaskbarFocus _focus;

    public WindowsKeyTaskbarFocusTests() => _focus = new WindowsKeyTaskbarFocus(_host, _injector);

    [Fact]
    public void ALoneTapSendsTheThrowawayKeyThenWindowsAndT()
    {
        _focus.Reconcile(true);

        _host.Raise(LeftWindows, down: true);
        _host.Raise(LeftWindows, down: false);

        Assert.Equal(["suppress_start", "focus_taskbar"], _injector.Sent);
    }

    [Fact]
    public void AShortcutSendsOnlyTheThrowawayKey()
    {
        _focus.Reconcile(true);

        _host.Raise(LeftWindows, down: true);
        _host.Raise(KeyE, down: true);
        _host.Raise(KeyE, down: false);
        _host.Raise(LeftWindows, down: false);

        Assert.Equal(["suppress_start"], _injector.Sent);
    }

    [Fact]
    public void AKeyRepeatSendsTheThrowawayKeyOnce()
    {
        _focus.Reconcile(true);

        _host.Raise(LeftWindows, down: true);
        _host.Raise(LeftWindows, down: true);
        _host.Raise(LeftWindows, down: true);

        Assert.Equal(["suppress_start"], _injector.Sent);
    }

    [Fact]
    public void ItsOwnSendsComingBackAreNotReadAgain()
    {
        _focus.Reconcile(true);

        _host.Raise(LeftWindows, down: true);
        _host.Raise(0xFF, down: true, injected: true);
        _host.Raise(0xFF, down: false, injected: true);
        _host.Raise(LeftWindows, down: false);
        _host.Raise(LeftWindows, down: true, injected: true);
        _host.Raise(0x54, down: true, injected: true);
        _host.Raise(0x54, down: false, injected: true);
        _host.Raise(LeftWindows, down: false, injected: true);

        Assert.Equal(["suppress_start", "focus_taskbar"], _injector.Sent);
    }

    [Fact]
    public void NothingIsSentWhileTheSettingIsOff()
    {
        _host.Raise(LeftWindows, down: true);
        _host.Raise(LeftWindows, down: false);

        Assert.Empty(_injector.Sent);
        Assert.Equal(0, _host.Holders);
    }

    [Fact]
    public void TurningTheSettingOnTakesOneHoldOnTheHostAndOffGivesItBack()
    {
        _focus.Reconcile(true);
        _focus.Reconcile(true);

        Assert.True(_focus.IsArmed);
        Assert.Equal(1, _host.Holders);

        _focus.Reconcile(false);
        _focus.Reconcile(false);

        Assert.False(_focus.IsArmed);
        Assert.Equal(0, _host.Holders);
        Assert.Equal(0, _host.Subscribers);
    }

    [Fact]
    public void ATurnedOffSettingSendsNothingMore()
    {
        _focus.Reconcile(true);
        _focus.Reconcile(false);

        _host.Raise(LeftWindows, down: true);
        _host.Raise(LeftWindows, down: false);

        Assert.Empty(_injector.Sent);
    }

    [Fact]
    public void AHostThatCannotStartLeavesTheRuntimeOff()
    {
        _host.StartSucceeds = false;

        _focus.Reconcile(true);

        Assert.False(_focus.IsArmed);
        Assert.Equal(0, _host.Subscribers);

        _host.StartSucceeds = true;
        _focus.Reconcile(true);

        Assert.True(_focus.IsArmed);
    }

    [Fact]
    public void DisposingReleasesTheHost()
    {
        _focus.Reconcile(true);

        _focus.Dispose();

        Assert.Equal(0, _host.Holders);
        Assert.Equal(0, _host.Subscribers);
    }

    private sealed class RecordingInjector : IWindowsKeyInjector
    {
        public List<string> Sent { get; } = [];

        public int LastWin32Error => 0;

        public bool SuppressStart() { Sent.Add("suppress_start"); return true; }

        public bool FocusTaskbar() { Sent.Add("focus_taskbar"); return true; }
    }

    private sealed class FakeHost : IKeyboardInputHost
    {
        private Action<KeyboardKeyEvent>? _keyReceived;

        public bool StartSucceeds { get; set; } = true;

        public int Holders { get; private set; }

        public int Subscribers => _keyReceived?.GetInvocationList().Length ?? 0;

        public event Action<KeyboardKeyEvent>? KeyReceived
        {
            add => _keyReceived += value;
            remove => _keyReceived -= value;
        }

        public event Action? PointerInteraction { add { } remove { } }

        public event Action<MouseWheelEvent>? WheelObserved { add { } remove { } }

        public event Action? FocusChanged { add { } remove { } }

        public event Action? DrainRequested { add { } remove { } }

        public void SetWheelInterceptor(IWheelInterceptor? interceptor) { }

        public void RequestDrain() { }

        public bool Start()
        {
            if (!StartSucceeds) return false;
            Holders++;
            return true;
        }

        public void Stop() => Holders--;

        public void Raise(ushort virtualKey, bool down, bool injected = false) =>
            _keyReceived?.Invoke(new KeyboardKeyEvent(virtualKey, 0, down, false, injected, 0));
    }
}
