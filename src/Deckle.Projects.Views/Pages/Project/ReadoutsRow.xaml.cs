using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Deckle.Projects.Views;

// The words of the three readouts and what the info button opens. A value that
// is not known yet is the empty string: on a first read the page shows nothing
// rather than a guess (spec, Readouts).
internal sealed record ReadoutsModel(
    string Version,
    VersionReading? VersionDetail,
    string Uncommitted,
    string Agent,
    string? AgentTooltip,
    string? GitNote = null);

internal sealed partial class ReadoutsRow : UserControl
{
    private VersionReading? _detail;
    private Flyout? _flyout;

    public ReadoutsRow()
    {
        InitializeComponent();
        VersionLabel.Text = ProjectsText.Get("Project_Version");
        UncommittedLabel.Text = ProjectsText.Get("Project_Uncommitted");
        AgentLabel.Text = ProjectsText.Get("Project_AgentReads");
        InfoIcon.Glyph = ProjectsGlyphs.Info;
    }

    public string VersionText => VersionValue.Text;

    public string UncommittedText => UncommittedValue.Text;

    public string AgentText => AgentValue.Text;

    public string GitNoteText => GitNote.Text;

    public bool InfoIsShown => InfoButton.Visibility == Visibility.Visible;

    public bool FlyoutIsOpen => _flyout?.IsOpen ?? false;

    public void Apply(ReadoutsModel model)
    {
        VersionValue.Text = model.Version;
        UncommittedValue.Text = model.Uncommitted;
        AgentValue.Text = model.Agent;
        ToolTipService.SetToolTip(AgentValue, model.AgentTooltip);
        GitNote.Text = model.GitNote ?? "";
        GitNote.Visibility = model.GitNote is null ? Visibility.Collapsed : Visibility.Visible;

        _detail = model.VersionDetail;
        bool info = _detail is not null;
        InfoButton.Visibility = info ? Visibility.Visible : Visibility.Collapsed;
        if (info)
        {
            string heading = VersionsFlyout.HeadingFor(_detail!);
            ToolTipService.SetToolTip(InfoButton, heading);
            AutomationProperties.SetName(InfoButton, heading);
            AutomationProperties.SetHelpText(InfoButton, ProjectsText.Get("Project_VersionInfo"));
        }
        else
        {
            _flyout?.Hide();
        }
    }

    // Opens the list of versions under the readout, as a press on the info
    // button does.
    //
    // The review's list is kept inside the window: one a person opens may leave
    // the window's edge, in a window of its own that a capture of this window
    // does not see and that the self-check's guard takes for a window on a
    // monitor. Inside the window the acrylic has nothing to blur, so the review's
    // list takes the solid fill acrylic falls back to, as the card's menu does.
    public void OpenVersions(bool constrainToWindow = false)
    {
        if (_detail is null) return;
        _flyout?.Hide();
        _flyout = VersionsFlyout.Build(_detail);
        if (constrainToWindow)
        {
            _flyout.ShouldConstrainToRootBounds = true;
            _flyout.FlyoutPresenterStyle = (Style)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"FlyoutPresenter\" "
                + "BasedOn=\"{StaticResource DefaultFlyoutPresenterStyle}\">"
                + "<Setter Property=\"Background\" Value=\"{ThemeResource SolidBackgroundFillColorQuarternaryBrush}\" /></Style>");
        }
        // Anchored on the whole readout, so the flyout lines up with the word
        // "Version" and not with the small button.
        if (constrainToWindow)
        {
            // A popup shown with an explicit placement is drawn in a window of
            // its own even when constrained, so the review's is placed through
            // the flyout's own property. The edge aligned left keeps the list's
            // left edge on the readout's; the centred Bottom placement put a
            // 380 DIP list under a ~100 DIP readout and pushed it to the
            // window's edge.
            _flyout.Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft;
            _flyout.ShowAt(VersionFact);
        }
        else _flyout.ShowAt(VersionFact, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }

    public void CloseVersions() => _flyout?.Hide();

    private void OnInfoClick(object sender, RoutedEventArgs e) => OpenVersions();
}
