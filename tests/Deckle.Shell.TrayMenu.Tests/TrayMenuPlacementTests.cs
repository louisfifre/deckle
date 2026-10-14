using Deckle.Shell.TrayMenu;
using Xunit;

namespace Deckle.Shell.TrayMenu.Tests;

// Behaviour of TrayMenuPlacement: a tray menu opens on the side of the icon
// that faces the work area, whatever edge the taskbar is docked to. Rects are
// a 3840 x 2160 monitor with a 88 px taskbar, in physical pixels.
public class TrayMenuPlacementTests
{
    private const int Width = 3840;
    private const int Height = 2160;
    private const int Bar = 88;
    private const int Icon = 48;

    [Fact]
    [Trait("Category", "unit")]
    public void ATaskbarAtTheBottomOpensTheMenuAbove()
    {
        var workArea = (0, 0, Width, Height - Bar);
        var icon = (Width - 400, Height - Bar + 20, Width - 400 + Icon, Height - Bar + 20 + Icon);

        Assert.Equal(TrayMenuSide.Above, TrayMenuPlacement.Choose(icon, workArea));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ATaskbarOnTheLeftOpensTheMenuToTheRight()
    {
        var workArea = (Bar, 0, Width, Height);
        var icon = (20, Height - 400, 20 + Icon, Height - 400 + Icon);

        Assert.Equal(TrayMenuSide.RightOf, TrayMenuPlacement.Choose(icon, workArea));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ATaskbarAtTheTopOpensTheMenuBelow()
    {
        var workArea = (0, Bar, Width, Height);
        var icon = (Width - 400, 20, Width - 400 + Icon, 20 + Icon);

        Assert.Equal(TrayMenuSide.Below, TrayMenuPlacement.Choose(icon, workArea));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ATaskbarOnTheRightOpensTheMenuToTheLeft()
    {
        var workArea = (0, 0, Width - Bar, Height);
        var icon = (Width - Bar + 20, Height - 400, Width - Bar + 20 + Icon, Height - 400 + Icon);

        Assert.Equal(TrayMenuSide.LeftOf, TrayMenuPlacement.Choose(icon, workArea));
    }

    // The overflow flyout and an auto-hidden taskbar put the icon inside the
    // work area; the menu then opens above it, as Windows does.
    [Fact]
    [Trait("Category", "unit")]
    public void AnIconInsideTheWorkAreaOpensTheMenuAbove()
    {
        var workArea = (0, 0, Width, Height - Bar);
        var icon = (Width - 600, Height - 300, Width - 600 + Icon, Height - 300 + Icon);

        Assert.Equal(TrayMenuSide.Above, TrayMenuPlacement.Choose(icon, workArea));
    }
}
