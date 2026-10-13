namespace Deckle.Autocorrect;

// The gate's port to the desktop: it lets the engine be tested against a
// chosen surface (password, editable, process) without a live UIA tree.
public interface ISurfaceProber
{
    FocusedSurface Probe();
}
