using Deckle.Catalog;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

[Trait("Category", "unit")]
public sealed class TaskbarSettingsModuleTests
{
    // The cover nests under this id by naming it as its ParentId, a string the
    // two modules each spell (the cover cannot reference this module), and the
    // store writes under modules/<ModuleId>. The cover's own tests pin the same
    // literal on their side.
    [Fact]
    public void TheNodeAnswersToTheIdTheCoverNestsUnder()
    {
        SettingsModuleDescriptor descriptor = TaskbarSettingsModule.Describe(order: 580);

        Assert.Equal("taskbar", TaskbarSettingsModule.ModuleId);
        Assert.Equal(TaskbarSettingsModule.ModuleId, descriptor.Id);
        Assert.Null(descriptor.ParentId);
    }

    // The shell navigates by resolving PageTag to a type; a tag that names no
    // page leaves the node's General entry blank.
    [Fact]
    public void ThePageTagResolvesToTheModulePage()
    {
        SettingsModuleDescriptor descriptor = TaskbarSettingsModule.Describe(order: 580);

        Type? page = Type.GetType(descriptor.PageTag);

        Assert.NotNull(page);
        Assert.Equal(typeof(TaskbarPage), page);
        Assert.True(typeof(Page).IsAssignableFrom(page));
        Assert.Equal(TaskbarSettingsModule.ResourceLibrary, page.Assembly.GetName().Name);
        Assert.Equal(TaskbarSettingsModule.ResourceLibrary, descriptor.OwningAssembly);
    }
}