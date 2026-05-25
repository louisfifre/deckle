using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Deckle.Llm.Rewrite;

namespace Deckle.Transcription.Corpus;

// Fingerprint stable du template effectif d'un RewriteProfile, retournée
// en hex SHA256 tronqué à 16 caractères. Permet aux analyses du corpus
// rewrite d'invalider une cohorte quand l'utilisateur retouche le
// SystemPrompt (ou un paramètre de génération) sans changer l'ID du
// profil. L'ID identifie l'instance ; le hash identifie le contenu.
//
// Champs inclus : Model, SystemPrompt, Temperature, NumCtxK, TopP,
// RepeatPenalty. C'est l'ensemble des entrées qui modifient
// sémantiquement la sortie d'Ollama pour une même entrée. Id et Name
// ne sont pas inclus — ils sont déjà émis comme champs distincts de
// l'event, et un rename ne change pas le contenu généré.
//
// Séparateur  (US, Unit Separator) entre champs : caractère
// non-imprimable réservé historiquement à ce rôle, ne peut pas
// apparaître dans un texte saisi normalement, évite toute collision
// avec un caractère présent dans le SystemPrompt.
//
// 16 chars hex = 8 octets de hash = 64 bits, largement assez pour
// distinguer deux templates dans un corpus utilisateur (collision
// 50% à ~5 milliards de templates distincts — hors de portée).
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
