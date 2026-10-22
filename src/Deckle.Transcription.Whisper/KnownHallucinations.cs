using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Deckle.Transcription;

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
// The match is per SEGMENT, on the whole segment, never a substring. Whisper
// emits a credit line as a segment of its own, but not always as an utterance
// of its own: once its decoder context holds a credit line, it can open a real
// take with one and go on with the speech in the next segment. Matching the
// whole utterance missed that case; dropping the matching segment strips the
// artifact and keeps the speech around it. A dictation that quotes the phrase
// inside a sentence keeps it, since that segment carries more than the phrase.
//
// Accepted risk: a segment that is nothing but a catalogue phrase is dropped
// even when someone really said it, as a sentence of its own. The entries are
// credit lines nobody dictates alone, and the whole-utterance rule already
// dropped a one-sentence take of them. Not caught: a credit line that Whisper
// glues to speech INSIDE one segment, which a whole-segment match cannot strip
// without matching substrings of real speech.
//
// Normalisation before matching keeps letters and digits only, case and
// diacritics folded: whisper varies the punctuation (a trailing '.' or '!', a
// period inside the line), the hyphens, the spacing and the case of the same
// credit, and the SousTitreur line carries a heart glyph.
//
// Whisper-specific by construction — the phrases ARE its training corpus — so it
// lives in this module, not in the backend-agnostic orchestrator.
internal static class KnownHallucinations
{
    // Curated catalogue of pure artifact signatures: subtitle/credit lines no
    // dictation would ever be reduced to on its own. Deliberately EXCLUDES
    // plausible phrases a user could say as a whole sentence (e.g. "Merci
    // d'avoir regardé cette vidéo") — there the false-positive risk outweighs
    // the catch. Keyed by the normalised form, valued by the entry as written,
    // which is what the log names.
    private static readonly Dictionary<string, string> Catalog =
        new[]
        {
            "Sous-titrage Société Radio-Canada",
            "Sous-titrage Radio-Canada",
            "Sous-titrage ST' 501",
            "Sous-titres réalisés par la communauté d'Amara.org",
            "Sous-titres réalisés para la communauté d'Amara.org",
            "Sous-titres faits par la communauté d'Amara.org",
            "Amara.org",
            "❤️ par SousTitreur.com",
        }
        .ToDictionary(Normalize, entry => entry, StringComparer.Ordinal);

    // The catalogue entry the text reduces to, or null when it is not a known
    // hallucination.
    public static string? Match(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        string key = Normalize(text);
        return key.Length > 0 ? Catalog.GetValueOrDefault(key) : null;
    }

    // Assembles the text to deliver from the decoded segments, leaving out every
    // segment that is a known hallucination. `filtered` lists what was left out,
    // in segment order.
    public static string StripFrom(
        IReadOnlyList<TranscriptionSegment> segments,
        out IReadOnlyList<FilteredHallucination> filtered)
    {
        var sb = new StringBuilder();
        List<FilteredHallucination>? found = null;
        for (int i = 0; i < segments.Count; i++)
        {
            string text = segments[i].Text;
            string? entry = Match(text);
            if (entry is null)
            {
                sb.Append(text);
                continue;
            }
            (found ??= new()).Add(new FilteredHallucination(i, text.Trim().Length, entry));
        }
        filtered = (IReadOnlyList<FilteredHallucination>?)found ?? Array.Empty<FilteredHallucination>();
        return sb.ToString().Trim();
    }

    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        // FormD splits an accented letter into its base letter and a combining
        // mark; the mark is not a letter, so it drops with the punctuation.
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}

// One segment left out of the delivered text: its index in the call's segments,
// its trimmed length, and the catalogue entry it matched.
internal readonly record struct FilteredHallucination(int SegmentIndex, int Characters, string Entry);
