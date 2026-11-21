using System.Diagnostics;
using Deckle.FolderMap;

namespace Deckle.Projects;

// The estimate behind "An agent always reads": the tokens an agent loads at
// the start of every session in the project.
public sealed record AgentEstimate(int Tokens, bool IsTruncated);

// Reuses Deckle Folders' map rather than counting a second time: the
// inventory finds AGENTS.md, CLAUDE.md, the skills and the subagents, and its
// EstimatedStartupTokens is what the spec describes ("AGENTS.md, CLAUDE.md and
// the skills' descriptions, which an agent loads when a session starts"). It
// is not EstimatedAlwaysReadTokens, which leaves the skills' descriptions out.
//
// Read once per opening of a project's page and never for Home: the inventory
// walks the whole folder, which is slow on a huge one. It runs on a pool
// thread and stops when the token is cancelled (the person left the page).
public static class AgentReading
{
    // Null when the folder could not be read (gone, refused). A folder with
    // nothing an agent reads gives 0, not null. A folder over the map's entry
    // limit gives what the map saw, flagged: the estimate is then a floor.
    public static Task<AgentEstimate?> EstimateAsync(string projectPath, CancellationToken cancellation) =>
        Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            MapInventory inventory = MapInventory.Build(projectPath, cancellation);
            if (!inventory.Succeeded) return (AgentEstimate?)null;

            int tokens = inventory.EstimatedStartupTokens;
            DeckleProjectsSource.Log.AgentReadingDone(tokens, inventory.IsTruncated, clock.ElapsedMilliseconds);
            return new AgentEstimate(tokens, inventory.IsTruncated);
        }, cancellation);
}
