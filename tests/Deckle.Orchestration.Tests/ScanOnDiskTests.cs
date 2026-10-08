using Deckle.Orchestration;
using Xunit;

namespace Deckle.Orchestration.Tests;

// The scan over a real directory tree built in the temp folder: a fake Steam
// install whose library holds Spacewar, and a "Games" folder with one game in
// a subfolder and one loose app. The settings file is never touched: the merge
// rule is exercised through AppMatcher.SelectNew, which the service uses.
[Trait("Category", "integration")]
public sealed class ScanOnDiskTests
{
    [Fact]
    public void ScanFindsSteamAndFolderEntriesAndASecondScanAddsNothing()
    {
        string root = Path.Combine(Path.GetTempPath(), "deckle-orchestration-" + Guid.NewGuid().ToString("N"));
        try
        {
            string steam = Path.Combine(root, "Steam");
            string steamapps = Path.Combine(steam, "steamapps");
            string spacewar = Path.Combine(steamapps, "common", "Spacewar");
            Directory.CreateDirectory(spacewar);
            File.WriteAllText(Path.Combine(steamapps, "appmanifest_480.acf"), Fixture.Read("appmanifest_480.acf"));
            File.WriteAllBytes(Path.Combine(spacewar, "Spacewar.exe"), []);
            File.WriteAllBytes(Path.Combine(spacewar, "unins000.exe"), []);

            string games = Path.Combine(root, "Games");
            string foo = Path.Combine(games, "Foo");
            Directory.CreateDirectory(foo);
            File.WriteAllBytes(Path.Combine(foo, "Foo.exe"), []);
            File.WriteAllBytes(Path.Combine(games, "tool.exe"), []);
            Directory.CreateDirectory(Path.Combine(games, "Empty"));

            // Steam libraries
            var manifest = Assert.Single(SteamLibrary.ScanLibraries([steam]));
            Assert.Equal("480", manifest.AppId);
            Assert.Equal(spacewar, manifest.InstallDirectory);

            // Steam entries
            var steamEntry = Assert.Single(RegistryScan.CollectSteam(steam));
            Assert.Equal(AppSource.Steam, steamEntry.Source);
            Assert.Equal(AppKind.Game, steamEntry.Kind);
            Assert.Equal("Spacewar", steamEntry.DisplayName);
            Assert.Equal("480", steamEntry.SteamAppId);
            Assert.Equal(Path.Combine(spacewar, "Spacewar.exe"), steamEntry.ExecutablePath);
            Assert.Equal("Spacewar", steamEntry.ProcessName);

            // Chosen folder: one game per subfolder with an exe, one app per loose exe
            var folderEntries = FolderScanner.Scan(games);
            Assert.Collection(
                folderEntries,
                game =>
                {
                    Assert.Equal(AppKind.Game, game.Kind);
                    Assert.Equal(AppSource.Folder, game.Source);
                    Assert.Equal("Foo", game.DisplayName);
                    Assert.Equal(foo, game.InstallDirectory);
                    Assert.Equal(Path.Combine(foo, "Foo.exe"), game.ExecutablePath);
                    Assert.Equal("Foo", game.ProcessName);
                },
                app =>
                {
                    Assert.Equal(AppKind.App, app.Kind);
                    Assert.Equal("Tool", app.DisplayName);
                    Assert.Equal("tool", app.ProcessName);
                });

            // A missing chosen folder is skipped, not fatal
            Assert.Equal(2, RegistryScan.CollectFolders([games, Path.Combine(root, "Gone")]).Count);

            // Merging: the first scan adds all three, a second adds nothing
            var first = AppMatcher.SelectNew([], [.. RegistryScan.CollectSteam(steam), .. RegistryScan.CollectFolders([games])]);
            Assert.Equal(3, first.Count);
            var second = AppMatcher.SelectNew(first, [.. RegistryScan.CollectSteam(steam), .. RegistryScan.CollectFolders([games])]);
            Assert.Empty(second);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
