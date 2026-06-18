using System;
using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Deckle.Catalog;

// ─── Loc ──────────────────────────────────────────────────────────
//
// Single entry point for code-behind string lookups. XAML uses
// `x:Uid="MyKey"` to bind to entries in Strings/en-US/Resources.resw,
// resolved automatically by the WinUI 3 framework. C# code that
// builds UI programmatically (ConsentDialogs, engine status,
// UserFeedback, HUD, tray, setup wizard pages) goes through Loc.
//
// Key naming conventions live in src/Deckle.Catalog/CLAUDE.md. Summary:
//
//   x:Uid in XAML       UidValue.Property        e.g. "LogWindowSearchBox.PlaceholderText"
//   C# direct lookup    Surface_Purpose          e.g. "CorpusConsent_Title"
//   C# parameterized    Surface_Purpose_Format   e.g. "Status_Rewriting_Format"
//   Common reusable     Common_Purpose           e.g. "Common_Cancel"
//
// Technical strings (file names, URLs, product names like "Ollama" /
// "Silero VAD" / "Deckle") stay hardcoded — never go through Loc.

public static class Loc
{
    // Lazy-initialized so the ResourceLoader is built on first use, after
    // the Windows App SDK runtime is bootstrapped in App.OnLaunched.
    // Default constructor binds to the "Resources" map (single monolithic
    // .resw under Strings/<lang>/Resources.resw).
    private static readonly Lazy<ResourceLoader> _loader =
        new(static () => new ResourceLoader());

    // One loader per class-library resource subtree, created on demand. Code in a
    // module resolves ITS OWN .resw through GetFrom; XAML in the module uses
    // x:Uid (component context) instead. The default _loader above only sees the
    // host app's root map, which a module's x:Uid strings are not part of.
    private static readonly ConcurrentDictionary<string, ResourceLoader> _libLoaders = new();

    /// <summary>
    /// Returns the localized string for <paramref name="key"/>. In
    /// DEBUG builds, missing keys surface as a bracket marker so empty
    /// TextBlocks and button labels can't ship silently. In RELEASE,
    /// the underlying contract (empty string on miss) is preserved.
    /// </summary>
    public static string Get(string key)
    {
        string? s;
        try
        {
            s = _loader.Value.GetString(key);
        }
        catch (Exception)
        {
            // Unpackaged MRT throws ("NamedResource Not Found") instead of
            // returning empty when the key is absent from the root map — the
            // typical cause is a code-style key left in a module's own .resw
            // without its Deckle.App mirror. Fold the throw into the documented
            // miss contract so the caller sees the marker, not a crash.
            s = null;
        }
#if DEBUG
        if (string.IsNullOrEmpty(s))
        {
            return "[!" + key + "]";
        }
#endif
        return s ?? string.Empty;
    }

    /// <summary>
    /// Returns the format-string entry for <paramref name="key"/> with
    /// <paramref name="args"/> substituted using
    /// <see cref="CultureInfo.CurrentCulture"/>. The .resw entry is
    /// expected to use composite-format placeholders ({0}, {1}, ...).
    /// </summary>
    public static string Format(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>
    /// Returns the localized string for <paramref name="key"/> from a referenced
    /// class library's resources, addressed by the library's PRI subtree
    /// ("<paramref name="library"/>/Resources"). The default <see cref="Get"/>
    /// only sees the host app's root resource map; a library's own .resw — the
    /// x:Uid strings of its pages — lives under its own subtree, so code that
    /// builds a module's UI programmatically (the settings composer) must target
    /// it explicitly. Same miss contract as <see cref="Get"/> (a DEBUG marker).
    /// </summary>
    public static string GetFrom(string library, string key)
    {
        string? s = TryGetFrom(library, key);
#if DEBUG
        if (string.IsNullOrEmpty(s))
        {
            return "[!" + library + "/" + key + "]";
        }
#endif
        return s ?? string.Empty;
    }

    /// <summary>
    /// Miss-tolerant variant of <see cref="GetFrom"/>: returns null on absence,
    /// with no DEBUG marker — for optional strings such as a setting card's
    /// description, which may legitimately not exist.
    /// </summary>
    public static string? GetFromOptional(string library, string key)
    {
        string? s = TryGetFrom(library, key);
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static string? TryGetFrom(string library, string key)
    {
        ResourceLoader loader = _libLoaders.GetOrAdd(library, static lib =>
            new ResourceLoader(ResourceLoader.GetDefaultResourceFilePath(), lib + "/Resources"));
        try
        {
            return loader.GetString(key);
        }
        catch
        {
            // Unpackaged MRT throws on a missing key instead of returning empty;
            // fold it into the miss contract — same reason as Get().
            return null;
        }
    }
}
