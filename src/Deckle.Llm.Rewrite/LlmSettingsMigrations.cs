using System;

namespace Deckle.Llm.Rewrite;

// ── LlmSettingsMigrations ─────────────────────────────────────────────────
//
// Profile-id reconciliation across the LlmSettings graph: fill missing stable
// ids (legacy configs, freshly built defaults), move the legacy primary slot
// into the active profile when no active choice exists yet, and re-pair
// ProfileId/ProfileName on the active profile and the legacy rules against
// the live Profiles list (the id wins, then the name).
//
// **Never deletes a legacy rule and never clears the active profile.** Orphan
// references (id and name both unresolvable) stay untouched, so older settings
// files round-trip without loss and the user can repair the active profile in
// the UI. The delete-cascade lives in LlmProfilesSection.DeleteProfile_Click,
// which clears references **before** the profile is removed.
//
// Returns true if anything was mutated: as the postLoadMigration hook of
// LlmSettingsService's JsonSettingsStore, that flushes the repair to disk.
public static partial class LlmSettingsMigrations
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

        // Legacy primary slot → active profile. Only when no active choice
        // exists yet, so a settings file written after the migration never
        // has its choice overwritten by a stale legacy value.
        bool activeEmpty = string.IsNullOrEmpty(s.ActiveProfileId)
                        && string.IsNullOrEmpty(s.ActiveProfileName);
        bool legacySet = !string.IsNullOrEmpty(s.PrimaryRewriteProfileId)
                      || !string.IsNullOrEmpty(s.PrimaryRewriteProfileName);
        if (activeEmpty && legacySet)
        {
            s.ActiveProfileId = s.PrimaryRewriteProfileId;
            s.ActiveProfileName = s.PrimaryRewriteProfileName;
            s.PrimaryRewriteProfileId = null;
            s.PrimaryRewriteProfileName = null;
            mutated = true;
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
