using System;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Deckle.Catalog;

public sealed partial class SettingsComposer
{
    private const uint ExtendedScanCodePrefix = 0xE000;

    // A global hotkey chord: four modifier ToggleButtons (Ctrl, Shift, Alt, Win)
    // and one Button that captures the key, plus Clear when the chord may be
    // unbound. Modifiers are toggles rather than read from a pressed chord
    // because pressing Win opens Start before the page sees the key; only a
    // low-level hook avoids that, and a settings card does not need one.
    //
    // The control edits a draft (modifiers + scancode) and writes it to the model
    // only when it is a complete chord. An incomplete draft (a key without a
    // modifier) stays on screen with the kind's advisory, and the model keeps its
    // previous chord, so the registered hotkey never becomes a bare key. The
    // refresher reloads the draft only when the MODEL value changes (a reset, a
    // write from elsewhere), so an unrelated PropertyChanged on the page does not
    // throw away an edit in progress.
    private void BuildHotkey(SettingsCard card, SettingDescriptor s)
    {
        var args = (HotkeyArgs)s.Args!;

        string pressKey = Loc.Get("SettingsComposer_HotkeyPressKey");
        string unbound = Loc.Get("SettingsComposer_HotkeyUnbound");
        string needsModifier = Loc.Get("SettingsComposer_HotkeyNeedsModifier");

        ToggleButton ctrl = ModifierToggle("SettingsComposer_HotkeyModifierCtrl");
        ToggleButton shift = ModifierToggle("SettingsComposer_HotkeyModifierShift");
        ToggleButton alt = ModifierToggle("SettingsComposer_HotkeyModifierAlt");
        ToggleButton win = ModifierToggle("SettingsComposer_HotkeyModifierWin");

        // MinWidth keeps the row from jumping between "Not set", "Press a key"
        // and a one-character key name.
        var keyButton = new Button { MinWidth = 96, VerticalAlignment = VerticalAlignment.Center };

        Button? clear = args.AllowUnbound
            ? new Button
            {
                Content = Loc.Get("SettingsComposer_HotkeyClear"),
                VerticalAlignment = VerticalAlignment.Center,
            }
            : null;

        HotkeyChord? synced = (HotkeyChord?)s.GetValue();
        HotkeyModifiers draftModifiers = synced?.Modifiers ?? HotkeyModifiers.None;
        uint draftKey = synced?.ScanCode ?? 0;
        string? kindMessage = null;
        bool listening = false;
        bool swallowKeyUp = false;

        _kindAdvisories[s] = () => kindMessage;

        void LoadDraft(HotkeyChord? value)
        {
            synced = value;
            draftModifiers = value?.Modifiers ?? HotkeyModifiers.None;
            draftKey = value?.ScanCode ?? 0;
            kindMessage = null;
        }

        void ShowDraft()
        {
            ctrl.IsChecked = draftModifiers.HasFlag(HotkeyModifiers.Control);
            shift.IsChecked = draftModifiers.HasFlag(HotkeyModifiers.Shift);
            alt.IsChecked = draftModifiers.HasFlag(HotkeyModifiers.Alt);
            win.IsChecked = draftModifiers.HasFlag(HotkeyModifiers.Win);

            keyButton.Content = listening ? pressKey
                : draftKey == 0 ? unbound
                : HotkeyKeyNames.Get(draftKey);

            string? chordText = draftKey == 0 ? null
                : HotkeyChordText.Format(new HotkeyChord(draftModifiers, draftKey));
            ToolTipService.SetToolTip(keyButton, chordText);

            if (clear is not null)
                clear.IsEnabled = synced is not null || draftKey != 0 || draftModifiers != HotkeyModifiers.None;
        }

        // Writes the draft when it is a complete chord; otherwise keeps the model
        // and says what is missing. A draft with no key yet says nothing: the user
        // is still choosing modifiers.
        void Commit()
        {
            var chord = new HotkeyChord(draftModifiers, draftKey);
            if (draftKey == 0)
            {
                kindMessage = null;
            }
            else if (!chord.IsComplete)
            {
                kindMessage = needsModifier;
            }
            else
            {
                kindMessage = null;
                if (!Equals(s.GetValue(), chord))
                {
                    synced = chord;
                    s.SetValue(chord);
                }
            }
            RefreshAdvisory(s);
        }

        void StopListening()
        {
            if (!listening) return;
            listening = false;
            ShowDraft();
        }

        void OnModifierClick(object sender, RoutedEventArgs e)
        {
            draftModifiers = HotkeyModifiers.None;
            if (ctrl.IsChecked == true) draftModifiers |= HotkeyModifiers.Control;
            if (shift.IsChecked == true) draftModifiers |= HotkeyModifiers.Shift;
            if (alt.IsChecked == true) draftModifiers |= HotkeyModifiers.Alt;
            if (win.IsChecked == true) draftModifiers |= HotkeyModifiers.Win;
            Commit();
            ShowDraft();
        }

        // Click, not Checked/Unchecked: Click fires only on a user gesture, so
        // ShowDraft setting IsChecked from the model never loops back here.
        ctrl.Click += OnModifierClick;
        shift.Click += OnModifierClick;
        alt.Click += OnModifierClick;
        win.Click += OnModifierClick;

        keyButton.Click += (_, _) =>
        {
            if (listening)
            {
                StopListening();
                return;
            }
            listening = true;
            ShowDraft();
        };

        // PreviewKeyDown runs before the Button's own key handling, so Enter and
        // Space are captured as keys instead of clicking the button again.
        keyButton.PreviewKeyDown += (_, e) =>
        {
            if (!listening) return;
            e.Handled = true;

            switch (e.Key)
            {
                case VirtualKey.Escape:
                    swallowKeyUp = true;
                    StopListening();
                    return;
                case VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift:
                case VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl:
                case VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu:
                case VirtualKey.LeftWindows or VirtualKey.RightWindows:
                    // A modifier alone is not a key: keep listening.
                    return;
            }

            uint scanCode = e.KeyStatus.ScanCode & 0xFF;
            if (scanCode == 0) return;
            if (e.KeyStatus.IsExtendedKey) scanCode |= ExtendedScanCodePrefix;

            // Modifiers held while pressing the key replace the toggles, so the
            // natural gesture (hold Ctrl+Shift, press K) works. With none held the
            // toggles stand.
            HotkeyModifiers held = HeldModifiers();
            if (held != HotkeyModifiers.None) draftModifiers = held;

            draftKey = scanCode;
            listening = false;
            swallowKeyUp = true;
            Commit();
            ShowDraft();
        };

        // The key-up of a captured key (or of Escape) belongs to the capture: let
        // it reach the button and Space would click it back into listening.
        keyButton.PreviewKeyUp += (_, e) =>
        {
            if (!swallowKeyUp) return;
            swallowKeyUp = false;
            e.Handled = true;
        };

        keyButton.LostFocus += (_, _) => StopListening();

        if (clear is not null)
        {
            clear.Click += (_, _) =>
            {
                listening = false;
                LoadDraft(null);
                if (s.GetValue() is not null) s.SetValue(null);
                ShowDraft();
                RefreshAdvisory(s);
            };
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(ctrl);
        row.Children.Add(shift);
        row.Children.Add(alt);
        row.Children.Add(win);
        row.Children.Add(keyButton);
        if (clear is not null) row.Children.Add(clear);

        ShowDraft();

        // The tooltip reads the default chord as the key button's tooltip reads
        // the current one; an unbound default reads as the button's own wording.
        (FrameworkElement content, Action? updateReset) = WrapWithReset(card, row, s,
            () => HotkeyChordText.Format((HotkeyChord?)s.Default!()) ?? unbound);
        card.Content = content;

        _refreshers.Add(() =>
        {
            var value = (HotkeyChord?)s.GetValue();
            if (!Equals(value, synced))
            {
                listening = false;
                LoadDraft(value);
            }
            ShowDraft();
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    private static ToggleButton ModifierToggle(string labelKey) => new()
    {
        Content = Loc.Get(labelKey),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static HotkeyModifiers HeldModifiers()
    {
        HotkeyModifiers held = HotkeyModifiers.None;
        if (IsDown(VirtualKey.Control)) held |= HotkeyModifiers.Control;
        if (IsDown(VirtualKey.Shift)) held |= HotkeyModifiers.Shift;
        if (IsDown(VirtualKey.Menu)) held |= HotkeyModifiers.Alt;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) held |= HotkeyModifiers.Win;
        return held;
    }

    private static bool IsDown(VirtualKey key)
        => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
