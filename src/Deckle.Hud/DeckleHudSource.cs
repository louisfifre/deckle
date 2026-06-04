using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Hud;

// Provider EventSource du module Deckle.Hud.
//
// Issu de la résolution du conflit obs ↔ carto : obs avait centralisé les
// observations HUD dans DeckleAppSource (côté host Deckle), mais carto a
// extrait Deckle.Hud en module séparé et un module ne peut pas dépendre
// du host. La doctrine modulaire (un provider par composant cohérent)
// commande qu'un module qui émet possède son provider — c'est ce que
// fait ce fichier, en suivant le pattern des autres providers Deckle.*.
//
// Initialement le provider portait uniquement le timeout warning du
// HideSync rendezvous. La vague d'instrumentation observabilité transverse
// (mai 2026) l'étend avec quatre axes d'observation interne — transitions
// de state machine, fade-in, retract message, rollup proximity — pour rendre
// la mécanique HUD diagnostiquable depuis la
// LogWindow et les JSONL plutôt que via File.AppendAllText ad hoc. Voir
// la fiche `reference--eventsource-convention--1.2.md` §*HUD interne
// sous-instrumenté* (lacune 1.1) qui motive l'extension, et CLAUDE.md
// du module §*Instrumentation interne* pour la doctrine de câblage.
[EventSource(Name = "Deckle.Hud")]
public sealed class DeckleHudSource : DeckleEventSource
{
    public static readonly DeckleHudSource Log = new();

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtHudWarning          = 1;
    public const int EvtStateChanged        = 2;
    public const int EvtFadeInStarted       = 3;
    public const int EvtMessageRetracted    = 4;
    // 5 reserved: former HUD composition warm pass event, removed in 2026-06
    // when boot-time PrimeAndHide was deleted.
    public const int EvtProximityRollup     = 6;

    [Event(EvtHudWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void HudWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtHudWarning, message);
    }

    // ─── Axe 1 — Transitions de state machine 6 états ──────────────────
    //
    // Émis par HudWindow.SetState à chaque transition (Hidden, Charging,
    // Recording, Transcribing, Rewriting, Message). `reason` capture le
    // déclencheur sémantique côté appelant (hotkey, paste, message_hide,
    // etc.). `alpha` et `dpi` sont les paramètres techniques
    // du window manager au moment de la transition — un mauvais alpha ou
    // un dpi inattendu signalent souvent un bug de fade-in ou de DPI-
    // aware resizing.
    [Event(EvtStateChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "state changed | from={0} | to={1} | reason={2} | alpha={3} | dpi={4}")]
    public void StateChanged(string from, string to, string reason, byte alpha, int dpi)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtStateChanged, from, to, reason, alpha, dpi);
    }

    // ─── Axe 2 — Fade-in 150 ms cubic ease-out ─────────────────────────
    //
    // Émis au début de chaque fade-in. `scope` distingue les surfaces qui
    // ont leur propre animator alpha — "hud" pour HudWindow (raw input
    // proximity), "overlay" pour HudOverlayWindow (60 Hz polling). Une
    // future surface "message" séparée (retract hybrid bleed décrit dans
    // CLAUDE.md mais non implémenté) viendrait s'ajouter ici.
    [Event(EvtFadeInStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "fade in start | scope={0} | duration_ms={1} | from={2} | to={3}")]
    public void FadeInStarted(string scope, int duration_ms, byte from_alpha, byte to_alpha)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtFadeInStarted, scope, duration_ms, from_alpha, to_alpha);
    }

    // ─── Axe 3 — Message retract 400×160 → 272×78 ──────────────────────
    //
    // Émis au début du retract (hybrid bleed → carte standalone). Pas de
    // site d'appel actif dans le code courant — la mécanique de retract
    // est décrite dans CLAUDE.md comme architecture cible mais HudMessage
    // est aujourd'hui fixe 272×78. L'event est déclaré pour figer la
    // signature ; il s'activera quand la mécanique de retract sera câblée.
    [Event(EvtMessageRetracted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "message retract | from={0}x{1} | to={2}x{3} | duration_ms={4}")]
    public void MessageRetracted(int from_w, int from_h, int to_w, int to_h, int duration_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtMessageRetracted, from_w, from_h, to_w, to_h, duration_ms);
    }

    // ─── Axe 4 — Proximity smoothstep (rollup per-session) ─────────────
    //
    // Pattern rollup canonique (cf. classe d'observables n°3 "Boucle temps
    // réel haute fréquence" de la fiche `reference--eventsource-
    // convention--1.2.md` §*Classes d'observables canoniques*) : la
    // proximité s'évalue à ~125 Hz sur WM_INPUT, fréquence trop chaude
    // pour la LogWindow selon la doctrine "heartbeats < 1 s ne sont pas
    // loggués". HudWindow accumule pendant toute la fenêtre de visibilité
    // (shown → hidden) et émet un récapitulatif unique au passage hidden,
    // sous deux conditions cumulatives : au moins un sample collecté ET
    // min_alpha != max_alpha (sinon la souris n'est pas rentrée dans le
    // rayon proximity, smoothstep est resté plat, aucune matière diag).
    // Une variante périodique 1 s a précédé ce design — elle inondait la
    // LogWindow d'events sans valeur sur les sessions où rien ne bougeait.
    // Le gate strict évite toute allocation quand aucun listener n'écoute,
    // y compris côté collecte (cf. _proximityRollupEnabled dans
    // HudWindow). `duration_ms` est la durée réelle de la session de
    // visibilité, pas une période fixe.
    [Event(EvtProximityRollup,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "proximity rollup | duration_ms={0} | samples={1} | min_alpha={2} | max_alpha={3} | p50_cursor_dist_dip={4} | p95_cursor_dist_dip={5}")]
    public void ProximityRollup(int duration_ms, int samples, byte min_alpha, byte max_alpha, int p50_cursor_dist_dip, int p95_cursor_dist_dip)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtProximityRollup, duration_ms, samples, min_alpha, max_alpha, p50_cursor_dist_dip, p95_cursor_dist_dip);
    }
}
