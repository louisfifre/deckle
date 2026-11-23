using System.ComponentModel;
using Xunit;

namespace Deckle.Shell.Tests;

// What the Start elevated switch tells the person: a refused prompt apart from
// a failed task, and a task left pointing at another exe apart from no task.
[Trait("Category", "unit")]
public sealed class ElevatedStartupServiceTests
{
    private const string Current = @"C:\Apps\Deckle\Deckle.exe";

    private static string TaskXml(string command) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Actions Context="Author">
            <Exec>
              <Command>"{command}"</Command>
            </Exec>
          </Actions>
        </Task>
        """;

    [Fact]
    public void A_task_that_starts_this_exe_reads_on_with_no_issue()
    {
        bool on = ElevatedStartupService.Classify(TaskXml(Current.ToUpperInvariant()), Current, out var issue);

        Assert.True(on);
        Assert.Equal(ElevatedStartupIssue.None, issue);
    }

    [Fact]
    public void A_task_that_starts_an_exe_from_another_folder_reads_off_and_points_elsewhere()
    {
        bool on = ElevatedStartupService.Classify(TaskXml(@"C:\Old\Deckle.exe"), Current, out var issue);

        Assert.False(on);
        Assert.Equal(ElevatedStartupIssue.PointsElsewhere, issue);
    }

    [Fact]
    public void A_task_whose_xml_cannot_be_read_reads_off_without_claiming_a_stale_target()
    {
        bool on = ElevatedStartupService.Classify("not xml", Current, out var issue);

        Assert.False(on);
        Assert.Equal(ElevatedStartupIssue.None, issue);
    }

    [Fact]
    public void A_declined_uac_prompt_is_named_apart_from_a_failed_task()
    {
        Assert.Equal(ElevatedStartupIssue.UacDeclined,
            ElevatedStartupService.IssueOf(new Win32Exception(1223)));
        Assert.Equal(ElevatedStartupIssue.TaskFailed,
            ElevatedStartupService.IssueOf(new Win32Exception(5)));
        Assert.Equal(ElevatedStartupIssue.TaskFailed,
            ElevatedStartupService.IssueOf(new InvalidOperationException()));
    }
}
