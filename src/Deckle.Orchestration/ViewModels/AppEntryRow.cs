using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deckle.Catalog;

namespace Deckle.Orchestration;

// One row of the apps and games list. The row never touches persistence: a
// toggle, a kind change or a removal calls back into the view-model, which
// routes to OrchestrationSettingsService, so the write stays in one place.
public sealed partial class AppEntryRow : ObservableObject
{
    private readonly Action<AppEntryRow, bool> _onEnabledChanged;
    private readonly Action<AppEntryRow, AppKind> _onKindChanged;
    private readonly Action<AppEntryRow> _onRemoved;

    // Set while the row is filled from the stored entry, so hydration is not
    // mistaken for a user change (same guard as AutocorrectAppRow).
    private bool _syncing;

    public string Id { get; }

    public string DisplayName { get; }

    // Expander description: where the app lives.
    [ObservableProperty]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial string? ExecutablePath { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    // ComboBox position: 0 = App, 1 = Game (the AppKind order).
    [ObservableProperty]
    public partial int KindIndex { get; set; }

    [ObservableProperty]
    public partial string Glyph { get; set; } = Glyphs.Window;

    public AppEntryRow(
        AppEntry entry,
        Action<AppEntryRow, bool> onEnabledChanged,
        Action<AppEntryRow, AppKind> onKindChanged,
        Action<AppEntryRow> onRemoved)
    {
        Id = entry.Id;
        DisplayName = entry.DisplayName;
        _onEnabledChanged = onEnabledChanged;
        _onKindChanged = onKindChanged;
        _onRemoved = onRemoved;
        Apply(entry);
    }

    // Refresh the row from the stored entry without echoing changes back.
    public void Apply(AppEntry entry)
    {
        _syncing = true;
        try
        {
            ExecutablePath = entry.ExecutablePath;
            Description = entry.ExecutablePath ?? entry.InstallDirectory;
            Enabled = entry.Enabled;
            KindIndex = (int)entry.Kind;
            Glyph = GlyphFor(entry.Kind);
        }
        finally
        {
            _syncing = false;
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_syncing) return;
        _onEnabledChanged(this, value);
    }

    partial void OnKindIndexChanged(int value)
    {
        if (value < 0) return;
        var kind = (AppKind)value;
        Glyph = GlyphFor(kind);
        if (_syncing) return;
        _onKindChanged(this, kind);
    }

    [RelayCommand]
    private void Remove() => _onRemoved(this);

    private static string GlyphFor(AppKind kind) =>
        kind == AppKind.Game ? Glyphs.Games : Glyphs.Window;
}
