namespace Deckle.Core;

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
        // After Forget took the current place, the place behind may be the
        // one gone to: it becomes current instead of being there twice.
        else if (_back.TryPeek(out T? behind) && behind.Equals(place)) _back.Pop();
        _forward.Clear();
        Current = place;
        return true;
    }

    // Drops every place that matches, behind, ahead and current, so Back and
    // Forward never lead into a place that is no longer there (an added
    // folder removed from the app). Two places left side by side that are
    // equal become one, so Back never "goes" to the place already shown.
    // When the current place goes, Current is null and the caller navigates
    // somewhere: what was behind stays behind, what was ahead goes, as on any
    // new navigation. Returns true when the current place went.
    public bool Forget(Func<T, bool> match)
    {
        bool currentGone = Current is not null && match(Current);

        // Oldest first: the back stack enumerates newest first.
        List<T> behind = Collapse(_back.Reverse().Where(p => !match(p)));
        List<T> ahead = Collapse(_forward.Where(p => !match(p)));

        _back.Clear();
        _forward.Clear();
        if (currentGone)
        {
            Current = null;
            foreach (T place in behind) _back.Push(place);
            return true;
        }

        for (int i = 0; i < behind.Count; i++)
        {
            bool nextToCurrent = i == behind.Count - 1;
            if (nextToCurrent && behind[i].Equals(Current)) continue;
            _back.Push(behind[i]);
        }
        // The forward stack pops the nearest first: push the farthest first.
        for (int i = ahead.Count - 1; i >= 0; i--)
        {
            bool nextToCurrent = i == 0;
            if (nextToCurrent && ahead[i].Equals(Current)) continue;
            _forward.Push(ahead[i]);
        }
        return false;
    }

    private static List<T> Collapse(IEnumerable<T> places)
    {
        var kept = new List<T>();
        foreach (T place in places)
            if (kept.Count == 0 || !kept[^1].Equals(place)) kept.Add(place);
        return kept;
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
