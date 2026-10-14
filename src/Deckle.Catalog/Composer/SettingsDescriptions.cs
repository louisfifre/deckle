using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;

namespace Deckle.Catalog;

// ── SettingsDescriptions ──────────────────────────────────────────────────────
//
// The one switch that shows or hides every card description on every settings
// page, driven by the Info button beside Reset all in the page header
// (2026-10-14). Louis read the pages with their descriptions and found them
// heavy with text: a page should read as its cards, and the long form of each
// setting should come on demand, the way a help panel does. The descriptions
// are not deleted: a card keeps its full text and the switch decides whether
// the card shows it.
//
// What the switch covers: the description of a composed card or fold (the
// composer attaches every one it builds), the sentence a curve card computes
// from its points, and the hand-authored cards a page marks with the Toggled
// attached property. What it never covers: a card whose Description carries
// data (a path, a status), an advisory bar, a section caption or the page's
// own line under its title, which are short and say where the user is.
//
// The state is process-wide, so every open page follows one click; the host
// persists it through Persist (the shell stores it with its appearance
// settings) and seeds IsShown at boot. Default hidden: the lean page is the
// page, the Info button is the way to the long form.
public static class SettingsDescriptions
{
    private static bool _isShown;

    // A tracked element keeps its full description here, whatever the card
    // currently shows. Weak keys: a page that is collected takes its cards'
    // entries with it; the list below is pruned on every change.
    private static readonly ConditionalWeakTable<DependencyObject, Entry> _entries = new();
    private static readonly List<WeakReference<DependencyObject>> _tracked = new();

    private sealed class Entry
    {
        public required DependencyProperty Property { get; init; }
        public string? Text { get; set; }
    }

    public static bool IsShown
    {
        get => _isShown;
        set
        {
            if (_isShown == value) return;
            _isShown = value;
            ApplyAll();
            Changed?.Invoke(null, EventArgs.Empty);
            Persist?.Invoke(value);
        }
    }

    // Raised after every change, once the cards are updated, so a header
    // button can follow a click made on another page.
    public static event EventHandler? Changed;

    // Host-supplied: writes the choice where the shell keeps it. Null until the
    // app wires it at boot; the switch then still works for the session.
    public static Action<bool>? Persist { get; set; }

    // The composer's entry point: remembers the card's full description and
    // applies the current state. Calling it again with a new text (a curve's
    // sentence re-read on refresh) replaces the stored text and re-applies, so
    // a refresh never shows a description the switch hides.
    public static void Attach(SettingsCard card, string? text)
        => Attach(card, SettingsCard.DescriptionProperty, text);

    public static void Attach(SettingsExpander expander, string? text)
        => Attach(expander, SettingsExpander.DescriptionProperty, text);

    private static void Attach(DependencyObject element, DependencyProperty property, string? text)
    {
        if (_entries.TryGetValue(element, out Entry? entry))
        {
            entry.Text = text;
        }
        else
        {
            entry = new Entry { Property = property, Text = text };
            _entries.Add(element, entry);
            _tracked.Add(new WeakReference<DependencyObject>(element));
        }
        Apply(element, entry);
    }

    // ── Toggled attached property (hand-authored cards) ──────────────────────
    //
    // `catalog:SettingsDescriptions.Toggled="True"` on a SettingsCard or a
    // SettingsExpander in XAML. The description is read at the element's
    // Loaded, after x:Uid has applied the .resw text, so the attribute's order
    // among the element's attributes does not matter.
    public static readonly DependencyProperty ToggledProperty = DependencyProperty.RegisterAttached(
        "Toggled", typeof(bool), typeof(SettingsDescriptions),
        new PropertyMetadata(false, OnToggledChanged));

    public static bool GetToggled(DependencyObject element) => (bool)element.GetValue(ToggledProperty);
    public static void SetToggled(DependencyObject element, bool value) => element.SetValue(ToggledProperty, value);

    private static void OnToggledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue || d is not FrameworkElement element) return;
        if (element.IsLoaded) TrackLoaded(element);
        else element.Loaded += OnTrackedLoaded;
    }

    private static void OnTrackedLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnTrackedLoaded;
        TrackLoaded(element);
    }

    private static void TrackLoaded(FrameworkElement element)
    {
        // Already tracked (a cached page reloads): the stored text stands.
        if (_entries.TryGetValue(element, out _)) return;
        switch (element)
        {
            case SettingsCard card:
                Attach(card, card.Description as string);
                break;
            case SettingsExpander expander:
                Attach(expander, expander.Description as string);
                break;
            case PreviewBlock block:
                // The block forwards its Description to the expander it wraps.
                Attach(block, PreviewBlock.DescriptionProperty, block.Description);
                break;
        }
    }

    // ── Applying ─────────────────────────────────────────────────────────────

    // Null, not empty: SettingsCard collapses its description row only on a
    // null Description.
    private static void Apply(DependencyObject element, Entry entry)
        => element.SetValue(entry.Property, _isShown ? entry.Text : null);

    private static void ApplyAll()
    {
        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            if (_tracked[i].TryGetTarget(out DependencyObject? element)
                && _entries.TryGetValue(element, out Entry? entry))
            {
                Apply(element, entry);
            }
            else
            {
                _tracked.RemoveAt(i);
            }
        }
    }
}
