using System.Text.Json.Serialization;

namespace Deckle.Orchestration;

// What to do with one monitor's HDR while the app runs. Leave = do not touch.
[JsonConverter(typeof(JsonStringEnumConverter<HdrRule>))]
public enum HdrRule
{
    Leave,
    On,
    Off,
}
