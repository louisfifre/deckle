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
    string? AgentTooltip);

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

    public bool InfoIsShown => InfoButton.Visibility == Visibility.Visible;

    public bool FlyoutIsOpen => _flyout?.IsOpen ?? false;

    public void Apply(ReadoutsModel model)
    {
        VersionValue.Text = model.Version;
        UncommittedValue.Text = model.Uncommitted;
        AgentValue.Text = model.Agent;
        ToolTipService.SetToolTip(AgentValue, model.AgentTooltip);

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
    public void OpenVersions()
    {
        if (_detail is null) return;
        _flyout?.Hide();
        _flyout = VersionsFlyout.Build(_detail);
        // Anchored on the whole readout, so the flyout lines up with the word
        // "Version" and not with the small button.
        _flyout.ShowAt(VersionFact, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }

    public void CloseVersions() => _flyout?.Hide();

    private void OnInfoClick(object sender, RoutedEventArgs e) => OpenVersions();
}
