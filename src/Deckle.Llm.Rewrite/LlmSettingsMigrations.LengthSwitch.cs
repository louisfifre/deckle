using System;
using System.Collections.Generic;
using System.Linq;

namespace Deckle.Llm.Rewrite;

// ── Length ladder migrations ─────────────────────────────────────────────────
//
// v0.77 to v0.79 kept a length threshold in each profile, in minutes, and a
// shortcut with no switch of its own. This pass moves the thresholds into
// the page-level ladder once, converted to characters at the display ratio
// and snapped to the nearest stop, and gives an existing chord its switch.
// It then keeps the ladder well formed: steps on stops, strictly increasing,
// one per stop. A step whose profile no longer exists stays (the delete
// cascade lives in LlmProfilesSection) and is skipped at selection.
public static partial class LlmSettingsMigrations
{
    public static bool MoveProfileThresholdsToLadder(LlmSettings s)
    {
        bool mutated = false;
        LengthSwitchSettings ladder = s.LengthSwitch ??= new LengthSwitchSettings();
        ladder.Steps ??= new List<LengthStep>();

        foreach (RewriteProfile profile in s.Profiles)
        {
            if (profile.HotkeyEnabled is null)
            {
                profile.HotkeyEnabled = profile.Hotkey is { IsComplete: true };
                mutated = true;
            }

            if (!profile.AutoSelect) continue;
            ladder.Steps.Add(new LengthStep
            {
                FromChars = profile.AutoSelectFromMinutes * LengthSwitchSettings.CharsPerMinute,
                ProfileId = profile.Id,
            });
            ladder.Enabled = true;
            profile.AutoSelect = false;
            mutated = true;
        }

        return NormalizeLadder(ladder) || mutated;
    }

    // Snaps each step to its nearest stop and sorts. A step landing on a stop
    // already taken moves to the next free stop rather than being dropped, so
    // two legacy thresholds a minute apart both survive; only a step past the
    // last stop is lost.
    public static bool NormalizeLadder(LengthSwitchSettings ladder)
    {
        IReadOnlyList<int> stops = LengthSwitchSettings.StopsChars;
        var normalized = new List<LengthStep>();
        foreach (LengthStep step in ladder.Steps.OrderBy(step => step.FromChars))
        {
            int snapped = NearestStop(step.FromChars);
            if (normalized.Count > 0 && snapped <= normalized[^1].FromChars)
            {
                int previous = normalized[^1].FromChars;
                int next = stops.FirstOrDefault(stop => stop > previous);
                if (next == 0) continue;
                snapped = next;
            }
            normalized.Add(new LengthStep { FromChars = snapped, ProfileId = step.ProfileId ?? "" });
        }

        bool changed = normalized.Count != ladder.Steps.Count
            || normalized.Where((step, i) =>
                step.FromChars != ladder.Steps[i].FromChars
                || step.ProfileId != ladder.Steps[i].ProfileId).Any();
        if (changed) ladder.Steps = normalized;
        return changed;
    }

    public static int NearestStop(int chars)
    {
        IReadOnlyList<int> stops = LengthSwitchSettings.StopsChars;
        int best = stops[0];
        foreach (int stop in stops)
            if (Math.Abs(stop - chars) < Math.Abs(best - chars)) best = stop;
        return best;
    }
}
