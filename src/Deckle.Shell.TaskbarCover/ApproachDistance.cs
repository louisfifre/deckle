using System.Text.Json.Serialization;

namespace Deckle.Shell.TaskbarCover;

// How far from the taskbar the pointer starts to reveal it, by name. The
// distance each level stands for lives in CoverGeometry.ApproachSpanDip, in
// device-independent pixels. Written as a name in settings.json so the file
// stays readable and a reordered enum never changes a stored value.
[JsonConverter(typeof(JsonStringEnumConverter<ApproachDistance>))]
public enum ApproachDistance
{
    Near,
    Standard,
    Far,
}
