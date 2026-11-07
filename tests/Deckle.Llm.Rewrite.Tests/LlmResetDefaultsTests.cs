using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// The Rewriting page's "Reset all" restores values and never flips the module
// switch: on or off is the person's choice, not a tuning value (2026-11-07).
[Trait("Category", "unit")]
public sealed class LlmResetDefaultsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResetKeepsTheModuleSwitchAsSet(bool enabled)
    {
        var current = new LlmSettings { Enabled = enabled };

        LlmSettings fresh = LlmResetDefaults.From(current);

        Assert.Equal(enabled, fresh.Enabled);
    }

    [Fact]
    public void ResetRestoresTheOtherValuesToTheirDefaults()
    {
        var current = new LlmSettings { Enabled = false };
        current.BatchRewriting.Enabled = true;
        current.Providers.Clear();
        current.Profiles.Clear();

        LlmSettings fresh = LlmResetDefaults.From(current);

        var defaults = new LlmSettings();
        Assert.Equal(defaults.BatchRewriting.Enabled, fresh.BatchRewriting.Enabled);
        Assert.Equal(defaults.Providers.Count, fresh.Providers.Count);
        Assert.Equal(defaults.Profiles.Count, fresh.Profiles.Count);
    }

    [Fact]
    public void ResetDoesNotWriteIntoTheCurrentSettings()
    {
        var current = new LlmSettings { Enabled = false };
        current.Providers.Clear();

        LlmResetDefaults.From(current);

        Assert.Empty(current.Providers);
        Assert.False(current.Enabled);
    }
}
