using Deckle.Shell;
using Xunit;

namespace Deckle.Shell.Tests;

// Contract: each chord is acquired on its own, failures come back as a list
// (never thrown), and every pass releases the previous set first.
[Trait("Category", "unit")]
public sealed class GlobalHotkeyBindingsTests
{
    private const int AlreadyRegistered = 1409;

    private static readonly GlobalHotkeyBinding[] Requested =
    [
        new(1, 0x08, 0x29),
        new(2, 0x02, 0x1E),
        new(3, 0x04, 0xE04B),
    ];

    // Layout stand-in: scancode → VK.
    private static uint Resolve(uint scanCode) => scanCode + 0x100;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FailingChordIsReportedAndDoesNotPreventTheOthers(int failingId)
    {
        var api = new FakeApi { FailingId = failingId, LastError = AlreadyRegistered };
        var bindings = new GlobalHotkeyBindings(new IntPtr(42), api);

        IReadOnlyList<HotkeyRegistrationFailure> failures = bindings.Register(Requested, Resolve);

        Assert.Equal([new HotkeyRegistrationFailure(failingId, AlreadyRegistered)], failures);
        Assert.Equal(
            Requested.Where(binding => binding.Id != failingId).Select(binding => binding.Id),
            api.Registered.Select(call => call.Id));
        Assert.Empty(api.UnregisteredIds);
    }

    [Fact]
    public void EachChordIsRegisteredWithItsModifiersAndResolvedKey()
    {
        var api = new FakeApi();
        var bindings = new GlobalHotkeyBindings(new IntPtr(42), api);

        bindings.Register(Requested, Resolve);

        Assert.Equal(
            Requested.Select(binding => (binding.Id, binding.Modifiers, Resolve(binding.ScanCode))),
            api.Registered);
    }

    [Fact]
    public void ReregistrationReleasesThePreviousSetBeforeAcquiringTheNewOne()
    {
        var api = new FakeApi();
        var bindings = new GlobalHotkeyBindings(new IntPtr(42), api);
        bindings.Register(Requested[..2], Resolve);

        bindings.Register(Requested[2..], Resolve);

        Assert.Equal([2, 1], api.UnregisteredIds);
        Assert.Equal([1, 2, 3], api.Registered.Select(call => call.Id));
        Assert.Equal(["register 1", "register 2", "release 2", "release 1", "register 3"], api.Calls);
    }

    [Fact]
    public void FailedReleaseRemainsTrackedForTheNextAttempt()
    {
        var api = new FakeApi();
        var bindings = new GlobalHotkeyBindings(new IntPtr(42), api);
        bindings.Register(Requested[..1], Resolve);
        api.UnregisterFailures.Add(1);

        Assert.False(bindings.Unregister());
        api.UnregisterFailures.Clear();

        Assert.True(bindings.Unregister());
        Assert.Equal([1], api.UnregisteredIds);
    }

    [Fact]
    public void ChordStillHeldAfterAFailedReleaseIsReportedNotStacked()
    {
        var api = new FakeApi();
        var bindings = new GlobalHotkeyBindings(new IntPtr(42), api);
        bindings.Register(Requested[..1], Resolve);
        api.UnregisterFailures.Add(1);
        api.LastError = 5;

        IReadOnlyList<HotkeyRegistrationFailure> failures = bindings.Register(Requested[..1], Resolve);

        Assert.Equal([new HotkeyRegistrationFailure(1, 5)], failures);
        Assert.Single(api.Registered);
    }

    [Fact]
    public void UnresolvableScanCodeIsReportedNotThrown()
    {
        var api = new FakeApi();
        var bindings = new GlobalHotkeyBindings(new IntPtr(42), api);

        IReadOnlyList<HotkeyRegistrationFailure> failures = bindings.Register(
            Requested, scanCode => scanCode == 0x1E ? 0u : Resolve(scanCode));

        Assert.Equal([new HotkeyRegistrationFailure(2, 0)], failures);
        Assert.Equal([1, 3], api.Registered.Select(call => call.Id));
    }

    private sealed class FakeApi : IGlobalHotkeyApi
    {
        public int? FailingId;
        public int LastError { get; set; }
        public HashSet<int> UnregisterFailures { get; } = [];
        public List<(int Id, uint Modifiers, uint VirtualKey)> Registered { get; } = [];
        public List<int> UnregisteredIds { get; } = [];
        public List<string> Calls { get; } = [];

        public bool Register(IntPtr window, int id, uint modifiers, uint virtualKey)
        {
            if (id == FailingId) return false;
            Registered.Add((id, modifiers, virtualKey));
            Calls.Add($"register {id}");
            return true;
        }

        public bool Unregister(IntPtr window, int id)
        {
            if (UnregisterFailures.Contains(id)) return false;
            UnregisteredIds.Add(id);
            Calls.Add($"release {id}");
            return true;
        }
    }
}
