namespace Deckle.Mail.Hub;

// The built-in synthetic dataset behind IMailHubSource. No mailbox, no model
// and no Anytype stand behind it: every name, address and mail is made up
// from the examples of the spec and its frames (exemple.fr, Perso, shop@).
//
// Apply only records the gesture: nothing leaves the process. The record is
// what tests read to see that a gesture reached the seam.
public sealed class SampleMailHubSource(SampleScenario scenario) : IMailHubSource
{
    private readonly List<MailGesture> _applied = [];

    public SampleScenario Scenario { get; } = scenario;

    public IReadOnlyList<MailGesture> Applied => _applied;

    public MailHubSnapshot Load() => SampleWorld.Build(Scenario);

    public void Apply(MailGesture gesture) => _applied.Add(gesture);
}
