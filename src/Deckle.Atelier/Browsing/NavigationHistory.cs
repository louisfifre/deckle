namespace Deckle.Atelier;

// Back and Forward, as File Explorer and a browser have them: going somewhere
// new pushes the current place on the back stack and forgets the forward one;
// going to the place already shown changes nothing, so Refresh and a click on
// the last segment leave the history alone.
public sealed class NavigationHistory<T> where T : class
{
    private readonly Stack<T> _back = new();
    private readonly Stack<T> _forward = new();

    public T? Current { get; private set; }

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    // True when Current changed.
    public bool Navigate(T place)
    {
        if (Current is not null && Current.Equals(place)) return false;
        if (Current is not null) _back.Push(Current);
        _forward.Clear();
        Current = place;
        return true;
    }

    public T? GoBack()
    {
        if (!CanGoBack) return null;
        _forward.Push(Current!);
        Current = _back.Pop();
        return Current;
    }

    public T? GoForward()
    {
        if (!CanGoForward) return null;
        _back.Push(Current!);
        Current = _forward.Pop();
        return Current;
    }
}
