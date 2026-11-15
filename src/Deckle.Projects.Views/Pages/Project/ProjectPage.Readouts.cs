namespace Deckle.Projects.Views;

// The three readouts: "Version", "Uncommitted" and "An agent always reads".
// Each is a label above a value, read from the folder each time the page opens,
// the window comes back to the front, Refresh is pressed or an action ends. While
// a reading is made the previous values stay, and on a first read the value is
// empty (spec, Readouts).
internal sealed partial class ProjectPage
{
    private void ApplyReadouts(ProjectEntry project)
    {
        ProjectReading? reading = _context.Workspace.Readings.Get(project.Id);
        Readouts.Apply(ReadoutText.Of(reading, _agent, _agentRead));
    }
}
