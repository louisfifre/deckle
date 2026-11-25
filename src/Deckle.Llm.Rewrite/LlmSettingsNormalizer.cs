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
//   - keep the dictation steps well formed, a first step included
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

    // Keeps the steps well formed:
    //   - a first step always exists, with a profile (the first one when it
    //     names none);
    //   - every length on its grain and within range (the first may be 0,
    //     from the beginning); each later step strictly above the one before,
    //     a step landing on or below its neighbour moving just above it rather
    //     than being dropped; only a step pushed past the maximum is lost;
    //   - the later steps sorted by length, the first one staying first;
    //   - every block size on its grain and within its bounds, the default
    //     one where none was set.
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
            d.Steps.Add(new RewriteStep { FromChars = 0, BlockChars = DictationRewriteSettings.DefaultBlockChars });
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

        return mutated;
    }
}
