using System.Diagnostics;
using Deckle.Browser.Adblock;
using Deckle.Core;

namespace Deckle.Browser.Views;

// ─── The ad blocker's engine, one for the process ────────────────────────────
//
// Built on a background thread from the lists kept in the module's Filters
// folder; the lists the folder lacks, or holds older than four days, are then
// fetched (the browser fetching its own lists, never anything a person
// visits), written atomically, and the engine is built again. While there is
// no engine, nothing is blocked.
//
// uBlock Origin's scriptlets come with it: its modules, fetched once at the pinned
// release tag, are converted to the engine's resources by a window's hidden web
// view (BrowserWindow.ConvertUboResourcesAsync, registered here as the Converter),
// and the result is kept in the Filters folder under the tag's name. They are put
// before Brave's resources, which give way on a name; uBlock Origin's own lists
// are the trusted ones that may inject the scriptlets that require trust.
//
// What is logged is a rule count, a duration and, when a list cannot be
// fetched, the list's name: no address of a page, ever.
public static class BrowserAdblock
{
    private static readonly object Gate = new();
    private static readonly TaskCompletionSource<bool> Settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static AdblockEngine? _engine;
    private static bool _started;
    private static bool _pinned;
    private static CancellationTokenSource? _cancel;

    /// <summary>The engine in force, or null while none was built (nothing is blocked).</summary>
    public static AdblockEngine? Engine
    {
        get { lock (Gate) return _engine; }
    }

    /// <summary>The rule count and the duration in milliseconds of the last build of the
    /// engine from the lists (0 before any): the figures the log line carries.</summary>
    public static (int Rules, long Milliseconds) LastBuild { get; private set; }

    /// <summary>What turns the modules in a folder into the engine's resources (JSON), or null.
    /// A window sets it; while none does, the conversion waits for the next start.</summary>
    public static Func<string, Task<string?>>? Converter { get; set; }

    /// <summary>The folder the lists are kept in.</summary>
    public static string FiltersDirectory => Path.Combine(AppPaths.GetModuleDirectory("browser"), "Filters");

    /// <summary>Completes (true when the lists were all fresh or fetched) once the engine
    /// stopped changing: after the last build the start will do. A self-check waits on it.</summary>
    public static Task<bool> Ready => Settled.Task;

    /// <summary>Starts building the engine on a background thread, once for the process.</summary>
    public static void Start()
    {
        lock (Gate)
        {
            if (_started || _pinned) return;
            _started = true;
            _cancel = new CancellationTokenSource();
        }
        CancellationToken token = _cancel!.Token;
        _ = Task.Run(() => LoadAsync(token), token);
    }

    /// <summary>Puts an engine in force in place of the lists' (a self-check's local rules)
    /// and stops the background load, so that nothing replaces it.</summary>
    public static void UseEngine(AdblockEngine engine)
    {
        AdblockEngine? old;
        lock (Gate)
        {
            _pinned = true;
            _cancel?.Cancel();
            old = _engine;
            _engine = engine;
        }
        old?.Dispose();
        Settled.TrySetResult(false);
    }

    private static async Task LoadAsync(CancellationToken token)
    {
        try
        {
            string folder = FiltersDirectory;
            Directory.CreateDirectory(folder);

            Build(folder);

            bool allFresh = true;
            bool convertedNew = false;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            bool fetchedAny = false;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (FilterLists.Source source in FilterLists.All)
            {
                token.ThrowIfCancellationRequested();
                string path = Path.Combine(folder, source.FileName);
                bool present = File.Exists(path);
                if (present && !FilterLists.IsStale(File.GetLastWriteTimeUtc(path), now)) continue;
                if (await FetchAsync(http, source, path, token)) fetchedAny = true;
                else if (!present) allFresh = false;
            }

            try { convertedNew = await EnsureUboResourcesAsync(http, folder, token); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DeckleBrowserViewsSource.Log.UboResourcesFailed("prepare", ex.GetType().Name);
            }

            if (fetchedAny || convertedNew) Build(folder);
            Settled.TrySetResult(allFresh);
        }
        catch (OperationCanceledException)
        {
            Settled.TrySetResult(false);
        }
        catch (Exception ex)
        {
            DeckleBrowserViewsSource.Log.AdblockBuildFailed(ex.GetType().Name);
            Settled.TrySetResult(false);
        }
    }

