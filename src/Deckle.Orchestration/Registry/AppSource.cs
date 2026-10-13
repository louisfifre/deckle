using System.Text.Json.Serialization;

namespace Deckle.Orchestration;

[JsonConverter(typeof(JsonStringEnumConverter<AppSource>))]
public enum AppSource
{
    Manual,
    RunningProcess,
    Steam,
    Folder,
}
