namespace Deckle.Orchestration;

// Module settings: the registry of apps and games, and the folders the user
// asked to scan for more. Plain POCO; OrchestrationSettingsService owns every
// mutation and swaps these lists by reference.
public sealed class OrchestrationSettings
{
    public List<AppEntry> Entries { get; set; } = new();

    public List<string> ScanFolders { get; set; } = new();
}
