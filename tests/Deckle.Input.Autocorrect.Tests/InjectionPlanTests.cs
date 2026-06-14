using Deckle.Input.Autocorrect;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// Comportement du diff minimal : combien de Backspaces, quel suffixe à taper.
// On n'assert que le contrat observable (le couple Backspaces/Text), jamais la
// façon dont le préfixe commun est calculé. Le cas paire de substitution est
// load-bearing : c'est lui qui garantit qu'on ne coupe jamais un char astral
// en deux demi-surrogates, ce qui produirait du texte corrompu à l'injection.
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
        // Le cas canonique de la restauration de diacritiques (CLAUDE.md).
        var plan = InjectionPlan.Compute("francais ", "français ");

        Assert.Equal(5, plan.Backspaces);  // c-a-i-s-espace
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
        // target prolonge current : rien à effacer, on tape juste la suite.
        var plan = InjectionPlan.Compute("auto", "automatique");

        Assert.Equal(0, plan.Backspaces);
        Assert.Equal("matique", plan.Text);
    }

    [Fact]
    public void SurrogatePairIsNeverSplit()
    {
        // "ab𝄞" (U+1D11E, paire de surrogates) vs "abc" : le préfixe commun
        // s'arrête à "ab", la clé de sol compte pour UN code point (un seul
        // Backspace), et le suffixe tapé est le "c" entier.
        var plan = InjectionPlan.Compute("ab\U0001D11E", "abc");

        Assert.Equal(1, plan.Backspaces);  // le 𝄞 = 1 code point, pas 2 unités
        Assert.Equal("c", plan.Text);
    }

    [Fact]
    public void SharedAstralPrefix_NotDivergent_StaysWhole()
    {
        // Préfixe commun qui inclut une paire de surrogates : elle ne doit pas
        // faire diverger le diff ni se faire couper.
        var plan = InjectionPlan.Compute("x\U0001D11Ey", "x\U0001D11Ez");

        Assert.Equal(1, plan.Backspaces);  // seul le 'y' final diverge
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
