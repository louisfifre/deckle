namespace Deckle.Input;

// Synchronous policy point for the low-level mouse hook. Implementations must
// stay bounded and allocation-free: returning true prevents Windows from
// delivering the physical wheel message to its target.
public interface IWheelInterceptor
{
    bool Intercept(in MouseWheelEvent wheelEvent);
}
