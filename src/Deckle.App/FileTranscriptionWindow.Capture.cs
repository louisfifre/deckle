namespace Deckle.App;

// The states the self-check photographs (App.SelfCheck.cs), set without a file
// or an engine: each step of the assistant, the Result step showing every
// state a file can be in.
public sealed partial class FileTranscriptionWindow
{
    internal static readonly string[] CaptureStates = ["files", "rewrite", "result"];

    private static readonly (string Name, string Duration, FileTranscriptionState State, bool RewriteFailed)[] SampleFiles =
    [
        ("Team meeting.m4a", "48:12", FileTranscriptionState.Done, false),
        ("Interview, part 1.mp3", "1:12:40", FileTranscriptionState.Done, true),
        ("Interview, part 2.mp3", "1:03:05", FileTranscriptionState.Rewriting, false),
        ("Voice memo.wav", "2:31", FileTranscriptionState.Waiting, false),
        ("Corrupted.ogg", "", FileTranscriptionState.TranscriptionFailed, false),
    ];

    internal void ShowStateForCapture(string state)
    {
        _items.Clear();
        _finished = false;
        string folder = Path.Combine(Path.GetTempPath(), "Recordings");
        foreach (var sample in SampleFiles)
        {
            var item = new FileTranscriptionItem(Path.Combine(folder, sample.Name)) { DurationText = sample.Duration };
            if (state == "result")
            {
                item.RewriteFailed = sample.RewriteFailed;
                if (sample.State == FileTranscriptionState.Done)
                    item.OutputPath = Path.ChangeExtension(item.Path, ".txt");
                item.State = sample.State;
            }
            _items.Add(item);
        }

        switch (state)
        {
            case "files":
                ShowStep(FilesStep);
                break;
            case "rewrite":
                RewriteSwitch.IsOn = true;
                ShowStep(RewriteStep);
                break;
            case "result":
                ShowStep(ResultStep);
                break;
        }
    }
}
