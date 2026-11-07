using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The test classes that read or write the isolated taskbar profile through the
// ViewModel (TaskbarSettingsService is a process-wide singleton). xUnit runs
// one collection's classes one after the other, so a test that flips a setting
// never lands in the middle of one that compares the profile to its defaults.
[CollectionDefinition(Name)]
public sealed class TaskbarProfileCollection
{
    public const string Name = "Taskbar profile";
}
