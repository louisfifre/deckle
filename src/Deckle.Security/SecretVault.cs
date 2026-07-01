using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Deckle.Core;

namespace Deckle.Security;

// ── SecretVault ───────────────────────────────────────────────────────────
//
// The default ISecretVault: a single file under %LOCALAPPDATA%\Deckle, sealed
// at rest with DPAPI (CurrentUser scope — only this Windows account can
// decrypt). Follows the HarvestStore pattern (DPAPI + atomic temp+Move) with
// two deliberate departures that the higher stakes of a credential store
// justify:
//
//   • No debounce. Secrets are low-volume, high-value: a Set/Remove writes
//     synchronously and is durable when the call returns, never deferred.
//
//   • Stateless on disk. Every operation reads the file fresh; a mutation
//     loads, edits, and writes back under a per-file process mutex. The vault
//     holds many independent secrets in one file, so a cached in-memory copy
//     would let two app instances clobber each other's keys (A loads, B loads,
//     A writes key1, B writes key2 over A). Reading fresh under the lock closes
//     that window — the cost is nil at this call rate.
//
//   • A present-but-unreadable file throws, it does not silently start empty.
//     A missing file is an empty vault (normal first run); a file that exists
//     but cannot be decrypted (wrong account, corruption) is an anomaly the
//     user must see — starting empty would then overwrite recoverable state on
//     the next Set. HarvestStore can afford the silent-empty fallback because
//     its data is a re-derivable capture; a credential is not.
//
// DPAPI is used with null entropy, matching HarvestStore. A static app-baked
// entropy would be security-by-obscurity (it ships in the binary) and would
// pin the format forever; per-install random entropy has a chicken-and-egg
// storage problem. CurrentUser scope already excludes other Windows accounts;
// hardening against same-user malware is a separate, deferred pass.
public sealed class SecretVault : ISecretVault
{
    private const int SchemaVersion = 1;
    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly string _mutexName;

    /// <summary>
    /// Opens (or, on first write, creates) a vault backed by the file at
    /// <paramref name="filePath"/>. The file is not touched until a secret is
    /// written; construction is cheap and side-effect-free.
    /// </summary>
    public SecretVault(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _path = System.IO.Path.GetFullPath(filePath);
        _mutexName = DeriveMutexName(_path);
    }

    /// <summary>The production vault at <see cref="AppPaths.SecretsFilePath"/>.</summary>
    public static SecretVault CreateDefault() => new(AppPaths.SecretsFilePath);

    /// <summary>The on-disk file backing this vault (resolved at construction).</summary>
    public string Path => _path;

    public bool TryGet(string name, out string? value)
    {
        ValidateName(name);
        return Load().TryGetValue(name, out value);
    }

    public bool Contains(string name)
    {
        ValidateName(name);
        return Load().ContainsKey(name);
    }

    public void Set(string name, string value)
    {
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(value);
        Mutate(secrets =>
        {
            secrets[name] = value;
            return true;
        });
    }

    public bool Remove(string name)
    {
        ValidateName(name);
        bool removed = false;
        Mutate(secrets => removed = secrets.Remove(name));
        return removed;
    }

    // ── Persistence ───────────────────────────────────────────────────────

    // Load → mutate → save, serialized across processes by a per-file mutex so
    // concurrent instances never drop each other's keys. Writes only when the
    // mutation reports a change (a no-op Remove leaves the file untouched).
    private void Mutate(Func<Dictionary<string, string>, bool> mutate)
    {
        using var processMutex = new Mutex(initiallyOwned: false, _mutexName);
        bool acquired = false;
        try
        {
            try
            {
                acquired = processMutex.WaitOne(MutexTimeout);
            }
            catch (AbandonedMutexException)
            {
                // A previous holder crashed mid-write; we inherited the mutex.
                // The file it left behind is intact (atomic Move) — proceed.
                acquired = true;
            }

            if (!acquired)
                throw new SecretVaultException(
                    $"Timed out acquiring the vault lock for {_path}; another instance may be writing.");

            Dictionary<string, string> secrets = Load();
            if (mutate(secrets))
                Save(secrets);
        }
        finally
        {
            if (acquired) processMutex.ReleaseMutex();
        }
    }

    // A missing file is an empty vault. A file that exists but cannot be read,
    // decrypted, or parsed is an anomaly: throw so the caller surfaces it rather
    // than silently overwriting it on the next Set.
    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_path))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        byte[] cipher;
        try
        {
            cipher = File.ReadAllBytes(_path);
        }
        catch (Exception ex)
        {
            throw new SecretVaultException($"Could not read the secret vault at {_path}.", ex);
        }

        byte[] plain;
        try
        {
            plain = ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new SecretVaultException(
                $"The secret vault at {_path} could not be decrypted — it may have been written by a different Windows account, or it is corrupt.",
                ex);
        }

        try
        {
            VaultData? data = JsonSerializer.Deserialize<VaultData>(Encoding.UTF8.GetString(plain), JsonOptions);
            return data?.Secrets ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            throw new SecretVaultException($"The secret vault at {_path} holds unreadable data.", ex);
        }
    }

    // Atomic write: encrypt, write a temp file, then Move over the target — a
    // kill mid-write can never leave a truncated (undecryptable) vault behind.
    private void Save(Dictionary<string, string> secrets)
    {
        var data = new VaultData { Version = SchemaVersion, Secrets = secrets };

        byte[] cipher;
        try
        {
            byte[] plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, JsonOptions));
            cipher = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new SecretVaultException($"Could not encrypt the secret vault for {_path}.", ex);
        }

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            string tmp = _path + ".tmp";
            File.WriteAllBytes(tmp, cipher);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            throw new SecretVaultException($"Could not write the secret vault at {_path}.", ex);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void ValidateName(string name)
        => ArgumentException.ThrowIfNullOrWhiteSpace(name);

    // A named kernel mutex can't carry a path (backslashes/colons are illegal),
    // and one global name would serialize unrelated vaults. Derive a stable,
    // collision-safe name from the file's full path so two handles onto the
    // same file coordinate while distinct files (and parallel tests) don't.
    private static string DeriveMutexName(string fullPath)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant()));
        return "Deckle-Secrets-" + Convert.ToHexString(hash, 0, 8);
    }

    // The on-disk envelope. Version is written for forward compatibility; the
    // reader tolerates its absence (defaults apply) and does not yet branch on
    // it — the first migration will.
    private sealed class VaultData
    {
        public int Version { get; set; } = SchemaVersion;
        public Dictionary<string, string> Secrets { get; set; } = new(StringComparer.Ordinal);
    }
}
