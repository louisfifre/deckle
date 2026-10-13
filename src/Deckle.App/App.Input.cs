using Deckle.Input;

namespace Deckle.App;

// Shared by the input consumers, which reference-count it through Start/Stop:
// Raw Input takes one target window per usage per process, the last
// registration winning, so two hosts would steal the stream from each other.
// Created before the consumers that depend on it.
public partial class App
{
    private KeyboardInputHost? _keyboardMouseHost;

    private void InitializeInputHost() => _keyboardMouseHost = new KeyboardInputHost();
}
