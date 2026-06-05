using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Deckle.Llm.Rewrite;

namespace Deckle.Transcription.Corpus;

// Stable fingerprint of a RewriteProfile's effective template, returned as
// SHA256 hex truncated to 16 characters. Allows rewrite corpus analyses to
// invalidate a cohort when the user edits the SystemPrompt (or a generation
// parameter) without changing the profile ID. The ID identifies the instance;
// the hash identifies the content.
//
// Included fields: Model, SystemPrompt, Temperature, NumCtxK, TopP,
// RepeatPenalty. This is the set of inputs that semantically change Ollama
// output for the same input. Id and Name are not included; they are already
// emitted as distinct event fields, and a rename does not change generated
// content.
//
// Separator  (US, Unit Separator) between fields: a non-printable character
// historically reserved for this role, cannot appear in normally typed text,
// avoids any collision with a character present in the SystemPrompt.
//
// 16 hex chars = 8 hash bytes = 64 bits, easily enough to distinguish two
// templates in a user corpus (50% collision at ~5 billion distinct templates:
// out of reach).
internal static class PromptTemplateHash
{
    private const char Separator = '';

    public static string Of(RewriteProfile? profile)
    {
        if (profile is null) return "";

        var sb = new StringBuilder((profile.SystemPrompt?.Length ?? 0) + 128);
        sb.Append(profile.Model ?? "");                                                              sb.Append(Separator);
        sb.Append(profile.SystemPrompt ?? "");                                                       sb.Append(Separator);
        sb.Append(profile.Temperature?.ToString("R", CultureInfo.InvariantCulture) ?? "");           sb.Append(Separator);
        sb.Append(profile.NumCtxK?.ToString(CultureInfo.InvariantCulture) ?? "");                    sb.Append(Separator);
        sb.Append(profile.TopP?.ToString("R", CultureInfo.InvariantCulture) ?? "");                  sb.Append(Separator);
        sb.Append(profile.RepeatPenalty?.ToString("R", CultureInfo.InvariantCulture) ?? "");

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
