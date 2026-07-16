namespace Deckle.Audio;

internal enum CaptureAnomalyTransition
{
    None,
    Opened,
    Recovered,
}

// One bounded anomaly episode for one capture run. Repetition contributes to
// the final technical summary but never re-opens the human timeline after the
// first warning/recovery pair.
internal struct CaptureAnomalyEpisode
{
    private bool _opened;
    private bool _open;

    public int Occurrences { get; private set; }
    public bool Recovered { get; private set; }

    public CaptureAnomalyTransition ObserveFailure()
    {
        Occurrences++;
        if (_opened) return CaptureAnomalyTransition.None;

        _opened = true;
        _open = true;
        return CaptureAnomalyTransition.Opened;
    }

    public CaptureAnomalyTransition ObserveSuccess()
    {
        if (!_open) return CaptureAnomalyTransition.None;

        _open = false;
        Recovered = true;
        return CaptureAnomalyTransition.Recovered;
    }
}
