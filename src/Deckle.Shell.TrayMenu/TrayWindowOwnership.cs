// TrayWindowOwnership — is a window owned, directly or through other owners, by a given window?

using System;

namespace Deckle.Shell.TrayMenu;

// A WinUI flyout renders in a popup window that the carrier owns. When that
// popup takes activation (a Standard flyout moves keyboard focus into it), the
// carrier is deactivated toward a window of its own, which is not a dismiss.
// The owner lookup is injected so the rule is testable without window handles.
internal static class TrayWindowOwnership
{
    // Owner chains are short (a popup, its carrier, the tray host); the bound
    // only guards against a cyclic lookup.
    internal const int MaxDepth = 8;

    /// <summary>
    /// True when <paramref name="window"/> is owned by <paramref name="owner"/>,
    /// directly or through intermediate owners. Zero handles are never owned.
    /// </summary>
    internal static bool IsOwnedBy(IntPtr window, IntPtr owner, Func<IntPtr, IntPtr> getOwner)
    {
        if (window == IntPtr.Zero || owner == IntPtr.Zero) return false;

        IntPtr current = window;
        for (int depth = 0; depth < MaxDepth; depth++)
        {
            current = getOwner(current);
            if (current == IntPtr.Zero) return false;
            if (current == owner) return true;
        }
        return false;
    }
}
