using System.Collections.ObjectModel;
using System.Diagnostics;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Localization;
using Deckle.Shell;
using Deckle.Shell.WindowChrome;
using Deckle.Transcription;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using WinRT.Interop;

namespace Deckle.App;

// The file assistant (specs/deckle-transcribe-and-rewrite.md, File assistant):
// audio files in, one .txt beside each out, rewritten or not, in three steps.
// It takes one file as well as many. The App hands in the engines as callbacks,
// as it does for the Transcribe and rewrite window; FileTranscriptionRun does
// the work, the window only shows it.
public sealed partial class FileTranscriptionWindow : Window
{
    private const int FilesStep = 0, RewriteStep = 1, ResultStep = 2;

    // The formats the engine's decoder reads.
    private static readonly string[] AudioExtensions =
        [".mp3", ".m4a", ".aac", ".wav", ".flac", ".wma", ".ogg", ".opus"];

    private readonly IntPtr _hwnd;
    private readonly ObservableCollection<FileTranscriptionItem> _items = [];
    private int _step;
    private bool _running;
    private bool _finished;

    public Func<string, Task<string?>>? TranscribeFile { get; set; }

    // Blocking, as RewriteService.Rewrite is: the run calls it off the UI thread.
    public Func<string, RewriteProfile, GenerationResult>? Rewrite { get; set; }

    public Action? OnShowRewriteSettingsRequested { get; set; }

    public FileTranscriptionWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        string? icon = IconAssets.ResolveImagePath(recording: false);
        if (icon is not null)
            AppTitleBarIcon.ImageSource = new BitmapImage(new Uri(icon));
        IconAssets.ApplyToWindow(AppWindow);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // The bar holds only the title: standard caption buttons (deckle-interface).
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        CaptionInsetCorrection.Attach(AppTitleBar, AppWindow);
        SystemBackdrop = new MicaBackdrop();
        Title = Loc.Get("FileTranscription_WindowTitle");

