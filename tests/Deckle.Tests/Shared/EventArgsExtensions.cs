using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Tests.Shared;

// ── EventArgsExtensions ─────────────────────────────────────────────────────
//
// EventSource ajoute des bits système hauts (bits 44-47, sessions ETW
// réservées) à `EventWrittenEventArgs.Keywords` en plus des keywords
// déclarés sur l'event. Une assertion stricte
// `Assert.Equal((EventKeywords)Keywords.Network, ev.Keywords)` échoue
// parce que le membre droit porte les bits Deckle PLUS les bits système.
//
// `HasKeyword` masque sur le bit Deckle pour répondre à la vraie question
// posée par les tests : « cet event est-il sur ce keyword Deckle » sans
// se soucier des bits ETW que le framework ajoute.
internal static class EventArgsExtensions
{
    public static bool HasKeyword(this EventWrittenEventArgs ev, Keywords k)
        => ((long)ev.Keywords & (long)k) == (long)k;
}
