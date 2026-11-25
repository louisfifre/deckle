using Xunit;

namespace Deckle.App.Tests;

// A file of the list opened in the Transcribe and rewrite field is saved by
// itself. These cases state what the user gets in the .txt on disk.
[Trait("Category", "regression")]
public sealed class OpenFileAutoSaveTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("deckle-autosave-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private FileTranscriptionItem OpenItem(string diskText, out string path)
    {
        path = Path.Combine(_dir, "talk.mp3.txt");
        File.WriteAllText(path, diskText);
        return new FileTranscriptionItem(Path.Combine(_dir, "talk.mp3"))
        {
            OutputPath = path,
            Text = diskText,
        };
    }

    [Fact]
    public void A_text_file_that_was_only_looked_at_is_never_rewritten()
    {
        FileTranscriptionItem item = OpenItem("Hello\nworld", out string path);
        DateTime stamp = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);
        // The field hands the same text back with its own line breaks.
        var autoSave = new OpenFileAutoSave(item, "Hello\rworld");

        OpenFileAutoSave.Result result = autoSave.Save("Hello\rworld");

        Assert.Equal(OpenFileAutoSave.Outcome.Unchanged, result.Outcome);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal("Hello\nworld", File.ReadAllText(path));
    }

    [Fact]
    public void A_corrected_text_file_is_written_back_and_the_list_shows_the_correction()
    {
        FileTranscriptionItem item = OpenItem("Helo world", out string path);
        var autoSave = new OpenFileAutoSave(item, "Helo world");

        OpenFileAutoSave.Result result = autoSave.Save("Hello world");

        Assert.Equal(OpenFileAutoSave.Outcome.Written, result.Outcome);
        Assert.Equal("Hello world", File.ReadAllText(path));
        Assert.Equal("Hello world", item.Text);
    }

    [Fact]
    public void A_correction_already_written_is_not_written_again()
    {
        FileTranscriptionItem item = OpenItem("Helo world", out string path);
        var autoSave = new OpenFileAutoSave(item, "Helo world");
        autoSave.Save("Hello world");
        File.WriteAllText(path, "changed elsewhere");

        OpenFileAutoSave.Result result = autoSave.Save("Hello world");

        Assert.Equal(OpenFileAutoSave.Outcome.Unchanged, result.Outcome);
        Assert.Equal("changed elsewhere", File.ReadAllText(path));
    }

    [Fact]
    public void A_write_that_fails_is_reported_and_tried_again_on_the_next_save()
    {
        FileTranscriptionItem item = OpenItem("Helo world", out string path);
        var autoSave = new OpenFileAutoSave(item, "Helo world");
        string held = path;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            OpenFileAutoSave.Result failed = autoSave.Save("Hello world");

            Assert.Equal(OpenFileAutoSave.Outcome.Failed, failed.Outcome);
            Assert.NotNull(failed.Error);
            Assert.Equal("Helo world", item.Text);
        }

        OpenFileAutoSave.Result retried = autoSave.Save("Hello world");

        Assert.Equal(OpenFileAutoSave.Outcome.Written, retried.Outcome);
        Assert.Equal("Hello world", File.ReadAllText(path));
    }
}
