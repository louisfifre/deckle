using System.Diagnostics;
using System.Text.Json;

namespace Deckle.Atelier;

// Builds a MapInventory in four passes: walk the folder, recognise each file
// by MapConventions, follow the @imports of instruction files (an imported
// file loads with its importer, so it becomes a context file read as often),
// then read every item's text for its links.
internal static class MapInventoryBuilder
{
    private sealed class Draft
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required MapItemKind Kind { get; init; }
        public required string Path { get; init; }
        public required string RelativePath { get; init; }
        public MapFileFacts? Facts { get; init; }
        public int? DeclaredAtLine { get; init; }
        public string? Summary { get; init; }
        public required MapLoading Loading { get; set; }
        public MapReaders Readers { get; set; }
        public bool ExpandsImports { get; set; }
        public int ImportHops { get; set; } = int.MaxValue;
        public List<MapHit>? Hits { get; set; }
    }

    public static MapInventory Build(string folder, MapLimits limits, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        try
        {
            return BuildCore(root, limits, cancellation, clock);
        }
        catch (OperationCanceledException)
        {
            DeckleAtelierSource.Log.MapInventoryCancelled(clock.ElapsedMilliseconds);
            throw;
        }
    }

    private static MapInventory BuildCore(string root, MapLimits limits, CancellationToken cancellation, Stopwatch clock)
    {
        MapWalkResult walk = MapFolderWalk.Walk(root, limits, cancellation);
        if (walk.Failure != MapWalkFailure.None)
        {
            var failure = walk.Failure == MapWalkFailure.NotFound ? MapInventoryFailure.NotFound : MapInventoryFailure.AccessDenied;
            DeckleAtelierSource.Log.MapInventoryFailed(failure.ToString());
            return MapInventory.Failed(root, failure);
        }
        if (walk.Truncated) DeckleAtelierSource.Log.MapInventoryTruncated(limits.MaxEntries);

        var skipped = new List<MapSkip>(walk.Skipped);
        var fullPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in walk.Files) fullPaths[MapPaths.Relative(root, file)] = file;
        bool Exists(string relative) => fullPaths.ContainsKey(relative);
        MapFileFacts Facts(string relative) => new(fullPaths[relative], relative, Exists, limits.MaxFileBytes);

        var drafts = new Dictionary<string, Draft>(StringComparer.OrdinalIgnoreCase);
        foreach ((string relative, string full) in fullPaths)
        {
            cancellation.ThrowIfCancellationRequested();
            ReadMcpConfig(relative, full, limits, drafts, skipped);
            MapFileFacts facts = Facts(relative);
            if (MapConventions.Recognise(facts) is not { } rule) continue;
            MapReading reading = rule.Reading(facts);
            drafts[relative] = new Draft
            {
                Id = relative,
                Name = NameOf(rule.Kind, facts),
                Kind = rule.Kind,
                Path = full,
                RelativePath = relative,
                Facts = facts,
                Summary = facts.Extension is ".md" or ".mdc" ? facts.FrontMatter["description"] : null,
                Loading = reading.Loading,
                Readers = reading.AtStartBy,
                ExpandsImports = rule.ExpandsImports,
                ImportHops = rule.ExpandsImports ? 0 : int.MaxValue,
            };
        }

        FollowImports(root, drafts, Exists, Facts, cancellation);
        List<MapLink> links = ReadLinks(root, drafts, fullPaths, Facts, cancellation);

        List<MapItem> items = [.. drafts.Values.Select(d => Finish(d, skipped))];
        items.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind)
            : FolderListing.NameOrder.Compare(a.Name, b.Name) is var byName and not 0 ? byName
            : string.CompareOrdinal(a.RelativePath, b.RelativePath));
        skipped.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));

        DeckleAtelierSource.Log.MapInventoryBuilt(items.Count, links.Count, skipped.Count, clock.ElapsedMilliseconds);
        return new MapInventory(root, items, links, skipped, walk.Truncated, MapInventoryFailure.None);
    }

    private static string NameOf(MapItemKind kind, MapFileFacts facts)
    {
        if (kind != MapItemKind.Skill) return facts.FileName;
        if (facts.FrontMatter["name"] is { } declared) return declared;
        if (!facts.Named("SKILL.md")) return facts.Stem;
        int slash = facts.Folder.LastIndexOf('/');
        return facts.Folder.Length == 0 ? facts.Stem : facts.Folder[(slash + 1)..];
    }

    private static void ReadMcpConfig(string relative, string full, MapLimits limits, Dictionary<string, Draft> drafts, List<MapSkip> skipped)
    {
        McpConfigConvention? config = MapConventions.McpConfigs.FirstOrDefault(c => string.Equals(c.RelativePath, relative, StringComparison.OrdinalIgnoreCase));
        if (config is null) return;
        IReadOnlyList<McpDeclaration> servers;
        try
        {
            if (new FileInfo(full).Length > limits.MaxFileBytes)
            {
                skipped.Add(new MapSkip(relative, MapSkipReason.TooLarge));
                return;
            }
            servers = McpDeclarations.Read(File.ReadAllText(full), config.ServersKey);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DeckleAtelierSource.Log.MapConfigUnreadable(ex.GetType().Name);
            skipped.Add(new MapSkip(relative, MapSkipReason.Unreadable));
            return;
        }
        foreach (McpDeclaration server in servers)
        {
            string id = relative + "#" + server.Name;
            drafts[id] = new Draft
            {
                Id = id,
                Name = server.Name,
                Kind = MapItemKind.McpServer,
                Path = full,
                RelativePath = relative,
                DeclaredAtLine = server.Line,
                Summary = server.Summary,
                Loading = MapLoading.NotLoaded,
            };
        }
    }

    private static List<MapHit> HitsOf(Draft draft) =>
        draft.Hits ??= draft.Facts?.Text is { } text
            ? [.. MapLinkReader.Read(text, draft.Kind == MapItemKind.Script, draft.ExpandsImports)]
            : [];

    // An import carries its importer's loading to the target: a file that
    // CLAUDE.md imports is read at every start by Claude Code, up to
    // MapConventions.MaxImportHops away from the file that started the chain.
    private static void FollowImports(string root, Dictionary<string, Draft> drafts, Func<string, bool> exists,
        Func<string, MapFileFacts> facts, CancellationToken cancellation)
    {
        var pending = new Queue<Draft>(drafts.Values.Where(d => d.ExpandsImports));
        while (pending.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            Draft importer = pending.Dequeue();
            if (importer.ImportHops >= MapConventions.MaxImportHops) continue;
            foreach (MapHit hit in HitsOf(importer).Where(h => h.Kind == MapHitKind.Import))
            {
                if (hit.Value.StartsWith('~')) continue;
                if (MapPaths.Resolve(root, importer.Facts!.Folder, hit.Value) is not { } target || !exists(target)) continue;
                if (!drafts.TryGetValue(target, out Draft? imported))
                {
                    MapFileFacts targetFacts = facts(target);
                    imported = new Draft
                    {
                        Id = target,
                        Name = targetFacts.FileName,
                        Kind = MapItemKind.ContextFile,
                        Path = targetFacts.FullPath,
                        RelativePath = target,
                        Facts = targetFacts,
                        Loading = MapLoading.NotLoaded,
                    };
                    drafts[target] = imported;
                }

                bool changed = !imported.ExpandsImports;
                imported.ExpandsImports = true;
                if (importer.ImportHops + 1 < imported.ImportHops)
                {
                    imported.ImportHops = importer.ImportHops + 1;
                    changed = true;
                }
                // Read at start by an agent that expands imports: so is the
                // target. Read at start only by one that does not (Codex reads
                // AGENTS.md as plain text): nothing loads the target. Read on
                // demand: the target comes with it.
                MapReaders carried = importer.Readers & MapConventions.ImportExpanding;
                MapLoading loading = importer.Loading switch
                {
                    MapLoading.AtSessionStart => carried != MapReaders.None ? MapLoading.AtSessionStart : MapLoading.NotLoaded,
                    MapLoading.OnDemand => MapLoading.OnDemand,
                    _ => MapLoading.NotLoaded,
                };
                if (loading > imported.Loading)
                {
                    imported.Loading = loading;
                    changed = true;
                }
                if ((imported.Readers | carried) != imported.Readers)
                {
                    imported.Readers |= carried;
                    changed = true;
                }
                if (changed)
                {
                    imported.Hits = null;
                    pending.Enqueue(imported);
                }
            }
        }
    }

    private static List<MapLink> ReadLinks(string root, Dictionary<string, Draft> drafts, Dictionary<string, string> fullPaths,
        Func<string, MapFileFacts> facts, CancellationToken cancellation)
    {
        // How a hit finds its item: by path for files (a skill also by its
        // folder), by file name for a script written without its folder, by
        // name for skills and servers.
        var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scriptsByFileName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (Draft d in drafts.Values)
        {
            if (d.Facts is { } f)
            {
                byPath[d.RelativePath] = d.Id;
                if (d.Kind == MapItemKind.Skill && f.Named("SKILL.md") && f.Folder.Length > 0) byPath.TryAdd(f.Folder, d.Id);
                if (d.Kind == MapItemKind.Script) Add(scriptsByFileName, f.FileName, d.Id);
            }
            if (d.Kind is MapItemKind.Skill or MapItemKind.McpServer) Add(byName, d.Name, d.Id);
        }

        // The Markdown files inside a skill's folder speak for the skill.
        var documents = new List<(Draft From, MapFileFacts File, List<MapHit> Hits)>();
        foreach (Draft d in drafts.Values.Where(d => d.Facts is not null))
            documents.Add((d, d.Facts!, HitsOf(d)));
        foreach (Draft skill in drafts.Values.Where(d => d.Kind == MapItemKind.Skill && d.Facts!.Named("SKILL.md") && d.Facts.Folder.Length > 0))
        {
            string prefix = skill.Facts!.Folder + "/";
            foreach (string relative in fullPaths.Keys)
            {
                if (!relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || drafts.ContainsKey(relative)
                    || !relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                MapFileFacts supporting = facts(relative);
                if (supporting.Text is { } text) documents.Add((skill, supporting, [.. MapLinkReader.Read(text, false, false)]));
            }
        }

        var evidence = new Dictionary<(string From, string To), List<MapLinkEvidence>>();
        foreach ((Draft from, MapFileFacts file, List<MapHit> hits) in documents)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (MapHit hit in hits)
            {
                MapLinkForm form = hit.Kind switch
                {
                    MapHitKind.Link => MapLinkForm.MarkdownLink,
                    MapHitKind.Import => MapLinkForm.Import,
                    _ => MapLinkForm.Mention,
                };
                foreach (string to in Targets(hit))
                {
                    if (string.Equals(to, from.Id, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!evidence.TryGetValue((from.Id, to), out List<MapLinkEvidence>? places)) evidence[(from.Id, to)] = places = [];
                    var place = new MapLinkEvidence(form, file.RelativePath, hit.Line);
                    if (!places.Contains(place)) places.Add(place);
                }
            }

            IEnumerable<string> Targets(MapHit hit)
            {
                switch (hit.Kind)
                {
                    case MapHitKind.Link:
                        if (MapLinkReader.LinkPath(hit.Value) is { } linked && MapPaths.Resolve(root, file.Folder, linked) is { } target
                            && byPath.TryGetValue(target, out string? id)) yield return id;
                        break;
                    case MapHitKind.Import:
                        if (!hit.Value.StartsWith('~') && MapPaths.Resolve(root, file.Folder, hit.Value) is { } imported
                            && byPath.TryGetValue(imported, out string? importedId)) yield return importedId;
                        break;
                    case MapHitKind.ScriptPath:
                        if (ScriptByPath(hit.Value) is { } script) yield return script;
                        break;
                    case MapHitKind.Name:
                        if (byName.TryGetValue(hit.Value, out List<string>? named))
                            foreach (string n in named) yield return n;
                        break;
                }
            }

            // A path is tried from the document's folder, then from the root;
            // a bare file name links only when one script carries it.
            string? ScriptByPath(string raw)
            {
                foreach (string basis in (string[])[file.Folder, ""])
                {
                    if (MapPaths.Resolve(root, basis, raw) is { } resolved && byPath.TryGetValue(resolved, out string? id)
                        && drafts[id].Kind == MapItemKind.Script) return id;
                }
                string name = System.IO.Path.GetFileName(raw.Replace('\\', '/'));
                return scriptsByFileName.TryGetValue(name, out List<string>? same) && same.Count == 1 ? same[0] : null;
            }
        }

        return [.. evidence
            .OrderBy(e => e.Key.From, StringComparer.Ordinal).ThenBy(e => e.Key.To, StringComparer.Ordinal)
            .Select(e => new MapLink(e.Key.From, e.Key.To, e.Value))];
    }

    private static void Add(Dictionary<string, List<string>> index, string key, string id)
    {
        if (!index.TryGetValue(key, out List<string>? ids)) index[key] = ids = [];
        ids.Add(id);
    }

    private static MapItem Finish(Draft d, List<MapSkip> skipped)
    {
        int? lines = null, tokens = null;
        DateTime modified = File.GetLastWriteTime(d.Path);
        if (d.Facts is { } f)
        {
            if (f.Text is { } text)
            {
                lines = TextLines.Count(text);
                tokens = TokenEstimate.Of(text);
            }
            else if (f.TooLarge) skipped.Add(new MapSkip(d.RelativePath, MapSkipReason.TooLarge));
            else if (f.Unreadable) skipped.Add(new MapSkip(d.RelativePath, MapSkipReason.Unreadable));
        }

        // Always read: the whole file. Chosen on demand from a description
        // (skills, subagents, rules that describe themselves): the name and
        // the description, which sit in the context from the start.
        int? startup = d.Loading switch
        {
            MapLoading.AtSessionStart => tokens,
            MapLoading.OnDemand when d.Facts?.FrontMatter["description"] is { } description => TokenEstimate.Of(d.Name + ": " + description),
            _ => null,
        };

        return new MapItem
        {
            Id = d.Id,
            Name = d.Name,
            Kind = d.Kind,
            Path = d.Path,
            RelativePath = d.RelativePath,
            DeclaredAtLine = d.DeclaredAtLine,
            Lines = lines,
            EstimatedTokens = tokens,
            Modified = modified,
            Loading = d.Loading,
            ReadAtStartBy = d.Loading == MapLoading.AtSessionStart ? d.Readers : MapReaders.None,
            EstimatedStartupTokens = startup,
            Summary = d.Summary,
        };
    }
}
