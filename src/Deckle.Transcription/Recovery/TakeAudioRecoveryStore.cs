using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Deckle.Transcription;

/// <summary>Outcome of <see cref="TakeAudioRecoveryStore.TryRead"/>.</summary>
public enum TakeAudioReadStatus
{
    /// <summary>No audio file: the last run did not die mid-take.</summary>
    Missing,

    /// <summary>The file was read; the audio holds every whole record.</summary>
    Restored,

    /// <summary>The file exists but cannot be read (other account, corrupt header).</summary>
    Unreadable,
}

/// <param name="Status">How the read ended.</param>
/// <param name="Samples">Mono 16 kHz float samples; empty unless <c>Restored</c>.</param>
/// <param name="Records">How many sealed blocks were read.</param>
/// <param name="TailLost">True when bytes after the last whole record were dropped.</param>
public readonly record struct TakeAudioRead(
    TakeAudioReadStatus Status,
    float[] Samples,
    int Records,
    bool TailLost)
{
    public static TakeAudioRead Missing { get; } = new(TakeAudioReadStatus.Missing, [], 0, false);

    public static TakeAudioRead Unreadable { get; } = new(TakeAudioReadStatus.Unreadable, [], 0, false);

    public double Seconds => Samples.Length / (double)TakeAudioRecoveryStore.SampleRate;
}

// ── TakeAudioRecoveryStore ─────────────────────────────────────────────────
//
// One file holding the audio of the take in progress, so a take that fails or
// a crash before the text is delivered does not lose what was said. The audio
// is personal data, so every block is sealed with DPAPI (CurrentUser, no
// entropy) before it touches the disk; no plaintext copy exists, not even a
// temporary one. The same limits as DictationRecoveryStore apply: another
// Windows account and an offline reader cannot open the file, a program under
// the same account can.
//
// Format. A plain header, then records until the end of the file:
//
//   header  "DKTA", version, sample rate, channels, bits per sample
//           (four little-endian int32 after the magic)
//   record  int32 length, then a DPAPI blob of that many bytes holding
//           PCM16 mono samples
//
// DPAPI seals a whole blob, so the audio is cut into records of about a
// second (the recorder decides the size) and each record is one write
// followed by a flush: a crash loses at most the record in flight, and the
// reader stops cleanly at a truncated or damaged tail, keeping everything
// before it.
//
// The file lives for one take. TryBegin starts a fresh one, replacing whatever
// a previous take left; the engine deletes it once the text is delivered and
// keeps it on every other ending. Only one take is ever kept.
//
// A recovery run first sets the file aside as ".attempted" (MarkAttempted). If
// the run finishes, the marker is deleted; if it fails cleanly, RestoreAttempted
// puts the file back; if the process dies, the marker stays and the next launch
// knows not to run the same audio through the same crash.
//
// No operation throws: the audio copy is a safety net, and a failure of the
// net must never break the dictation it protects.
public sealed class TakeAudioRecoveryStore(string filePath)
{
    internal const int SampleRate = 16_000;

    private const int Version = 1;
    private const int Channels = 1;
    private const int BitsPerSample = 16;
    private const int HeaderLength = 4 + 4 * 4;

    // A record holds about a second of audio (some 32 KB sealed). This bound
    // only keeps a corrupt length from allocating the machine's memory.
    private const int MaxRecordLength = 16 * 1024 * 1024;

    private static ReadOnlySpan<byte> Magic => "DKTA"u8;

    private readonly object _gate = new();

    // Open from TryBegin to Close, so each record is one write on a handle that
    // already exists. Read and written under _gate.
    private FileStream? _stream;

    // Consecutive failed records of this take; read and written under _gate.
    // The write runs about every second, so only the first failure warns.
    private int _writeFailures;

    public string FilePath { get; } = filePath;

    public string AttemptedFilePath { get; } = Path.ChangeExtension(filePath, ".attempted");

    public bool Exists => File.Exists(FilePath);

    public bool AttemptedExists => File.Exists(AttemptedFilePath);

