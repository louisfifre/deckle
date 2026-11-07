using System.Text.Encodings.Web;
using System.Text.Json;

namespace Deckle.Vision;

// ── InterfaceCaptureIndex ─────────────────────────────────────────────────────
//
// The index.json beside the images of one interface capture: what each file
// shows, so a reader that never saw the screen knows which window and which
// page it looks at, and at what size. A capture that failed is listed too,
// with the step that failed, so a missing file reads as a failure and not as
// a window that was never open.
//
//   {
//     "capturedAt": "2026-11-07T22:10:03+01:00",
//     "technique": "Windows.Graphics.Capture",
//     "captures": [
//       { "file": "settings-recording.png", "window": "settings",
//         "page": "Recording", "title": "Deckle Settings",
//         "width": 1920, "height": 2880 }
//     ],
//     "failures": [ { "name": "hud", "step": "first_frame", "error": "TimeoutException" } ]
//   }
//
// Width and height are physical pixels, the size of the image. Written by
// hand with Utf8JsonWriter: the shape is the contract, and a writer keeps it
// visible here rather than in serializer attributes.
public sealed class InterfaceCaptureIndex
{
    public const string Technique = "Windows.Graphics.Capture";

    private readonly List<InterfaceCaptureEntry> _captures = [];
    private readonly List<InterfaceCaptureFailure> _failures = [];

    public InterfaceCaptureIndex(DateTimeOffset capturedAt) => CapturedAt = capturedAt;

    public DateTimeOffset CapturedAt { get; }
    public IReadOnlyList<InterfaceCaptureEntry> Captures => _captures;
    public IReadOnlyList<InterfaceCaptureFailure> Failures => _failures;

    public void Add(InterfaceCaptureEntry entry) => _captures.Add(entry);
    public void Add(InterfaceCaptureFailure failure) => _failures.Add(failure);

    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,
            // Window titles and page labels are French as often as not: keep
            // their letters readable instead of \u escapes.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            json.WriteStartObject();
            json.WriteString("capturedAt", CapturedAt);
            json.WriteString("technique", Technique);

            json.WriteStartArray("captures");
            foreach (InterfaceCaptureEntry entry in _captures)
            {
                json.WriteStartObject();
                json.WriteString("file", entry.File);
                json.WriteString("window", entry.Window);
                if (entry.Page is not null) json.WriteString("page", entry.Page);
                json.WriteString("title", entry.Title);
                json.WriteNumber("width", entry.Width);
                json.WriteNumber("height", entry.Height);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("failures");
            foreach (InterfaceCaptureFailure failure in _failures)
            {
                json.WriteStartObject();
                json.WriteString("name", failure.Name);
                json.WriteString("step", failure.Step);
                json.WriteString("error", failure.Error);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}

// One image. Page is the rail label of a settings page, null for a window
// captured whole.
public sealed record InterfaceCaptureEntry(
    string File, string Window, string? Page, string Title, int Width, int Height);

// One capture that produced no image: the file name it would have had, the
// step that failed and the exception type.
public sealed record InterfaceCaptureFailure(string Name, string Step, string Error);
