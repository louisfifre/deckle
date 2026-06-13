namespace Deckle.Input.Autocorrect.Surfaces;

// Resolves the focused control into a FocusedSurface. SurfaceProber is the
// production implementation, doing one targeted UIA read per focus change. The
// interface is the gate's port to the desktop: it lets the engine be tested
// against a chosen surface (password, editable, process) without a live UIA tree.
public interface ISurfaceProber
{
    FocusedSurface Probe();
}
