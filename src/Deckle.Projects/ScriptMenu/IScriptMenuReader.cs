namespace Deckle.Projects;

// What reads a project's script menu, so the cache and its tests do not start a
// PowerShell.
public interface IScriptMenuReader
{
    Task<ScriptMenuListing> ListAsync(string projectPath, CancellationToken cancellation);
}
