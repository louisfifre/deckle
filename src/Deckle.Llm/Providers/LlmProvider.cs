namespace Deckle.Llm;

// ─── Provider ────────────────────────────────────────────────────────────────
//
// A named place a model is served from: a protocol, an address and, when the
// place wants one, a key in the secret vault. A consumer's profile names a
// provider by Id and a model by name; the provider list is what the user
// edits in Settings. The key never sits here: LlmProviderSecrets derives its
// vault name from the Id, so this object can be serialized to settings.json
// without carrying a secret.
public sealed class LlmProvider
{
    // Stable across renames: profiles reference it, never the Name.
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public LlmProviderKind Kind { get; set; } = LlmProviderKind.OpenAiCompatible;

    // Ollama: the /api/generate URL (or the server root). OpenAiCompatible:
    // the server root or its /v1 base. Claude: empty, the SDK knows the host.
    public string Endpoint { get; set; } = "";

    // A disabled provider stays in the list but serves no request, so a
    // profile pointing at it fails visibly instead of silently switching.
    public bool Enabled { get; set; } = true;
}
