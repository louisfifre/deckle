// TrayQuickActionsHost — panel content, built in code.

using System;
using System.Collections.Generic;
using Deckle.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Shapes;

namespace Deckle.Shell.TrayMenu;

public sealed partial class TrayQuickActionsHost
{
    // Gap between a button's glyph and its label: the 8 px step of the Fluent
    // spacing ramp, the value every icon + label pair in Deckle's XAML uses.
    private const double IconLabelSpacing = 8;

    // Vertical gap between panel rows when the App-level SettingsCardSpacing
    // resource cannot be resolved (it is defined in Deckle.App's App.xaml).
    private const double FallbackRowSpacing = 4;

    // Theme-following 1 px divider. Parsed from XAML so the Fill keeps a real
    // {ThemeResource} reference and follows light, dark and contrast changes
    // while the panel lives; a brush read from code would freeze the theme it
    // was read in. MenuFlyoutSeparator is menu-only, hence the Rectangle.
    private const string DividerXaml =
        "<Rectangle xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" "
        + "Height=\"1\" HorizontalAlignment=\"Stretch\" "
        + "Fill=\"{ThemeResource DividerStrokeColorDefaultBrush}\" />";

    private StackPanel? _content;
    private Button? _recordButton;
    private FontIcon? _recordIcon;
    private TextBlock? _recordLabel;
    private readonly List<(TrayToggle Toggle, ToggleSwitch Switch)> _rows = new();

    // True while Show() writes IsOn from the module state: ToggleSwitch raises
    // Toggled for programmatic changes too, which must not flip the module.
    private bool _syncingSwitches;

    // Content depends on what the App wires (presence). The App wires right
    // after construction, before the first show, so this normally runs once;
    // a later change rebuilds and drops the captured presenter size.
    private void InvalidateContent()
    {
        _content = null;
        _presenterSizeIdle = null;
        _presenterSizeRecording = null;
    }

    private void EnsureContent()
    {
        if (_content is not null) return;

        double rowSpacing = Application.Current.Resources.TryGetValue("SettingsCardSpacing", out var s)
            && s is double d ? d : FallbackRowSpacing;

        var panel = new StackPanel { Spacing = rowSpacing };
        _rows.Clear();

        // Record / Stop recording: the accent action, label set at each show.
        _recordButton = null;
        _recordIcon = null;
        _recordLabel = null;
        if (_onRecord is not null)
        {
            _recordIcon = new FontIcon { Glyph = Glyphs.Microphone };
            _recordLabel = new TextBlock { Text = Loc.Get("TrayPanel_Record") };
            _recordButton = new Button
            {
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = IconLabelSpacing,
                    Children = { _recordIcon, _recordLabel },
                },
            };
            _recordButton.Click += (_, _) => OnRecordClicked();
            panel.Children.Add(_recordButton);
        }

        if (_onTranscribeFiles is not null)
        {
            var transcribe = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = Loc.Get("TrayMenu_TranscribeFile"),
            };
            transcribe.Click += (_, _) => OnTranscribeFilesClicked();
            panel.Children.Add(transcribe);
        }

        // A divider separates each group from what precedes it, never leads.
        foreach (var group in new[] { _toggles, _footerToggles })
        {
            if (group.Count == 0) continue;
            if (panel.Children.Count > 0)
                panel.Children.Add(CreateDivider());
            foreach (var toggle in group)
                panel.Children.Add(CreateToggleRow(toggle));
        }

        _content = panel;
        _flyout.Content = panel;
    }

    private static UIElement CreateDivider() => (UIElement)XamlReader.Load(DividerXaml);

    // One row: label left, ToggleSwitch right. OnContent/OffContent are empty
    // (the label carries the meaning) and MinWidth is released so the switch
    // takes only its knob track instead of the control's default text column.
    private Grid CreateToggleRow(TrayToggle toggle)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        var label = new TextBlock
        {
            Text = toggle.Label,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);

        var toggleSwitch = new ToggleSwitch
        {
            OnContent = string.Empty,
            OffContent = string.Empty,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(toggleSwitch, toggle.Label);
        toggleSwitch.Toggled += (_, _) => OnSwitchToggled(toggle, toggleSwitch);
        Grid.SetColumn(toggleSwitch, 1);

        grid.Children.Add(label);
        grid.Children.Add(toggleSwitch);
        _rows.Add((toggle, toggleSwitch));
        return grid;
    }

    // ── Interactions ─────────────────────────────────────────────────────────

    // A switch flips its module and leaves the panel open: the user may flip
    // several in a row, as in Windows quick settings.
    private void OnSwitchToggled(TrayToggle toggle, ToggleSwitch toggleSwitch)
    {
        if (_syncingSwitches) return;
        DeckleShellTrayMenuSource.Log.PanelToggleClicked();
        DeckleShellTrayMenuSource.Log.PanelToggleClickedDetail(toggle.Id, toggleSwitch.IsOn);
        toggle.Toggle();
    }

    // Actions hide the panel BEFORE invoking: hiding the carrier hands
    // activation back to the window the user was in, which a dictation's
    // paste at Stop needs.
    private void OnRecordClicked()
    {
        bool wasRecording = IsRecording?.Invoke() ?? false;
        DeckleShellTrayMenuSource.Log.PanelActionClicked();
        DeckleShellTrayMenuSource.Log.PanelActionClickedDetail("record", wasRecording);
        Hide("action:record");
        _onRecord?.Invoke();
    }

    private void OnTranscribeFilesClicked()
    {
        DeckleShellTrayMenuSource.Log.PanelActionClicked();
        DeckleShellTrayMenuSource.Log.PanelActionClickedDetail(
            "transcribe-files", IsRecording?.Invoke() ?? false);
        Hide("action:transcribe-files");
        _onTranscribeFiles?.Invoke();
    }

    // Reflects module and engine state at each open.
    private void SyncState(bool isRecording)
    {
        if (_recordIcon is not null)
            _recordIcon.Glyph = isRecording ? Glyphs.AudioRecording : Glyphs.Microphone;
        if (_recordLabel is not null)
            _recordLabel.Text = Loc.Get(isRecording ? "TrayPanel_StopRecording" : "TrayPanel_Record");

        _syncingSwitches = true;
        try
        {
            foreach (var (toggle, toggleSwitch) in _rows)
                toggleSwitch.IsOn = toggle.IsOn();
        }
        finally
        {
            _syncingSwitches = false;
        }
    }
}
