namespace Deckle.Vision;

// Watchdog de stall de la boucle de capture, extrait en type pur pour
// être testable sans DXGI ni horloge réelle. ScreenCaptureService
// l'alimente à chaque itération avec « est-ce que cette itération a
// acquis une frame » et le tick courant ; le détecteur décide quand la
// capture est passée « vivante mais muette » — la boucle tourne mais
// AcquireNextFrame ne livre plus de S_OK depuis trop longtemps — et quand
// elle reprend.
//
// Pourquoi un capteur dédié. La boucle absorbe en silence les transients
// (WAIT_TIMEOUT sur écran statique, ACCESS_LOST → recreate) ; aucun event
// existant ne modélise l'état « la boucle vit mais ne sort plus rien ».
// C'est l'angle mort qui laissait l'ambient geler sans laisser de trace.
// Le détecteur fournit le signal manquant. Il ne couvre PAS la boucle
// fautée — si la Task meurt, Observe n'est plus appelé ; ce cas est traité
// par le try/catch autour de la boucle.
//
// Limite assumée : un écran réellement statique n'acquiert rien non plus
// (WAIT_TIMEOUT légitime), donc le détecteur lèvera un stall après le
// seuil sur un écran figé. Faux positif bénin — c'est un Warning, et dans
// le cas d'usage ambient l'écran est dynamique, donc 0 acquire pendant
// plusieurs secondes = quasi toujours un vrai gel.
//
// Unité-agnostique : tout est en ticks monotones, le seuil est dans la
// même unité. La conversion Stopwatch.Frequency → ticks vit côté appelant
// pour garder ce type pur et trivialement testable.
public sealed class CaptureStallDetector
{
    private readonly long _thresholdTicks;
    private long _lastAcquireTicks;
    private bool _stalled;

    public CaptureStallDetector(long thresholdTicks, long startTicks)
    {
        _thresholdTicks = thresholdTicks;
        _lastAcquireTicks = startTicks;
    }

    /// <summary>True tant que le détecteur considère la capture en stall.</summary>
    public bool IsStalled => _stalled;

    /// <summary>
    /// Alimenté à chaque itération de la boucle. <paramref name="acquired"/>
    /// vaut true quand l'itération a obtenu une frame (AcquireNextFrame
    /// S_OK), false sinon (timeout, erreur, recreate). Renvoie la transition
    /// à émettre : <see cref="CaptureStallTransition.Stalled"/> une seule
    /// fois à l'entrée du stall, <see cref="CaptureStallTransition.Recovered"/>
    /// au premier acquire qui suit un stall, <see cref="CaptureStallTransition.None"/>
    /// le reste du temps (pas de réémission tant que l'état ne change pas).
    /// </summary>
    public CaptureStallTransition Observe(bool acquired, long nowTicks)
    {
        if (acquired)
        {
            _lastAcquireTicks = nowTicks;
            if (_stalled)
            {
                _stalled = false;
                return CaptureStallTransition.Recovered;
            }
            return CaptureStallTransition.None;
        }

        if (!_stalled && (nowTicks - _lastAcquireTicks) >= _thresholdTicks)
        {
            _stalled = true;
            return CaptureStallTransition.Stalled;
        }

        return CaptureStallTransition.None;
    }
}

// Transition signalée par CaptureStallDetector.Observe. Pilote l'émission
// côté ScreenCaptureService : Stalled → Warning « Capture stalled »,
// Recovered → Info « Capture resumed », None → rien. Enum co-localisée
// avec son unique producteur — couplage assumé, pas de fichier séparé
// pour un type aussi étroitement lié.
public enum CaptureStallTransition
{
    None,
    Stalled,
    Recovered,
}
