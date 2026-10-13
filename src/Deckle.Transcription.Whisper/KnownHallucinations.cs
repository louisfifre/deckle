using System;
using System.Collections.Generic;
using System.Linq;

namespace Deckle.Transcription.Whisper;

// Whisper was trained on a large body of TV/film subtitles; on silence, music,
// or noise without clear speech it does not stay silent — it "completes" with
// the most frequent credit lines of that corpus. The French signature is
// "Sous-titrage Société Radio-Canada"; others recur across languages (the
// Amara.org / SousTitreur.com credits). These land at HIGH confidence, so
// logprob_thold / no_speech_thold never trip, and the RepetitionDetector —
// which only catches loops (A A A, A B A B) — does not see a single isolated
// emission. A phrase catalog is the only thing that catches them.
//
// The match is on the WHOLE utterance, never a substring. The artifact always
// arrives as an utterance to itself (a noise/music chunk decoded alone), while a
// real dictation that happens to quote the phrase carries surrounding context —
// so whole-utterance matching kills the artifact and leaves the citation
// intact.
//
// Normalisation before matching: edge whitespace and edge punctuation/symbols
// stripped (whisper ends the line with '.' or '!' some emissions, not others;
// the SousTitreur line is prefixed with a heart glyph), case folded through the
// comparer. Interior punctuation is kept (the apostrophe in "d'Amara").
//
// Whisper-specific by construction — the phrases ARE its training corpus — so it
// lives in this module, not in the backend-agnostic orchestrator.
internal static class KnownHallucinations
{
    // Curated catalogue of pure artifact signatures: subtitle/credit lines no
    // dictation would ever be reduced to on its own. Deliberately EXCLUDES
    // plausible phrases a user could say as a whole utterance (e.g. "Merci
    // d'avoir regardé cette vidéo") — there the false-positive risk outweighs
    // the catch. Stored normalised; the comparer folds case.
    private static readonly HashSet<string> Catalog =
        new[]
        {
            "Sous-titrage Société Radio-Canada",
            "Sous-titrage ST' 501",
            "Sous-titres réalisés par la communauté d'Amara.org",
            "Sous-titres faits par la communauté d'Amara.org",
            "Amara.org",
            "❤️ par SousTitreur.com",
        }
        .Select(Normalize)
        .ToHashSet(StringComparer.InvariantCultureIgnoreCase);

    public static bool Matches(string utteranceText)
        => !string.IsNullOrEmpty(utteranceText) && Catalog.Contains(Normalize(utteranceText));

    private static string Normalize(string text)
    {
        int start = 0, end = text.Length;
        while (start < end && IsTrimmable(text[start])) start++;
        while (end > start && IsTrimmable(text[end - 1])) end--;
        return text[start..end];
    }

    private static bool IsTrimmable(char c)
        => char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c);
}
