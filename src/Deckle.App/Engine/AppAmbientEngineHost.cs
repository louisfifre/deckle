using Deckle.Lighting.Ambient;

namespace Deckle.App;

// No caching: a setting changed in the UI takes effect on the next read, with no
// event subscription.
internal sealed class AppAmbientEngineHost : IAmbientEngineHost
{
    public AmbientSettings Ambient => AmbientSettingsService.Instance.Current;
}
