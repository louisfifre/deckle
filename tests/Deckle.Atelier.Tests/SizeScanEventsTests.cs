using System.Diagnostics.Tracing;
using System.Security.AccessControl;
using System.Security.Principal;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Atelier.Tests;

// A size analysis writes its start and its end, paired by one id, and one
// Warning when folders could not be read; no event carries a path.
[Collection("atelier provider singleton")]
[Trait("Category", "observability")]
public sealed class SizeScanEventsTests
{
    [Fact]
    public async Task AnAnalysisIsAStartAndAnEndPairedByItsId()
    {
        using var scratch = new ScratchFolder();
        scratch.File("abc", "Sub", "a.bin");
        using var listener = new TestEventListener("Deckle-Atelier");

        await SizeScan.Start(scratch.Path).Completion.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(
            [DeckleAtelierSource.EvtSizeScanStarted, DeckleAtelierSource.EvtSizeScanEnded],
            listener.Events.Select(e => e.EventId));
        Assert.Equal(listener.Events[0].Payload?[0], listener.Events[1].Payload?[0]);
        Assert.Equal("Completed", listener.Events[1].Payload?[1]);
        Assert.DoesNotContain(listener.Events.SelectMany(e => e.Payload ?? []),
            value => value is string text && text.Contains(scratch.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnreadableFoldersAreOneWarningWithTheirCount()
    {
        using var scratch = new ScratchFolder();
        string locked = scratch.Folder("Locked");
        var info = new DirectoryInfo(locked);
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory, AccessControlType.Deny);
        DirectorySecurity security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        try
        {
            using var listener = new TestEventListener("Deckle-Atelier");

            await SizeScan.Start(scratch.Path).Completion.WaitAsync(TimeSpan.FromSeconds(30));

            EventWrittenEventArgs warning = Assert.Single(listener.Events, e => e.Level == EventLevel.Warning);
            Assert.Equal(DeckleAtelierSource.EvtSizeScanPartial, warning.EventId);
            Assert.Equal(1L, warning.Payload?[1]);
        }
        finally
        {
            DirectorySecurity back = info.GetAccessControl();
            back.RemoveAccessRule(rule);
            info.SetAccessControl(back);
        }
    }
}
