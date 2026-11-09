using System.Text.Json.Nodes;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

// Where a replay sheet lands and how a relation names its target.
[Trait("Category", "unit")]
public sealed class HomeReplaySheetTests
{
    [Fact]
    public void TheDefaultPathIsDatedAndNamedAfterTheSourceAlias()
    {
        string path = HomeReplaySheet.DefaultPath(
            @"C:\fictional\modules\home", "home", new DateTimeOffset(2026, 11, 9, 14, 5, 0, TimeSpan.Zero));

        Assert.Equal(@"C:\fictional\modules\home\rehearsal\20261109-1405-home.json", path);
    }

    [Fact]
    public void AnAliasNeverEscapesTheRehearsalFolder()
    {
        string path = HomeReplaySheet.DefaultPath(
            @"C:\fictional\modules\home", @"..\other", new DateTimeOffset(2026, 11, 9, 14, 5, 0, TimeSpan.Zero));

        Assert.Equal(@"C:\fictional\modules\home\rehearsal\20261109-1405----other.json", path);
    }

    [Fact]
    public void AReportSitsBesideTheSheetUnderItsStem()
    {
        Assert.Equal(
            @"C:\fictional\rehearsal\20261109-1405-home.replay.json",
            HomeReplaySheet.Beside(@"C:\fictional\rehearsal\20261109-1405-home.json", "replay"));
    }

    [Fact]
    public void ARelationNamesACodedObjectByCodeAnIdeaByItsFirstLineAndTheRestByName()
    {
        Assert.Equal("ZZ-PS01", new HomeReplayObject { Type = "point", Code = "ZZ-PS01", Name = "Prise fictive" }.Selector);
        Assert.Equal("Idée fictive", new HomeReplayObject { Type = "idea", Body = "Idée fictive\nsuite" }.Selector);
        Assert.Equal("Appareil fictif", new HomeReplayObject { Type = "device", Name = "Appareil fictif" }.Selector);
    }
}

// The sheet written and read back from a temporary folder.
[Trait("Category", "integration")]
public sealed class HomeReplaySheetFileTests
{
    [Fact]
    public void ASheetRoundTripsThroughItsFile()
    {
        string folder = Path.Combine(Path.GetTempPath(), "deckle-home-sheet-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(folder, "rehearsal", "sheet.json");
            var sheet = new HomeReplaySheet
            {
                SourceSpace = "space-fictional",
                SourceAlias = "fictional",
                ExportedAt = new DateTimeOffset(2026, 11, 9, 14, 5, 0, TimeSpan.Zero),
                Objects =
                [
                    new HomeReplayObject
                    {
                        OldId = "old-1",
                        Type = "device",
                        Name = "Appareil fictif é",
                        Body = "Corps fictif",
                        Properties = new JsonObject { ["purchase_price"] = 12.5, ["installed_in"] = new JsonArray("ZZ") },
                        References = new() { ["installed_in"] = ["old-room"] },
                        Unmapped = new JsonObject { ["power_watts"] = 40 },
                    },
                ],
            };
            HomeReplaySheetReport.Increment(sheet.Report.Types, "device");

            sheet.Save(path);
            HomeReplaySheet loaded = HomeReplaySheet.Load(path);

            Assert.Contains("Appareil fictif é", File.ReadAllText(path));
            HomeReplayObject value = Assert.Single(loaded.Objects);
            Assert.Equal("Appareil fictif é", value.Name);
            Assert.Equal(12.5, value.Properties["purchase_price"]!.GetValue<double>());
            Assert.Equal(["old-room"], value.References["installed_in"]);
            Assert.Equal(40, value.Unmapped["power_watts"]!.GetValue<int>());
            Assert.Null(value.Code);
            Assert.Equal(1, loaded.Report.Types["device"]);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
