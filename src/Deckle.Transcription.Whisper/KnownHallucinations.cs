using System;
using System.Collections.Generic;
using System.Linq;

namespace Deckle.Transcription.Whisper;

// ── KnownHallucinations ──────────────────────────────────────────────────────
//
// Whisper was trained on a large body of TV/film subtitles; on silence, music,
// or noise without clear speech it does not stay silent — it "completes" with
// the most frequent credit lines of that corpus. The French signature is
// "Sous-titrage Société Radio-Canada"; others recur across languages (the
// Amara.org / SousTitreur.com credits). These land at HIGH confidence
// (observed p̄ ≈ 0.9, no_speech ≈ 0%), so logprob_thold / no_speech_thold never
// trip and the RepetitionDetector — which only catches loops (A A A, A B A B) —
// does not see a single isolated emission. A phrase catalog is the only thing
// that catches them. Observed 2026-06 on this machine: 24 isolated emissions of
// the Radio-Canada line.
//
// The match is on the WHOLE utterance, never a substring. The artifact always
// arrives as an utterance to itself (a noise/music chunk decoded alone), while a
// real dictation that happens to quote the phrase carries surrounding context —
// so whole-utterance matching kills the artifact and leaves the citation
// intact. Confirmed against the logged corpus: the real dictations that
// mentioned "sous-titrage Radio-Canada" were all longer utterances, the
// artifact was always the bare phrase.
//
// Normalisation before matching: edge whitespace and edge punctuation/symbols
// stripped (whisper ends the line with '.' or '!' some emissions, not others;
// the SousTitreur line is prefixed with a heart glyph), case folded through the
// comparer. Interior punctuation is kept (the apostrophe in "d'Amara").
//
// Whisper-specific by construction — the phrases ARE its training corpus — so it
// lives here beside RepetitionDetector, not in the backend-agnostic
// orchestrator. Voxtral and future backends hallucinate differently and will
// bring their own catalogue if needed.
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

    // True when the whole utterance is a known hallucination. The caller passes
    // the assembled, trimmed utterance text; matching is whole-string only — a
    // hit means the entire utterance is the artifact and nothing else.
    public static bool Matches(string utteranceText)
        => !string.IsNullOrEmpty(utteranceText) && Catalog.Contains(Normalize(utteranceText));

    // Strip edge whitespace, then edge punctuation/symbols, keeping the interior
    // intact. Applied identically to the catalogue entries and the candidate, so
    // the two are compared on equal footing.
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
