namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // Raised from the streaming consumer task for every utterance that
    // contributes text, in emission order, after that text has been appended
    // to the take. Live dictation and file transcription share the consumer,
    // so both raise it. Fires on a background thread — subscribers marshal.
    public event Action<TranscribedUtterance>? UtteranceTranscribed;
}
