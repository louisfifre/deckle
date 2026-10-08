using System.Text.Json.Serialization;

namespace Deckle.Orchestration;

// What the user says an entry is. Written as a name in settings.json so the
// file stays readable and a reordered enum never changes a stored value.
[JsonConverter(typeof(JsonStringEnumConverter<AppKind>))]
public enum AppKind
{
    App,
    Game,
}
