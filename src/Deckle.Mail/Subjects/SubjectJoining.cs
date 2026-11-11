namespace Deckle.Mail;

/// <summary>
/// Which waiting subject a new mail belongs to. Two mails are one subject when
/// they are in the same thread of replies or carry the same order, parcel or
/// file number. A shared sender alone joins nothing.
/// </summary>
internal static class SubjectJoining
{
    /// <summary>
    /// The open subject the mail joins, or null when it starts a subject of its
    /// own. A treated or closed subject is never joined: a mail that arrives for
    /// a subject already treated is sorted as a new mail.
    /// </summary>
    /// <remarks>
    /// When the mail ties to several open subjects at once, it joins the oldest
    /// one and the others stay apart: the spec does not say whether two
    /// subjects a mail bridges become one, and merging them would rewrite rows
    /// the person may be reading.
    /// </remarks>
    public static Subject? FindOpenSubject(IEnumerable<Subject> subjects, Mail mail, MailJudgment judgment)
    {
        var threadIds = mail.ThreadIds.ToHashSet(StringComparer.Ordinal);
        var numbers = judgment.MatterNumbers.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return subjects
            .Where(subject => subject.Stage == SubjectStage.Open)
            .Where(subject => SharesThread(subject, threadIds) || subject.MatterNumbers.Overlaps(numbers))
            .OrderBy(subject => subject.Since)
            .ThenBy(subject => subject.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    // Comparing the whole set of ids each side knows, not only In-Reply-To
    // against Message-ID, also joins two replies to a first mail that was never
    // sorted here, and mails that arrive out of order.
    private static bool SharesThread(Subject subject, HashSet<string> threadIds) =>
        subject.Mails.Exists(known => known.ThreadIds.Any(threadIds.Contains));
}
