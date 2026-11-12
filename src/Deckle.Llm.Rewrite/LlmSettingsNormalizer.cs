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
//   - re-pair an old file's ActiveProfileId/ActiveProfileName against the
//     live Profiles list (the id wins, then the name), so the migration reads
//     the profile the person had;
//   - keep the dictation steps well formed, build them from the shape before
//     the steps when a file has none, and drop that old shape
//     (NormalizeDictation).
//
// **Never clears a step's profile.** A step whose profile no longer exists
// stays untouched, so the person can repair it on the page, and is skipped at
// selection. The delete-cascade lives in RemoveProfileFromSteps, which the
// Profiles section runs **before** the profile is removed.
public static class LlmSettingsNormalizer
{
    /// <summary>Every repair in order; the postLoadMigration hook of the
    /// settings store, and what a reset runs on fresh defaults.</summary>
    public static bool Apply(LlmSettings s)
    {
        bool profiles = RepairProfileReferences(s);
        bool dictation = NormalizeDictation(s);
        return profiles || dictation;
    }

    // A profile about to be removed leaves the steps: a later step that names
    // it goes with it, since a step left behind would hold a length nobody can
    // see the use of; the first step, which always exists, takes the first
    // profile that stays (none when it was the last one).
    public static void RemoveProfileFromSteps(LlmSettings s, string removedId)
    {
        List<RewriteStep> steps = s.Dictation.Steps;
        if (steps.Count == 0) return;

        steps.RemoveAll(step => step != steps[0] && step.ProfileId == removedId);
        if (steps[0].ProfileId == removedId)
            steps[0].ProfileId = s.Profiles.FirstOrDefault(p => p.Id != removedId)?.Id ?? "";
    }

    // ── Dictation steps ──────────────────────────────────────────────────────

    // Keeps the steps well formed, and builds them from the shape before the
    // steps when there are none (MigrateToSteps):
    //   - a first step always exists, with a profile (the first one when it
    //     names none);
    //   - every length on its grain and within range (the first may be 0,
    //     from the beginning); each later step strictly above the one before,
    //     a step landing on or below its neighbour moving just above it rather
    //     than being dropped; only a step pushed past the maximum is lost;
    //   - the later steps sorted by length, the first one staying first;
    //   - every block size on its grain and within its bounds, the default
    //     one where none was set;
    //   - the per-step "Streaming" switch and the single passage size of
    //     2026-11-11 turned into the shape of 2026-11-12 (MigrateToOneSwitch),
    //     and its "Start at" box into the first step's length.
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

        // The shape before the steps has been read (or the steps already
        // existed): it is not kept, and the next save drops it from the file.
        if (s.ActiveProfileId is not null || s.ActiveProfileName is not null
            || s.LengthSwitch is not null || s.BatchRewriting is not null)
        {
            s.ActiveProfileId = null;
            s.ActiveProfileName = null;
            s.LengthSwitch = null;
            s.BatchRewriting = null;
            changed = true;
        }

        if (MigrateToOneSwitch(d)) changed = true;

        // "Start at" unticked: the first step started from the beginning,
        // which a length of 0 now says.
        if (d.LegacyStartAtLength is { } startAt)
        {
            if (!startAt) d.Steps[0].FromChars = 0;
            d.LegacyStartAtLength = null;
            changed = true;
        }

        RewriteStep first = d.Steps[0];
        if (string.IsNullOrEmpty(first.ProfileId) && s.Profiles.Count > 0)
        {
            first.ProfileId = s.Profiles[0].Id;
            changed = true;
        }

