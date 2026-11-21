namespace Deckle.Contributions;

// What a contribution may ask of its host while it composes its hooks, kept
// narrow on purpose: a module reaches the rest of the application through
// these four doors, never through the host's types.
public interface IContributionContext
{
    // The module's own data folder under the data root (modules/<id>/).
    string ModuleDirectory(string moduleId);

    // Whether the person chose to have the module installed.
    bool IsModulePresent(string moduleId);

    // Whether Developer mode is on at this moment.
    bool DeveloperModeEnabled { get; }

    // Opens a page of the Settings window; the page tag is the one the
    // page's module publishes.
    void OpenSettingsPage(string pageTag);
}