    // Reads what the folder holds, builds the engine and puts it in force. False
    // when there was nothing to read.
    private static bool Build(string folder)
    {
        var clock = Stopwatch.StartNew();
        var text = new System.Text.StringBuilder();
        var trusted = new System.Text.StringBuilder();
        int rules = 0;
        foreach (FilterLists.Source list in FilterLists.Lists)
        {
            string path = Path.Combine(folder, list.FileName);
            if (!File.Exists(path)) continue;
            System.Text.StringBuilder into = list.Trusted ? trusted : text;
            foreach (string line in File.ReadLines(path))
            {
                into.Append(line).Append('\n');
                if (line.Length > 0 && line[0] != '!' && line[0] != '[') rules++;
            }
        }
        if (rules == 0) return false;

        AdblockEngine engine = AdblockEngine.Create(text.ToString(), trusted.ToString());
        string brave = Path.Combine(folder, FilterLists.Resources.FileName);
        string ubo = Path.Combine(folder, FilterLists.UboConvertedFileName());
        string braveJson = File.Exists(brave) ? File.ReadAllText(brave) : "[]";
        string uboJson = File.Exists(ubo) ? File.ReadAllText(ubo) : "[]";
        if (File.Exists(brave) || File.Exists(ubo)) engine.UseResources(ResourceMerge.Merge(uboJson, braveJson));

        AdblockEngine? old;
        lock (Gate)
        {
            if (_pinned)
            {
                engine.Dispose();
                return false;
            }
            old = _engine;
            _engine = engine;
        }
        old?.Dispose();
        LastBuild = (rules, clock.ElapsedMilliseconds);
        DeckleBrowserViewsSource.Log.AdblockEngineBuilt(rules, clock.ElapsedMilliseconds);
        return true;
    }

    // uBlock Origin's modules at the pinned tag, fetched when the folder does not hold that
    // tag (the tag's name is written last, so a half fetch is fetched again), then converted
    // when the tag has no converted file yet. True when a conversion was made now.
    private static async Task<bool> EnsureUboResourcesAsync(HttpClient http, string folder, CancellationToken token)
    {
        string converted = Path.Combine(folder, FilterLists.UboConvertedFileName());
        if (File.Exists(converted) && new FileInfo(converted).Length > 2) return false;

        string modules = Path.Combine(folder, FilterLists.UboFolderName);
        string marker = Path.Combine(modules, "TAG");
        bool have = File.Exists(marker) && File.ReadAllText(marker).Trim() == FilterLists.UboTag
            && FilterLists.UboFiles.All(f => File.Exists(Path.Combine(modules, f)));
        if (!have)
        {
            if (Directory.Exists(modules)) Directory.Delete(modules, recursive: true);
            Directory.CreateDirectory(modules);
            foreach (string file in FilterLists.UboFiles)
            {
                token.ThrowIfCancellationRequested();
                string path = Path.Combine(modules, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var source = new FilterLists.Source("uBlock scriptlets", FilterLists.UboAddress(file), file);
                if (!await FetchAsync(http, source, path, token)) return false;
            }
            await File.WriteAllTextAsync(marker, FilterLists.UboTag, token);
        }

        Func<string, Task<string?>>? convert = Converter;
        if (convert is null) return false;
        string? json = await convert(modules);
        if (json is null) return false;

        string temporary = converted + ".tmp";
        await File.WriteAllTextAsync(temporary, json, token);
        File.Move(temporary, converted, overwrite: true);
        foreach (string old in Directory.EnumerateFiles(folder, "ubo-resources-*.json"))
            if (!string.Equals(old, converted, StringComparison.OrdinalIgnoreCase)) File.Delete(old);
        return true;
    }

    // One list, fetched and written atomically (a temporary file in the folder,
    // then moved over the old copy). A list that cannot be fetched is named in
    // a warning and left as it was.
    private static async Task<bool> FetchAsync(HttpClient http, FilterLists.Source source, string path, CancellationToken token)
    {
        string temporary = path + ".tmp";
        try
        {
            using HttpResponseMessage response = await http.GetAsync(source.Address, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (Stream body = await response.Content.ReadAsStreamAsync(token))
            await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                await body.CopyToAsync(file, token);
            if (new FileInfo(temporary).Length == 0) throw new IOException("empty list");
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when ((ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException) && !token.IsCancellationRequested)
        {
            DeckleBrowserViewsSource.Log.AdblockListFailed(source.Name);
            try { File.Delete(temporary); } catch (IOException) { }
            return false;
        }
    }
}
