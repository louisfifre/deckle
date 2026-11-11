namespace Deckle.Hud;

// Decides whether a plain Hide() request may dismiss what the main HUD shows.
//
// A pipeline that fails raises its user message and then, a moment later in the
// same call chain, its "finished with nothing" signal, and the host answers
// that signal with Hide(). Both land on the UI queue in that order, so without
// this rule the warning or error is replaced by Hidden within a frame and the
// person never reads it, whatever its own display time is. The message keeps its
// own timer instead; a new recording or another message still replaces it,
// because they go through SetState and not through Hide().
//
// Only warnings and errors are protected: they carry text the person has to
// read. A success or an information clears as before.
internal static class HudHidePolicy
{
    public static bool ShouldHonorHide(HudState current, MessageKind? currentMessageKind) =>
        !(current == HudState.Message
          && currentMessageKind is MessageKind.Warning or MessageKind.Critical);
}
