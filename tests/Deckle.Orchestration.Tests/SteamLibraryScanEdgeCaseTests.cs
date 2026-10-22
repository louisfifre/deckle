using Xunit;

namespace Deckle.Orchestration.Tests;

// The skip rules of SteamLibrary.ScanLibraries and the root ordering of
// LibraryRootsOf, over a throwaway Steam tree in the temp folder. The happy path
// is ScanOnDiskTests; this is every reason a manifest or a library is left out.
[Trait("Category", "integration")]
public sealed class SteamLibraryScanEdgeCaseTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "deckle-steam-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* temp folder: the OS cleans it eventually */ }
    }

    [Fact]
    public void ScanSkipsTheRedistributablesPackage()
    {
        string library = NewLibrary("lib");
        AddGame(library, appId: "228980", name: "Steamworks Common Redistributables", installDir: "Redist");
        AddGame(library, appId: "480", name: "Spacewar", installDir: "Spacewar");

        SteamManifest manifest = Assert.Single(SteamLibrary.ScanLibraries([library]));

        Assert.Equal("480", manifest.AppId);
    }

    [Fact]
    public void ScanSkipsAManifestWhoseGameFolderIsNotOnDisk()
    {
        string library = NewLibrary("lib");
        AddGame(library, appId: "480", name: "Spacewar", installDir: "Spacewar", createFolder: false);

        Assert.Empty(SteamLibrary.ScanLibraries([library]));
    }

    [Fact]
    public void ScanSkipsAManifestWithoutAnInstallFolder()
    {
        string library = NewLibrary("lib");
        File.WriteAllText(
            Path.Combine(library, "steamapps", "appmanifest_480.acf"),
            "\"AppState\" { \"appid\" \"480\" \"name\" \"Spacewar\" }");

        Assert.Empty(SteamLibrary.ScanLibraries([library]));
    }

    [Fact]
    public void ScanSkipsALibraryThatIsNotOnDiskAndKeepsTheOthers()
    {
        string present = NewLibrary("present");
        AddGame(present, appId: "480", name: "Spacewar", installDir: "Spacewar");
        string unplugged = Path.Combine(_root, "unplugged");

        SteamManifest manifest = Assert.Single(SteamLibrary.ScanLibraries([unplugged, present]));

        Assert.Equal("480", manifest.AppId);
    }

    [Fact]
    public void ScanIgnoresARelativeLibraryPath()
    {
        Assert.Empty(SteamLibrary.ScanLibraries([Path.Combine("steamapps", "relative")]));
    }

    [Fact]
    public void ScanReturnsTheGamesOfEveryLibraryInManifestFileOrder()
    {
        string first = NewLibrary("first");
        AddGame(first, appId: "20", name: "Beta", installDir: "Beta");
        AddGame(first, appId: "10", name: "Alpha", installDir: "Alpha");
        string second = NewLibrary("second");
        AddGame(second, appId: "30", name: "Gamma", installDir: "Gamma");

        var ids = SteamLibrary.ScanLibraries([first, second]).Select(manifest => manifest.AppId);

        // Within a library the manifests are read in file-name order (appmanifest_10
        // before appmanifest_20), libraries in the order given.
        Assert.Equal(["10", "20", "30"], ids);
    }

    [Fact]
    public void LibraryRootsStartWithTheSteamRootAndAddEachListedLibraryOnce()
    {
        string steam = NewLibrary("Steam");
        string other = Path.Combine(_root, "OtherLibrary");
        File.WriteAllText(
            Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            $$"""
            "libraryfolders"
            {
                "0" { "path" "{{Escape(steam)}}" }
                "1" { "path" "{{Escape(other)}}" }
                "2" { "path" "{{Escape(other)}}\\" }
            }
            """);

        Assert.Equal([steam, other], SteamLibrary.LibraryRootsOf(steam));
    }

    [Fact]
    public void LibraryRootsAreJustTheSteamRootWhenTheLibraryListIsMissing()
    {
        string steam = NewLibrary("Steam");

        Assert.Equal([steam], SteamLibrary.LibraryRootsOf(steam));
    }

    private string NewLibrary(string name)
    {
        string library = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(library, "steamapps", "common"));
        return library;
    }

    private static void AddGame(
        string library, string appId, string name, string installDir, bool createFolder = true)
    {
        File.WriteAllText(
            Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf"),
            $"\"AppState\" {{ \"appid\" \"{appId}\" \"name\" \"{name}\" \"installdir\" \"{installDir}\" }}");
        if (createFolder)
            Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", installDir));
    }

    // VDF strings escape the backslash, as Steam writes them.
    private static string Escape(string path) => path.Replace(@"\", @"\\");
}