using Deckle.Diagnostics;
using Deckle.Modules;
using Deckle.Shell.TrayMenu;
using Xunit;

namespace Deckle.App.Tests;

// The tray's reading of the user incidents: which line each one folds into,
// in what order, and where the lines sit in the left-click menu. Built from
// UserIncident records by hand, so the process-wide registry is never touched.
[Trait("Category", "unit")]
public sealed class TrayIncidentLinesTests
{
    [Fact]
    public void EveryIncidentIdIsDeclaredWithAModuleThatHasALabel()
    {
        var declared = TrayIncidentLines.Sources.Select(s => s.IncidentId).ToList();

        Assert.Equal(UserIncidents.All.Order(), declared.Order());
        Assert.All(TrayIncidentLines.Sources,
            s => Assert.True(TrayIncidentLines.HasLabel(s.Module), s.Module));
    }

    [Fact]
    public void TwoSourcesOfOneModuleFoldIntoOneLineOpeningTheFirstSourcesPage()
    {
        IReadOnlyList<TrayIncidentLines.Line> lines = TrayIncidentLines.Fold(
        [
            Declared(UserIncidents.WhisperModel),
            Declared(UserIncidents.VadModel),
        ]);

        var line = Assert.Single(lines);
        Assert.Equal(ModuleIds.Transcription, line.Module);
        Assert.Equal(PageOf(UserIncidents.WhisperModel), line.PageTag);
    }

    [Fact]
    public void ALineFollowsTheOrderTheRegistryHandsTheIncidentsIn()
    {
        IReadOnlyList<TrayIncidentLines.Line> lines = TrayIncidentLines.Fold(
        [
            Declared(UserIncidents.Microphone),
            Declared(UserIncidents.HuePush),
            Declared(UserIncidents.ToastsElevated),
        ]);

        Assert.Equal(
            [TrayIncidentLines.RecordingModule, ModuleIds.Ambient, TrayIncidentLines.NotificationsModule],
            lines.Select(l => l.Module));
    }

    [Fact]
    public void AnUndeclaredIncidentHasNoLine()
    {
        IReadOnlyList<TrayIncidentLines.Line> lines = TrayIncidentLines.Fold(
            [new UserIncident("unknown", "reason", Module: null, PageTag: null)]);

        Assert.Empty(lines);
    }

    [Fact]
    public void LinesGoAboveTheRestBehindTheirOwnSeparator()
    {
        TrayMenuEntry line = Command("incident-ambient");
        TrayMenuEntry record = Command("record");

        IReadOnlyList<TrayMenuEntry> entries = TrayIncidentLines.Compose([line], [record]);

        Assert.Collection(entries,
            e => Assert.Same(line, e),
            e => Assert.IsType<TrayMenuSeparator>(e),
            e => Assert.Same(record, e));
    }

    [Fact]
    public void WithNoLineTheMenuIsUnchanged()
    {
        IReadOnlyList<TrayMenuEntry> rest = [Command("record")];

        Assert.Same(rest, TrayIncidentLines.Compose([], rest));
    }

    private static UserIncident Declared(string id)
    {
        TrayIncidentLines.Source source = TrayIncidentLines.Sources.Single(s => s.IncidentId == id);
        return new UserIncident(id, "reason", source.Module, source.PageTag);
    }

    private static string PageOf(string id)
        => TrayIncidentLines.Sources.Single(s => s.IncidentId == id).PageTag;

    private static TrayMenuCommand Command(string id) => new(id, () => id, () => { });
}