        var normalized = new List<RewriteStep> { Normalized(first, DictationRewriteSettings.Snap(first.FromChars)) };
        int previous = normalized[0].FromChars;
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
                || step.BlockChars != d.Steps[i].BlockChars).Any();
        if (stepsChanged)
        {
            d.Steps = normalized;
            changed = true;
        }

        return changed;

        static RewriteStep Normalized(RewriteStep step, int fromChars) => new()
        {
            ProfileId = step.ProfileId ?? "",
            FromChars = fromChars,
            BlockChars = step.BlockChars > 0
                ? DictationRewriteSettings.SnapBlock(step.BlockChars)
                : DictationRewriteSettings.DefaultBlockChars,
        };
    }

    // The shape of 2026-11-11 (a "Streaming" switch on each step, one passage
    // size for all) becomes the shape of 2026-11-12 (one "Rewrite while I
    // speak" switch, a block size on each step), so a dictation is rewritten
    // while the person speaks if it was before:
    //   - the switch is on when any step streamed. A step that waited for
    //     Stop above a streaming one now streams too: the relay has one mode
    //     for the whole list, and losing the rewriting while speaking would
    //     be the larger change;
    //   - each step without a block size takes the old passage size.
    // Returns whether it changed anything.
    private static bool MigrateToOneSwitch(DictationRewriteSettings d)
    {
        bool changed = false;
        if (d.Steps.Any(step => step.LegacyStreaming is not null))
        {
            if (d.Steps.Any(step => step.LegacyStreaming == true)) d.RewriteWhileSpeaking = true;
            foreach (RewriteStep step in d.Steps) step.LegacyStreaming = null;
            changed = true;
        }

        if (d.LegacyPassageChars is { } passage)
        {
            foreach (RewriteStep step in d.Steps.Where(step => step.BlockChars <= 0))
                step.BlockChars = passage;
            d.LegacyPassageChars = null;
            changed = true;
        }
        return changed;
    }

    // The shape before the steps (an active profile, a length ladder, a
    // switch for batches) becomes steps, so a dictation goes where it went:
    //   - an active profile becomes the first step, from 0, and the ladder's
    //     steps follow it;
    //   - with no active profile, the ladder's first step becomes the first
    //     step at its length, since below it a dictation stayed as said;
    //   - with neither, "Rewrite dictations" is off and the first step takes
    //     the first profile from 0, ready for the day it is switched on;
    //   - the switch for batches becomes "Rewrite while I speak", and the
    //     batch size every step's block size.
    // A ladder that was switched off is not carried over: its steps never
    // applied, and as steps they would.
    //
    // Also what a fresh LlmSettings goes through: its old fields hold their
    // defaults (nothing active, ladder and batches off), so it gets the
    // third case.
    private static void MigrateToSteps(LlmSettings s)
    {
        DictationRewriteSettings d = s.Dictation;
        d.RewriteWhileSpeaking = s.BatchRewriting?.Enabled ?? false;
        int blockChars = s.BatchRewriting?.ThresholdChars ?? DictationRewriteSettings.DefaultBlockChars;

        List<LegacyLengthStep> ladder = s.LengthSwitch is { Enabled: true, Steps: { } ladderSteps }
            ? ladderSteps.Where(step => step is not null).OrderBy(step => step.FromChars).ToList()
            : new List<LegacyLengthStep>();

        RewriteStep FromLadder(LegacyLengthStep step) =>
            new() { ProfileId = step.ProfileId ?? "", FromChars = step.FromChars, BlockChars = blockChars };

        // RepairProfileReferences ran first, so a name that still resolves
        // has its id by now; an id that resolves to nothing is kept, as the
        // active profile was, and the step is skipped at selection.
        bool hasActive = !string.IsNullOrEmpty(s.ActiveProfileId)
            || !string.IsNullOrWhiteSpace(s.ActiveProfileName);

        if (hasActive)
        {
            d.Enabled = true;
            d.Steps.Add(new RewriteStep { ProfileId = s.ActiveProfileId ?? "", FromChars = 0, BlockChars = blockChars });
            d.Steps.AddRange(ladder.Select(FromLadder));
        }
        else if (ladder.Count > 0)
        {
            d.Enabled = true;
            d.Steps.AddRange(ladder.Select(FromLadder));
        }
        else
        {
            d.Enabled = false;
            d.Steps.Add(new RewriteStep { FromChars = 0, BlockChars = blockChars });
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
}
