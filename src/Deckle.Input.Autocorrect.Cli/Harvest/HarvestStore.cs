using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Deckle.Input.Autocorrect.Cli;

// Encrypted persistence for the observation harvest. Unlike the personal
// dictionary (plaintext, inspectable by doctrine), the harvest is a raw,
// higher-volume capture of real typed words, so it is sealed at rest with DPAPI
// (CurrentUser scope — only this Windows account can decrypt). The readable
// surface is the `harvest list` command, which decrypts in memory and prints;
// the file on disk is ciphertext.
//
// Writes are debounced (a burst of typing collapses to one flush) and atomic
// (temp + Move); a synchronous Flush on stop bounds loss to the debounce
// window. One writer is assumed — a single `harvest` process; concurrent
// instances would last-writer-win, never tear the file.
internal sealed class HarvestStore : IDisposable
{
    private const int DebounceMs = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly object _lock = new();       // guards _data and _dirty
    private readonly object _flushLock = new();  // serializes overlapping flushes
    private readonly Func<DateTimeOffset> _clock;
    private readonly System.Threading.Timer _debounce;
    private readonly HarvestData _data;
    private bool _dirty;                          // unsaved mutation pending

    public HarvestStore(string path, Func<DateTimeOffset>? clock = null)
    {
        _path = path;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _data = Load(path);
        _debounce = new System.Threading.Timer(
            _ => Flush(), null,
            System.Threading.Timeout.Infinite,
            System.Threading.Timeout.Infinite);
    }

    public string Path => _path;

    public void RecordEdit(string original, string replacement)
    {
        lock (_lock)
        {
            _data.RecordEdit(original, replacement, _clock());
            _dirty = true;
        }
        Save();
    }

    public void RecordUnknownWord(string word)
    {
        lock (_lock)
        {
            _data.RecordUnknownWord(word, _clock());
            _dirty = true;
        }
        Save();
    }

    // Snapshots both streams, heaviest first — the order the `list` command and
    // any offline ranking want.
    public (IReadOnlyList<HarvestedEdit> Edits, IReadOnlyList<HarvestedWord> Words) Snapshot()
    {
        lock (_lock)
            return (
                _data.Edits.OrderByDescending(e => e.Count).ThenBy(e => e.Original, StringComparer.Ordinal).ToList(),
                _data.UnknownWords.OrderByDescending(w => w.Count).ThenBy(w => w.Word, StringComparer.Ordinal).ToList());
    }

    public void Purge()
    {
        lock (_lock)
        {
            _data.Edits.Clear();
            _data.UnknownWords.Clear();
            _dirty = true;
        }
        Flush();
    }

    private void Save() => _debounce.Change(DebounceMs, System.Threading.Timeout.Infinite);

    // Writes the harvest to disk, encrypted, but only when there is an unsaved
    // mutation — so a read-only command (`harvest list`) never creates or
    // rewrites the file. _flushLock serializes overlapping callers (the debounce
    // timer and the shutdown flush) so they never collide on the temp path.
    public void Flush()
    {
        lock (_flushLock)
        {
            string json;
            lock (_lock)
            {
                if (!_dirty) return;
                json = JsonSerializer.Serialize(_data, JsonOptions);
                _dirty = false;
            }

            try
            {
                byte[] plain = Encoding.UTF8.GetBytes(json);
                byte[] cipher = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);

                // Atomic write: temp then Move, so a kill mid-write never leaves a
                // truncated (and thus undecryptable) file behind.
                string tmp = _path + ".tmp";
                File.WriteAllBytes(tmp, cipher);
                File.Move(tmp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                lock (_lock) _dirty = true; // failed write: let a later flush retry
                Console.Error.WriteLine($"harvest: save failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    // Reads and decrypts the harvest. A missing, corrupt, or undecryptable file
    // falls back to an empty harvest — capture simply starts fresh rather than
    // crashing on a file written under another account or a partial write.
    private static HarvestData Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new HarvestData();

            byte[] cipher = File.ReadAllBytes(path);
            byte[] plain = ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);
            string json = Encoding.UTF8.GetString(plain);
            return JsonSerializer.Deserialize<HarvestData>(json, JsonOptions) ?? new HarvestData();
        }
        catch
        {
            return new HarvestData();
        }
    }

    public void Dispose()
    {
        _debounce.Dispose();
        Flush();
    }
}
