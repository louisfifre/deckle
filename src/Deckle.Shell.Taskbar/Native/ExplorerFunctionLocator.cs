using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using static Deckle.Shell.Taskbar.TaskbarNativeMethods;

namespace Deckle.Shell.Taskbar;

/// <summary>
/// A function of a module explorer has loaded, as the engine needs it to patch
/// it: the module's identity (so the engine checks it patches that very file),
/// the function's offset in it, and its first 16 bytes as the file holds them.
/// </summary>
internal sealed record ExplorerFunction(string Module, uint TimeDateStamp, uint ImageSize, uint Rva, byte[] Prologue);

/// <summary>What a lookup gave: the function, or the reason there is none (snake_case, for the log).</summary>
internal readonly record struct FunctionLookup(ExplorerFunction? Function, string? Failure)
{
    /// <summary>Microsoft's symbols for this build of the module do not name the function.</summary>
    public bool FunctionGone => Failure == SymbolMissing;

    public const string SymbolMissing = "symbol_missing";
}

// Finds where a function of explorer lives, from Microsoft's public symbols.
//
// A Windows module does not export its internal functions, so their address is
// in its PDB, which Microsoft publishes for every build at an address made of
// the PDB's name and identity (GUID and age, both written in the module's debug
// directory). The first lookup for a build downloads that PDB once, reads the
// one offset Deckle needs with the dbghelp of System32, deletes the PDB and
// keeps the offset in symbols.json. Every later lookup for that build is a read
// of the cache: the network is needed again only after a Windows update changes
// the module. Nothing else is ever sent.
//
// The module's file is read from disk, never from explorer's memory: the bytes
// and the identity come from the file explorer loaded, which is what the engine
// checks against before it writes anything.
internal sealed class ExplorerFunctionLocator
{
    private const string SymbolServer = "https://msdl.microsoft.com/download/symbols";
    private const int PrologueLength = 16;
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(10);
    private static readonly object DbgHelpGate = new();

    private static readonly Lazy<ExplorerFunctionLocator> _instance = new(() =>
        new ExplorerFunctionLocator(Deckle.Core.AppPaths.GetModuleDirectory(TaskbarSettingsModule.ModuleId)));

    public static ExplorerFunctionLocator Instance => _instance.Value;

    private readonly string _directory;
    private readonly string _cachePath;
    private readonly Dictionary<string, DateTime> _failedAt = new(StringComparer.Ordinal);

    internal ExplorerFunctionLocator(string directory)
    {
        _directory = directory;
        _cachePath = Path.Combine(directory, "symbols.json");
    }

