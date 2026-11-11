using System.Globalization;
using System.Text.RegularExpressions;

namespace Deckle.Atelier;

// The name a renamed photo takes: "A renamed photo has a name that starts with
// the date it was taken, year then month then day, so that names sort by time"
// (spec, Photos).
//
//     DSC_0217.NEF taken 14 June 2026 at 15:42:07  ->  2026-06-14_154207_DSC_0217.NEF
//
// Settled here, since the spec leaves them open:
// - The time follows the date. The spec's reason is that names sort by time;
//   the date alone sorts days but not the shots inside a day, and two cameras
//   (a camera and a phone) number their files apart, so their shots of one day
//   would not interleave. Seconds, because bursts take several per minute.
// - The original name follows the date (Open question 5, "the original name
//   kept, or a new one"): it is the least destructive choice, it keeps the
//   camera's counter, which breaks ties within a second, and it keeps a raw and
//   its JPEG paired by name.
// - A name that already starts with a date in this shape is re-dated, never
//   prefixed twice, so renaming the same folder again changes nothing.
// - A collision takes "-2", "-3"… before the extension.
public static partial class PhotoRenameRule
{
    public const string DateFormat = "yyyy-MM-dd_HHmmss";

    public static string NameFor(string fileName, DateTime taken) =>
        taken.ToString(DateFormat, CultureInfo.InvariantCulture) + "_" + WithoutDatePrefix(fileName);

    public static bool HasDatePrefix(string fileName) => DatePrefix().IsMatch(fileName);

    // "2026-06-14_154207_DSC_0217.NEF" -> "DSC_0217.NEF".
    public static string WithoutDatePrefix(string fileName)
    {
        Match m = DatePrefix().Match(fileName);
        return m.Success ? fileName[m.Length..] : fileName;
    }

    // "name.NEF", 2 -> "name-2.NEF".
    public static string WithCounter(string fileName, int counter) =>
        Path.GetFileNameWithoutExtension(fileName) + "-" + counter.ToString(CultureInfo.InvariantCulture) + Path.GetExtension(fileName);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}_\d{6}_")]
    private static partial Regex DatePrefix();
}
