using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Deckle.Projects.Views;

internal sealed partial class NoScriptMenuNotice : UserControl
{
    // How long the link says "Copied" before it goes back to its words.
    private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(2);

    private DispatcherQueueTimer? _timer;

    public NoScriptMenuNotice()
    {
        InitializeComponent();
        SentenceText.Text = ProjectsText.Get("Pinned_NoMenu");
        CopyIcon.Glyph = ProjectsGlyphs.Copy;
        CopyLabel.Text = ProjectsText.Get("Pinned_CopyInstall");
        Unloaded += (_, _) => _timer?.Stop();
    }

    // The link, for the self-check to find.
    public HyperlinkButton Link => CopyLink;

    public string LinkText => CopyLabel.Text;

    // Puts the install line of the scripts on the clipboard and says so for a
    // moment (the spec says "copies" and nothing more; the confirmation is this
    // build's choice, so a click that seems to do nothing is not repeated).
    public void Copy()
    {
        try
        {
            var package = new DataPackage();
            package.SetText(DeckleScriptsInstall.Command);
            Clipboard.SetContent(package);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // Another program holds the clipboard: the link stays as it was and
            // the person can press it again.
            DeckleProjectsSource.Log.ProjectsPageFailed("project", ex.GetType().Name);
            return;
        }

        CopyLabel.Text = ProjectsText.Get("Pinned_Copied");
        _timer ??= CreateTimer();
        _timer.Stop();
        _timer.Start();
    }

    private DispatcherQueueTimer CreateTimer()
    {
        DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = CopiedFor;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => CopyLabel.Text = ProjectsText.Get("Pinned_CopyInstall");
        return timer;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e) => Copy();
}
