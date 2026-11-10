using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Mail.Hub;

// ── The problems button ───────────────────────────────────────────────────────
//
// Shown only while something does not work; absent when everything works. No
// bar ever pushes a page down instead: the rows and lines stay where they were
// whatever fails or comes back (spec, The window). Its accessible name and the
// title of its list are not decided (Open 59): the words are resource keys of
// their own, Problems_Name and Problems_Name_Format.
public sealed partial class MailWindow
{
    private void RefreshProblems()
    {
        IReadOnlyList<MailProblem> problems = Session.Problems;
        ProblemsButton.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (problems.Count == 0)
        {
            ProblemsFlyout.Hide();
            return;
        }

        ProblemsBadge.Value = problems.Count;
        string name = MailText.Format("Problems_Name_Format", problems.Count);
        AutomationProperties.SetName(ProblemsButton, name);
        ToolTipService.SetToolTip(ProblemsButton, name);

        ProblemsList.Children.Clear();
        for (int i = 0; i < problems.Count; i++)
        {
            MailProblem problem = problems[i];
            if (i > 0)
                ProblemsList.Children.Add(new MenuFlyoutSeparator());
            ProblemsList.Children.Add(new ProblemLineView(
                problem, Session,
                command: () => OpenProblem(problem),
                dismiss: problem.CanBeDismissed ? Session.DismissAnytypeNotice : null));
        }
    }

    // The useful command of a problem: the page where it is fixed.
    private void OpenProblem(MailProblem problem)
    {
        ProblemsFlyout.Hide();
        Go(problem.MailboxId is { } id ? MailDestination.MailboxSettings(id) : MailDestination.Settings);
    }

    // For the self-check: the list as the person sees it once opened.
    public void OpenProblems()
    {
        if (ProblemsButton.Visibility == Visibility.Visible)
            ProblemsFlyout.ShowAt(ProblemsButton);
    }
}
