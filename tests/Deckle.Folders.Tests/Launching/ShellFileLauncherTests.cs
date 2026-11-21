using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Folders.Tests;

// The real launcher on paths the test makes sure are gone. An item that
// exists would open an application or a File Explorer window on the machine
// running the tests, so that path is left to the self-check's eyes and to the
// person: what can be checked here is that a missing item opens nothing,
// says so, and logs it without its name or its path.
[Collection("folders provider singleton")]
[Trait("Category", "observability")]
public sealed class ShellFileLauncherTests
{
    [Fact]
    public void AMissingFileOpensNothingAndSaysItIsMissing()
    {
        using var scratch = new ScratchFolder();
        string gone = Path.Combine(scratch.Path, "secret-report.pdf");
        using var listener = new TestEventListener("Deckle-Folders");

        LaunchOutcome outcome = new ShellFileLauncher().Open(gone);

        Assert.Equal(LaunchStatus.Missing, outcome.Status);
        EventWrittenEventArgs ev = Assert.Single(listener.Events, e => e.EventId == DeckleFoldersSource.EvtItemNotLaunched);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.Equal(["open", ".pdf", "missing", 0], ev.Payload);
        AssertNoPathOrName(listener, scratch.Path, "secret-report");
    }

    [Fact]
    public void AMissingItemIsNotShownInFileExplorer()
    {
        using var scratch = new ScratchFolder();
        string gone = Path.Combine(scratch.Path, "secret-folder");
        using var listener = new TestEventListener("Deckle-Folders");

        LaunchOutcome outcome = new ShellFileLauncher().ShowInFolder(gone);

        Assert.Equal(LaunchStatus.Missing, outcome.Status);
        EventWrittenEventArgs ev = Assert.Single(listener.Events, e => e.EventId == DeckleFoldersSource.EvtItemNotLaunched);
        Assert.Equal("show-in-folder", ev.Payload?[0]);
        AssertNoPathOrName(listener, scratch.Path, "secret-folder");
    }

    private static void AssertNoPathOrName(TestEventListener listener, string root, string name)
    {
        foreach (EventWrittenEventArgs e in listener.Events)
        {
            foreach (object? value in e.Payload ?? [])
            {
                string text = value?.ToString() ?? "";
                Assert.DoesNotContain(root, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(name, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
