using System.Text;
using Deckle.Input.Autocorrect;
using Deckle.Input;

namespace Deckle.Input.Autocorrect.Tests;

// Shared rig for the AutocorrectEngine orchestration tests. Only the engine's
// three OS-facing ports are substituted (keyboard host, surface prober, text
// injector); the KeyDecoder and TypedWordTracker run for real, so a test drives
// raw key events and exercises the genuine decode → track → decide → inject
// chain — what actually happens at runtime, minus the desktop.
//
// Set Prober.Surface / Settings / Policy / Injector.Result BEFORE Start(); the
// engine seeds the surface on Start and captures the policy reference then.
internal sealed class AutocorrectEngineHarness : IDisposable
{
    public readonly FakeKeyboardInputHost Host = new();
    public readonly FakeSurfaceProber Prober = new();
    public readonly RecordingInjector Injector = new();
    public readonly TypedWordTracker Tracker = new();
    public readonly ICorrectionPolicy Policy;
    public AutocorrectSettings Settings = new();
    public readonly AutocorrectEngine Engine;

    // Observable engine outputs, recorded in order.
    public readonly List<CorrectionDecision> Applied = new();
    public readonly List<(string Original, string Replacement)> Reverted = new();
    public readonly List<(string Original, string Replacement, bool IsRevert)> InjectionFailures = new();
    public readonly List<(FocusedSurface Surface, bool Enrolled)> SurfaceChanges = new();
    public readonly List<string> EnrollmentSuggestions = new();

    // Timestamp stamped on every raised key — only the rollup heartbeat cares.
    public double TimeMs { get; set; }

    // Counts character-key decode attempts (one per ToUnicode call) — lets a
    // test prove the password gate cut BEFORE decoding, not merely before tracking.
    public int DecodeCharCount { get; private set; }

    private readonly KeyDecoder _decoder;
    private readonly Dictionary<ushort, char> _layout = new();

    public AutocorrectEngineHarness(
        ICorrectionPolicy? policy = null,
        PersonalDictionary? dictionary = null,
        FrequencyLexicon? french = null,
        FrequencyLexicon? english = null)
    {
        Policy = policy ?? NeverCorrects;

        // Fake layout: a key produces whatever char Type() registered for its
        // synthetic VK. Control/navigation keys are classified by the decoder
        // structurally and never reach this.
        _decoder = new KeyDecoder((vk, _, _, buffer) =>
        {
            DecodeCharCount++; // a character key reached translation — i.e. decoding ran
            buffer.Clear();
            if (_layout.TryGetValue(vk, out char ch)) { buffer.Append(ch); return 1; }
            return 0;
        });

        Engine = new AutocorrectEngine(
            Host, _decoder, Tracker, Prober, Policy, Injector,
            () => Settings, dictionary, french, english);

        Engine.SurfaceChanged += (s, e) => SurfaceChanges.Add((s, e));
        Engine.CorrectionApplied += d => Applied.Add(d);
        Engine.CorrectionReverted += (o, r) => Reverted.Add((o, r));
        Engine.InjectionFailed += (o, r, rev) => InjectionFailures.Add((o, r, rev));
        Engine.EnrollmentSuggested += p => EnrollmentSuggestions.Add(p);
    }

    public bool Start() => Engine.Start();

    // ── Driving input ────────────────────────────────────────────────────

    /// <summary>Types each character as a physical key-down through the host.</summary>
    public void Type(string text)
    {
        foreach (char c in text)
        {
            ushort vk = VkFor(c);
            _layout[vk] = c;
            RaiseDown(vk);
        }
    }

    public void Backspace() => RaiseDown(0x08); // VK_BACK
    public void Enter() => RaiseDown(0x0D);     // VK_RETURN
    public void Tab() => RaiseDown(0x09);       // VK_TAB
    public void Pointer() => Host.RaisePointer();

    /// <summary>Re-probes the (already updated) surface, as a focus change would.</summary>
    public void RefocusOn(FocusedSurface surface)
    {
        Prober.Surface = surface;
        Host.RaiseFocusChanged();
    }

    /// <summary>Raises a key that carries the injected signature — the engine must ignore it.</summary>
    public void RaiseInjected(char c)
    {
        ushort vk = VkFor(c);
        _layout[vk] = c;
        Host.RaiseKey(new KeyboardKeyEvent(
            vk, ScanCode: 0, IsKeyDown: true, IsExtended: false, IsInjected: true, TimestampMs: TimeMs));
    }

