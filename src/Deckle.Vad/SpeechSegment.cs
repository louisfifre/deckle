namespace Deckle.Vad;

// A contiguous span of speech within an audio buffer, in 16 kHz mono sample
// indices. StartSample is inclusive, EndSample exclusive.
public readonly record struct SpeechSegment(int StartSample, int EndSample)
{
    public int LengthSamples => EndSample - StartSample;
}
