namespace Deckle.Transcription;

// ── ModelEntry ───────────────────────────────────────────────────────────────
//
// One downloadable speech model the wizard can install. SizeBytes is nominal:
// it sizes the progress bar and the disk estimate, while Sha256 verifies.
//
// Lives in the transcription parent because the shape is backend-agnostic;
// catalogs are backend-specific (Deckle.Transcription.Whisper/Setup/SpeechModels.cs).
public sealed record ModelEntry(
    string Id,
    string FileName,
    string DisplayName,
    string Url,
    long SizeBytes,
    string? Sha256 = null);
