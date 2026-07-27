using System.Text;

namespace Deckle.Autocorrect.Tests;

// End-of-field text surface for engine integration tests. Physical character
// keys update it before Raw Input reaches the engine, matching the correction
// contract that the committed boundary is already visible when injection runs.
// An injected replacement succeeds only when its declared current tail really is
// on screen, so a wrong backspace/tail reconstruction becomes a failed behavior.
internal sealed class SimulatedTextSurface
{
    private readonly object _lock = new();
    private readonly StringBuilder _text = new();

    public string Text
    {
        get { lock (_lock) return _text.ToString(); }
    }

    public void Type(char value)
    {
        lock (_lock) _text.Append(value);
    }

    public void Backspace()
    {
        lock (_lock)
            if (_text.Length > 0)
                _text.Length--;
    }

    public bool ReplaceSuffix(string current, string target)
    {
        lock (_lock)
        {
            if (_text.Length < current.Length)
                return false;

            int start = _text.Length - current.Length;
            for (int i = 0; i < current.Length; i++)
                if (_text[start + i] != current[i])
                    return false;

            _text.Length = start;
            _text.Append(target);
            return true;
        }
    }

    // Mirrors production SendInput: the injector cannot inspect the field, it
    // only executes the minimal backspace/text plan it computed. Tests that
    // exercise stale surface models use this path so corruption stays visible.
    public void ApplyBlindReplacement(string current, string target)
    {
        InjectionPlan plan = InjectionPlan.Compute(current, target);
        lock (_lock)
        {
            _text.Length = Math.Max(0, _text.Length - plan.Backspaces);
            _text.Append(plan.Text);
        }
    }
}
