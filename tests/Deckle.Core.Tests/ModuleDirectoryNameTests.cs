using Xunit;

namespace Deckle.Core.Tests;

// A module id names one folder under modules\. Path.Combine returns a rooted
// second argument as is and does not look at "..", so an id that was not a
// single plain segment would point outside the modules folder.
[Trait("Category", "unit")]
public sealed class ModuleDirectoryNameTests
{
    [Theory]
    [InlineData("anytype")]
    [InlineData("precision-scroll")]
    [InlineData("whisp")]
    public void A_single_plain_segment_is_a_module_id(string id)
        => Assert.True(ModuleDirectoryName.IsSingleSegment(id));

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"\\server\share")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(@"..\other")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("a:b")]
    [InlineData("")]
    [InlineData("  ")]
    public void An_id_that_could_leave_the_modules_folder_is_refused(string id)
        => Assert.False(ModuleDirectoryName.IsSingleSegment(id));

    [Fact]
    public void A_null_id_is_refused()
        => Assert.False(ModuleDirectoryName.IsSingleSegment(null));
}
