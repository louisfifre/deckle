using Deckle.Input;

namespace Deckle.App;

// The process's single keyboard-and-mouse Raw Input host, created once here
// and shared across input consumers. The mouse is a one-window-per-process
// Raw Input resource — only one window may receive it (the last registered
// wins) — so sharing is a correctness requirement, not a convenience: two
// hosts would steal the stream from each other. Consumers (the autocorrect
// engine, the wheel recorder) reference-count it through Start/Stop; the
// native window and registration come up on the first and unwind on the
// last. Created before the consumers that depend on it.
public partial class App
{
    private KeyboardInputHost? _keyboardMouseHost;

    private void InitializeInputHost() => _keyboardMouseHost = new KeyboardInputHost();
}
