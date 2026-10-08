using Deckle.Hud;

namespace Deckle.App;

// Live transcript panel wiring. The panel follows the HUD rather than owning a
// second state machine: a take opens when the engine starts recording, each
// transcribed utterance is appended, the take closes when the transcription
// finishes, and the panel leaves when the HUD leaves. Engine events arrive on
// background threads; the panel marshals every call to its UI thread.
public partial class App
{
    private TranscriptPanelWindow? _transcriptPanel;

    private void InitializeTranscriptPanel()
    {
        var hud = _hudWindow!;
        var panel = new TranscriptPanelWindow(_cursorSignal!);
        _transcriptPanel = panel;

        hud.MainHudVisibilityChanged += (_, visible) =>
        {
            if (!visible) panel.Hide();
        };

        // No engine when speech is not provisioned: the panel stays hidden.
        var engine = _engine;
        if (engine is null) return;

        engine.StatusChanged += _ =>
        {
            if (engine.IsRecording) panel.BeginTake();
        };
        engine.UtteranceTranscribed += u => panel.Append(u.Text, u.EndedOnSilence);
        engine.TranscriptionFinished += _ => panel.EndTake();
    }
}
