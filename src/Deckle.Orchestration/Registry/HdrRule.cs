using System.Text.Json.Serialization;

namespace Deckle.Orchestration;

[JsonConverter(typeof(JsonStringEnumConverter<HdrRule>))]
public enum HdrRule
{
    Leave,
    On,
    Off,
}