    public async Task<FunctionLookup> FindAsync(string module, string symbol, CancellationToken cancellation = default)
    {
        if (LoadedModulePath(module) is not { } path) return new(null, "module_not_loaded");

        ModuleIdentity identity;
        try { identity = ModuleIdentity.Read(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return new(null, "module_unreadable");
        }
        if (identity.PdbName is null) return new(null, "no_debug_directory");

        string key = CacheKey(module, identity, symbol);
        Dictionary<string, uint?> cache = ReadCache();
        if (!cache.TryGetValue(key, out uint? rva))
        {
            if (_failedAt.TryGetValue(key, out DateTime failed) && DateTime.UtcNow - failed < RetryAfterFailure)
                return new(null, "download_failed");

            string? failure;
            (rva, failure) = await ResolveFromServerAsync(module, identity, symbol, cancellation).ConfigureAwait(false);
            if (failure is not null)
            {
                _failedAt[key] = DateTime.UtcNow;
                DeckleShellTaskbarSource.Log.SymbolLookupFailed(module, failure);
                return new(null, failure);
            }
            cache[key] = rva;
            WriteCache(cache);
        }

        if (rva is not { } offset) return new(null, FunctionLookup.SymbolMissing);

        byte[]? prologue = ModuleIdentity.ReadBytes(path, offset, PrologueLength);
        return prologue is null
            ? new(null, "outside_the_image")
            : new(new ExplorerFunction(module, identity.TimeDateStamp, identity.ImageSize, offset, prologue), null);
    }

    internal static string CacheKey(string module, ModuleIdentity identity, string symbol) =>
        $"{module}|{identity.TimeDateStamp:X8}{identity.ImageSize:X}|{symbol}";

    // The PDB once, its one offset, then the file goes: it weighs tens of megabytes.
    private async Task<(uint? Rva, string? Failure)> ResolveFromServerAsync(
        string module, ModuleIdentity identity, string symbol, CancellationToken cancellation)
    {
        Directory.CreateDirectory(_directory);
        // dbghelp tells a PDB from an image by its extension.
        string pdb = Path.Combine(_directory, $"{identity.PdbKey}.{identity.PdbName}");
        var watch = Stopwatch.StartNew();
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                // The symbol server answers a client that names itself the way the debuggers do.
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Microsoft-Symbol-Server/10.0.0.0");
                string url = $"{SymbolServer}/{identity.PdbName}/{identity.PdbKey}/{identity.PdbName}";
                using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return (null, response.StatusCode == System.Net.HttpStatusCode.NotFound ? "no_symbols_published" : "download_failed");
                await using var file = File.Create(pdb);
                await response.Content.CopyToAsync(file, cancellation).ConfigureAwait(false);
            }

            long bytes = new FileInfo(pdb).Length;
            DeckleShellTaskbarSource.Log.SymbolsDownloaded(module, (int)(bytes / (1024 * 1024)), (int)watch.Elapsed.TotalSeconds);
            return ReadOffset(pdb, identity.ImageSize, symbol);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            return (null, "download_failed");
        }
        finally
        {
            try { File.Delete(pdb); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // Any base will do: what comes back is an offset from it. A name the PDB
    // does not hold is an answer too (null), kept so the PDB is not fetched again.
    private static (uint? Rva, string? Failure) ReadOffset(string pdb, uint imageSize, string symbol)
    {
        const ulong Base = 0x10000000;
        lock (DbgHelpGate)
        {
            nint session = Environment.ProcessId + 0x5EED;
            DbgHelpNativeMethods.SymSetOptions(DbgHelpNativeMethods.SYMOPT_EXACT_SYMBOLS | DbgHelpNativeMethods.SYMOPT_FAIL_CRITICAL_ERRORS);
            if (!DbgHelpNativeMethods.SymInitialize(session, null, false)) return (null, "dbghelp_failed");
            nint info = Marshal.AllocHGlobal(DbgHelpNativeMethods.SymbolInfoSize + 2 * 1024);
            try
            {
                if (DbgHelpNativeMethods.SymLoadModuleEx(session, 0, pdb, null, Base, imageSize, 0, 0) == 0) return (null, "pdb_unreadable");
                unsafe { new Span<byte>((void*)info, DbgHelpNativeMethods.SymbolInfoSize).Clear(); }
                Marshal.WriteInt32(info, 0, DbgHelpNativeMethods.SymbolInfoSize);
                Marshal.WriteInt32(info, DbgHelpNativeMethods.SymbolInfoMaxNameLenOffset, 1024);
                if (!DbgHelpNativeMethods.SymFromName(session, symbol, info)) return (null, null);
                ulong address = (ulong)Marshal.ReadInt64(info, DbgHelpNativeMethods.SymbolInfoAddressOffset);
                return address > Base && address - Base < imageSize ? ((uint)(address - Base), null) : (null, "pdb_unreadable");
            }
            finally
            {
                Marshal.FreeHGlobal(info);
                DbgHelpNativeMethods.SymCleanup(session);
            }
        }
    }

    // The file explorer loaded, read from explorer's module list: the taskbar's
    // modules live in a SystemApps package folder whose name a build may change.
    private static string? LoadedModulePath(string module)
    {
        nint taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == 0) return null;
        GetWindowThreadProcessId(taskbar, out uint processId);
        try
        {
            using Process explorer = Process.GetProcessById((int)processId);
            foreach (ProcessModule loaded in explorer.Modules)
            {
                if (string.Equals(loaded.ModuleName, module, StringComparison.OrdinalIgnoreCase)) return loaded.FileName;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        return null;
    }

    private Dictionary<string, uint?> ReadCache()
    {
        try
        {
            return File.Exists(_cachePath)
                ? JsonSerializer.Deserialize<Dictionary<string, uint?>>(File.ReadAllText(_cachePath)) ?? new()
                : new();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();   // an unreadable cache costs one download
        }
    }

    private void WriteCache(Dictionary<string, uint?> cache)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(cache));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// What a module's file says about itself: the identity the engine checks in
/// memory (TimeDateStamp, SizeOfImage) and the one its PDB is published under.
/// </summary>
internal sealed record ModuleIdentity(uint TimeDateStamp, uint ImageSize, string? PdbName, string? PdbKey)
{
    public static ModuleIdentity Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        PEHeaders headers = reader.PEHeaders;
        uint timestamp = (uint)headers.CoffHeader.TimeDateStamp;
        uint size = (uint)(headers.PEHeader?.SizeOfImage ?? 0);

        foreach (DebugDirectoryEntry entry in reader.ReadDebugDirectory())
        {
            if (entry.Type != DebugDirectoryEntryType.CodeView) continue;
            CodeViewDebugDirectoryData codeView = reader.ReadCodeViewDebugDirectoryData(entry);
            // The server spells the key as the GUID's 32 hex digits, then the age in hex.
            string key = codeView.Guid.ToString("N").ToUpperInvariant() + codeView.Age.ToString("X");
            return new(timestamp, size, Path.GetFileName(codeView.Path), key);
        }
        return new(timestamp, size, null, null);
    }

    /// <summary>The bytes at an offset of the loaded image, as the file holds them; null outside every section.</summary>
    public static byte[]? ReadBytes(string path, uint rva, int count)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        PEMemoryBlock block = reader.GetSectionData((int)rva);
        return block.Length >= count ? block.GetContent(0, count).ToArray() : null;
    }
}
