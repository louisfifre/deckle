using Xunit;

namespace Deckle.Anytype.Tests;

// The words of the project-management space come from Terms/pm.<language>.json.
// Every language names the same keys, French is what Deckle shows, and a name
// typed in any shipped language resolves to the key.
[Trait("Category", "unit")]
public sealed class PmTermsTests
{
    [Fact]
    public void FrenchIsTheDisplayLanguageAndEnglishShips()
    {
        Assert.Equal("fr", PmTerms.Display.Language);
        Assert.Contains(PmTerms.Languages, t => t.Language == "en");
    }

    [Fact]
    public void EveryLanguageNamesTheSameKeys()
    {
        SpaceTerms display = PmTerms.Display;
        foreach (SpaceTerms other in PmTerms.Languages.Skip(1))
        {
            Assert.Equal(display.TypeKeys.Order(), other.TypeKeys.Order());
            Assert.Equal(display.PropertyKeys.Order(), other.PropertyKeys.Order());
            Assert.Equal(display.OptionKeys.Order(), other.OptionKeys.Order());
        }
    }

    [Fact]
    public void TermsNameEveryTypePropertyAndOptionDevSpaceKnows()
    {
        foreach (SpaceTerms terms in PmTerms.Languages)
            foreach (string typeKey in DevSpace.TypeKeys)
            {
                Assert.False(string.IsNullOrWhiteSpace(terms.TypeName(typeKey)));
                foreach (PropertyDef def in DevSpace.PropertiesFor(typeKey))
                {
                    Assert.False(string.IsNullOrWhiteSpace(terms.PropertyName(def.Key)));
                    foreach (TagOption option in def.Options ?? [])
                        Assert.False(string.IsNullOrWhiteSpace(terms.OptionName(def.Key, option.Key)));
                }
            }
    }

    [Fact]
    public void DevSpaceShowsFrenchNames()
    {
        Assert.Equal("Définition de fini", DevSpace.PropertyLabel(DevSpace.Props.DoneDefinition));
        Assert.Equal("En cours", DevSpace.Status.NameFor(DevSpace.Status.Doing));
        Assert.Equal("Créneau", DevSpace.Slot.Name);
    }

    [Theory]
    [InlineData("In progress", "doing")]
    [InlineData("En cours", "doing")]
    [InlineData("doing", "doing")]
    public void StatusAcceptsAKeyOrANameInAnyLanguage(string typed, string key) =>
        Assert.Equal(key, DevSpace.Status.Resolve(typed));

    [Fact]
    public void TaskTypeAcceptsAnEnglishName() =>
        Assert.Equal("production", DevSpace.ResolveTag(DevSpace.Props.TaskType, "Produce"));

    [Fact]
    public void PropertyAcceptsAnEnglishName()
    {
        Assert.True(DevSpace.TryResolveProperty(DevSpace.Types.Task, "Definition of done", out string key, out _));
        Assert.Equal(DevSpace.Props.DoneDefinition, key);
    }

    [Fact]
    public void LiveSlotOptionIsFoundWhateverLanguageTheSpaceSpeaks()
    {
        var english = new LiveSelect("slot", "Slot", [("opt_focus", "Focus"), ("opt_practice", "Practice")]);
        Assert.Equal("Focus", english.OptionName(DevSpace.Slot.Default, DevSpace.Props.Slot));
        Assert.Equal("Practice", english.OptionName("Entraînement", DevSpace.Props.Slot));

        var french = new LiveSelect("slot", "Créneau", [("opt_cap", "Cap")]);
        Assert.Equal("Cap", french.OptionName("Focus", DevSpace.Props.Slot));
        Assert.Null(french.OptionName("Passion", DevSpace.Props.Slot));
    }
}
