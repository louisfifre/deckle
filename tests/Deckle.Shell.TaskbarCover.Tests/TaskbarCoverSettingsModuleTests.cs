using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

[Trait("Category", "unit")]
public sealed class TaskbarCoverSettingsModuleTests
{
    // The cover nests under the Taskbar node by id. This module cannot reference
    // Deckle.Shell.Taskbar, so the literal is pinned here and the Taskbar suite
    // pins the same one on its side.
    [Fact]
    public void TheCoverNestsUnderTheTaskbarNode()
    {
        Assert.Equal("taskbar", TaskbarCoverSettingsModule.Describe(order: 581).ParentId);
    }
}