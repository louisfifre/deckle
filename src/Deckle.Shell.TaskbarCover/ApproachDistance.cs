using System.Text.Json.Serialization;

namespace Deckle.Shell.TaskbarCover;

// How far from the taskbar the pointer starts to reveal it, by name. The
// depth each level stands for lives in CoverGeometry.ZoneFactor, in band
// thicknesses. Written as a name in settings.json so the file stays readable
// and a reordered enum never changes a stored value.
[JsonConverter(typeof(JsonStringEnumConverter<ApproachDistance>))]
public enum ApproachDistance
{
    Near,
    Standard,
    Far,
}
