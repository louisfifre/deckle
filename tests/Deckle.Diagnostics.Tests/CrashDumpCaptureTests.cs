using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "unit")]
public sealed class CrashDumpCaptureDecisionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_variable_arms_and_relaunches(string? value)
    {
        Assert.Equal(CrashDumpCapture.Decision.ArmAndRelaunch, CrashDumpCapture.Decide(value));
    }

    [Theory]
    [InlineData("1")]
    [InlineData(" 1 ")]
    public void A_variable_at_one_means_this_process_is_already_armed(string value)
    {
        Assert.Equal(CrashDumpCapture.Decision.AlreadyArmed, CrashDumpCapture.Decide(value));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    public void Any_other_value_is_a_deliberate_off_and_stays_off(string value)
    {
        Assert.Equal(CrashDumpCapture.Decision.LeftOff, CrashDumpCapture.Decide(value));
    }

    [Fact]
    public void Variables_enable_capture_into_the_given_folder_with_the_default_type()
    {
        var variables = CrashDumpCapture.Variables(@"C:\data\dumps");

        Assert.Equal("1", variables[CrashDumpCapture.EnableVariable]);
        Assert.Equal(((int)CrashDumpCapture.DefaultType).ToString(), variables[CrashDumpCapture.TypeVariable]);
        Assert.Equal(Path.Combine(@"C:\data\dumps", CrashDumpCapture.FileNameTemplate), variables[CrashDumpCapture.NameVariable]);
    }
}

[Trait("Category", "integration")]
public sealed class CrashDumpCapturePruneTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "deckle-dumps-" + Guid.NewGuid().ToString("N"));

    public CrashDumpCapturePruneTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Prune_keeps_the_newest_dumps_and_deletes_the_rest()
    {
        var baseTime = new DateTime(2026, 10, 17, 23, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 5; i++)
        {
            string path = Path.Combine(_dir, $"Deckle-crash-{i}-100{i}{CrashDumpCapture.FileExtension}");
            File.WriteAllText(path, "dump");
            File.SetLastWriteTimeUtc(path, baseTime.AddMinutes(i));
        }

        var kept = CrashDumpCapture.Prune(_dir, keep: 2);

        Assert.Equal(2, kept.Count);
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
        Assert.Equal(baseTime.AddMinutes(4), kept[0].LastWriteTimeUtc);
        Assert.Equal(baseTime.AddMinutes(3), kept[1].LastWriteTimeUtc);
    }

    [Fact]
    public void Prune_leaves_other_files_alone()
    {
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "keep me");

        var kept = CrashDumpCapture.Prune(_dir, keep: 0);

        Assert.Empty(kept);
        Assert.True(File.Exists(Path.Combine(_dir, "notes.txt")));
    }

    [Fact]
    public void Prune_on_a_missing_folder_returns_nothing()
    {
        Assert.Empty(CrashDumpCapture.Prune(Path.Combine(_dir, "absent")));
    }
}
