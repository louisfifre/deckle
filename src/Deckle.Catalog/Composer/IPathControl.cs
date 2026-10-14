using System;
using Microsoft.UI.Xaml;

namespace Deckle.Catalog;

// ── IPathControl ──────────────────────────────────────────────────────────────
//
// The contract between the composer and the folder-picker control that renders a
// Path setting. The concrete controls (FolderPickerCard, FolderPickerEditableCard)
// live in Deckle.Settings: they anchor the OS folder picker to the Settings window
// and log through that module's ETW source, neither of which belongs in Catalog.
// So the composer builds them through SettingsComposer.PathControlFactory and
// drives them through this surface. View is the row of actions the composer
// places at the card's trailing edge; the path line under the description is
// the composer's own (FolderCardDescription).
public interface IPathControl
{
    string Path { get; set; }
    event EventHandler? PathChanged;
    FrameworkElement View { get; }
}
