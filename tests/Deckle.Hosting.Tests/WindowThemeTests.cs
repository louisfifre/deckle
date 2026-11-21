using Deckle.Hosting;
using Microsoft.UI.Xaml;
using Xunit;

namespace Deckle.Hosting.Tests;

public class WindowThemeTests
{
    [Theory]
    [InlineData("Light", ElementTheme.Light)]
    [InlineData("Dark", ElementTheme.Dark)]
    [InlineData("System", ElementTheme.Default)]
    [InlineData("nonsense", ElementTheme.Default)]
    [InlineData(null, ElementTheme.Default)]
    [Trait("Category", "unit")]
    public void FromName_FollowsTheSystemUnlessTheNameSaysLightOrDark(string? name, ElementTheme expected)
    {
        Assert.Equal(expected, WindowTheme.FromName(name));
    }
}
