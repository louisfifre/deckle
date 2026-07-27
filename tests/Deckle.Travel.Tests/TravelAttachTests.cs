using System.Text.Json.Nodes;
using Xunit;

namespace Deckle.Travel.Tests;

// Attaching is the one gesture that carries bytes. These assert its WIRE
// EFFECT: what the upload route receives, and what the object PATCH then says.
[Trait("Category", "integration")]
public class TravelAttachTests
{
    private const string TransferId = "bafyreitransferaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string StayId = "bafyreistayaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Transfer(params string[] fileIds)
    {
        var files = new JsonArray();
        foreach (string id in fileIds) files.Add(id);
        return new JsonObject
        {
            ["id"] = TransferId,
            ["name"] = "Paris → Lisbonne",
            ["type"] = new JsonObject { ["key"] = TravelSchema.Types.Transfer },
            ["properties"] = new JsonArray(
                new JsonObject { ["key"] = TravelSchema.Properties.Files, ["files"] = files }),
        };
    }

    // In its own directory, so the basename the upload carries is exactly the
    // name under test.
    private static string WriteTempFile(string name, string content)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"deckle-travel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task AttachUploadsEachFileUnderTheFileFieldAndKeepsWhatWasAlreadyThere()
    {
        using var space = new FakeTravelSpace();
        space.OnListObjects(Transfer("bafyreialreadythere"));
        space.OnUploadFile("bafyreiticket", "billet.pdf");
        space.OnPatchObject(TransferId, new JsonObject { ["object"] = Transfer() });

        string path = WriteTempFile("billet.pdf", "PDF-ish bytes");
        try
        {
            await space.NewGestures().AttachAsync(TransferId, [path], Ct);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }

        // The multipart part is named "file" and carries the caller's filename —
        // anytype-heart derives the stored name from it.
        string upload = space.Last("POST", "/files").Body;
        Assert.Contains("name=\"file\"", upload, StringComparison.Ordinal);
        Assert.Contains("filename=\"billet.pdf\"", upload, StringComparison.Ordinal);
        Assert.Contains("PDF-ish bytes", upload, StringComparison.Ordinal);

        // A files PATCH replaces the whole list, so the gesture rewrites the
        // existing attachment alongside the new one.
        JsonObject patched = space.LastBody("PATCH", $"/objects/{TransferId}");
        JsonObject entry = (JsonObject)((JsonArray)patched["properties"]!)[0]!;
        Assert.Equal(TravelSchema.Properties.Files, entry["key"]!.GetValue<string>());
        Assert.Equal(
            ["bafyreialreadythere", "bafyreiticket"],
            ((JsonArray)entry["files"]!).Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public async Task AttachRefusesATypeThatCarriesNoFilesBeforeAnyByteLeavesTheDisk()
    {
        using var space = new FakeTravelSpace();
        space.OnListObjects(new JsonObject
        {
            ["id"] = StayId,
            ["name"] = "Portugal, mai",
            ["type"] = new JsonObject { ["key"] = TravelSchema.Types.Stay },
            ["properties"] = new JsonArray(),
        });

        string path = WriteTempFile("billet.pdf", "PDF-ish bytes");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                space.NewGestures().AttachAsync(StayId, [path], Ct));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }

        Assert.DoesNotContain(space.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task AttachRefusesAMissingPathWithoutTouchingTheSpace()
    {
        using var space = new FakeTravelSpace();
        space.OnListObjects(Transfer());

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            space.NewGestures().AttachAsync(
                TransferId,
                [Path.Combine(Path.GetTempPath(), "deckle-travel-absent.pdf")],
                Ct));

        Assert.DoesNotContain(space.Requests, request => request.Method == "POST");
    }
}
