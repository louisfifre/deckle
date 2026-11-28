namespace Deckle.Browser;

// The filter lists the ad blocker is built from, where they come from and when
// the copy kept on disk is too old. The browser fetching its own lists is the
// one network use the browser makes for itself; nothing visited is involved.
public static class FilterLists
{
    /// <summary>One source: its name (what a log line says), its address and the
    /// file it is kept in, under the browser module's Filters folder.</summary>
    public sealed record Source(string Name, Uri Address, string FileName);

    public const string ResourcesName = "adblock-resources";

    /// <summary>The filter lists, in the order they are read into the engine.</summary>
    public static IReadOnlyList<Source> Lists { get; } =
    [
        new("EasyList", new("https://easylist.to/easylist/easylist.txt"), "easylist.txt"),
        new("EasyPrivacy", new("https://easylist.to/easylist/easyprivacy.txt"), "easyprivacy.txt"),
        new("uBlock filters", new("https://ublockorigin.github.io/uAssets/filters/filters.txt"), "ublock-filters.txt"),
        new("uBlock quick fixes", new("https://ublockorigin.github.io/uAssets/filters/quick-fixes.txt"), "ublock-quick-fixes.txt"),
        new("uBlock privacy", new("https://ublockorigin.github.io/uAssets/filters/privacy.txt"), "ublock-privacy.txt"),
    ];

    /// <summary>Brave's scriptlets and redirect resources, the JSON the engine reads
    /// with <c>use_resources</c>.</summary>
    public static Source Resources { get; } =
        new(ResourcesName, new("https://raw.githubusercontent.com/brave/adblock-resources/master/dist/resources.json"), "resources.json");

    /// <summary>Every source, lists then resources.</summary>
    public static IEnumerable<Source> All => Lists.Append(Resources);

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(4);

    /// <summary>True when a file last written at <paramref name="lastWrite"/> is older
    /// than four days at <paramref name="now"/>. A write dated in the future is not stale.</summary>
    public static bool IsStale(DateTimeOffset lastWrite, DateTimeOffset now) =>
        now - lastWrite > MaxAge;
}