    private void RaiseDown(ushort vk) => Host.RaiseKey(new KeyboardKeyEvent(
        vk, ScanCode: 0, IsKeyDown: true, IsExtended: false, IsInjected: false, TimestampMs: TimeMs));

    // Synthetic VK for a character. Letters/digits use their natural codes;
    // boundary punctuation uses its OEM code; anything else (accented letters)
    // uses the code point — all outside the control/navigation VK range the
    // decoder intercepts before character translation.
    private static ushort VkFor(char c) => c switch
    {
        ' ' => 0x20,
        >= 'a' and <= 'z' => (ushort)('A' + (c - 'a')),
        >= 'A' and <= 'Z' => (ushort)c,
        >= '0' and <= '9' => (ushort)c,
        '\'' => 0xDE, // VK_OEM_7
        '-' => 0xBD,  // VK_OEM_MINUS
        '.' => 0xBE,  // VK_OEM_PERIOD
        ',' => 0xBC,  // VK_OEM_COMMA
        _ => (ushort)c,
    };

    // ── Surface factories ────────────────────────────────────────────────

    public static FocusedSurface Editable(string process = "notepad") =>
        new(process, IsPassword: false, IsTextEditable: true);

    public static FocusedSurface PasswordBox(string process = "notepad") =>
        new(process, IsPassword: true, IsTextEditable: true);

    public static FocusedSurface ReadOnly(string process = "notepad") =>
        new(process, IsPassword: false, IsTextEditable: false);

    private static readonly ICorrectionPolicy NeverCorrects = new ScriptedPolicy((_, _) => null);

    public void Dispose() => Engine.Dispose();
}

// ── Port substitutes ─────────────────────────────────────────────────────

// Raises the host signals on demand; records the lifecycle calls.
internal sealed class FakeKeyboardInputHost : IKeyboardInputHost
{
    public event Action<KeyboardKeyEvent>? KeyReceived;
    public event Action? PointerInteraction;
    public event Action<MouseWheelEvent>? WheelObserved;
    public event Action? FocusChanged;

    public bool StartResult = true;
    public int StartCount;
    public int StopCount;

    public bool Start() { StartCount++; return StartResult; }
    public void Stop() => StopCount++;

    public void RaiseKey(KeyboardKeyEvent e) => KeyReceived?.Invoke(e);
    public void RaisePointer() => PointerInteraction?.Invoke();
    public void RaiseWheel(MouseWheelEvent e) => WheelObserved?.Invoke(e);
    public void RaiseFocusChanged() => FocusChanged?.Invoke();
}

// Returns a surface the test chose; counts the probes.
internal sealed class FakeSurfaceProber : ISurfaceProber
{
    public FocusedSurface Surface = FocusedSurface.Unknown;
    public int ProbeCount;

    public FocusedSurface Probe() { ProbeCount++; return Surface; }
}

// Records each requested edit and returns a controllable verdict.
internal sealed class RecordingInjector : ITextInjector
{
    public bool Result = true;
    public readonly List<(string Current, string Target)> Calls = new();

    public bool Replace(string current, string target)
    {
        Calls.Add((current, target));
        return Result;
    }
}

// A policy scripted by the test. The convenience factory corrects one exact
// form, case-sensitive, leaving everything else untouched.
internal sealed class ScriptedPolicy : ICorrectionPolicy
{
    private readonly Func<string, string?, CorrectionDecision?> _evaluate;
    public readonly List<(string Word, string? Previous)> Calls = new();

    public ScriptedPolicy(Func<string, string?, CorrectionDecision?> evaluate) => _evaluate = evaluate;

    public static ScriptedPolicy Maps(
        string from, string to, CorrectionReason reason = CorrectionReason.LexicalGate) =>
        new((word, _) => string.Equals(word, from, StringComparison.Ordinal)
            ? new CorrectionDecision(word, to, reason)
            : null);

    public CorrectionDecision? Evaluate(string word, IReadOnlyList<string> leftContext)
    {
        string? previousWord = leftContext.Count > 0 ? leftContext[^1] : null;
        Calls.Add((word, previousWord));
        return _evaluate(word, previousWord);
    }
}
