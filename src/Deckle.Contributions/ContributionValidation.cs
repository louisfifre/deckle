using System.Text.RegularExpressions;
using static Deckle.Contributions.DeckleContributionsSource;

namespace Deckle.Contributions;

// The rules a composition must satisfy before the host builds anything from
// it. Pure, so a test hands it a list: a refusal is a
// ContributionRegistrationException naming the contribution and the rule.
internal static partial class ContributionValidation
{
    // The shape of a module id (modules/<id>/) and of a window name (an image
    // prefix, a placement key): lowercase ASCII, digits and hyphens, starting
    // with a letter.
    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex NameShape();

    public static void Check(IReadOnlyList<IContribution> contributions, IReadOnlySet<string> reservedWindowNames)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var windowNames = new HashSet<string>(reservedWindowNames, StringComparer.Ordinal);

        foreach (IContribution contribution in contributions)
        {
            // A contribution may hand back null for a text it never set; the
            // nullable annotations do not hold against a foreign implementer.
            string id = contribution.Id ?? "";
            if (!NameShape().IsMatch(id)) throw Refuse(id, RegistrationRefusals.InvalidId);
            if (!ids.Add(id)) throw Refuse(id, RegistrationRefusals.DuplicateContribution);
            if (contribution.Descriptor is { } d && d.Id != id)
                throw Refuse(id, RegistrationRefusals.DescriptorIdMismatch);

            var own = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContributedWindow window in contribution.Windows)
            {
                string name = window.Name ?? "";
                if (!NameShape().IsMatch(name))
                    throw Refuse(id, RegistrationRefusals.InvalidWindowName);
                if (reservedWindowNames.Contains(name))
                    throw Refuse(id, RegistrationRefusals.ReservedWindowName);
                if (!windowNames.Add(name))
                    throw Refuse(id, RegistrationRefusals.DuplicateWindow);
                own.Add(name);
            }

            foreach (ContributedWalk walk in contribution.SelfCheckWalks)
            {
                if (!own.Contains(walk.WindowName))
                    throw Refuse(id, RegistrationRefusals.WalkWithoutWindow);
            }
        }

        CheckCycles(contributions);
    }

    // Edges to modules the composition does not carry (the host's own) are
    // not followed: they cannot close a loop inside it.
    private static void CheckCycles(IReadOnlyList<IContribution> contributions)
    {
        var edges = contributions
            .Where(c => c.Descriptor is not null)
            .ToDictionary(c => c.Id, c => c.Descriptor!.DependsOn, StringComparer.Ordinal);

        // 1 = on the current path, 2 = cleared.
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string start in edges.Keys)
            Visit(start);

        void Visit(string id)
        {
            if (state.TryGetValue(id, out int s))
            {
                if (s == 1) throw Refuse(id, RegistrationRefusals.DependencyCycle);
                return;
            }
            state[id] = 1;
            foreach (string dep in edges[id])
            {
                if (edges.ContainsKey(dep)) Visit(dep);
            }
            state[id] = 2;
        }
    }

    private static ContributionRegistrationException Refuse(string id, string reason)
    {
        Log.RegistrationRefused(id, reason);
        return new ContributionRegistrationException(reason, id);
    }
}
