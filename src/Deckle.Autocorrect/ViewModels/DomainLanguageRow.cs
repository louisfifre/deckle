using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;

namespace Deckle.Autocorrect;

// One language row inside a lexical domain on LexicalDomainsPage. The row never
// persists anything itself: toggling Active calls back into the view-model,
// which routes to AutocorrectSettingsService.
//
// The rows are enumerated from DomainPack.Shipped rather than from the settings
// file, because a language nobody has decided on must still appear (its default
// follows the Windows language list, DomainActivation) with what it brings
// readable before turning it on.
//
// The label is built here rather than resolved from a per-language .resw key:
// the language name comes from the OS, already localized and already spelled the
// way the rest of Windows spells it, so a new pack adds no wording to translate.
// The term count comes from the pack's shipped manifest and is dropped when no
// manifest ships — the figures inform, they are never a precondition.
public sealed partial class DomainLanguageRow : ObservableObject
{
    private readonly Action<DomainLanguageRow, bool> _onToggled;

    // Guards the seed assignment so hydrating from the stored state is not
    // mistaken for a user toggle.
    private bool _syncing;

    public string PackId { get; }

    public DomainPack Pack { get; }

    public string LanguageName { get; }

    public string Label { get; }

    [ObservableProperty]
    public partial bool Active { get; set; }

    public DomainLanguageRow(
        DomainPack pack,
        bool active,
        DomainPackManifest? manifest,
        Action<DomainLanguageRow, bool> onToggled)
    {
        Pack = pack;
        PackId = pack.Id;

        LanguageName = ResolveLanguageName(pack.Language);
        Label = manifest is null
            ? LanguageName
            : string.Format(
                CultureInfo.CurrentCulture,
                Loc.GetFrom(
                    AutocorrectSettingsModule.ResourceLibrary,
                    "LexicalDomainsPage_LanguageRow_Format"),
                LanguageName,
                manifest.ShippedForms);

        _onToggled = onToggled;
        Sync(active);
    }

    internal void Sync(bool active)
    {
        _syncing = true;
        Active = active;
        _syncing = false;
    }

    // The language as Windows names it in the user's UI language. A tag no
    // culture matches falls back to the tag itself: an unnamed row is still a
    // usable row, and a build that ships such a pack has a fabrication bug, not
    // a settings bug.
    private static string ResolveLanguageName(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return tag;
        }
    }

    partial void OnActiveChanged(bool value)
    {
        if (_syncing) return;
        _onToggled(this, value);
    }
}
