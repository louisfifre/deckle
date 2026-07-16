using System;

namespace Deckle.Llm.Rewrite;

// ── LlmSettingsMigrations ─────────────────────────────────────────────────
//
// Profile-id reconciliation across the LlmSettings graph. Lives in
// Deckle.Llm next to the POCO it operates on (moved from
// Deckle.Settings.SettingsService when LlmSettings got its own per-module
// JSON file in slice C2b).
//
// Two jobs:
//
//   1. Fill missing stable ids — each RewriteProfile gets a 12-char Guid
//      suffix on first encounter (legacy configs, freshly-instantiated
//      defaults).
//   2. Re-pair ProfileId/ProfileName on shortcut slots and legacy automatic
//      rules when the live Profiles list still contains a match. The legacy
//      lists are no longer executed or exposed, but remain deserializable so
//      older settings files can be loaded without data loss. Three cases:
//        - id resolves → sync the cached name in case the profile was
//          renamed since the rule was last saved
//        - id is empty but name resolves → fill id from name (post-
//          migration of an older config that never had ids)
//        - id is stale but name resolves → rewire id from name
//
// **Never deletes a legacy rule and never clears a slot.** Orphan references
// (id+name both unresolvable) stay untouched. Slots remain user-repairable in
// the UI; legacy rules are preserved only for backward-compatible round trips.
//
// The delete-cascade for "remove a profile, drop its dependants" lives
// in LlmProfilesSection.DeleteProfile_Click, which clears references
// explicitly **before** the profile is removed.
//
// Returns true if anything was mutated. Used as the postLoadMigration
// hook on LlmSettingsService's JsonSettingsStore so loads that need
// repair flush the cleanup back to disk; also called explicitly by
// page-level reset paths after building a fresh LlmSettings instance.
public static class LlmSettingsMigrations
{
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

        // Re-pair (id, name) against the live Profiles list. Mutates the
        // ref arguments only when a live profile matches; leaves them
        // untouched (orphan kept as-is) otherwise. Returns true if anything
        // changed so the caller can flag the parent as mutated.
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

            // Neither resolves — keep the orphan for a lossless settings
            // round trip. Live slots can still be repaired in the UI.
            return false;
        }

        foreach (var rule in s.AutoRewriteRules)
        {
            string id = rule.ProfileId ?? "";
            string name = rule.ProfileName ?? "";
            if (RepairPair(ref id, ref name))
            {
                rule.ProfileId = id;
                rule.ProfileName = name;
                mutated = true;
            }
        }

        foreach (var rule in s.AutoRewriteRulesByWords)
        {
            string id = rule.ProfileId ?? "";
            string name = rule.ProfileName ?? "";
            if (RepairPair(ref id, ref name))
            {
                rule.ProfileId = id;
                rule.ProfileName = name;
                mutated = true;
            }
        }

        // Slots: same re-pair logic, but the storage uses nullable strings
        // (null = "(None)"). A repair flips empty strings back to null so
        // the JSON stays clean, and an orphan slot is left as-is — the user
        // sees the stale name in the ComboBox and reassigns or clears it.
        bool RepairSlot(ref string? id, ref string? name)
        {
            string idVal = id ?? "";
            string nameVal = name ?? "";
            // Nothing set: nothing to do.
            if (idVal.Length == 0 && nameVal.Length == 0) return false;
            if (RepairPair(ref idVal, ref nameVal))
            {
                id = string.IsNullOrEmpty(idVal) ? null : idVal;
                name = string.IsNullOrEmpty(nameVal) ? null : nameVal;
                return true;
            }
            return false;
        }

        string? primaryId = s.PrimaryRewriteProfileId;
        string? primaryName = s.PrimaryRewriteProfileName;
        if (RepairSlot(ref primaryId, ref primaryName))
        {
            s.PrimaryRewriteProfileId = primaryId;
            s.PrimaryRewriteProfileName = primaryName;
            mutated = true;
        }

        string? secondaryId = s.SecondaryRewriteProfileId;
        string? secondaryName = s.SecondaryRewriteProfileName;
        if (RepairSlot(ref secondaryId, ref secondaryName))
        {
            s.SecondaryRewriteProfileId = secondaryId;
            s.SecondaryRewriteProfileName = secondaryName;
            mutated = true;
        }

        return mutated;
    }
}
