namespace Deckle.Hud;

// Aggrégateur de la session de proximity du HUD. Folde min/max alpha
// incrémentalement (pas de buffer pour alpha) et tient un ring buffer
// borné de distances pour calcul de percentiles flush en fin de
// session.
//
// Pourquoi un ring plutôt qu'une fenêtre tronquée. WM_INPUT ~125 Hz
// pendant une session HUD typique de ~10 s = ~1250 samples ; la
// capacité 2048 couvre ~16 s en intégralité, soit la quasi-totalité
// des sessions de visibilité réelles. Pour les rares sessions plus
// longues, les percentiles reflètent les ~16 dernières secondes —
// fenêtre toujours diagnostiquement représentative, contrairement à
// un buffer tronqué qui figerait les samples du début.
internal sealed class ProximityRollupAggregator
{
    internal const int Capacity = 2048;

    private readonly int[] _distances = new int[Capacity];
    private int _writeIdx;
    private int _totalSamples;
    private byte _minAlpha;
    private byte _maxAlpha;

    public ProximityRollupAggregator() { Reset(); }

    public int TotalSamples => _totalSamples;
    public byte MinAlpha => _minAlpha;
    public byte MaxAlpha => _maxAlpha;

    public void Reset()
    {
        _writeIdx = 0;
        _totalSamples = 0;
        _minAlpha = 255;
        _maxAlpha = 0;
    }

    public void Add(int distanceDip, byte alpha)
    {
        _distances[_writeIdx] = distanceDip;
        _writeIdx = (_writeIdx + 1) % Capacity;
        _totalSamples++;
        if (alpha < _minAlpha) _minAlpha = alpha;
        if (alpha > _maxAlpha) _maxAlpha = alpha;
    }

    // Retourne (p50, p95) en DIP sur les min(TotalSamples, Capacity)
    // derniers samples collectés. Appelé une seule fois en fin de
    // session — l'allocation de la copie + tri est acceptable à cette
    // cadence (une fois par show de HUD, pas par WM_INPUT). Lève
    // InvalidOperationException si aucun sample n'a été ajouté — le
    // caller doit gate sur TotalSamples > 0 avant.
    public (int P50, int P95) ComputePercentiles()
    {
        if (_totalSamples == 0)
            throw new InvalidOperationException("ComputePercentiles requires at least one sample.");

        int count = Math.Min(_totalSamples, Capacity);
        var snap = new int[count];
        Array.Copy(_distances, snap, count);
        Array.Sort(snap);
        int p50 = snap[count / 2];
        int p95Idx = (int)Math.Min(count - 1, Math.Floor(count * 0.95));
        return (p50, snap[p95Idx]);
    }
}
