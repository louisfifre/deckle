namespace Deckle.Transcription.Setup;

// ── ModelEntry ───────────────────────────────────────────────────────────────
//
// One downloadable speech model — Whisper, Silero, or any future ASR family
// the wizard knows how to install. Url + Sha256 + SizeBytes drive the
// downloader; Url empty means the entry can only be satisfied by a local
// copy. SizeBytes is nominal — used to size the progress bar and budget
// the disk estimate, not for verification.
//
// Lives in the transcription parent (not in any backend child) because the
// shape is backend-agnostic: a Voxtral catalog would reuse the same record.
// Catalogs themselves are backend-specific (cf. Deckle.Transcription.Whisper
// /Setup/SpeechModels.cs for the Whisper + Silero catalog).
public sealed record ModelEntry(
    string Id,
    string FileName,
    string DisplayName,
    string Url,
    long SizeBytes,
    string? Sha256 = null);
