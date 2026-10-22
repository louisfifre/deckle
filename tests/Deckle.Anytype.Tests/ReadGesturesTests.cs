using System.Net;
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

    // The v2 reads: the replies are queued in the order the gesture asks them
    // (properties, then markdown, then one properties read per linked name).
    static ReadGestures NewGestures(FakeV2Handler v2)
    {
        var credentials = new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1");
        var client = new AnytypeApiClient(credentials);
        return new ReadGestures(new NameResolver(client), new AnytypeV2Client(credentials, null, v2));
    }

    static FakeV2Handler Reads(params string[] replies)
    {
        var handler = new FakeV2Handler();
        foreach (string json in replies) handler.Reply(HttpStatusCode.OK, json);
        return handler;
    }

    // include=properties: properties keyed by property key, the name among them
    // (measured 2026-10-22; the values below are synthetic).
    static string PropsReply(string id, string type, string name, params (string Key, JsonNode Value)[] properties)
    {
        var props = new JsonObject { ["name"] = name };
        foreach ((string key, JsonNode value) in properties) props[key] = value;
        return new JsonObject { ["id"] = id, ["type"] = type, ["properties"] = props }.ToJsonString();
    }

    // format=md: the markdown only, no name and no properties.
    static string MdReply(string id, string type, string markdown) =>
        new JsonObject { ["etag"] = "etag-1", ["id"] = id, ["type"] = type, ["markdown"] = markdown }.ToJsonString();

    static (string Key, JsonNode Value) Flag(string key) => (key, JsonValue.Create(true));

    static (string Key, JsonNode Value) Str(string key, string value) => (key, JsonValue.Create(value));

    static (string Key, JsonNode Value) Ids(string key, params string[] ids) =>
        (key, new JsonArray(ids.Select(i => (JsonNode)JsonValue.Create(i)).ToArray()));

    // A select is its option name in an array; a number is a plain number
    // (the number shape is not measured on a live server).
    static (string Key, JsonNode Value) Sel(string key, params string[] names) =>
        (key, new JsonArray(names.Select(n => (JsonNode)JsonValue.Create(n)).ToArray()));

    static (string Key, JsonNode Value) Num(string key, double value) => (key, JsonValue.Create(value));
    static string Lines(params string[] lines) => string.Join("\n", lines);


    // ── TaskPropertiesAsync ───────────────────────────────────────────────────

    // The properties are given in a shuffled order on purpose: the digest order
    // is the type's (DevSpace.PropertiesFor), not the object's. Done is folded
    // into the title box, the empty contact link and the unmapped « tag » never
    // show, the project link is read by name, the date is cut to its day. v2
    // serves a select as its option name, so the labels show as served.
    [Fact]
    public async Task TaskPropertiesRendersTheTitleBoxThenEachPropertyInTheTypesOrder()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(TaskId, "task", "Ma tâche",
                Str(DevSpace.Props.DefinitionDeFini, "Aucune action manuelle requise."),
                Ids(DevSpace.Props.ContactLie),
                Sel(DevSpace.Props.Priorite, "4"),
                Str(DevSpace.Props.Description, "Reprendre après la veille."),
                Sel(DevSpace.Props.Etat, "En cours"),
                Str(DevSpace.Props.DueDate, "2026-07-21T00:00:00Z"),
                Ids(DevSpace.Props.RelationProjet, ProjectId),
                Flag(DevSpace.Props.Done),
                Ids(DevSpace.Props.FichiersLies, "file0", "file1"),
                Sel(DevSpace.Props.Livrables, "Texte"),
                Sel(DevSpace.Props.TypeDeTache, "Produire"),
                Num(DevSpace.Props.ChargeEstimee, 3),
                Num(DevSpace.Props.ChargeReelle, 1.5),
                Sel(DevSpace.Props.Tag, "urgent"),
                Flag(DevSpace.Props.Archive)),
            PropsReply(ProjectId, "project", "Deckle"));

        string digest = await NewGestures(v2).TaskPropertiesAsync(TaskId, Ct);

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

        // The task, then the one project link: one read per object, no body.
        Assert.Equal(2, v2.Requests.Count);
        Assert.All(v2.Requests, r => Assert.DoesNotContain("format=md", r.PathAndQuery));
    }

    // The priority is served under "priorite" as its option name, the level.
    [Fact]
    [Trait("Category", "regression")]
    public async Task TaskPropertiesReadsThePriorityUnderTheKeyTheServerServes()
    {
        FakeV2Handler v2 = Reads(PropsReply(TaskId, "task", "Ma tâche", Sel("priorite", "4")));

        string digest = await NewGestures(v2).TaskPropertiesAsync(TaskId, Ct);

        Assert.Contains("Priorité : 4", digest.Split('\n'));
    }

    // Silence reads as « non »: an absent checkbox adds no line, and a task
    // with nothing set is just its identity and its open box.
    [Fact]
    public async Task TaskPropertiesShowsAnOpenBoxAndNoLineForAbsentCheckboxes()
    {
        FakeV2Handler v2 = Reads(PropsReply(TaskId, "task", "Ma tâche"));

        string digest = await NewGestures(v2).TaskPropertiesAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche", digest);
        Assert.Single(v2.Requests);
    }

    [Fact]
    public async Task TaskPropertiesJoinsSeveralLinkedObjectsByName()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(TaskId, "task", "Ma tâche", Ids(DevSpace.Props.RelationProjet, ProjectId, OtherTaskId)),
            PropsReply(ProjectId, "project", "Deckle"),
            PropsReply(OtherTaskId, "project", "Autre chantier"));

        string digest = await NewGestures(v2).TaskPropertiesAsync(TaskId, Ct);

        Assert.Equal(
            Lines($"id : {TaskId}", "[ ] Ma tâche", "Projet(s) lié(s) : Deckle, Autre chantier"),
            digest);
        Assert.Equal(3, v2.Requests.Count);
    }

    [Fact]
    public async Task TaskPropertiesFlagsAnObjectInAnytypesBinWithACorbeilleLine()
    {
        // The v2 flag's place is not measured: the root is one of the two places read.
        JsonObject binned = JsonNode.Parse(PropsReply(TaskId, "task", "Ma tâche"))!.AsObject();
        binned["archived"] = true;
        FakeV2Handler v2 = Reads(binned.ToJsonString());

        string digest = await NewGestures(v2).TaskPropertiesAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche\nCorbeille : oui", digest);
    }

    // The type is checked from the first read, before any link is followed.
    [Fact]
    public async Task TaskPropertiesRefusesADocumentSelectorBeforeReadingLinks()
    {
        FakeV2Handler v2 = Reads(PropsReply(DocId, "document", "Doc"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(v2).TaskPropertiesAsync(DocId, Ct));

        Assert.Equal(
            "Doc est un objet document, pas task : lis-le avec la lecture de son type.",
            error.Message);
    }
    // ── TaskChecklistAsync ────────────────────────────────────────────────────

    // The template's empty `- [ ] ` slot is not a subtask, labels come back
    // unescaped and trimmed, and the count only covers real items.
    [Fact]
    public async Task TaskChecklistListsTheItemsThenTheCheckedCount()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(TaskId, "task", "Ma tâche"),
            MdReply(TaskId, "task",
                $"## {ReadGestures.ChecklistHeading}\n---\n- [x] fait\n- [ ] module\\_X à relire   \n- [ ] \n## {ReadGestures.NotesHeading}\n---"));

        string digest = await NewGestures(v2).TaskChecklistAsync(TaskId, Ct);

        Assert.Equal(
            Lines(
                $"id : {TaskId}",
                "[ ] Ma tâche",
                "- [x] fait",
                "- [ ] module_X à relire",
                "Cochées : 1 / 2"),
            digest);
    }

    // The v2 reads, as asked: the properties first (title, type, done), then
    // the markdown rendering, each by its own query.
    [Fact]
    public async Task ATypedReadAsksTheV2PropertiesThenTheMarkdown()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(TaskId, "task", "Ma tâche"),
            MdReply(TaskId, "task", "- [ ] une"));

        await NewGestures(v2).TaskChecklistAsync(TaskId, Ct);

        Assert.Equal(2, v2.Requests.Count);
        Assert.Equal($"/v2/spaces/space-1/objects/{TaskId}", v2.Requests[0].PathAndQuery.Split('?')[0]);
        Assert.Contains("include=properties", v2.Requests[0].PathAndQuery);
        Assert.DoesNotContain("format=md", v2.Requests[0].PathAndQuery);
        Assert.Contains("format=md", v2.Requests[1].PathAndQuery);
    }

    // The v2 markdown of a task opens with its name as a `# ` line and writes the
    // divider as « --- » and every line with three trailing spaces (measured
    // 2026-10-22): none of it may reach a reader.
    [Fact]
    public async Task TheV2MarkdownRenderIsReadAsTheBodyTheAuthorWrote()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(TaskId, "task", "Ma tâche"),
            MdReply(TaskId, "task",
                "# Ma tâche   \n --- \n## Sous-tâches   \n- [x] fait   \n- [ ] reste   \n## Details   \n --- \nUne note.   \n"));

        string notes = await NewGestures(v2).TaskNotesAsync(TaskId, Ct);
        FakeV2Handler v2b = Reads(
            PropsReply(TaskId, "task", "Ma tâche"),
            MdReply(TaskId, "task",
                "# Ma tâche   \n --- \n## Sous-tâches   \n- [x] fait   \n- [ ] reste   \n## Details   \n --- \nUne note.   \n"));
        string checklist = await NewGestures(v2b).TaskChecklistAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche\nUne note.", notes);
        Assert.Equal(
            Lines($"id : {TaskId}", "[ ] Ma tâche", "- [x] fait", "- [ ] reste", "Cochées : 1 / 2"),
            checklist);
    }

    // A task written outside the template has no notes heading, so its notes are
    // the body: the title line must not be taken for one of them.
    [Fact]
    public async Task TheTitleLineIsNotTakenForANoteInATaskWithoutTheTemplate()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(TaskId, "task", "Ma tâche"),
            MdReply(TaskId, "task", "# Ma tâche   \nTexte libre.   \n"));

        string digest = await NewGestures(v2).TaskNotesAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche\nTexte libre.", digest);
    }

    [Fact]
    public async Task TaskChecklistWithOnlyTheTemplateSlotSaysThereAreNoSubtasks()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(TaskId, "task", "Ma tâche"),
            MdReply(TaskId, "task",
                $"## {ReadGestures.ChecklistHeading}\n---\n- [ ] \n## {ReadGestures.NotesHeading}\n---"));

        string digest = await NewGestures(v2).TaskChecklistAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche\nAucune sous-tâche.", digest);
    }

    [Fact]
    public async Task TaskChecklistOfADoneTaskCarriesTheCheckedBoxInItsTitle()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(TaskId, "task", "Ma tâche", Flag(DevSpace.Props.Done)),
            MdReply(TaskId, "task", "- [x] seule"));

        string digest = await NewGestures(v2).TaskChecklistAsync(TaskId, Ct);

        Assert.Equal(
            Lines($"id : {TaskId}", "[x] Ma tâche", "- [x] seule", "Cochées : 1 / 1"),
            digest);
    }

    // The typed read refuses another type before paying for the body.
    [Fact]
    public async Task TaskChecklistOfAProjectThrowsNamingTheActualTypeWithoutReadingTheBody()
    {
        FakeV2Handler v2 = Reads(PropsReply(TaskId, "project", "Deckle"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(v2).TaskChecklistAsync(TaskId, Ct));

        Assert.Equal(
            "Deckle est un objet project, pas task : lis-le avec la lecture de son type.",
            error.Message);
        Assert.Single(v2.Requests);
    }

    // ── TaskNotesAsync ────────────────────────────────────────────────────────

    // The notes are the text under the notes heading; the checklist sits in its
    // own section so it stays out, and the next section ends the notes.
    [Fact]
    public async Task TaskNotesReturnsTheCleanedTextUnderTheNotesHeadingOnly()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(TaskId, "task", "Ma tâche"),
            MdReply(TaskId, "task",
                $"## {ReadGestures.ChecklistHeading}\n---\n- [ ] une étape\n" +
                $"## {ReadGestures.NotesHeading}\n---\nAttention au cache.   \nLigne\\_deux\n\n## Autre\nhors notes"));

        string digest = await NewGestures(v2).TaskNotesAsync(TaskId, Ct);

        Assert.Equal(
            Lines($"id : {TaskId}", "[ ] Ma tâche", "Attention au cache.", "Ligne_deux"),
            digest);
    }

    // « À savoir » is the name proposed for the section: reading it keeps the
    // gesture working once the template is renamed in Anytype.
    [Fact]
    public async Task TaskNotesAlsoReadsTheAlternativeNotesHeading()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(TaskId, "task", "Ma tâche"),
            MdReply(TaskId, "task",
                $"## {ReadGestures.ChecklistHeading}\n- [ ] une étape\n## À savoir\nUne note."));

        string digest = await NewGestures(v2).TaskNotesAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche\nUne note.", digest);
    }

    [Fact]
    public async Task TaskNotesWithAnEmptyNotesSectionSaysThereAreNoNotes()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(TaskId, "task", "Ma tâche"),
            MdReply(TaskId, "task",
                $"## {ReadGestures.ChecklistHeading}\n- [ ] une étape\n## {ReadGestures.NotesHeading}\n---\n"));

        string digest = await NewGestures(v2).TaskNotesAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche\nAucune note.", digest);
    }

    // A task written outside the template has no notes heading: the gesture
    // falls back to the body minus its checklist lines and checklist heading.
    [Fact]
    public async Task TaskNotesWithoutANotesHeadingFallsBackToTheNonChecklistLines()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(TaskId, "task", "Ma tâche"),
            MdReply(TaskId, "task",
                $"Texte libre.\n- [x] fait\n## {ReadGestures.ChecklistHeading}\nAutre ligne"));

        string digest = await NewGestures(v2).TaskNotesAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche\nTexte libre.\nAutre ligne", digest);
    }

    [Fact]
    public async Task TaskNotesOfAnEmptyBodySaysThereAreNoNotes()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(TaskId, "task", "Ma tâche"),
            MdReply(TaskId, "task", ""));

        string digest = await NewGestures(v2).TaskNotesAsync(TaskId, Ct);

        Assert.Equal($"id : {TaskId}\n[ ] Ma tâche\nAucune note.", digest);
    }

    // ── ReportJournalAsync ────────────────────────────────────────────────────

    // A report has no name on v2 (`name` is empty, measured 2026-10-22); its
    // markdown opens on its first heading block, « # Journal <date> ».
    static string ReportProps(params string[] linkedTaskIds) => PropsReply(
        ReportId, "rapport", "",
        Str(DevSpace.Props.DateDuJournal, "2026-07-21T00:00:00Z"),
        linkedTaskIds.Length == 0 ? Str("tag", "") : Ids(DevSpace.Props.TachesLiees, linkedTaskIds));

    static string ReportMd(string markdown) => MdReply(ReportId, "rapport", markdown);

    static string[] Entries(int count) =>
        Enumerable.Range(1, count).Select(i => $"- entrée {i}").ToArray();

    [Fact]
    public async Task ReportJournalShowsTheLastTwentyEntriesByDefaultAndCountsTheOlderOnes()
    {
        FakeV2Handler v2 = Reads(
            ReportProps(TaskId, OtherTaskId),
            ReportMd("# Journal 2026-07-21\n" + Lines(Entries(25))),
            PropsReply(TaskId, "task", "Tâche A"),
            PropsReply(OtherTaskId, "task", "Tâche B"));

        string digest = await NewGestures(v2).ReportJournalAsync(ReportId, ct: Ct);

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
        // The report's properties and body, then one properties read per link.
        Assert.Equal(4, v2.Requests.Count);
    }

    [Fact]
    public async Task ReportJournalHonoursAnExplicitLastAndExcludesTheTitleHeading()
    {
        FakeV2Handler v2 = Reads(
            ReportProps(),
            ReportMd("# Journal 2026-07-21   \n" + Lines(Entries(4))));

        string digest = await NewGestures(v2).ReportJournalAsync(ReportId, last: 2, ct: Ct);

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
        FakeV2Handler v2 = Reads(
            ReportProps(),
            ReportMd(
                "# Journal 2026-07-21\n- ancienne\n2026-07-21 10:00   \n\npremière   \n\n" +
                "2026-07-21 11:30\n\nseconde\n\nsuite"));

        string digest = await NewGestures(v2).ReportJournalAsync(ReportId, last: 2, ct: Ct);

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
        FakeV2Handler v2 = Reads(
            ReportProps(),
            ReportMd("# Journal 2026-07-21\n\n- module\\_X ok   \n---\n- fin"));

        string digest = await NewGestures(v2).ReportJournalAsync(ReportId, last: 2, ct: Ct);

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
        FakeV2Handler v2 = Reads(ReportProps(), ReportMd(markdown));

        string digest = await NewGestures(v2).ReportJournalAsync(ReportId, ct: Ct);

        Assert.Equal(
            Lines($"id : {ReportId}", "Date du journal : 2026-07-21", "Journal vide."),
            digest);
    }

    // v2 serves links as ids only, and a properties read does not carry a
    // snippet: a linked object without a title reads « (sans titre) ».
    [Fact]
    public async Task ReportJournalNamesAnUntitledLinkedObjectSansTitre()
    {
        FakeV2Handler v2 = Reads(
            ReportProps(TaskId),
            ReportMd("# Journal 2026-07-21"),
            PropsReply(TaskId, "task", ""));

        string digest = await NewGestures(v2).ReportJournalAsync(ReportId, ct: Ct);

        Assert.Contains("Tâches liées : (sans titre)", digest.Split('\n'));
    }

    // A relation can hold many links and every name costs a request: a repeated
    // id is read once, and past eight reads the rest is only counted.
    [Fact]
    [Trait("Category", "regression")]
    public async Task ReportJournalReadsADuplicatedLinkOnceAndCapsTheNameReads()
    {
        string[] ids = Enumerable.Range(1, 10).Select(i => $"bafyreiLink{i}").ToArray();
        string[] linked = [.. ids, ids[0]];
        var replies = new List<string> { ReportProps(linked), ReportMd("# Journal 2026-07-21") };
        replies.AddRange(ids.Take(LinkedNames.MaxReads).Select((id, i) => PropsReply(id, "task", $"T{i + 1}")));
        FakeV2Handler v2 = Reads([.. replies]);

        string digest = await NewGestures(v2).ReportJournalAsync(ReportId, ct: Ct);

        Assert.Contains("Tâches liées : T1, T2, T3, T4, T5, T6, T7, T8, +2", digest.Split('\n'));
        Assert.Equal(2 + LinkedNames.MaxReads, v2.Requests.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task ReportJournalRefusesALastBelowOneBeforeAnyRequest(int last)
    {
        var v2 = new FakeV2Handler();

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(v2).ReportJournalAsync(ReportId, last, Ct));

        Assert.Empty(v2.Requests);
    }

    // ── IdeaTextAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task IdeaTextReturnsTheTitleThenTheCleanedTextAndTheArchivedFlag()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(IdeaId, "idee", "Une idée", Flag(DevSpace.Props.Archive)),
            MdReply(IdeaId, "idee", "# Une idée   \nTexte de l'idée.\n\nSuite\\_x   \n---"));

        string digest = await NewGestures(v2).IdeaTextAsync(IdeaId, Ct);

        Assert.Equal(
            Lines($"id : {IdeaId}", "Une idée", "Texte de l'idée.", "Suite_x", "Archivé : oui"),
            digest);
    }

    [Fact]
    public async Task IdeaTextWithNoBodyAndNoArchiveIsJustItsIdentity()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(IdeaId, "idee", "Une idée"),
            MdReply(IdeaId, "idee", "# Une idée   \n"));

        string digest = await NewGestures(v2).IdeaTextAsync(IdeaId, Ct);

        Assert.Equal($"id : {IdeaId}\nUne idée", digest);
    }

    [Fact]
    public async Task IdeaTextWithoutANameIsTitledByItsFirstBodyLine()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(IdeaId, "idee", ""),
            MdReply(IdeaId, "idee", "\n## Piste\\_une   \nSuite"));

        string digest = await NewGestures(v2).IdeaTextAsync(IdeaId, Ct);

        Assert.Equal(Lines($"id : {IdeaId}", "Piste_une", "## Piste_une", "Suite"), digest);
    }

    [Fact]
    public async Task IdeaTextWithoutNameOrBodyStaysSansTitre()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(IdeaId, "idee", ""),
            MdReply(IdeaId, "idee", "\n"));

        string digest = await NewGestures(v2).IdeaTextAsync(IdeaId, Ct);

        Assert.Equal($"id : {IdeaId}\n(sans titre)", digest);
    }

    // ── DocumentOutlineAsync ──────────────────────────────────────────────────

    // Sizes are cleaned characters: a section counts its sub-sections, which is
    // why « Alpha » outweighs « Sous »; a heading comes back unescaped.
    [Fact]
    public async Task DocumentOutlineListsThePropertiesThePreambleSizeAndEachHeadingWithItsSize()
    {
        FakeV2Handler v2 = Reads(
            PropsReply(DocId, "document", "Doc",
                Sel(DevSpace.Props.TypeDeDocument, "Référence"),
                Flag(DevSpace.Props.DocumentSysteme),
                Str(DevSpace.Props.Version, "1.2")),
            MdReply(DocId, "document",
                "# Doc   \nIntro courte.\n\n## Alpha\nabc\ndef\n### Sous\nxy\n## Beta\\_bis\n --- \n"));

        string digest = await NewGestures(v2).DocumentOutlineAsync(DocId, Ct);

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
        FakeV2Handler v2 = Reads(PropsReply(DocId, "document", "Doc"), MdReply(DocId, "document", "Texte seul."));

        string digest = await NewGestures(v2).DocumentOutlineAsync(DocId, Ct);

        Assert.Equal($"id : {DocId}\nDoc\nPréambule : 11 car.", digest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("---\n")]
    public async Task DocumentOutlineOfAnEmptyBodySaysTheBodyIsEmpty(string markdown)
    {
        FakeV2Handler v2 = Reads(PropsReply(DocId, "document", "Doc"), MdReply(DocId, "document", markdown));

        string digest = await NewGestures(v2).DocumentOutlineAsync(DocId, Ct);

        Assert.Equal($"id : {DocId}\nDoc\nCorps vide.", digest);
    }

    // ── DocumentSectionAsync ──────────────────────────────────────────────────

    const string SectionBody = "Intro courte.\n\n## Alpha\nabc\\_d   \n---\n### Sous\nxy\n## Beta\\_bis\n---\n";

    static FakeV2Handler DocReads(string markdown) => Reads(
        PropsReply(DocId, "document", "Doc"),
        MdReply(DocId, "document", markdown));

    // No heading, a null, an empty or a blank one all mean the preamble.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DocumentSectionWithoutAHeadingReadsThePreamble(string? heading)
    {
        string text = await NewGestures(DocReads(SectionBody)).DocumentSectionAsync(DocId, heading, Ct);

        Assert.Equal($"id : {DocId}\nDoc\nIntro courte.", text);
    }

    // The v2 render opens a document with its title as a level-1 heading: kept,
    // it would be the whole body's only section and hide the preamble.
    [Fact]
    public async Task DocumentSectionReadsThePreambleAfterTheV2TitleLine()
    {
        string text = await NewGestures(DocReads("# Doc   \n" + SectionBody)).DocumentSectionAsync(DocId, null, Ct);

        Assert.Equal($"id : {DocId}\nDoc\nIntro courte.", text);
    }

    // The section keeps its sub-sections, ends at the next heading of its own
    // level, and comes back cleaned.
    [Fact]
    public async Task DocumentSectionReadsTheCleanedTextUnderTheHeadingSubSectionsIncluded()
    {
        string text = await NewGestures(DocReads(SectionBody)).DocumentSectionAsync(DocId, "Alpha", Ct);

        Assert.Equal(
            Lines($"id : {DocId}", "Doc", "## Alpha", "abc_d", "### Sous", "xy"),
            text);
    }

    [Fact]
    public async Task DocumentSectionFindsAnEscapedHeadingFromItsNaturalSpelling()
    {
        string text = await NewGestures(DocReads("## Beta\\_bis\ncorps")).DocumentSectionAsync(DocId, "Beta_bis", Ct);

        Assert.Equal($"id : {DocId}\nDoc\n## Beta_bis\ncorps", text);
    }

    // The error is the retry hint: it lists the headings the body does carry.
    [Fact]
    public async Task DocumentSectionWithAnUnknownHeadingThrowsListingThePresentHeadings()
    {
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(DocReads(SectionBody)).DocumentSectionAsync(DocId, "Zeta", Ct));

        Assert.Equal(
            "Section « Zeta » introuvable dans Doc. Titres présents : « Alpha », « Sous », « Beta_bis ».",
            error.Message);
    }

    [Fact]
    public async Task DocumentSectionWithAnUnknownHeadingInAHeadinglessBodySaysThereAreNone()
    {
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(DocReads("Texte seul.")).DocumentSectionAsync(DocId, "Zeta", Ct));

        Assert.Equal(
            "Section « Zeta » introuvable dans Doc. Titres présents : (aucun).",
            error.Message);
    }

    [Fact]
    public async Task DocumentSectionWithNothingUnderItsHeadingSaysTheSectionIsEmpty()
    {
        string text = await NewGestures(DocReads(SectionBody)).DocumentSectionAsync(DocId, "Beta_bis", Ct);

        Assert.Equal($"id : {DocId}\nDoc\n## Beta_bis\nSection vide.", text);
    }

    // A body that opens on a heading has no preamble to read.
    [Fact]
    public async Task DocumentSectionReadingThePreambleOfABodyThatOpensOnAHeadingSaysTheSectionIsEmpty()
    {
        string text = await NewGestures(DocReads("## Alpha\nabc")).DocumentSectionAsync(DocId, null, Ct);

        Assert.Equal($"id : {DocId}\nDoc\nSection vide.", text);
    }
}
