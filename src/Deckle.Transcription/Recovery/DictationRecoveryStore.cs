using System.Security.Cryptography;
using System.Text;

namespace Deckle.Transcription;

/// <summary>Outcome of <see cref="DictationRecoveryStore.TryRead"/>.</summary>
public enum DictationRecoveryReadStatus
{
    /// <summary>No recovery file: the last run did not die mid-dictation.</summary>
    Missing,

    /// <summary>The file was unsealed; <c>text</c> holds the raw transcript.</summary>
    Restored,

    /// <summary>The file exists but cannot be unsealed (other account, corruption).</summary>
    Unreadable,
}

// ── DictationRecoveryStore ─────────────────────────────────────────────────
//
// One file holding the raw transcript of the take in progress, so a crash
// mid-dictation does not lose what was already said. The text is personal
// data, so it is sealed with DPAPI (CurrentUser, no entropy) before it touches
// the disk.
//
// What DPAPI covers here: another Windows account, and someone reading the
// disk offline, cannot read the file. What it does not cover: a program
// running under the same Windows account can unseal it, since CurrentUser
// scope trusts the whole account. That is accepted for now; the app's
// security is to be reviewed as a whole later. The SecretVault is not used on
// purpose, it holds keys and is read at every launch, not a rewritten buffer.
//
// The file lives only during a dictation. The engine rewrites it at every
// utterance and deletes it once the text is delivered or the take is
// discarded. The write is atomic (temp file, then File.Move overwrite) so a
// crash during the write leaves the previous complete copy.
//
// No operation throws: the recovery copy is a safety net, and a failure of
// the net must never break the dictation it protects.
public sealed class DictationRecoveryStore(string filePath)
{
    private readonly object _gate = new();

    public string FilePath { get; } = filePath;

    public bool Exists => File.Exists(FilePath);

    /// <summary>
    /// Seals <paramref name="text"/> and replaces the file with it. Returns
    /// false (after logging a warning without the text) when the disk or DPAPI
    /// refuses.
    /// </summary>
    public bool TryWrite(string text)
    {
        lock (_gate)
        {
            string tmp = FilePath + ".tmp";
            try
            {
                string? directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                byte[] plain = Encoding.UTF8.GetBytes(text);
                byte[] sealedBytes = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(tmp, sealedBytes);
                File.Move(tmp, FilePath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                DeckleWhispSource.Log.DictationRecoveryWriteFailed();
                DeckleWhispSource.Log.DictationRecoveryWriteFailedDetail(ex.GetType().Name);
                TryDeleteQuietly(tmp);
                return false;
            }
        }
    }

    /// <summary>
    /// Reads the file back. <c>Unreadable</c> covers a file DPAPI refuses and
    /// one the disk cannot deliver; the caller decides whether to delete it.
    /// </summary>
    public DictationRecoveryReadStatus TryRead(out string text)
    {
        text = string.Empty;
        lock (_gate)
        {
            if (!File.Exists(FilePath))
                return DictationRecoveryReadStatus.Missing;

            try
            {
                byte[] sealedBytes = File.ReadAllBytes(FilePath);
                byte[] plain = ProtectedData.Unprotect(sealedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                text = Encoding.UTF8.GetString(plain);
                return DictationRecoveryReadStatus.Restored;
            }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
            {
                return DictationRecoveryReadStatus.Unreadable;
            }
        }
    }

    /// <summary>Deletes the file. A missing file is success; a refusal is logged and returns false.</summary>
    public bool Delete()
    {
        lock (_gate)
        {
            try
            {
                File.Delete(FilePath);
                return true;
            }
            catch (Exception ex)
            {
                DeckleWhispSource.Log.DictationRecoveryDeleteFailed();
                DeckleWhispSource.Log.DictationRecoveryDeleteFailedDetail(ex.GetType().Name);
                return false;
            }
        }
    }

    private static void TryDeleteQuietly(string path)
    {
        try { File.Delete(path); } catch { /* the temp file is only a leftover */ }
    }
}
