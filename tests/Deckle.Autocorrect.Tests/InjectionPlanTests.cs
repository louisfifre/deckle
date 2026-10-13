using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The minimal diff: how many Backspaces, which suffix to type. Only the
// observable contract (the Backspaces/Text pair) is asserted, never how the
// common prefix is computed. The surrogate-pair cases are load-bearing: an
// astral char cut into two half-surrogates would inject corrupt text.
[Trait("Category", "unit")]
public class InjectionPlanTests
{
    [Fact]
    public void IdenticalStringsAreNoOp()
    {
        var plan = InjectionPlan.Compute("bonjour ", "bonjour ");

        Assert.Equal(0, plan.Backspaces);
        Assert.Equal("", plan.Text);
        Assert.True(plan.IsNoOp);
    }

    [Fact]
    public void DiacriticSuffixChange_BackspacesTheTail_TypesTheAccentedSuffix()
    {
        var plan = InjectionPlan.Compute("francais ", "français ");

        Assert.Equal(5, plan.Backspaces);  // c-a-i-s-space
        Assert.Equal("çais ", plan.Text);
        Assert.False(plan.IsNoOp);
    }

    [Fact]
    public void NoCommonPrefix_FullReplace()
    {
        var plan = InjectionPlan.Compute("abc", "xyz");

        Assert.Equal(3, plan.Backspaces);
        Assert.Equal("xyz", plan.Text);
    }

    [Fact]
    public void PrefixOnlyGrowth_NoBackspaces()
    {
        var plan = InjectionPlan.Compute("auto", "automatique");

        Assert.Equal(0, plan.Backspaces);
        Assert.Equal("matique", plan.Text);
    }

    [Fact]
    public void SurrogatePairIsNeverSplit()
    {
        // U+1D11E is two UTF-16 units but one code point, so one Backspace.
        var plan = InjectionPlan.Compute("ab\U0001D11E", "abc");

        Assert.Equal(1, plan.Backspaces);
        Assert.Equal("c", plan.Text);
    }

    [Fact]
    public void SharedAstralPrefix_NotDivergent_StaysWhole()
    {
        var plan = InjectionPlan.Compute("x\U0001D11Ey", "x\U0001D11Ez");

        Assert.Equal(1, plan.Backspaces);
        Assert.Equal("z", plan.Text);
    }

    [Fact]
    public void EmptyCurrent_TypesEverything_NoBackspaces()
    {
        var plan = InjectionPlan.Compute("", "hello");

        Assert.Equal(0, plan.Backspaces);
        Assert.Equal("hello", plan.Text);
    }

    [Fact]
    public void EmptyTarget_BackspacesEverything()
    {
        var plan = InjectionPlan.Compute("oops", "");

        Assert.Equal(4, plan.Backspaces);
        Assert.Equal("", plan.Text);
    }
}