        double dpiScale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(_hwnd));
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min((int)(680 * dpiScale), workArea.Width),
            Math.Min((int)(480 * dpiScale), workArea.Height)));
        var presenter = OverlappedPresenter.Create();
        presenter.PreferredMinimumWidth  = (int)(560 * dpiScale);
        presenter.PreferredMinimumHeight = (int)(360 * dpiScale);
        AppWindow.SetPresenter(presenter);

        // A run outlives a close: the window hides and the files keep coming,
        // so the tray entry brings it back with their progress.
        AppWindow.Closing += (_, args) =>
        {
            if (!_running) return;
            args.Cancel = true;
            AppWindow.Hide();
        };

        StepList.ItemsSource = new[]
        {
            Loc.Format("FileTranscription_StepFormat", 1, Loc.Get("FileTranscription_StepFiles")),
            Loc.Format("FileTranscription_StepFormat", 2, Loc.Get("FileTranscription_StepRewrite")),
            Loc.Format("FileTranscription_StepFormat", 3, Loc.Get("FileTranscription_StepResult")),
        };
        FileList.ItemsSource = _items;
        ResultList.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => UpdateCommands();
        LoadProfiles();
        LlmSettingsService.Instance.Changed += OnLlmSettingsChanged;
        Closed += (_, _) => LlmSettingsService.Instance.Changed -= OnLlmSettingsChanged;

        ShowStep(FilesStep);
    }

    public bool IsRunning => _running;

    public void ShowAndActivate()
    {
        if (AppWindow.Presenter is OverlappedPresenter op &&
            op.State == OverlappedPresenterState.Minimized)
        {
            op.Restore();
        }

        AppWindow.Show();
        Activate();
        // Activate() does not always bring the window to front from a tray
        // callback. SetForegroundWindow is allowed: the tray host is same-process.
        NativeMethods.SetForegroundWindow(_hwnd);
        WindowingProbe.EmitWindowPositioned(_hwnd, "file-transcription", "Center");
    }

    // What a file's line says under its name on the Result step.
    public static string StatusOf(FileTranscriptionState state, bool rewriteFailed, string? outputPath) => state switch
    {
        FileTranscriptionState.Waiting => Loc.Get("FileTranscription_StateWaiting"),
        FileTranscriptionState.Transcribing => Loc.Get("FileTranscription_StateTranscribing"),
        FileTranscriptionState.Rewriting => Loc.Get("FileTranscription_StateRewriting"),
        FileTranscriptionState.Done => Loc.Format(
            rewriteFailed ? "FileTranscription_StateSavedNotRewritten" : "FileTranscription_StateSaved",
            // The name alone: the .txt sits beside its audio, and Open folder shows where.
            Path.GetFileName(outputPath ?? "")),
        FileTranscriptionState.TranscriptionFailed => Loc.Get("FileTranscription_StateTranscriptionFailed"),
        FileTranscriptionState.WriteFailed => Loc.Get("FileTranscription_StateWriteFailed"),
        _ => "",
    };

    private async void OnAddFilesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
            };
            foreach (string extension in AudioExtensions)
                picker.FileTypeFilter.Add(extension);
            IReadOnlyList<PickFileResult> picked = await picker.PickMultipleFilesAsync();
            AddFiles(picked.Select(p => p.Path));
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.WindowOperationFailed(
                "file-transcription", "add-files", "exception", ex.GetType().Name, ex.Message);
        }
    }

    // A file already in the list is not added twice.
    internal void AddFiles(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (_items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            var item = new FileTranscriptionItem(path);
            _items.Add(item);
            _ = ReadDurationAsync(item);
        }
    }

    // The duration Windows' property system reads for the file; nothing shown
    // for a format it does not describe.
    private static async Task ReadDurationAsync(FileTranscriptionItem item)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(item.Path);
            var music = await file.Properties.GetMusicPropertiesAsync();
            TimeSpan d = music.Duration;
            if (d > TimeSpan.Zero)
                item.DurationText = d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss") : d.ToString(@"m\:ss");
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.WindowOperationFailed(
                "file-transcription", "read-duration", "exception", ex.GetType().Name, ex.Message);
        }
    }

    private void OnRemoveFileClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FileTranscriptionItem item)
            _items.Remove(item);
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FileTranscriptionItem { OutputPath: { } path }) return;
        try
        {
            // Explorer opens the folder with the .txt selected.
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.WindowOperationFailed(
                "file-transcription", "open-folder", "exception", ex.GetType().Name, ex.Message);
        }
    }

    private void OnRewriteToggled(object sender, RoutedEventArgs e)
    {
        ProfileBox.Visibility = RewriteSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;
        Notice.IsOpen = false;
        UpdateCommands();
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_step > FilesStep && !_running && !_finished) ShowStep(_step - 1);
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case FilesStep:
                ShowStep(RewriteStep);
                break;
            case RewriteStep:
                StartRun();
                break;
            case ResultStep:
                Close();
                break;
        }
    }

    private async void StartRun()
    {
        try
        {
            if (TranscribeFile is not { } transcribe) return;

            Func<string, string>? rewrite = null;
            if (RewriteSwitch.IsOn)
            {
                if (Rewrite is not { } rewriteWith || ProfileBox.SelectedItem is not RewriteProfile profile) return;
                // A profile with no provider would only come back empty: say
                // what is missing and where to set it, before anything starts.
                if (string.IsNullOrWhiteSpace(profile.ProviderId) || string.IsNullOrWhiteSpace(profile.Model))
                {
                    ShowNoProvider();
                    return;
                }
                s_lastProfileId = profile.Id;
                rewrite = text => rewriteWith(text, profile).Text ?? "";
            }

            var run = new FileTranscriptionRun(transcribe, rewrite, TranscriptFileWriter.Write);
            _running = true;
            ShowStep(ResultStep);
            await run.RunAsync([.. _items]);
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.WindowOperationFailed(
                "file-transcription", "run", "exception", ex.GetType().Name, ex.Message);
        }
        finally
        {
            if (_running)
            {
                _running = false;
                _finished = true;
                UpdateCommands();
            }
        }
    }

    private void ShowNoProvider()
    {
        Notice.Severity = InfoBarSeverity.Warning;
        Notice.Title = Loc.Get("TranscribeRewrite_NoProvider_Title");
        Notice.Message = Loc.Get("TranscribeRewrite_NoProvider_Body");
        var button = new Button { Content = Loc.Get("TranscribeRewrite_OpenRewriteSettings") };
        button.Click += (_, _) => OnShowRewriteSettingsRequested?.Invoke();
        Notice.ActionButton = button;
        Notice.IsOpen = true;
    }

    private void ShowStep(int step)
    {
        _step = step;
        StepList.SelectedIndex = step;
        FilesPanel.Visibility = step == FilesStep ? Visibility.Visible : Visibility.Collapsed;
        RewritePanel.Visibility = step == RewriteStep ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = step == ResultStep ? Visibility.Visible : Visibility.Collapsed;
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        FilesEmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Content = Loc.Get("FileTranscription_Back");
        BackButton.IsEnabled = _step > FilesStep && !_running && !_finished;
        BackButton.Visibility = _step == ResultStep ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = Loc.Get(_step switch
        {
            RewriteStep => "FileTranscription_Start",
            ResultStep => "FileTranscription_Close",
            _ => "FileTranscription_Next",
        });
        NextButton.IsEnabled = _step switch
        {
            FilesStep => _items.Count > 0,
            RewriteStep => !RewriteSwitch.IsOn || ProfileBox.SelectedItem is RewriteProfile,
            _ => !_running,
        };
    }

    // The profiles of the Rewriting page, by name, as the Transcribe and
    // rewrite window lists them.
    private void LoadProfiles()
    {
        string? selectedId = (ProfileBox.SelectedItem as RewriteProfile)?.Id;
        List<RewriteProfile> profiles = LlmSettingsService.Instance.Current.Profiles.ToList();
        ProfileBox.ItemsSource = profiles;
        ProfileBox.SelectedItem =
            profiles.FirstOrDefault(p => p.Id == (selectedId ?? s_lastProfileId))
            ?? profiles.FirstOrDefault();
    }

    // The last profile used, for the next opening of the assistant in this run.
    private static string? s_lastProfileId;

    private void OnLlmSettingsChanged()
        => DispatcherQueue.TryEnqueue(LoadProfiles);
}
