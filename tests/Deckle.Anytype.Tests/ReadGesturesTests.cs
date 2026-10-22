using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for ReadGestures over the shared FakeAnytypeServer (defined
// in TaskGesturesTests.cs). A typed read is a projection: its promise is the
// shape of the string it returns, so most assertions compare the whole digest,
// line by line (`id : …` first, then one fact per line). Selectors are bafy*
// ids so the resolver short-circuits and no /search route is needed; a linked
// object is read by name through one extra GET, which the tests count.
[Trait("Category", "integration")]
public class ReadGesturesTests
{
    const string TaskId      = "bafyreiTaskaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string OtherTaskId = "bafyreiTask2aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string ProjectId   = "bafyreiprojectaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string ReportId    = "bafyreiReportaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string IdeaId      = "bafyreiIdeeaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string DocId       = "bafyreiDocaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static ReadGestures NewGestures(FakeAnytypeServer server)
    {
        var client = new AnytypeApiClient(server.Credentials);
        return new ReadGestures(client, new NameResolver(client));
    }

    // A GET response: the object under the "object" wrapper, as the API sends it.
    static JsonObject Wrap(string id, string name, string typeKey, string markdown, params JsonObject[] properties)
    {
        var props = new JsonArray();
        foreach (JsonObject property in properties) props.Add(property);

        return new JsonObject
        {
            ["object"] = new JsonObject
            {
                ["id"] = id,
                ["name"] = name,
                ["type"] = new JsonObject { ["key"] = typeKey },
                ["markdown"] = markdown,
                ["properties"] = props,
            },
        };
    }

    static JsonObject TaskOf(string markdown, params JsonObject[] properties) =>
        Wrap(TaskId, "Ma tâche", DevSpace.Types.Task, markdown, properties);

    static JsonObject DocOf(string markdown, params JsonObject[] properties) =>
        Wrap(DocId, "Doc", DevSpace.Types.Document, markdown, properties);

    // Anytype's own bin: a root flag, distinct from the « Archivé » checkbox.
    static JsonObject Binned(JsonObject wrapper)
    {
        wrapper["object"]!["archived"] = true;
        return wrapper;
    }

    static JsonObject TextProp(string key, string value) =>
        new() { ["key"] = key, ["format"] = "text", ["text"] = value };

    static JsonObject NumberProp(string key, double value) =>
        new() { ["key"] = key, ["format"] = "number", ["number"] = value };

    static JsonObject SelectProp(string key, string value) =>
        new() { ["key"] = key, ["format"] = "select", ["select"] = value };

    static JsonObject MultiSelectProp(string key, params string[] values)
    {
        var array = new JsonArray();
        foreach (string value in values) array.Add(value);
        return new() { ["key"] = key, ["format"] = "multi_select", ["multi_select"] = array };
    }

    static JsonObject DateProp(string key, string iso) =>
        new() { ["key"] = key, ["format"] = "date", ["date"] = iso };

    static JsonObject CheckboxProp(string key, bool value) =>
        new() { ["key"] = key, ["format"] = "checkbox", ["checkbox"] = value };

    static JsonObject FilesProp(string key, int count)
    {
        var array = new JsonArray();
        for (int i = 0; i < count; i++) array.Add($"file{i}");
        return new() { ["key"] = key, ["format"] = "files", ["files"] = array };
    }

    static JsonObject ObjectsProp(string key, params string[] ids)
    {
        var array = new JsonArray();
        foreach (string id in ids) array.Add(id);
        return new() { ["key"] = key, ["format"] = "objects", ["objects"] = array };
    }

    static string Lines(params string[] lines) => string.Join("\n", lines);

    // ── TaskPropertiesAsync ───────────────────────────────────────────────────

    // The properties are given in a shuffled order on purpose: the digest order
    // is the type's (DevSpace.PropertiesFor), not the object's. Done is folded
    // into the title box, the empty contact link and the unmapped « tag » never
    // show, the project link is read by name, the date is cut to its day.
    [Fact]
    public async Task TaskPropertiesRendersTheTitleBoxThenEachPropertyInTheTypesOrder()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskOf(
            "## Details\ncorps qui ne doit pas sortir",
            TextProp(DevSpace.Props.DefinitionDeFini, "Aucune action manuelle requise."),
            ObjectsProp(DevSpace.Props.ContactLie),
            SelectProp(DevSpace.Props.Priorite, "67c6d722341c1628147d7b1e"),
            TextProp(DevSpace.Props.Description, "Reprendre après la veille."),
            SelectProp(DevSpace.Props.Etat, "en_cours"),
            DateProp(DevSpace.Props.DueDate, "2026-07-21T00:00:00Z"),
            ObjectsProp(DevSpace.Props.RelationProjet, ProjectId),
            CheckboxProp(DevSpace.Props.Done, true),
            FilesProp(DevSpace.Props.FichiersLies, 2),
            MultiSelectProp(DevSpace.Props.Livrables, "document_de_cadrage"),
            SelectProp(DevSpace.Props.TypeDeTache, "production"),
            NumberProp(DevSpace.Props.ChargeEstimee, 3),
            NumberProp(DevSpace.Props.ChargeReelle, 1.5),
            MultiSelectProp(DevSpace.Props.Tag, "urgent"),
            CheckboxProp(DevSpace.Props.Archive, true)));
        server.OnGetObject(ProjectId, Wrap(ProjectId, "Deckle", DevSpace.Types.Project, ""));

        string digest = await NewGestures(server).TaskPropertiesAsync(TaskId, Ct);

        Assert.Equal(
            Lines(
                $"id : {TaskId}",
                "[x] Ma tâche",
                "Description : Reprendre après la veille.",
                "État : En cours",
                "Priorité : 4",
                "Type de tâche : Produire",
                "Date cible : 2026-07-21",
                "Projet(s) lié(s) : Deckle",
                "Fichier(s) lié(s) : 2 fichier(s)",
                "Livrable(s) : Texte",
                "Définition de fini : Aucune action manuelle requise.",
                "Charge estimée : 3",
                "Charge réelle : 1.5",
                "Archivé : oui"),
            digest);

        // The task, then the one project link: one GET per link, nothing else.
        Assert.Equal(2, server.Requests.Count(r => r.Method == "GET"));
    }

    // The key is written out because it is the promise: the server serves
    // Priorité under "priorite", and reading it under the space's opaque
    // relation id dropped every priority from the digests.
    [Fact]
    [Trait("Category", "regression")]
    public async Task TaskPropertiesReadsThePriorityUnderTheKeyTheServerServes()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskOf("", SelectProp("priorite", DevSpace.Priority.KeyFor(4))));

        string digest = await NewGestures(server).TaskPropertiesAsync(TaskId, Ct);

        Assert.Contains("Priorité : 4", digest.Split('\n'));
    }

    // Silence reads as « non »: an unticked checkbox adds no line, and a task
    // with nothing set is just its identity and its open box.
    [Fact]
    public async Task TaskPropertiesShowsAnOpenBoxAndNoLineForUntickedCheckboxes()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskOf(
            "",
            CheckboxProp(DevSpace.Props.Done, false),
            CheckboxProp(DevSpace.Props.Archive, false)));

        string digest = await NewGestures(server).TaskPropertiesAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche", digest);
        Assert.Equal(1, server.Requests.Count(r => r.Method == "GET"));
    }

    [Fact]
    public async Task TaskPropertiesJoinsSeveralLinkedObjectsByName()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskOf("", ObjectsProp(DevSpace.Props.RelationProjet, ProjectId, OtherTaskId)));
        server.OnGetObject(ProjectId, Wrap(ProjectId, "Deckle", DevSpace.Types.Project, ""));
        server.OnGetObject(OtherTaskId, Wrap(OtherTaskId, "Autre chantier", DevSpace.Types.Project, ""));

        string digest = await NewGestures(server).TaskPropertiesAsync(TaskId, Ct);

        Assert.Equal(
            Lines($"id : {TaskId}", "[ ] Ma tâche", "Projet(s) lié(s) : Deckle, Autre chantier"),
            digest);
        Assert.Equal(3, server.Requests.Count(r => r.Method == "GET"));
    }

    [Fact]
    public async Task TaskPropertiesFlagsAnObjectInAnytypesBinWithACorbeilleLine()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, Binned(TaskOf("")));

        string digest = await NewGestures(server).TaskPropertiesAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche\nCorbeille : oui", digest);
    }

    [Fact]
    public async Task TaskPropertiesOfALiveObjectHasNoCorbeilleLine()
    {
        using var server = new FakeAnytypeServer();
        JsonObject live = TaskOf("");
        live["object"]!["archived"] = false;
        server.OnGetObject(TaskId, live);

        string digest = await NewGestures(server).TaskPropertiesAsync(TaskId, Ct);

        Assert.DoesNotContain("Corbeille", digest);
    }

    // The typed read refuses another type rather than projecting it with the
    // wrong property table, and says what the object really is.
    [Fact]
    public async Task TaskPropertiesOfAProjectThrowsNamingTheActualType()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, Wrap(TaskId, "Deckle", DevSpace.Types.Project, ""));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).TaskPropertiesAsync(TaskId, Ct));

        Assert.Equal(
            "Deckle est un objet project, pas task : lis-le avec la lecture de son type.",
            error.Message);
    }

    // ── TaskChecklistAsync ────────────────────────────────────────────────────

    // The template's empty `- [ ] ` slot is not a subtask, labels come back
    // unescaped and trimmed, and the count only covers real items.
    [Fact]
    public async Task TaskChecklistListsTheItemsThenTheCheckedCount()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskOf(
            $"## {ReadGestures.ChecklistHeading}\n---\n- [x] fait\n- [ ] module\\_X à relire   \n- [ ] \n## {ReadGestures.NotesHeading}\n---"));

        string digest = await NewGestures(server).TaskChecklistAsync(TaskId, Ct);

        Assert.Equal(
            Lines(
                $"id : {TaskId}",
                "[ ] Ma tâche",
                "- [x] fait",
                "- [ ] module_X à relire",
                "Cochées : 1 / 2"),
            digest);
    }

    [Fact]
    public async Task TaskChecklistWithOnlyTheTemplateSlotSaysThereAreNoSubtasks()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskOf(
            $"## {ReadGestures.ChecklistHeading}\n---\n- [ ] \n## {ReadGestures.NotesHeading}\n---"));

        string digest = await NewGestures(server).TaskChecklistAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche\nAucune sous-tâche.", digest);
    }

    [Fact]
    public async Task TaskChecklistOfADoneTaskCarriesTheCheckedBoxInItsTitle()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskOf("- [x] seule", CheckboxProp(DevSpace.Props.Done, true)));

        string digest = await NewGestures(server).TaskChecklistAsync(TaskId, Ct);

        Assert.Equal(
            Lines($"id : {TaskId}", "[x] Ma tâche", "- [x] seule", "Cochées : 1 / 1"),
            digest);
    }

    // ── TaskNotesAsync ────────────────────────────────────────────────────────

    // The notes are the text under the notes heading; the checklist sits in its
    // own section so it stays out, and the next section ends the notes.
    [Fact]
    public async Task TaskNotesReturnsTheCleanedTextUnderTheNotesHeadingOnly()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskOf(
            $"## {ReadGestures.ChecklistHeading}\n---\n- [ ] une étape\n" +
            $"## {ReadGestures.NotesHeading}\n---\nAttention au cache.   \nLigne\\_deux\n\n## Autre\nhors notes"));

        string digest = await NewGestures(server).TaskNotesAsync(TaskId, Ct);

        Assert.Equal(
            Lines($"id : {TaskId}", "[ ] Ma tâche", "Attention au cache.", "Ligne_deux"),
            digest);
    }

    // « À savoir » is the name proposed for the section: reading it keeps the
    // gesture working once the template is renamed in Anytype.
    [Fact]
    public async Task TaskNotesAlsoReadsTheAlternativeNotesHeading()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskOf(
            $"## {ReadGestures.ChecklistHeading}\n- [ ] une étape\n## À savoir\nUne note."));

        string digest = await NewGestures(server).TaskNotesAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche\nUne note.", digest);
    }

    [Fact]
    public async Task TaskNotesWithAnEmptyNotesSectionSaysThereAreNoNotes()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskOf(
            $"## {ReadGestures.ChecklistHeading}\n- [ ] une étape\n## {ReadGestures.NotesHeading}\n---\n"));

        string digest = await NewGestures(server).TaskNotesAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche\nAucune note.", digest);
    }

    // A task written outside the template has no notes heading: the gesture
    // falls back to the body minus its checklist lines and checklist heading.
    [Fact]
    public async Task TaskNotesWithoutANotesHeadingFallsBackToTheNonChecklistLines()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskOf(
            $"Texte libre.\n- [x] fait\n## {ReadGestures.ChecklistHeading}\nAutre ligne"));

        string digest = await NewGestures(server).TaskNotesAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche\nTexte libre.\nAutre ligne", digest);
    }

    [Fact]
    public async Task TaskNotesOfAnEmptyBodySaysThereAreNoNotes()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskOf(""));

        string digest = await NewGestures(server).TaskNotesAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche\nAucune note.", digest);
    }

    // ── ReportJournalAsync ────────────────────────────────────────────────────

    static JsonObject ReportOf(string markdown, params string[] linkedTaskIds) => Wrap(
        ReportId, "Journal 2026-07-21", DevSpace.Types.Rapport, markdown,
        DateProp(DevSpace.Props.DateDuJournal, "2026-07-21T00:00:00Z"),
        ObjectsProp(DevSpace.Props.TachesLiees, linkedTaskIds));

    static string[] Entries(int count) =>
        Enumerable.Range(1, count).Select(i => $"- entrée {i}").ToArray();

    [Fact]
    public async Task ReportJournalShowsTheLastTwentyEntriesByDefaultAndCountsTheOlderOnes()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ReportId, ReportOf("# Journal 2026-07-21\n" + Lines(Entries(25)), TaskId, OtherTaskId));
        server.OnGetObject(TaskId, Wrap(TaskId, "Tâche A", DevSpace.Types.Task, ""));
        server.OnGetObject(OtherTaskId, Wrap(OtherTaskId, "Tâche B", DevSpace.Types.Task, ""));

        string digest = await NewGestures(server).ReportJournalAsync(ReportId, ct: Ct);

        Assert.Equal(
            Lines(
                new[]
                {
                    $"id : {ReportId}",
                    "Date du journal : 2026-07-21",
                    "Tâches liées : Tâche A, Tâche B",
                    "… 5 entrée(s) avant",
                }
                .Concat(Entries(25).Skip(5)).ToArray()),
            digest);
    }

    [Fact]
    public async Task ReportJournalHonoursAnExplicitLastAndExcludesTheTitleHeading()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ReportId, ReportOf("# Journal 2026-07-21\n" + Lines(Entries(4))));

        string digest = await NewGestures(server).ReportJournalAsync(ReportId, last: 2, ct: Ct);

        Assert.Equal(
            Lines(
                $"id : {ReportId}",
                "Date du journal : 2026-07-21",
                "… 2 entrée(s) avant",
                "- entrée 3",
                "- entrée 4"),
            digest);
    }

    // session_log writes a stamp then the text as two paragraphs: the tail
    // counts and shows whole entries, after the one-bullet entries of a report
    // started before the stamped form.
    [Fact]
    public async Task ReportJournalCountsAStampedEntryAsOneEntryWithItsText()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ReportId, ReportOf(
            "# Journal 2026-07-21\n- ancienne\n2026-07-21 10:00   \n\npremière   \n\n" +
            "2026-07-21 11:30\n\nseconde\n\nsuite"));

        string digest = await NewGestures(server).ReportJournalAsync(ReportId, last: 2, ct: Ct);

        Assert.Equal(
            Lines(
                $"id : {ReportId}",
                "Date du journal : 2026-07-21",
                "… 1 entrée(s) avant",
                "2026-07-21 10:00 · première",
                "2026-07-21 11:30 · seconde",
                "suite"),
            digest);
    }

    // At or under the limit nothing is skipped, so no marker; lines come back
    // cleaned (unescaped, trailing spaces gone, blank lines and dividers dropped).
    [Fact]
    public async Task ReportJournalOfAShortBodyShowsEveryCleanedLineWithoutAMarker()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ReportId, ReportOf("# Journal 2026-07-21\n\n- module\\_X ok   \n---\n- fin"));

        string digest = await NewGestures(server).ReportJournalAsync(ReportId, last: 2, ct: Ct);

        Assert.Equal(
            Lines($"id : {ReportId}", "Date du journal : 2026-07-21", "- module_X ok", "- fin"),
            digest);
    }

    // A report fresh out of session_start reads back with no markdown at all.
    [Theory]
    [InlineData("")]
    [InlineData("# Journal 2026-07-21")]
    public async Task ReportJournalWithNoEntriesSaysTheJournalIsEmpty(string markdown)
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ReportId, ReportOf(markdown));

        string digest = await NewGestures(server).ReportJournalAsync(ReportId, ct: Ct);

        Assert.Equal(
            Lines($"id : {ReportId}", "Date du journal : 2026-07-21", "Journal vide."),
            digest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task ReportJournalRefusesALastBelowOneBeforeAnyRequest(int last)
    {
        using var server = new FakeAnytypeServer();

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).ReportJournalAsync(ReportId, last, Ct));

        Assert.Empty(server.Requests);
    }

    // ── IdeaTextAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task IdeaTextReturnsTheTitleThenTheCleanedTextAndTheArchivedFlag()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(IdeaId, Wrap(
            IdeaId, "Une idée", DevSpace.Types.Idee,
            "Texte de l'idée.\n\nSuite\\_x   \n---",
            CheckboxProp(DevSpace.Props.Archive, true)));

        string digest = await NewGestures(server).IdeaTextAsync(IdeaId, Ct);

        Assert.Equal(
            Lines($"id : {IdeaId}", "Une idée", "Texte de l'idée.", "Suite_x", "Archivé : oui"),
            digest);
    }

    [Fact]
    public async Task IdeaTextWithNoBodyAndNoArchiveIsJustItsIdentity()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(IdeaId, Wrap(
            IdeaId, "Une idée", DevSpace.Types.Idee, "",
            CheckboxProp(DevSpace.Props.Archive, false)));

        string digest = await NewGestures(server).IdeaTextAsync(IdeaId, Ct);

        Assert.Equal($"id : {IdeaId}\nUne idée", digest);
    }

    // ── DocumentOutlineAsync ──────────────────────────────────────────────────

    // Sizes are cleaned characters: a section counts its sub-sections, which is
    // why « Alpha » outweighs « Sous »; a heading comes back unescaped.
    [Fact]
    public async Task DocumentOutlineListsThePropertiesThePreambleSizeAndEachHeadingWithItsSize()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(DocId, DocOf(
            "Intro courte.\n\n## Alpha\nabc\ndef\n### Sous\nxy\n## Beta\\_bis\n---\n",
            SelectProp(DevSpace.Props.TypeDeDocument, "reference"),
            CheckboxProp(DevSpace.Props.DocumentSysteme, true),
            TextProp(DevSpace.Props.Version, "1.2")));

        string digest = await NewGestures(server).DocumentOutlineAsync(DocId, Ct);

        Assert.Equal(
            Lines(
                $"id : {DocId}",
                "Doc",
                "Type de document : Référence",
                "Document système : oui",
                "Version : 1.2",
                "Préambule : 13 car.",
                "## Alpha (19 car.)",
                "### Sous (2 car.)",
                "## Beta_bis (0 car.)"),
            digest);
    }

    [Fact]
    public async Task DocumentOutlineOfABodyWithoutHeadingsReportsOnlyThePreamble()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(DocId, DocOf("Texte seul."));

        string digest = await NewGestures(server).DocumentOutlineAsync(DocId, Ct);

        Assert.Equal($"id : {DocId}\nDoc\nPréambule : 11 car.", digest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("---\n")]
    public async Task DocumentOutlineOfAnEmptyBodySaysTheBodyIsEmpty(string markdown)
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(DocId, DocOf(markdown));

        string digest = await NewGestures(server).DocumentOutlineAsync(DocId, Ct);

        Assert.Equal($"id : {DocId}\nDoc\nCorps vide.", digest);
    }

    // ── DocumentSectionAsync ──────────────────────────────────────────────────

    const string SectionBody = "Intro courte.\n\n## Alpha\nabc\\_d   \n---\n### Sous\nxy\n## Beta\\_bis\n---\n";

    // No heading, a null, an empty or a blank one all mean the preamble.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DocumentSectionWithoutAHeadingReadsThePreamble(string? heading)
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(DocId, DocOf(SectionBody));

        string text = await NewGestures(server).DocumentSectionAsync(DocId, heading, Ct);

        Assert.Equal($"id : {DocId}\nDoc\nIntro courte.", text);
    }

    // The section keeps its sub-sections, ends at the next heading of its own
    // level, and comes back cleaned.
    [Fact]
    public async Task DocumentSectionReadsTheCleanedTextUnderTheHeadingSubSectionsIncluded()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(DocId, DocOf(SectionBody));

        string text = await NewGestures(server).DocumentSectionAsync(DocId, "Alpha", Ct);

        Assert.Equal(
            Lines($"id : {DocId}", "Doc", "## Alpha", "abc_d", "### Sous", "xy"),
            text);
    }

    [Fact]
    public async Task DocumentSectionFindsAnEscapedHeadingFromItsNaturalSpelling()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(DocId, DocOf("## Beta\\_bis\ncorps"));

        string text = await NewGestures(server).DocumentSectionAsync(DocId, "Beta_bis", Ct);

        Assert.Equal($"id : {DocId}\nDoc\n## Beta_bis\ncorps", text);
    }

    // The error is the retry hint: it lists the headings the body does carry.
    [Fact]
    public async Task DocumentSectionWithAnUnknownHeadingThrowsListingThePresentHeadings()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(DocId, DocOf(SectionBody));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).DocumentSectionAsync(DocId, "Zeta", Ct));

        Assert.Equal(
            "Section « Zeta » introuvable dans Doc. Titres présents : « Alpha », « Sous », « Beta_bis ».",
            error.Message);
    }

    [Fact]
    public async Task DocumentSectionWithAnUnknownHeadingInAHeadinglessBodySaysThereAreNone()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(DocId, DocOf("Texte seul."));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).DocumentSectionAsync(DocId, "Zeta", Ct));

        Assert.Equal(
            "Section « Zeta » introuvable dans Doc. Titres présents : (aucun).",
            error.Message);
    }

    [Fact]
    public async Task DocumentSectionWithNothingUnderItsHeadingSaysTheSectionIsEmpty()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(DocId, DocOf(SectionBody));

        string text = await NewGestures(server).DocumentSectionAsync(DocId, "Beta_bis", Ct);

        Assert.Equal($"id : {DocId}\nDoc\n## Beta_bis\nSection vide.", text);
    }

    // A body that opens on a heading has no preamble to read.
    [Fact]
    public async Task DocumentSectionReadingThePreambleOfABodyThatOpensOnAHeadingSaysTheSectionIsEmpty()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(DocId, DocOf("## Alpha\nabc"));

        string text = await NewGestures(server).DocumentSectionAsync(DocId, null, Ct);

        Assert.Equal($"id : {DocId}\nDoc\nSection vide.", text);
    }
}
