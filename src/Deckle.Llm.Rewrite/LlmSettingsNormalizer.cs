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
//     strictly increasing;
//   - keep the dictation steps well formed, and build them from the shape
//     before the steps when a file has none (NormalizeDictation).
//
// **Never clears the active profile.** An orphan reference (id and name both
// unresolvable) stays untouched so the user can repair it in the UI. The
// delete-cascade lives in LlmProfilesSection.RemoveProfileAsync, which clears
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
        bool dictation = NormalizeDictation(s);
        return profiles || ladder || dictation;
    }

    // ── Dictation steps ──────────────────────────────────────────────────────

    // Keeps the steps well formed, and builds them from the shape before the
    // steps when there are none (MigrateToSteps):
    //   - a first step always exists, with a profile (the first one when it
    //     names none);
    //   - every length on its grain and within range; each later step strictly
    //     above the one before (the first counting only with "Start at" on),
    //     a step landing on or below its neighbour moving just above it rather
    //     than being dropped; only a step pushed past the maximum is lost;
    //   - the later steps sorted by length, the first one staying first;
    //   - the passage size within its bounds.
    // A step whose profile no longer exists stays, and is skipped when a
    // dictation picks its step, as the ladder's were.
    public static bool NormalizeDictation(LlmSettings s)
    {
        bool changed = false;
        if (s.Dictation is null)
        {
            s.Dictation = new DictationRewriteSettings();
            changed = true;
        }
        DictationRewriteSettings d = s.Dictation;
        if (d.Steps is null)
        {
            d.Steps = new List<RewriteStep>();
            changed = true;
        }
        d.Steps.RemoveAll(step => step is null);

        if (d.Steps.Count == 0)
        {
            MigrateToSteps(s);
            changed = true;
        }

        RewriteStep first = d.Steps[0];
        if (string.IsNullOrEmpty(first.ProfileId) && s.Profiles.Count > 0)
        {
            first.ProfileId = s.Profiles[0].Id;
            changed = true;
        }

        var normalized = new List<RewriteStep> { Normalized(first, DictationRewriteSettings.Snap(first.FromChars)) };
        int previous = d.StartAtLength ? normalized[0].FromChars : 0;
        foreach (RewriteStep step in d.Steps.Skip(1).OrderBy(step => step.FromChars))
        {
            int snapped = DictationRewriteSettings.Snap(step.FromChars);
            if (snapped <= previous) snapped = DictationRewriteSettings.Above(previous);
            if (snapped > DictationRewriteSettings.MaxChars) continue;
            normalized.Add(Normalized(step, snapped));
            previous = snapped;
        }

        bool stepsChanged = normalized.Count != d.Steps.Count
            || normalized.Where((step, i) =>
                step.FromChars != d.Steps[i].FromChars
                || step.ProfileId != d.Steps[i].ProfileId
                || step.Streaming != d.Steps[i].Streaming).Any();
        if (stepsChanged)
        {
            d.Steps = normalized;
            changed = true;
        }

        int passage = Math.Clamp(d.PassageChars,
            DictationRewriteSettings.MinPassageChars, DictationRewriteSettings.MaxPassageChars);
        if (passage != d.PassageChars)
        {
            d.PassageChars = passage;
            changed = true;
        }

        return changed;

        static RewriteStep Normalized(RewriteStep step, int fromChars) => new()
        {
            ProfileId = step.ProfileId ?? "",
            FromChars = fromChars,
            Streaming = step.Streaming,
        };
    }

    // The shape before the steps (an active profile, a length ladder, a
    // switch for batches) becomes steps, so a dictation goes where it went:
    //   - an active profile becomes the first step, from the beginning, and
    //     the ladder's steps follow it;
    //   - with no active profile, the ladder's first step becomes the first
    //     step with "Start at" on at its length, since below it a dictation
    //     stayed as said;
    //   - with neither, "Rewrite dictations" is off and the first step takes
    //     the first profile, ready for the day it is switched on;
    //   - the switch for batches sets Streaming on every step, and the batch
    //     size becomes the passage size.
    // A ladder that was switched off is not carried over: its steps never
    // applied, and as steps they would.
    //
    // Also what a fresh LlmSettings goes through: its old fields hold their
    // defaults (nothing active, ladder and batches off), so it gets the
    // third case.
    private static void MigrateToSteps(LlmSettings s)
    {
        DictationRewriteSettings d = s.Dictation;
        bool streaming = s.BatchRewriting?.Enabled ?? false;
        if (s.BatchRewriting is { } batching) d.PassageChars = batching.ThresholdChars;

        List<LengthStep> ladder = s.LengthSwitch is { Enabled: true, Steps: { } ladderSteps }
            ? ladderSteps.Where(step => step is not null).OrderBy(step => step.FromChars).ToList()
            : new List<LengthStep>();

        RewriteStep FromLadder(LengthStep step) =>
            new() { ProfileId = step.ProfileId ?? "", FromChars = step.FromChars, Streaming = streaming };

        // RepairProfileReferences ran first, so a name that still resolves
        // has its id by now; an id that resolves to nothing is kept, as the
        // active profile was, and the step is skipped at selection.
        bool hasActive = !string.IsNullOrEmpty(s.ActiveProfileId)
            || !string.IsNullOrWhiteSpace(s.ActiveProfileName);

        if (hasActive)
        {
            d.Enabled = true;
            d.StartAtLength = false;
            d.Steps.Add(new RewriteStep
            {
                ProfileId = s.ActiveProfileId ?? "",
                FromChars = DictationRewriteSettings.DefaultStartAtChars,
                Streaming = streaming,
            });
            d.Steps.AddRange(ladder.Select(FromLadder));
        }
        else if (ladder.Count > 0)
        {
            d.Enabled = true;
            d.StartAtLength = true;
            d.Steps.AddRange(ladder.Select(FromLadder));
        }
        else
        {
            d.Enabled = false;
            d.StartAtLength = false;
            d.Steps.Add(new RewriteStep
            {
                FromChars = DictationRewriteSettings.DefaultStartAtChars,
                Streaming = streaming,
            });
        }
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
