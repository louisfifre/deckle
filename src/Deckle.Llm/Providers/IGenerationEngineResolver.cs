namespace Deckle.Llm;

// What a consumer holds instead of an engine: the provider list changes at
// runtime (Settings edits, a key saved), so the engine is looked up per
// request by provider Id.
public interface IGenerationEngineResolver
{
    /// <summary>Returns null when no enabled provider carries
    /// <paramref name="providerId"/>; the caller owns the human-facing
    /// outcome.</summary>
    IGenerationEngine? Resolve(string providerId);
}
