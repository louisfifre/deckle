using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deckle.Browser.Adblock;

/// <summary>What a page needs besides the blocked requests: the CSS selectors to hide and
/// the script of the scriptlets to run in it.</summary>
public sealed class CosmeticResources
{
    public static CosmeticResources Empty { get; } = new();

    [JsonPropertyName("hide_selectors")]
    public IReadOnlyList<string> HideSelectors { get; init; } = [];

    [JsonPropertyName("injected_script")]
    public string InjectedScript { get; init; } = "";

    [JsonPropertyName("generichide")]
    public bool GenericHide { get; init; }
}

/// <summary>
/// A filter engine built from the text of filter lists. The native engine is not Sync (its
/// resource storage is a boxed trait object), so every call takes one lock: reads queue, which
/// costs microseconds a request. Disposing while another thread calls is safe: the call either
/// finishes first or finds the engine gone and answers "do not block".
/// </summary>
public sealed class AdblockEngine : IDisposable
{
    private readonly object _gate = new();
    private nint _engine;

    private AdblockEngine(nint engine) => _engine = engine;

    /// <summary>Builds an engine from the concatenated text of filter lists.</summary>
    public static unsafe AdblockEngine Create(string rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        byte[] bytes = Encoding.UTF8.GetBytes(rules);
        nint engine;
        fixed (byte* p = bytes) engine = NativeAdblock.EngineNew(p, (nuint)bytes.Length);
        if (engine == 0) throw new InvalidOperationException("The ad blocking engine could not be built from the rules.");
        return new AdblockEngine(engine);
    }

    /// <summary>Loads the scriptlets and redirect resources (the crate's Resource JSON array).
    /// False when the JSON is not that.</summary>
    public unsafe bool UseResources(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        lock (_gate)
        {
            if (_engine == 0) return false;
            fixed (byte* p = bytes) return NativeAdblock.UseResources(_engine, p, (nuint)bytes.Length) == 1;
        }
    }

    /// <summary>True when the request should be stopped. <paramref name="requestType"/> is one of
    /// the blocker's types (see Deckle.Browser.RequestKind). An address that cannot be read is not blocked.</summary>
    public unsafe bool ShouldBlock(string url, string sourceUrl, string requestType)
    {
        byte[] u = Encoding.UTF8.GetBytes(url ?? "");
        byte[] s = Encoding.UTF8.GetBytes(sourceUrl ?? "");
        byte[] t = Encoding.UTF8.GetBytes(requestType ?? "");
        lock (_gate)
        {
            if (_engine == 0) return false;
            fixed (byte* up = u) fixed (byte* sp = s) fixed (byte* tp = t)
                return NativeAdblock.ShouldBlock(_engine, up, (nuint)u.Length, sp, (nuint)s.Length, tp, (nuint)t.Length) == 1;
        }
    }

    /// <summary>The selectors to hide and the scriptlet script for a page.</summary>
    public unsafe CosmeticResources Cosmetic(string url)
    {
        byte[] u = Encoding.UTF8.GetBytes(url ?? "");
        string json;
        lock (_gate)
        {
            if (_engine == 0) return CosmeticResources.Empty;
            nuint length = 0;
            byte* result;
            fixed (byte* up = u) result = NativeAdblock.Cosmetic(_engine, up, (nuint)u.Length, &length);
            if (result == null) return CosmeticResources.Empty;
            try { json = Encoding.UTF8.GetString(result, (int)length); }
            finally { NativeAdblock.StringFree(result, length); }
        }
        try { return JsonSerializer.Deserialize<CosmeticResources>(json) ?? CosmeticResources.Empty; }
        catch (JsonException) { return CosmeticResources.Empty; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_engine == 0) return;
            NativeAdblock.EngineFree(_engine);
            _engine = 0;
        }
    }
}
