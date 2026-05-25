using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Sub-provider transverse — cycle de vie des ressources natives non
// managées (textures D3D11 côté Vision, visuals Composition côté HUD,
// futurs handles Whisper natif). Capter acquire / release / leak en un
// schéma unique permet de corréler une fuite GPU ou un crash OOM avec
// la dernière acquisition tracée plutôt que de remonter à l'aveugle un
// stack natif. La primitive est strictement non-métier et consommée
// par plusieurs modules avec le même set de paramètres — promotion en
// sub-provider transverse au sens du critère à deux clauses de la
// fiche `reference--eventsource-convention--1.2.md` §*Sub-providers
// transverses*.
//
// Vocabulaire fermé `kind` :
//   "d3d11-texture"       — ID3D11Texture2D (capture frames, sampler)
//   "duplication-output"  — IDXGIOutputDuplication
//   "dxgi-resource"       — IDXGIResource générique
//   "composition-visual"  — Microsoft.UI.Composition.Visual et dérivés
//   "composition-surface" — ICompositionSurface, CompositionDrawingSurface
//   "composition-brush"   — CompositionBrush et dérivés
// Toute apparition d'un nouveau kind doit être ajoutée ici avant
// utilisation pour préserver la grep-abilité côté listener.
//
// Conventions de handle :
//   - COM / natif : IntPtr du pointeur d'interface, cast en long.
//   - Managé Composition : RuntimeHelpers.GetHashCode(obj), cast en
//     long ; identifiant stable pour la durée de vie d'un objet managé
//     donné, suffisant pour matcher un acquire et son release.
//
// Convention de owner :
//   Nom court du site logique qui pilote la ressource ("capture-loop",
//   "frame-sampler", "hud-message", "hud-glow", etc.). Permet de
//   différencier deux acquires du même kind sur des sites distincts
//   sans gonfler le schéma.
//
// Convention de size_bytes :
//   Approximation taille mémoire. Pour textures : w * h * bytes_per_pixel.
//   Pour visuals Composition : 0 (impossible à mesurer côté managé sans
//   introspection coûteuse). Pour duplication output : 0 (handle pur).
//
// L'event `ResourceLeakSuspect` est déclaré pour figer le contrat dès
// cette vague, mais le câblage actif (détection de release manqué via
// finalizer ou watchdog) viendra dans une passe ultérieure. Aucun
// site d'appel actif dans le code courant.
[EventSource(Name = "Deckle.Diagnostics.Resource")]
public sealed class DeckleResourceSource : DeckleEventSource
{
    public static readonly DeckleResourceSource Log = new();

    private DeckleResourceSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtResourceAcquired    = 1;
    public const int EvtResourceReleased    = 2;
    public const int EvtResourceLeakSuspect = 3;

    // Acquire — émis au moment de la prise d'un handle natif ou de la
    // création d'un objet Composition managé. Verbose parce qu'il porte
    // un identifiant opaque (handle hex) et que la cadence peut être
    // élevée (capture loop ~15 Hz).
    [Event(EvtResourceAcquired,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Resource,
           Message = "resource acquired | kind={0} | handle=0x{1:X} | size_bytes={2} | owner={3}")]
    public void ResourceAcquired(string kind, long handle, int size_bytes, string owner)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Resource)) return;
        WriteEvent(EvtResourceAcquired, kind, handle, size_bytes, owner);
    }

    // Release — émis au moment du Marshal.ReleaseComObject, Dispose, ou
    // équivalent. `age_ms` mesure le delta entre l'acquire et le release
    // via Stopwatch.GetTimestamp, capturé côté site d'appel. Verbose
    // mêmes raisons que l'acquire.
    [Event(EvtResourceReleased,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Resource,
           Message = "resource released | kind={0} | handle=0x{1:X} | age_ms={2} | owner={3}")]
    public void ResourceReleased(string kind, long handle, int age_ms, string owner)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Resource)) return;
        WriteEvent(EvtResourceReleased, kind, handle, age_ms, owner);
    }

    // Leak suspect — événement spécialisé du cas anormal (release manqué
    // détecté à finalization ou par watchdog). Warning parce que c'est
    // une anomalie qui mérite une remontée même quand le Verbose n'est
    // pas écouté. Aucun site actif aujourd'hui — déclaré pour figer la
    // signature avant que la détection ne soit câblée.
    [Event(EvtResourceLeakSuspect,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Resource,
           Message = "resource leak suspect | kind={0} | handle=0x{1:X} | age_ms={2} | owner={3} | symptom={4}")]
    public void ResourceLeakSuspect(string kind, long handle, int age_ms, string owner, string symptom)
    {
        if (!IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Resource)) return;
        WriteEvent(EvtResourceLeakSuspect, kind, handle, age_ms, owner, symptom);
    }
}
