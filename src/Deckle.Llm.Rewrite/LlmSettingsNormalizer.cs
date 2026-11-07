using System;
using System.Collections.Generic;
using System.Linq;

namespace Deckle.Llm.Rewrite;

// ── LlmSettingsNormalizer ─────────────────────────────────────────────────
//
// Keeps the LlmSettings graph well formed. It runs as the post-load hook of
// LlmSettingsService's JsonSettingsStore (returning true flushes the repair to
// disk) and on fresh defaults, which carry no profile ids:
//   - fill missing stable profile ids;
//   - re-pair ActiveProfileId/ActiveProfileName against the live Profiles list
//     (the id wins, then the name), so a rename or a reset keeps the active
//     choice;
//   - keep the length ladder well formed: steps on the grain, within range,
//     strictly increasing.
//
// **Never clears the active profile.** An orphan reference (id and name both
// unresolvable) stays untouched so the user can repair it in the UI. The
// delete-cascade lives in LlmProfilesSection.DeleteProfile_Click, which clears
// references **before** the profile is removed. A ladder step whose profile no
// longer exists stays too, and is skipped at selection.
public static class LlmSettingsNormalizer
{
    /// <summary>Every repair in order; the postLoadMigration hook of the
    /// settings store, and what a reset runs on fresh defaults.</summary>
    public static bool Apply(LlmSettings s)
    {
        bool profiles = RepairProfileReferences(s);
        s.LengthSwitch ??= new LengthSwitchSettings();
        s.LengthSwitch.Steps ??= new List<LengthStep>();
        bool ladder = NormalizeLadder(s.LengthSwitch);
        return profiles || ladder;
    }

    public static bool RepairProfileReferences(LlmSettings s)
    {
        bool mutated = false;

        foreach (var p in s.Profiles)
        {
            if (string.IsNullOrWhiteSpace(p.Id))
            {
                p.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
                mutated = true;
            }
        }

        string? IdForName(string? name) =>
            string.IsNullOrWhiteSpace(name)
                ? null
                : s.Profiles.Find(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Id;

        string? NameForId(string? id) =>
            string.IsNullOrEmpty(id)
                ? null
                : s.Profiles.Find(p => p.Id == id)?.Name;

        bool RepairPair(ref string id, ref string name)
        {
            string? nameFromId = NameForId(id);
            if (nameFromId is not null)
            {
                if (name == nameFromId) return false;
                name = nameFromId;
                return true;
            }

            string? idFromName = IdForName(name);
            if (idFromName is not null)
            {
                if (id == idFromName) return false;
                id = idFromName;
                return true;
            }

            return false;
        }

        // The active profile stores nullable strings (null = "(None)"): a
        // repair flips empty strings back to null so the JSON stays clean.
        bool RepairSlot(ref string? id, ref string? name)
        {
            string idVal = id ?? "";
            string nameVal = name ?? "";
            if (idVal.Length == 0 && nameVal.Length == 0) return false;
            if (RepairPair(ref idVal, ref nameVal))
            {
                id = string.IsNullOrEmpty(idVal) ? null : idVal;
                name = string.IsNullOrEmpty(nameVal) ? null : nameVal;
                return true;
            }
            return false;
        }

        string? activeId = s.ActiveProfileId;
        string? activeName = s.ActiveProfileName;
        if (RepairSlot(ref activeId, ref activeName))
        {
            s.ActiveProfileId = activeId;
            s.ActiveProfileName = activeName;
            mutated = true;
        }

        return mutated;
    }

    // Snaps each step to the grain within range, and sorts. A step landing on
    // or below the one before moves just above it rather than being dropped,
    // so two thresholds close together both survive; only a step pushed past
    // the maximum is lost.
    public static bool NormalizeLadder(LengthSwitchSettings ladder)
    {
        var normalized = new List<LengthStep>();
        foreach (LengthStep step in ladder.Steps.OrderBy(step => step.FromChars))
        {
            int snapped = LengthSwitchSettings.Snap(step.FromChars);
            if (normalized.Count > 0 && snapped <= normalized[^1].FromChars)
                snapped = LengthSwitchSettings.Above(normalized[^1].FromChars);
            if (snapped > LengthSwitchSettings.MaxChars) continue;
            normalized.Add(new LengthStep { FromChars = snapped, ProfileId = step.ProfileId ?? "" });
        }

        bool changed = normalized.Count != ladder.Steps.Count
            || normalized.Where((step, i) =>
                step.FromChars != ladder.Steps[i].FromChars
                || step.ProfileId != ladder.Steps[i].ProfileId).Any();
        if (changed) ladder.Steps = normalized;
        return changed;
    }
}
