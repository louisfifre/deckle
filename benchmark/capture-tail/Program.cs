// CaptureTail: measures, on the dictation corpus, whether the 400 ms end-of-capture
// tail can go down or must go up.
//
//   CaptureTail energy <telemetry-dir> <since yyyy-MM-dd>
//   CaptureTail replay <telemetry-dir> <since yyyy-MM-dd> [<max takes>]
//
// <telemetry-dir> is the folder holding `audio\` and `corpus\` (the corpus root of
// the data folder). Takes are the raw captures written since <since> (the tail
// exists since 2026-10-21): "raw" comes from each corpus line's audio_content
// field, read in memory by RawTakeCatalog, never from a guess on the audio.
//
// Privacy, strict: the corpus is personal data. This tool prints only aggregates
// (counts, shares, milliseconds, percentiles), each line prefixed "RESULT ", and
// never a file name, a transcript or a corpus line. Run it with stdout sent to a
// file and read only the RESULT lines. Any change must keep that property.

using System.Globalization;
using Deckle.Benchmark.CaptureTail;

if (args.Length < 3 || args[0] is not ("energy" or "replay"))
{
    Console.Error.WriteLine("usage: CaptureTail energy|replay <telemetry-dir> <since yyyy-MM-dd> [<max takes>]");
    return 2;
}

string telemetryDir = args[1];
var since = DateTime.Parse(args[2], CultureInfo.InvariantCulture);
int maxTakes = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : int.MaxValue;

var catalog = RawTakeCatalog.Load(Path.Combine(telemetryDir, "corpus"));
string audioDir = Path.Combine(telemetryDir, "audio");
List<string> wavs = Directory.Exists(audioDir)
    ? new DirectoryInfo(audioDir).GetFiles("*.wav")
        .Where(f => f.LastWriteTime >= since && catalog.RawFiles.Contains(f.Name))
        .OrderBy(f => f.LastWriteTime)
        .Select(f => f.FullName)
        .ToList()
    : [];

Stats.Result($"corpus lines by audio content: raw {catalog.RawFiles.Count}, processed {catalog.Processed}, unlabeled {catalog.Unlabeled}");
Stats.Result($"raw takes with audio since {args[2]}: {wavs.Count}");
if (wavs.Count == 0) return 0;

if (args[0] == "energy")
    EnergyProfile.Run(wavs);
else
    await TailReplay.RunAsync(wavs, maxTakes);
return 0;
