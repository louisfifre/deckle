using System.Runtime.InteropServices;
using System.Text;
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
    private delegate int DescribeSelectorFn(
        [MarshalAs(UnmanagedType.LPWStr)] string text, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder output, int capacity);

    private readonly IntPtr _library;
    private readonly DescribeSelectorFn? _describe;

    public NativeSelectorTests()
    {
        string path = Path.Combine(AppContext.BaseDirectory, VersionedNativeCopy.DllName);
        if (!File.Exists(path)) return;

        _library = NativeLibrary.Load(path);
        _describe = Marshal.GetDelegateForFunctionPointer<DescribeSelectorFn>(
            NativeLibrary.GetExport(_library, "DeckleTaskbarDescribeSelector"));
    }

    public void Dispose()
    {
        if (_library != IntPtr.Zero) NativeLibrary.Free(_library);
    }

    // The canonical text on success, "error: ..." on failure.
    private string? Describe(string selector)
    {
        if (_describe is null) return null;
        var output = new StringBuilder(1024);
        int length = _describe(selector, output, output.Capacity);
        return length >= 0 ? output.ToString() : "error: " + output;
    }

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
        Assert.Equal(_describe is null ? null : expected, Describe(selector));
    }

    [Fact]
    public void APathIsReadFromTheAncestorToTheTargetWithOneSpaceAroundEachArrow()
    {
        string? text = Describe("Taskbar.TaskbarFrame>Grid#RootGrid  >   Taskbar.TaskbarBackground > Grid > Rectangle#BackgroundFill");

        Assert.Equal(
            _describe is null ? null
            : "Taskbar.TaskbarFrame > Windows.UI.Xaml.Controls.Grid#RootGrid > Taskbar.TaskbarBackground"
              + " > Windows.UI.Xaml.Controls.Grid > Windows.UI.Xaml.Shapes.Rectangle#BackgroundFill",
            text);
    }

    [Fact]
    public void AStarStandsForAnyChainOfAncestors()
    {
        Assert.Equal(
            _describe is null ? null : "SystemTray.SystemTrayFrame > * > SystemTray.NotifyIconView#NotifyItemIcon",
            Describe("SystemTray.SystemTrayFrame > * > SystemTray.NotifyIconView#NotifyItemIcon"));
    }

    [Fact]
    public void AnIndexAStateGroupAndFiltersAreKept()
    {
        Assert.Equal(
            _describe is null ? null
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

        if (_describe is null) return;
        Assert.StartsWith("error: " + reason, text);
    }

    [Fact]
    public void ANameWithoutATypeMatchesAnyTypeOfThatName()
    {
        Assert.Equal(_describe is null ? null : "#BackgroundStroke", Describe("#BackgroundStroke"));
    }
}
