namespace Deckle.Mail.Hub;

// The day a task is proposed for, and the menu of days Add task opens.
//
// The proposed day follows one fixed rule, without a setting: two days before
// the deadline, never before today (spec, Adding a task). Open 6 weighs two
// against three days; the number lives in LeadDays alone.
//
// The menu holds Today, Tomorrow, Pick a day and No day yet, as the spec
// writes it (Open 51 asks whether No day yet stays and whether the word is
// day or date). When a day is proposed it is the first line, already
// selected; a proposed today or tomorrow is that line itself, selected,
// rather than a second copy of it.
public static class TaskDays
{
    public const int LeadDays = 2;

    public static DateOnly? Propose(DateOnly? deadline, DateOnly today)
    {
        if (deadline is not { } day) return null;
        DateOnly lead = day.AddDays(-LeadDays);
        return lead < today ? today : lead;
    }

    public static IReadOnlyList<TaskDayChoice> Menu(DateOnly? proposed, DateOnly today)
    {
        DateOnly tomorrow = today.AddDays(1);
        var choices = new List<TaskDayChoice>();
        if (proposed is { } own && own != today && own != tomorrow)
            choices.Add(new TaskDayChoice(TaskDayChoiceKind.Proposed, own, IsSelected: true));
        choices.Add(new TaskDayChoice(TaskDayChoiceKind.Today, today, IsSelected: proposed == today));
        choices.Add(new TaskDayChoice(TaskDayChoiceKind.Tomorrow, tomorrow, IsSelected: proposed == tomorrow));
        choices.Add(new TaskDayChoice(TaskDayChoiceKind.PickADay, null, IsSelected: false));
        choices.Add(new TaskDayChoice(TaskDayChoiceKind.NoDay, null, IsSelected: false));
        return choices;
    }
}

public enum TaskDayChoiceKind
{
    Proposed,
    Today,
    Tomorrow,
    PickADay,
    NoDay,
}

// Day is the day the choice creates the task for; null for Pick a day (the
// person picks it next) and No day yet.
public sealed record TaskDayChoice(TaskDayChoiceKind Kind, DateOnly? Day, bool IsSelected);
