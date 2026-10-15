namespace Deckle.Llm;

// The vault name of a provider's API key, derived from its Id so the settings
// file and the vault never have to agree on a second identifier. Deleting a
// provider should remove its secret too; LlmProvider itself never sees it.
public static class LlmProviderSecrets
{
    public static string ApiKeyName(string providerId) => $"llm-provider-{providerId}-api-key";
}
