using System.Collections.Immutable;

namespace Deckle.Mail;

/// <summary>
/// One mailbox the person added, with its main address and the aliases that
/// deliver into it. Deckle Mail reads its inbox only and sorts by the address
/// a mail arrived through, never by the server's folders.
/// </summary>
/// <param name="Id">Stable identity, chosen by whoever stores the mailbox.</param>
/// <param name="Address">The main address, the one Inbox stands for.</param>
/// <param name="Name">The name given when the mailbox was added, or null when none was.</param>
public sealed record Mailbox(string Id, string Address, string? Name = null)
{
    /// <remarks>Copied on the way in, so the caller's list cannot change a mailbox afterwards.</remarks>
    public IReadOnlyList<MailAlias> Aliases { get; init => field = value.ToImmutableArray(); } = ImmutableArray<MailAlias>.Empty;

    /// <summary>
    /// The switch of the mailbox page. It is on as soon as the mailbox has
    /// aliases; off makes the mailbox a single item in the navigation.
    /// </summary>
    public bool SortsByAlias { get; init; } = true;

    /// <summary>
    /// The alias option that names an alias from its address with a capital
    /// (shop@ → « Shop »). On by default.
    /// </summary>
    public bool CapitalizesAliasNames { get; init; } = true;

    /// <summary>True when the navigation shows the mailbox as a group header over Inbox and its aliases.</summary>
    public bool IsGroup => SortsByAlias && Aliases.Count > 0;
}

/// <summary>An address that delivers into a mailbox, with the name the person gave it by hand, if any.</summary>
public sealed record MailAlias(string Address, string? Name = null);
