using System.Diagnostics.Tracing;
using System.Text.Json;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "regression")]
public sealed class JsonlSinkRegressionTests
{
    [Fact]
    public void A_row_cut_short_before_its_newline_does_not_glue_to_the_next_row()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, $"jsonl-unterminated-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "cut.jsonl");
        // What a crash between a row and its newline leaves behind.
        File.WriteAllText(path, "{\"left\":\"unterminated\"}");

        var sink = new JsonlSink(path, "test", static _ => true);
        try
        {
            sink.Write(Entry());
            sink.Flush();

            string[] lines = SharedFileReader.ReadAllLines(path).ToArray();
            Assert.Equal(2, lines.Length);
            foreach (string line in lines)
                JsonDocument.Parse(line).Dispose();
        }
        finally
        {
            sink.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static EventEntry Entry() =>
        new(
            DateTimeOffset.UtcNow,
            "Deckle-Tests",
            "Row",
            EventLevel.Verbose,
            EventKeywords.None,
            ObservationKind.Operational,
            null,
            new Dictionary<string, object?> { ["n"] = 1 });
}
