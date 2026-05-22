using Deckle.Diagnostics;
using Deckle.Logging;

namespace Deckle.Diagnostics;

// Bridge sink that converts a FeedbackEntry emitted on the EventSource
// pipeline into a legacy UserFeedback, then routes to the existing HUD
// surfaces (HudWindow main slot for Replacement, HudOverlayManager
// stack for Overlay). Used only during the migration window — once
// the HUD itself consumes Deckle.Diagnostics directly, this bridge
// disappears.
//
// Severity / role mapping mirrors the legacy enum ordinals so a module
// that migrates can hardcode the ints at the call site without
// pulling Deckle.Logging:
//   severity 0 = Info, 1 = Warning, 2 = Error
//   role     0 = Replacement, 1 = Overlay
internal sealed class LegacyHudFeedbackSink : IHudFeedbackSink
{
    private readonly System.Action<UserFeedback> _onReplacement;
    private readonly System.Action<UserFeedback> _onOverlay;

    public LegacyHudFeedbackSink(
        System.Action<UserFeedback> onReplacement,
        System.Action<UserFeedback> onOverlay)
    {
        _onReplacement = onReplacement;
        _onOverlay = onOverlay;
    }

    public void Write(FeedbackEntry entry)
    {
        var feedback = new UserFeedback(
            Title:    entry.Title,
            Body:     entry.Body,
            Severity: MapSeverity(entry.Severity),
            Role:     MapRole(entry.Role));

        switch (feedback.Role)
        {
            case UserFeedbackRole.Replacement: _onReplacement(feedback); break;
            case UserFeedbackRole.Overlay:     _onOverlay(feedback); break;
        }
    }

    private static UserFeedbackSeverity MapSeverity(int s) => s switch
    {
        0 => UserFeedbackSeverity.Info,
        1 => UserFeedbackSeverity.Warning,
        2 => UserFeedbackSeverity.Error,
        _ => UserFeedbackSeverity.Info,
    };

    private static UserFeedbackRole MapRole(int r) => r switch
    {
        0 => UserFeedbackRole.Replacement,
        1 => UserFeedbackRole.Overlay,
        _ => UserFeedbackRole.Replacement,
    };
}
