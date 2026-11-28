namespace Deckle.Browser;

// The filter lists the ad blocker is built from, where they come from and when
// the copy kept on disk is too old. The browser fetching its own lists is the
// one network use the browser makes for itself; nothing visited is involved.
public static class FilterLists
{
    /// <summary>One source: its name (what a log line says), its address and the
    /// file it is kept in, under the browser module's Filters folder.</summary>
    public sealed record Source(string Name, Uri Address, string FileName, bool Trusted = false);

    public const string ResourcesName = "adblock-resources";

    /// <summary>The filter lists, in the order they are read into the engine.</summary>
    public static IReadOnlyList<Source> Lists { get; } =
    [
        new("EasyList", new("https://easylist.to/easylist/easylist.txt"), "easylist.txt"),
        new("EasyPrivacy", new("https://easylist.to/easylist/easyprivacy.txt"), "easyprivacy.txt"),
        new("uBlock filters", new("https://ublockorigin.github.io/uAssets/filters/filters.txt"), "ublock-filters.txt", Trusted: true),
        new("uBlock quick fixes", new("https://ublockorigin.github.io/uAssets/filters/quick-fixes.txt"), "ublock-quick-fixes.txt", Trusted: true),
        new("uBlock privacy", new("https://ublockorigin.github.io/uAssets/filters/privacy.txt"), "ublock-privacy.txt", Trusted: true),
    ];

    /// <summary>Brave's scriptlets and redirect resources, the JSON the engine reads
    /// with <c>use_resources</c>.</summary>
    public static Source Resources { get; } =
        new(ResourcesName, new("https://raw.githubusercontent.com/brave/adblock-resources/master/dist/resources.json"), "resources.json");

    // ── uBlock Origin's scriptlets ───────────────────────────────────────────
    //
    // adblock-rust reads only uBO's old scriptlet format; current uBO keeps its
    // scriptlets as ES modules. They are fetched at one release tag and turned
    // into the engine's resources by the browser's own web engine (the views'
    // converter). A tag's files never change, so they are refreshed when the
    // tag below does, not by age.

    /// <summary>The uBlock Origin release the scriptlets are taken from.</summary>
    public const string UboTag = "1.77.4";

    /// <summary>The folder (under Filters) holding the modules, laid out as in uBO's
    /// <c>src</c> so that their relative imports resolve.</summary>
    public const string UboFolderName = "ubo-resources";

    /// <summary>The module the converter imports, relative to the folder.</summary>
    public const string UboEntry = "js/resources/scriptlets.js";

    /// <summary>The modules <c>scriptlets.js</c> reaches by its imports, transitively,
    /// relative to uBO's <c>src</c>.</summary>
    public static IReadOnlyList<string> UboFiles { get; } =
    [
        "js/resources/abort-current-script.js", "js/resources/attribute.js", "js/resources/base.js",
        "js/resources/cookie.js", "js/resources/create-html.js", "js/resources/href-sanitizer.js",
        "js/resources/json-edit.js", "js/resources/json-prune.js", "js/resources/localstorage.js",
        "js/resources/noeval.js", "js/resources/object-prune.js", "js/resources/parse-replace.js",
        "js/resources/prevent-addeventlistener.js", "js/resources/prevent-bab.js",
        "js/resources/prevent-clipboard-write.js", "js/resources/prevent-dialog.js",
        "js/resources/prevent-fetch.js", "js/resources/prevent-innerHTML.js",
        "js/resources/prevent-navigation.js", "js/resources/prevent-settimeout.js",
        "js/resources/prevent-xhr.js", "js/resources/proxy-apply.js", "js/resources/replace-argument.js",
        "js/resources/run-at.js", "js/resources/safe-self.js", "js/resources/scriptlets.js",
        "js/resources/set-constant.js", "js/resources/shared.js", "js/resources/spoof-css.js",
        "js/resources/stack-trace.js", "js/resources/utils.js", "js/resources/vod.js",
        "js/urlskip.js", "js/arglist-parser.js", "js/jsonpath.js",
    ];

    /// <summary>Where one module is fetched from, at the pinned tag.</summary>
    public static Uri UboAddress(string file, string tag = UboTag) =>
        new($"https://raw.githubusercontent.com/gorhill/uBlock/{tag}/src/{file}");

    /// <summary>The file, in Filters, that keeps the converted resources of one tag.</summary>
    public static string UboConvertedFileName(string tag = UboTag) => $"ubo-resources-{tag}.json";

    /// <summary>Every source, lists then resources.</summary>
    public static IEnumerable<Source> All => Lists.Append(Resources);

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(4);

    /// <summary>True when a file last written at <paramref name="lastWrite"/> is older
    /// than four days at <paramref name="now"/>. A write dated in the future is not stale.</summary>
    public static bool IsStale(DateTimeOffset lastWrite, DateTimeOffset now) =>
        now - lastWrite > MaxAge;
}
