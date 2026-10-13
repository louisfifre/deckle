using System;
using System.Collections.Generic;
using Deckle.Shell.TrayMenu;
using Xunit;

namespace Deckle.Shell.TrayMenu.Tests;

// Behaviour of TrayWindowOwnership: the carrier hides its flyout only when
// activation leaves for a window it does not own. Handles are arbitrary
// numbers; the owner lookup is a dictionary standing in for GetWindow(GW_OWNER).
public class TrayWindowOwnershipTests
{
    private static readonly IntPtr TrayHost     = new(0x10);
    private static readonly IntPtr Carrier      = new(0x20);
    private static readonly IntPtr Popup        = new(0x30);
    private static readonly IntPtr OtherCarrier = new(0x40);
    private static readonly IntPtr OtherPopup   = new(0x50);
    private static readonly IntPtr Elsewhere    = new(0x60);

    private static readonly Dictionary<IntPtr, IntPtr> Owners = new()
    {
        [Carrier]      = TrayHost,
        [Popup]        = Carrier,
        [OtherCarrier] = TrayHost,
        [OtherPopup]   = OtherCarrier,
    };

    private static IntPtr GetOwner(IntPtr window)
        => Owners.TryGetValue(window, out var owner) ? owner : IntPtr.Zero;

    // A Standard flyout moves focus into its popup, which the carrier owns;
    // hiding on that deactivation would close the panel inside its own Show().
    [Fact]
    [Trait("Category", "regression")]
    public void ADeactivationTowardTheCarriersOwnPopupIsNotADismiss()
    {
        Assert.True(TrayWindowOwnership.IsOwnedBy(Popup, Carrier, GetOwner));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void AWindowOwnedByAnotherTrayHostDismisses()
    {
        Assert.False(TrayWindowOwnership.IsOwnedBy(OtherPopup, Carrier, GetOwner));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void AnUnownedWindowOrNoWindowDismisses()
    {
        Assert.False(TrayWindowOwnership.IsOwnedBy(Elsewhere, Carrier, GetOwner));
        Assert.False(TrayWindowOwnership.IsOwnedBy(IntPtr.Zero, Carrier, GetOwner));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ACyclicOwnerChainEndsWithoutAMatch()
    {
        IntPtr a = new(0x70);
        IntPtr b = new(0x80);

        Assert.False(TrayWindowOwnership.IsOwnedBy(a, Carrier, window => window == a ? b : a));
    }
}
