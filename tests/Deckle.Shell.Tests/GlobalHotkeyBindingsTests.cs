using Deckle.Shell;
using Xunit;

namespace Deckle.Shell.Tests;

[Trait("Category", "unit")]
public sealed class GlobalHotkeyBindingsTests
{
    private static readonly GlobalHotkeyBinding[] Requested =
    [
        new(1, 0x10),
        new(2, 0x20),
        new(3, 0x40),
    ];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FailedAcquisitionReleasesEveryEarlierChord(int failingId)
    {
        var api = new FakeApi { FailingId = failingId, LastError = 1409 };
        var bindings = new GlobalHotkeyBindings(new IntPtr(42), Requested, api);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => bindings.Register(virtualKey: 0xC0));

        Assert.Contains("Win32 err 1409", error.Message, StringComparison.Ordinal);
        Assert.Equal(
            Requested.Take(failingId - 1).Select(binding => binding.Id).Reverse(),
            api.UnregisteredIds);
    }

    [Fact]
    public void ReregistrationReleasesThePreviousSetBeforeAcquiringTheNewOne()
    {
        var api = new FakeApi();
        var bindings = new GlobalHotkeyBindings(new IntPtr(42), Requested[..2], api);
        bindings.Register(virtualKey: 0xC0);

        bindings.Register(virtualKey: 0xDF);

        Assert.Equal(new[] { 2, 1 }, api.UnregisteredIds);
        Assert.Equal(
            new[] { (1, 0xC0u), (2, 0xC0u), (1, 0xDFu), (2, 0xDFu) },
            api.Registered.Select(call => (call.Id, call.VirtualKey)));
    }

    [Fact]
    public void FailedReleaseRemainsTrackedForTheNextAttempt()
    {
        var api = new FakeApi();
        var bindings = new GlobalHotkeyBindings(new IntPtr(42), Requested[..1], api);
        bindings.Register(virtualKey: 0xC0);
        api.UnregisterFailures.Add(1);

        Assert.False(bindings.Unregister());
        api.UnregisterFailures.Clear();

        Assert.True(bindings.Unregister());
        Assert.Equal(new[] { 1 }, api.UnregisteredIds);
    }

    private sealed class FakeApi : IGlobalHotkeyApi
    {
        public int? FailingId;
        public int LastError { get; set; }
        public HashSet<int> UnregisterFailures { get; } = [];
        public List<(int Id, uint Modifiers, uint VirtualKey)> Registered { get; } = [];
        public List<int> UnregisteredIds { get; } = [];

        public bool Register(IntPtr window, int id, uint modifiers, uint virtualKey)
        {
            if (id == FailingId) return false;
            Registered.Add((id, modifiers, virtualKey));
            return true;
        }

        public bool Unregister(IntPtr window, int id)
        {
            if (UnregisterFailures.Contains(id)) return false;
            UnregisteredIds.Add(id);
            return true;
        }
    }
}
