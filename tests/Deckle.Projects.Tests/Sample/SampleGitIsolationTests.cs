using System.Diagnostics;
using Xunit;

namespace Deckle.Projects.Tests;

// The tests below change the process's environment, which every git command of
// every test inherits: they run alone.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MachineGitConfigurationCollection
{
    public const string Name = "machine git configuration";
}

// The sample world is the same whatever git setup the machine and the calling
// terminal carry. The self-check's P1 failed from a terminal without the
// maintainer's GIT_AUTHOR_* variables: his global configuration holds a
// commit-msg hook that refuses any other author, so the sample's commit was
// refused, its tags had nothing to point at, and Deckle's sample read "No
// version yet" with every file uncommitted.
[Collection(MachineGitConfigurationCollection.Name)]
[Trait("Category", "regression")]
public sealed class SampleGitIsolationTests : IDisposable
{
    private static readonly string[] Variables =
        ["GIT_CONFIG_GLOBAL", "GIT_AUTHOR_NAME", "GIT_AUTHOR_EMAIL", "GIT_COMMITTER_NAME", "GIT_COMMITTER_EMAIL"];

    private readonly ScratchFolder _scratch = new();
    private readonly Dictionary<string, string?> _saved = Variables.ToDictionary(v => v, Environment.GetEnvironmentVariable);

    public void Dispose()
    {
        foreach ((string name, string? value) in _saved) Environment.SetEnvironmentVariable(name, value);
        _scratch.Dispose();
    }

    [Fact]
    public async Task TheSampleIsCommittedAndTaggedUnderItsOwnNameWhateverTheMachinesGitSetup()
    {
        Assert.SkipUnless(SampleGit.IsAvailable, "git is not on the PATH");

        // A global configuration with hooks that refuse every commit, and a
        // terminal whose identity variables name someone else.
        string hostile = _scratch.File(
            "[hook \"refuse-message\"]\n\tevent = commit-msg\n\tcommand = exit 1\n"
            + "[hook \"refuse-commit\"]\n\tevent = pre-commit\n\tcommand = exit 1\n"
            + "[user]\n\tname = Intruder\n\temail = intruder@example.invalid\n",
            "hostile.gitconfig");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", hostile);
        foreach (string variable in (string[])["GIT_AUTHOR_NAME", "GIT_COMMITTER_NAME"])
            Environment.SetEnvironmentVariable(variable, "Intruder");
        foreach (string variable in (string[])["GIT_AUTHOR_EMAIL", "GIT_COMMITTER_EMAIL"])
            Environment.SetEnvironmentVariable(variable, "intruder@example.invalid");

        ProjectsSample sample = ProjectsSampleWorld.Create(_scratch.Path);

        // Read without the hostile configuration: the reader is not under test.
        foreach ((string name, string? value) in _saved) Environment.SetEnvironmentVariable(name, value);
        string deckle = sample.PathOf(SampleProjectNames.Deckle);
        ProjectReading reading = await new ProjectReader().ReadAsync(deckle, CancellationToken.None);

        Assert.Equal("v0.131.0", reading.Version.Tag);
        Assert.Equal(3, reading.UncommittedFiles);
        Assert.Equal("Sample <sample@example.invalid>", Git(deckle, "log", "-1", "--format=%an <%ae>"));
    }

    private static string Git(string folder, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output.Trim();
    }
}
