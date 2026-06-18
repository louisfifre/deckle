using System;
using Microsoft.UI.Xaml;

namespace Deckle.Catalog;

// ── IPathControl ──────────────────────────────────────────────────────────────
//
// The contract between the floor composer and the folder-picker control that
// renders a Path setting. The concrete control (FolderPickerCard) lives in the
// Settings module: it anchors the OS folder picker to the Settings window and
// logs through the module's ETW source — neither of which belongs at the floor.
// So the composer never news it up; it builds the control through a host-supplied
// factory (SettingsComposer.PathControlFactory) and drives it through this
// minimal surface — the same lib-exposes-delegate / app-owns-contract inversion
// already used for the Settings window (SettingsHost.GetSettingsWindow). View is
// the control as a FrameworkElement, hosted as the card's content.
public interface IPathControl
{
    string Path { get; set; }
    event EventHandler? PathChanged;
    FrameworkElement View { get; }
}