    /// <summary>
    /// Starts a fresh file for a new take, replacing a leftover one. Returns
    /// false (after logging a warning) when the disk refuses; the take then
    /// goes on unprotected.
    /// </summary>
    public bool TryBegin()
    {
        lock (_gate)
        {
            CloseStream();
            _writeFailures = 0;
            try
            {
                string? directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var stream = new FileStream(
                    FilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                try
                {
                    Span<byte> header = stackalloc byte[HeaderLength];
                    Magic.CopyTo(header);
                    BinaryPrimitives.WriteInt32LittleEndian(header[4..], Version);
                    BinaryPrimitives.WriteInt32LittleEndian(header[8..], SampleRate);
                    BinaryPrimitives.WriteInt32LittleEndian(header[12..], Channels);
                    BinaryPrimitives.WriteInt32LittleEndian(header[16..], BitsPerSample);
                    stream.Write(header);
                    stream.Flush();
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }

                _stream = stream;
                return true;
            }
            catch (Exception ex)
            {
                DeckleWhispSource.Log.TakeAudioWriteFailed(ex.GetType().Name, ex.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// Seals <paramref name="samples"/> as one record and appends it. Returns
    /// false when the take has no open file or the disk or DPAPI refuses; the
    /// block is then lost and the next one is tried on a clean end of file.
    /// </summary>
    public bool TryAppend(ReadOnlySpan<float> samples)
    {
        lock (_gate)
        {
            FileStream? stream = _stream;
            if (stream is null) return false;
            if (samples.IsEmpty) return true;

            long position = stream.Position;
            byte[] pcm = new byte[samples.Length * 2];
            try
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    int value = (int)MathF.Round(samples[i] * 32768f);
                    BinaryPrimitives.WriteInt16LittleEndian(
                        pcm.AsSpan(i * 2), (short)Math.Clamp(value, short.MinValue, short.MaxValue));
                }

                byte[] sealedBlock = ProtectedData.Protect(
                    pcm, optionalEntropy: null, DataProtectionScope.CurrentUser);
                byte[] record = new byte[4 + sealedBlock.Length];
                BinaryPrimitives.WriteInt32LittleEndian(record, sealedBlock.Length);
                sealedBlock.CopyTo(record, 4);

                stream.Write(record);
                stream.Flush();

                if (_writeFailures > 0)
                {
                    DeckleWhispSource.Log.TakeAudioWriteRecovered(_writeFailures);
                    _writeFailures = 0;
                }
                return true;
            }
            catch (Exception ex)
            {
                if (_writeFailures++ == 0)
                    DeckleWhispSource.Log.TakeAudioWriteFailed(ex.GetType().Name, ex.Message);
                else
                    DeckleWhispSource.Log.TakeAudioWriteRetryFailed(ex.GetType().Name, ex.Message);

                // A partial record would shift every later one, so the file goes
                // back to its last whole record. If even that is refused, the
                // handle is abandoned: what was written before stays readable.
                try
                {
                    stream.SetLength(position);
                    stream.Position = position;
                }
                catch (Exception truncateEx)
                {
                    DeckleWhispSource.Log.TeardownExceptionIgnored(
                        "take_audio_truncate", truncateEx.GetType().Name, truncateEx.Message);
                    CloseStream();
                }
                return false;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pcm);
            }
        }
    }

    /// <summary>Closes the write handle, keeping the file. A closed store appends nothing.</summary>
    public void Close()
    {
        lock (_gate) CloseStream();
    }

    /// <summary>Size of the file now, 0 when it is missing or unreadable.</summary>
    public long Length
    {
        get
        {
            try { return File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0; }
            catch (Exception ex)
            {
                // The size only enriches a log line; 0 means unknown.
                DeckleWhispSource.Log.TeardownExceptionIgnored("take_audio_length", ex.GetType().Name, ex.Message);
                return 0;
            }
        }
    }

    /// <summary>Reads the take's file. Never throws.</summary>
    public TakeAudioRead TryRead() => ReadFrom(FilePath);

    /// <summary>Reads the file a recovery run has set aside. Never throws.</summary>
    public TakeAudioRead TryReadAttempted() => ReadFrom(AttemptedFilePath);

    /// <summary>
    /// Deletes the take's file. A missing file is success; a refusal is logged
    /// and returns false.
    /// </summary>
    public bool Delete()
    {
        lock (_gate)
        {
            CloseStream();
            return DeleteFile(FilePath);
        }
    }

    /// <summary>Deletes the set-aside file. A missing file is success.</summary>
    public bool DeleteAttempted()
    {
        lock (_gate) return DeleteFile(AttemptedFilePath);
    }

    /// <summary>
    /// Sets the take's file aside before a recovery run, so a crash during the
    /// run leaves a marker. Returns false when the move is refused.
    /// </summary>
    public bool MarkAttempted()
    {
        lock (_gate)
        {
            CloseStream();
            try
            {
                File.Move(FilePath, AttemptedFilePath, overwrite: false);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The caller names the consequence; this keeps the cause.
                DeckleWhispSource.Log.TeardownExceptionIgnored(
                    "take_audio_mark_attempted", ex.GetType().Name, ex.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// Puts the set-aside file back after a recovery run that failed cleanly, so
    /// the next launch tries again. A newer take already holds the file name, and
    /// only one take is kept: the set-aside one is then deleted. Returns whether
    /// the audio is still on disk.
    /// </summary>
    public bool RestoreAttempted()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    DeleteFile(AttemptedFilePath);
                    return false;
                }

                File.Move(AttemptedFilePath, FilePath, overwrite: false);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The marker stays, which is the safe side: no automatic retry.
                DeckleWhispSource.Log.TeardownExceptionIgnored(
                    "take_audio_restore_attempted", ex.GetType().Name, ex.Message);
                return File.Exists(AttemptedFilePath);
            }
        }
    }

    private TakeAudioRead ReadFrom(string path)
    {
        lock (_gate)
        {
            if (!File.Exists(path))
                return TakeAudioRead.Missing;

            try
            {
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                Span<byte> header = stackalloc byte[HeaderLength];
                if (stream.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false) < HeaderLength
                    || !header[..4].SequenceEqual(Magic)
                    || BinaryPrimitives.ReadInt32LittleEndian(header[4..]) != Version
                    || BinaryPrimitives.ReadInt32LittleEndian(header[8..]) != SampleRate
                    || BinaryPrimitives.ReadInt32LittleEndian(header[12..]) != Channels
                    || BinaryPrimitives.ReadInt32LittleEndian(header[16..]) != BitsPerSample)
                {
                    DeckleWhispSource.Log.TakeAudioReadFailed("InvalidData", "header");
                    return TakeAudioRead.Unreadable;
                }

                var blocks = new List<float[]>();
                int total = 0;
                bool tailLost = false;
                Span<byte> lengthBytes = stackalloc byte[4];
                while (true)
                {
                    int got = stream.ReadAtLeast(lengthBytes, 4, throwOnEndOfStream: false);
                    if (got == 0) break;

                    int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                    if (got < 4 || length <= 0 || length > MaxRecordLength)
                    {
                        tailLost = true;
                        break;
                    }

                    byte[] sealedBlock = new byte[length];
                    if (stream.ReadAtLeast(sealedBlock, length, throwOnEndOfStream: false) < length)
                    {
                        tailLost = true;
                        break;
                    }

                    byte[] pcm;
                    try
                    {
                        pcm = ProtectedData.Unprotect(
                            sealedBlock, optionalEntropy: null, DataProtectionScope.CurrentUser);
                    }
                    catch (CryptographicException ex)
                    {
                        // The first record failing means the file is not ours to
                        // read (other account); a later one is a damaged tail.
                        DeckleWhispSource.Log.TakeAudioReadFailed(ex.GetType().Name, ex.Message);
                        if (blocks.Count == 0) return TakeAudioRead.Unreadable;
                        tailLost = true;
                        break;
                    }

                    if (pcm.Length % 2 != 0)
                    {
                        CryptographicOperations.ZeroMemory(pcm);
                        tailLost = true;
                        break;
                    }

                    var block = new float[pcm.Length / 2];
                    for (int i = 0; i < block.Length; i++)
                        block[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 2)) / 32768f;
                    CryptographicOperations.ZeroMemory(pcm);

                    blocks.Add(block);
                    total += block.Length;
                }

                var samples = new float[total];
                int offset = 0;
                foreach (float[] block in blocks)
                {
                    block.CopyTo(samples, offset);
                    offset += block.Length;
                }
                return new TakeAudioRead(TakeAudioReadStatus.Restored, samples, blocks.Count, tailLost);
            }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
            {
                DeckleWhispSource.Log.TakeAudioReadFailed(ex.GetType().Name, ex.Message);
                return TakeAudioRead.Unreadable;
            }
        }
    }

    private static bool DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            DeckleWhispSource.Log.TakeAudioDeleteFailed(ex.GetType().Name, ex.Message);
            return false;
        }
    }

    private void CloseStream()
    {
        FileStream? stream = _stream;
        _stream = null;
        if (stream is null) return;
        try { stream.Dispose(); }
        catch (Exception ex)
        {
            // The flush already ran after each record; a refusal here loses nothing.
            DeckleWhispSource.Log.TeardownExceptionIgnored("take_audio_close", ex.GetType().Name, ex.Message);
        }
    }
}
