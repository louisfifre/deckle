namespace Deckle.Transcription;

// Explicit output command carried by a transcription run. The pipeline core
// produces text once; this value selects only the delivery edge.
internal readonly record struct TranscriptionDelivery(
    TranscriptionDeliveryKind Kind,
    string? SourceAudioPath = null)
{
    public static TranscriptionDelivery Dictation =>
        new(TranscriptionDeliveryKind.Dictation);

    public static TranscriptionDelivery AdjacentFile(string sourceAudioPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAudioPath);
        return new(TranscriptionDeliveryKind.AdjacentFile, sourceAudioPath);
    }

    public bool IsFile => Kind == TranscriptionDeliveryKind.AdjacentFile;
}

internal enum TranscriptionDeliveryKind
{
    Dictation,
    AdjacentFile,
}
