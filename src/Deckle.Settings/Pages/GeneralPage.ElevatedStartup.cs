using System.ComponentModel;
using Deckle.Localization;
using Deckle.Shell;

namespace Deckle.Settings;

// Explains what the Start elevated switch could not do: a refused UAC prompt,
// a failed task, or an old task that starts another exe. The view model holds
// the issue; this only words it.
public sealed partial class GeneralPage
{
    private void WireElevatedStartupInfoBar()
    {
        ViewModel.PropertyChanged += OnElevatedStartupPropertyChanged;
        ShowElevatedStartupIssue(ViewModel.ElevatedStartupIssue);
    }

    private void OnElevatedStartupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GeneralViewModel.ElevatedStartupIssue))
            ShowElevatedStartupIssue(ViewModel.ElevatedStartupIssue);
    }

    private void ShowElevatedStartupIssue(ElevatedStartupIssue issue)
    {
        string? key = ElevatedStartupMessageKey(issue);
        if (key is null)
        {
            ElevatedStartupInfoBar.IsOpen = false;
            return;
        }

        ElevatedStartupInfoBar.Message = Loc.GetFrom("Deckle.Settings", key);
        ElevatedStartupInfoBar.IsOpen = true;
    }

    internal static string? ElevatedStartupMessageKey(ElevatedStartupIssue issue) => issue switch
    {
        ElevatedStartupIssue.UacDeclined      => "GeneralElevatedStartup_UacDeclined",
        ElevatedStartupIssue.TaskFailed       => "GeneralElevatedStartup_TaskFailed",
        ElevatedStartupIssue.PointsElsewhere  => "GeneralElevatedStartup_PointsElsewhere",
        _                                     => null,
    };
}
