namespace Deckle.Benchmark.PhiBench.Models;

/// <summary>
/// One transcription outcome, mirroring the Python Transcription dataclass
/// in benchmark/asr/lib/sources/_base.py. RTF = ElapsedSeconds / AudioSeconds ;
/// &lt; 1.0 means faster than realtime.
/// </summary>
public sealed record TranscriptionResult(
    string Text,
    double ElapsedSeconds,
    double AudioSeconds,
    double Rtf,
    int GeneratedTokens,
    bool Ok,
    string Error,
    Dictionary<string, object?> Extras);
