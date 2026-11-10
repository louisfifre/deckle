namespace Deckle.Shell.Taskbar;

// Turns the engine's status into the few things the page says. The engine
// reports each rule and behavior by id (see TaskbarDocumentBuilder) with a
// state (see NativeRuleState); the ids map to the part of the page that asked
// for them, the worst state of a part wins, and a state that is only a
// passing one (pending) or a quiet one (applied, idle) says nothing.
internal static class TaskbarProblemReader
{
    /// <summary>The part an engine id belongs to, or null for an id Deckle does not know.</summary>
    public static TaskbarPart? PartOf(string id) => id switch
    {
        _ when id.StartsWith("background.", StringComparison.Ordinal) => TaskbarPart.Background,
        _ when id.StartsWith("tray.", StringComparison.Ordinal) => TaskbarPart.CompactTray,
        _ when id.StartsWith("quickSettings.", StringComparison.Ordinal) => TaskbarPart.CompactTray,
        _ when id.StartsWith("taskButtons.", StringComparison.Ordinal) => TaskbarPart.CompactTaskButtons,
        _ when id.StartsWith("clock.", StringComparison.Ordinal) || id == "clock" => TaskbarPart.CompactClock,
        _ when id.StartsWith("thumbnails.", StringComparison.Ordinal) => TaskbarPart.Thumbnails,
        "startMenuHeight" => TaskbarPart.StartMenuHeight,
        "startMenuWidth" => TaskbarPart.StartMenuWidth,
        _ when id.StartsWith("startMenu.recommended.", StringComparison.Ordinal) => TaskbarPart.StartMenuRecommended,
        _ => null,
    };

    /// <summary>The problem a rule state stands for; null when it is not one.</summary>
    public static TaskbarProblem? ProblemOf(string state) => state switch
    {
        "never_matched" => TaskbarProblem.NotApplied,
        "contested" => TaskbarProblem.Contested,
        "error" => TaskbarProblem.Error,
        _ => null,
    };

    /// <summary>The parts the customization asks for.</summary>
    public static IReadOnlyList<TaskbarPart> WantedParts(TaskbarCustomization wanted)
    {
        var parts = new List<TaskbarPart>();
        if (wanted.Background != TaskbarBackground.Native) parts.Add(TaskbarPart.Background);
        if (wanted.BarThickness is not null) parts.Add(TaskbarPart.BarThickness);
        if (wanted.TrayIconPitch is not null || wanted.TightenQuickSettings) parts.Add(TaskbarPart.CompactTray);
        if (wanted.TaskButtonPitch is not null) parts.Add(TaskbarPart.CompactTaskButtons);
        if (wanted.Clock is not null || wanted.TightenClockButton) parts.Add(TaskbarPart.CompactClock);
        if (wanted.Thumbnails is not null) parts.Add(TaskbarPart.Thumbnails);
        if (wanted.StartMenu is { FullHeight: true }) parts.Add(TaskbarPart.StartMenuHeight);
        if (wanted.StartMenu?.Width is not null) parts.Add(TaskbarPart.StartMenuWidth);
        if (wanted.StartMenu is { HideRecommended: true }) parts.Add(TaskbarPart.StartMenuRecommended);
        return parts;
    }

    /// <summary>
    /// What is wrong with the parts asked for. An unavailable process (no
    /// engine to ask) puts its parts in error; otherwise the status decides,
    /// and no status yet says nothing. The thickness is no rule, so it has no
    /// status line: <paramref name="barThickness"/> is what the customizer
    /// learned of it (the function's lookup, the engine's word on the patch).
    /// </summary>
    public static IReadOnlyDictionary<TaskbarPart, TaskbarProblem> Read(
        TaskbarCustomization wanted,
        NativeStatus? explorer, bool explorerUnavailable,
        NativeStatus? startMenu, bool startMenuUnavailable,
        TaskbarProblem? barThickness = null)
    {
        var problems = new Dictionary<TaskbarPart, TaskbarProblem>();
        foreach (TaskbarPart part in WantedParts(wanted))
        {
            bool unavailable = part.IsStartMenu() ? startMenuUnavailable : explorerUnavailable;
            if (unavailable) problems[part] = TaskbarProblem.Error;
        }
        if (wanted.BarThickness is not null && barThickness is { } thickness && !problems.ContainsKey(TaskbarPart.BarThickness))
            problems[TaskbarPart.BarThickness] = thickness;

        AddFrom(problems, wanted, explorer);
        AddFrom(problems, wanted, startMenu);
        return problems;
    }

    private static void AddFrom(Dictionary<TaskbarPart, TaskbarProblem> problems, TaskbarCustomization wanted, NativeStatus? status)
    {
        if (status is null) return;

        IReadOnlyList<TaskbarPart> asked = WantedParts(wanted);
        HashSet<string> optional = TaskbarDocumentBuilder.Rules(wanted)
            .Where(rule => rule.Optional).Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);
        foreach (NativeRuleState state in status.Rules.Concat(status.Behaviors))
        {
            if (PartOf(state.Id) is not { } part || !asked.Contains(part)) continue;
            if (ProblemOf(state.State) is not { } problem) continue;
            // An element that is only there now and then may well be absent.
            if (problem == TaskbarProblem.NotApplied && optional.Contains(state.Id)) continue;
            // So may a thumbnail: it exists only while a hover flyout is open.
            if (problem == TaskbarProblem.NotApplied && part == TaskbarPart.Thumbnails) continue;
            if (!problems.TryGetValue(part, out TaskbarProblem known) || problem > known) problems[part] = problem;
        }
    }
}
