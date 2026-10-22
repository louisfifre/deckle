using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// The registered roots are process-wide, so the tests that add one run one after
// the other and forget it when they end.
[Collection(PathRedactionCollection.Name)]
[Trait("Category", "unit")]
public sealed class PathRedactionTests : IDisposable
{
    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public PathRedactionTests() => PathRedaction.ResetRoots();

    public void Dispose() => PathRedaction.ResetRoots();

    [Fact]
    public void A_path_under_the_profile_is_named_by_the_profile_variable()
    {
        string redacted = PathRedaction.Redact(Path.Combine(Profile, "Documents", "notes.txt"));

        Assert.Equal(@"%USERPROFILE%\Documents\notes.txt", redacted);
    }

    [Fact]
    public void The_profile_is_found_wherever_the_text_quotes_it_and_whatever_its_case()
    {
        string message = $"Access to the path '{Profile.ToUpperInvariant()}\\x.bin' is denied (also {Profile}).";

        string redacted = PathRedaction.Redact(message);

        Assert.Equal(@"Access to the path '%USERPROFILE%\x.bin' is denied (also %USERPROFILE%).", redacted);
    }

    [Fact]
    public void A_folder_whose_name_only_starts_like_the_profile_is_left_alone()
    {
        string sibling = Profile + "2" + @"\x.bin";

        Assert.Equal(sibling, PathRedaction.Redact(sibling));
    }

    [Fact]
    public void A_registered_data_root_wins_over_the_profile_it_sits_in()
    {
        string dataRoot = Path.Combine(Profile, "AppData", "Local", "Deckle");
        PathRedaction.AddRoot(PathRedaction.DataRootToken, dataRoot);

        string redacted = PathRedaction.Redact(Path.Combine(dataRoot, "models", "ggml.bin"));

        Assert.Equal(@"%DECKLE_DATA%\models\ggml.bin", redacted);
    }

    [Fact]
    public void A_data_root_moved_outside_the_profile_is_still_named_by_its_variable()
    {
        string dataRoot = @"D:\Elsewhere\DeckleData";
        PathRedaction.AddRoot(PathRedaction.DataRootToken, dataRoot + @"\");

        Assert.Equal(@"%DECKLE_DATA%\native\whisper.dll",
            PathRedaction.Redact(@"D:\Elsewhere\DeckleData\native\whisper.dll"));
    }

    [Fact]
    public void A_text_without_a_known_root_comes_back_unchanged()
    {
        Assert.Equal(@"relative\path\file.txt", PathRedaction.Redact(@"relative\path\file.txt"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_absent_text_comes_back_empty(string? text)
    {
        Assert.Equal(string.Empty, PathRedaction.Redact(text));
    }

    [Fact]
    public void A_drive_root_is_refused_as_a_root_instead_of_redacting_the_machine()
    {
        PathRedaction.AddRoot("%DRIVE%", @"C:\");

        Assert.Equal(@"C:\Windows\System32", PathRedaction.Redact(@"C:\Windows\System32"));
    }

    [Fact]
    public void The_folder_action_event_redacts_through_the_same_rule()
    {
        using var listener = new TestEventListener("Deckle-SettingsUx");

        DeckleSettingsUxSource.Log.FolderActionFailed(
            "open", "IOException", unchecked((int)0x80070005), $"denied: {Profile}\\Deckle");

        var ev = Assert.Single(listener.Events);
        Assert.Equal(@"denied: %USERPROFILE%\Deckle", ev.Payload?[3]);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PathRedactionCollection
{
    public const string Name = "path redaction roots";
}
