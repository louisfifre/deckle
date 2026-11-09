using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The rule selector parser lives in the native DLL (xamldiag/rules/selector.cpp)
// because explorer runs it. These tests load the DLL the module build produced
// and call its DeckleTaskbarDescribeSelector export, so they run the real parser
// and no second copy exists to drift from it. A machine that built the C#
// without the C++ tools has no DLL; the tests then pass without checking
// anything, and the build already warned (DECKLE001).
[Trait("Category", "unit")]
public sealed class NativeSelectorTests : IDisposable
{
    private readonly NativeSelectorLibrary _library = new();

    public void Dispose() => _library.Dispose();

    private string? Describe(string selector) => _library.Describe(selector);

    private bool NoDll => !_library.IsAvailable;

    [Theory]
    [InlineData("Rectangle#BackgroundStroke", "Windows.UI.Xaml.Shapes.Rectangle#BackgroundStroke")]
    [InlineData("Grid", "Windows.UI.Xaml.Controls.Grid")]
    [InlineData("StackPanel", "Windows.UI.Xaml.Controls.StackPanel")]
    [InlineData("ToggleButton", "Windows.UI.Xaml.Controls.Primitives.ToggleButton")]
    [InlineData("FrameworkElement", "Windows.UI.Xaml.FrameworkElement")]
    [InlineData("Taskbar.TaskbarFrame", "Taskbar.TaskbarFrame")]
    [InlineData("taskbar:TaskbarFrame", "Taskbar.TaskbarFrame")]
    [InlineData("systemtray:NotifyIconView#NotifyItemIcon", "SystemTray.NotifyIconView#NotifyItemIcon")]
    public void AShortTypeNameStandsForTheXamlClassOfThatName(string selector, string expected)
    {
        Assert.Equal(NoDll ? null : expected, Describe(selector));
    }

    [Fact]
    public void APathIsReadFromTheAncestorToTheTargetWithOneSpaceAroundEachArrow()
    {
        string? text = Describe("Taskbar.TaskbarFrame>Grid#RootGrid  >   Taskbar.TaskbarBackground > Grid > Rectangle#BackgroundFill");

        Assert.Equal(
            NoDll ? null
            : "Taskbar.TaskbarFrame > Windows.UI.Xaml.Controls.Grid#RootGrid > Taskbar.TaskbarBackground"
              + " > Windows.UI.Xaml.Controls.Grid > Windows.UI.Xaml.Shapes.Rectangle#BackgroundFill",
            text);
    }

    [Fact]
    public void AStarStandsForAnyChainOfAncestors()
    {
        Assert.Equal(
            NoDll ? null : "SystemTray.SystemTrayFrame > * > SystemTray.NotifyIconView#NotifyItemIcon",
            Describe("SystemTray.SystemTrayFrame > * > SystemTray.NotifyIconView#NotifyItemIcon"));
    }

    [Fact]
    public void AnIndexAStateGroupAndFiltersAreKept()
    {
        Assert.Equal(
            NoDll ? null
            : "Taskbar.TaskListButton[2] > Windows.UI.Xaml.Controls.Grid#IconPanel@CommonStates"
              + " > Taskbar.ExperienceToggleButton[AutomationProperties.AutomationId=StartButton]",
            Describe("Taskbar.TaskListButton[2] > Grid#IconPanel@CommonStates > Taskbar.ExperienceToggleButton[AutomationProperties.AutomationId=StartButton]"));
    }

    [Theory]
    [InlineData("", "the selector is empty")]
    [InlineData("   ", "the selector is empty")]
    [InlineData("Grid >", "the selector ends with '>'")]
    [InlineData("Grid > *", "a selector cannot end with '*'")]
    [InlineData("Grid Border", "expected '>' between two elements")]
    [InlineData("Grid[0]", "a child index is a number from 1")]
    [InlineData("Grid[Padding]", "'[...]' holds a child index or Property=Value")]
    [InlineData("Grid[2", "'[' has no closing ']'")]
    [InlineData("Grid#", "'#' needs an element name")]
    [InlineData("Grid@", "'@' needs a visual state group name")]
    [InlineData("#", "'#' needs an element name")]
    [InlineData("> Grid", "an element needs a type, a name, a state group or a filter")]
    public void ABrokenSelectorSaysWhatIsWrong(string selector, string reason)
    {
        string? text = Describe(selector);

        if (NoDll) return;
        Assert.StartsWith("error: " + reason, text);
    }

    [Fact]
    public void ANameWithoutATypeMatchesAnyTypeOfThatName()
    {
        Assert.Equal(NoDll ? null : "#BackgroundStroke", Describe("#BackgroundStroke"));
    }
}
