using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;

namespace Deckle.Autocorrect;

// One row of the vocabulary-pack list on AutocorrectPage: a pack the build
// ships, whether the user turned it on, and the wording that names it. Same
// discipline as AutocorrectAppRow — the row never persists anything itself,
// toggling Active calls back into the view-model, which routes to
// AutocorrectSettingsService.
//
// The rows are enumerated from DomainPack.Shipped rather than from the settings
// file: a pack the user has never met must still appear, off, with what it
// brings visible before activation. Its label and description come from this
// module's own .resw, resolved through Loc.GetFrom because the rows are built in
// code — XAML x:Uid cannot reach a runtime-enumerated item.
public sealed partial class AutocorrectPackRow : ObservableObject
{
    private readonly Action<AutocorrectPackRow, bool> _onToggled;

    // Guards the seed assignment so hydrating from the stored state is not
    // mistaken for a user toggle (the _isSyncing pattern of the page VMs).
    private bool _syncing;

    public string PackId { get; }

    public string DisplayName { get; }

    public string Description { get; }

    [ObservableProperty]
    public partial bool Active { get; set; }

    public AutocorrectPackRow(
        DomainPack pack, bool active, Action<AutocorrectPackRow, bool> onToggled)
    {
        PackId = pack.Id;
        DisplayName = Loc.GetFrom(ResourceLibrary, $"{pack.ResourceKey}.Header");
        Description = Loc.GetFrom(ResourceLibrary, $"{pack.ResourceKey}.Description");
        _onToggled = onToggled;

        _syncing = true;
        Active = active;
        _syncing = false;
    }

    // This module's PRI subtree — the .resw carrying AutocorrectPage's strings.
    internal const string ResourceLibrary = "Deckle.Autocorrect";

    partial void OnActiveChanged(bool value)
    {
        if (_syncing) return;
        _onToggled(this, value);
    }
}
