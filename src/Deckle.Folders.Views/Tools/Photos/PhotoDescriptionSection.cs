using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Deckle.Core;

namespace Deckle.Folders.Views;

// The description in a photo's panel (spec, Photos; boards 12.4, 12.24, 12.5),
// the same in the photo tool and the file tool since both build the panel
// with PhotoDetails:
// - none yet: says so and offers "Describe this photo" (12.4);
// - under way: "Describing…" and "Stop" (12.24);
// - written: the text, that a model on this computer wrote it, and "Describe
//   again" (12.5);
// - failed or stopped (Open 33, no board): one plain sentence and the way to
//   try again, as Explorer says a step that did not happen and offers it
//   again; a failure of the setup (no model chosen, a server that does not
//   answer, a model that cannot read a picture, a provider not on this
//   machine) leads to the setting instead, the same failure before any
//   request when the setup already shows it.
//
// The section follows the photo while it is on screen: a description
// written by a folder run, or a stop, redraws it in place. It listens only
// while loaded, so a panel left behind holds nothing.
internal sealed class PhotoDescriptionSection
{
    private readonly PhotoDescriptions _descriptions;
    private readonly PhotoFile _photo;
    private readonly StackPanel _root = new() { Spacing = 8 };
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private int _showVersion;
    private bool _listening;

    public PhotoDescriptionSection(PhotoDescriptions descriptions, PhotoFile photo)
    {
        _descriptions = descriptions;
        _photo = photo;
        _root.Loaded += (_, _) =>
        {
            if (_listening) return;
            _listening = true;
            _descriptions.PhotoChanged += OnPhotoChanged;
            _ = ShowAsync();
        };
        _root.Unloaded += (_, _) =>
        {
            _listening = false;
            _descriptions.PhotoChanged -= OnPhotoChanged;
        };
        _ = ShowAsync();
    }

    public UIElement Element => _root;

    private void OnPhotoChanged(string path)
    {
        if (!FolderPaths.Comparer.Equals(path, _photo.Path)) return;
        _dispatcher.TryEnqueue(() => _ = ShowAsync());
    }

    private async Task ShowAsync()
    {
        int version = ++_showVersion;
        PhotoDescriptionState state = _descriptions.StateOf(_photo);

        // With nothing written nor asked, the setup is read first, so a panel
        // never offers a button that can only fail.
        PhotoDescriptionFailure? failure = state.Failure;
        if (state is { Description: null, InProgress: false, Failure: null })
            failure = await _descriptions.ReadinessAsync();
        if (version != _showVersion) return;

        _root.Children.Clear();
        if (state.InProgress)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            line.Children.Add(new ProgressRing { IsActive = true, Width = 16, Height = 16 });
            line.Children.Add(Caption(FoldersText.Get("Photos_Description_InProgress"), secondary: false));
            _root.Children.Add(line);
            _root.Children.Add(Buttons(Command("Photos_Description_Stop", () => _descriptions.Stop(_photo))));
        }
        else if (state.Description is { } description)
        {
            _root.Children.Add(new TextBlock
            {
                Text = description.Text,
                Style = Resource<Style>("BodyTextBlockStyle"),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            });
            _root.Children.Add(Caption(FoldersText.Get("Photos_Description_WrittenBy"), secondary: true));
            _root.Children.Add(Buttons(Command("Photos_Description_Again", Describe)));
        }
        else if (failure is { } f)
        {
            _root.Children.Add(Caption(PhotoDescriptionText.Failure(f), secondary: false));
            _root.Children.Add(Buttons(FailureCommands(f)));
        }
        else
        {
            _root.Children.Add(Caption(FoldersText.Get("Photos_Description_None"), secondary: true));
            _root.Children.Add(Buttons(Command("Photos_Description_DescribeOne", Describe)));
        }
    }

    // A failure of the setup leads to the setting; one that a second try may
    // mend (a server just started, a photo unlocked, a stop) offers it.
    private Button[] FailureCommands(PhotoDescriptionFailure failure)
    {
        var commands = new List<Button>();
        if (PhotoDescriptionText.CanRetry(failure))
            commands.Add(Command(failure == PhotoDescriptionFailure.Stopped
                ? "Photos_Description_DescribeOne" : "Photos_Description_Retry", Describe));
        if (PhotoDescriptionResult.IsSetupFailure(failure) && _descriptions.OpenSettings is { } open)
            commands.Add(Command("Photos_Description_Settings", open));
        return [.. commands];
    }

    private void Describe() => _ = _descriptions.DescribeAsync(_photo);

    private static StackPanel Buttons(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (Button button in buttons) row.Children.Add(button);
        return row;
    }

    private static Button Command(string key, Action click)
    {
        string text = FoldersText.Get(key);
        var button = new Button { Content = text };
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => click();
        return button;
    }

    private static TextBlock Caption(string text, bool secondary)
    {
        var block = new TextBlock
        {
            Text = text,
            Style = Resource<Style>("CaptionTextBlockStyle"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (secondary) block.Foreground = Resource<Brush>("TextFillColorSecondaryBrush");
        return block;
    }

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}

// The sentences of a description that did not happen, shared by the panel
// and the folder command so both say the same thing for the same cause.
internal static class PhotoDescriptionText
{
    public static string Failure(PhotoDescriptionFailure failure) =>
        FoldersText.Get("Photos_DescriptionFailure_" + failure);

    // No model chosen, a provider not on this machine, a model that cannot
    // read a picture: trying again changes nothing until the setting does.
    public static bool CanRetry(PhotoDescriptionFailure failure) => failure is not (
        PhotoDescriptionFailure.NoModelChosen or
        PhotoDescriptionFailure.EndpointNotLocal or
        PhotoDescriptionFailure.ImagesNotSupported);
}
