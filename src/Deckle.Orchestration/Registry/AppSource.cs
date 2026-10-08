using System.Text.Json.Serialization;

namespace Deckle.Orchestration;

// Where an entry came from: added by hand (an executable or a running
// process), found in a Steam library, or found in a scanned folder.
[JsonConverter(typeof(JsonStringEnumConverter<AppSource>))]
public enum AppSource
{
    Manual,
    RunningProcess,
    Steam,
    Folder,
}
