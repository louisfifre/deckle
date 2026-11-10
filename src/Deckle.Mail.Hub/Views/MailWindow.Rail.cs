using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Deckle.Catalog;

namespace Deckle.Mail.Hub;

// ── The navigation ────────────────────────────────────────────────────────────
//
// Built from MailRail, which holds the rules (order, groups, numbers); this
// file only turns its entries into NavigationViewItems. A structural change (a
// mailbox added, renamed, removed, Activity's first line) rebuilds the items;
// a gesture only refreshes the numbers, so the rail never flickers under the
// hand.
//
// The numbers are native InfoBadges. A mailbox that cannot connect wears the
// caution style of the badge, a dot or its number in caution colour, and its
// name says so to a screen reader. The frames draw the dot on the mark
// itself; a NavigationViewItem's icon is an IconElement and cannot carry a
// badge, so the native badge slot carries it, and the rail still does not
// move when a mailbox fails.
public sealed partial class MailWindow
{
    // Groups the person folded. A group starts unfolded: its children are the
    // only way to its pages.
    private readonly HashSet<string> _folded = new(StringComparer.Ordinal);

    private readonly List<(NavigationViewItem Item, RailEntry Entry)> _railItems = [];
    private bool _syncingSelection;

    // The open pane fits its longest label between these two widths. Neither
    // is set by the spec (the current design is 216 px); kept here alone so
    // the maintainer moves them in one place.
    private const double MinPaneWidth = 200;
    private const double MaxPaneWidth = 296;

    private void RebuildRail()
    {
        _railItems.Clear();
        Nav.MenuItems.Clear();
        Nav.FooterMenuItems.Clear();

        IReadOnlyList<RailEntry> entries = MailRail.Build(Session, id => !_folded.Contains(id));
        bool separated = false;
        foreach (RailEntry entry in entries)
        {
            if (entry.Kind == RailEntryKind.Settings)
            {
                Nav.FooterMenuItems.Add(BuildItem(entry));
                continue;
            }
            if (entry.MailboxId is not null && !separated)
            {
                Nav.MenuItems.Add(new NavigationViewItemSeparator());
                separated = true;
            }
            Nav.MenuItems.Add(BuildItem(entry));
        }

        FitPaneToLabels();
        if (_history.Current is { } current) SyncRailSelection(current);
    }

    // Numbers only: the items stay, so focus and selection stay too.
    private void RefreshRailCounts()
    {
        IReadOnlyList<RailEntry> fresh = MailRail.Build(Session, id => !_folded.Contains(id));
        var byKey = new Dictionary<string, RailEntry>(StringComparer.Ordinal);
        void Index(IEnumerable<RailEntry> list)
        {
            foreach (RailEntry e in list)
            {
                byKey[Key(e)] = e;
                Index(e.Children);
            }
        }
        Index(fresh);

        for (int i = 0; i < _railItems.Count; i++)
        {
            (NavigationViewItem item, RailEntry old) = _railItems[i];
            if (!byKey.TryGetValue(Key(old), out RailEntry? entry)) continue;
            _railItems[i] = (item, entry);
            ApplyBadge(item, entry);
        }
    }

    private static string Key(RailEntry e) => $"{e.Kind}|{e.MailboxId}|{e.AliasId}";

    private NavigationViewItem BuildItem(RailEntry entry)
    {
        var item = new NavigationViewItem
        {
            Content = LabelOf(entry),
            Tag = entry,
        };

        IconElement? icon = entry.Kind switch
        {
            RailEntryKind.Triage => new FontIcon { Glyph = Glyphs.Home },
            RailEntryKind.Activity => new FontIcon { Glyph = Glyphs.Mail.Activity },
            RailEntryKind.Settings => new FontIcon { Glyph = Glyphs.Settings },
            RailEntryKind.Group or RailEntryKind.Mailbox => MailboxMark(LabelOf(entry)),
            // A child stands one step in from its group, without an icon, as
            // Windows draws a group.
            _ => null,
        };
        if (icon is not null) item.Icon = icon;

        if (entry.Kind == RailEntryKind.Group)
        {
            // A group header folds and unfolds; it opens no page.
            item.SelectsOnInvoked = false;
            item.IsExpanded = !_folded.Contains(entry.MailboxId!);
            foreach (RailEntry child in entry.Children)
                item.MenuItems.Add(BuildItem(child));
        }

        ApplyBadge(item, entry);
        _railItems.Add((item, entry));
        return item;
    }

    private static string LabelOf(RailEntry entry) => entry.Kind switch
    {
        RailEntryKind.Triage => MailText.Get("Nav_Triage"),
        RailEntryKind.Activity => MailText.Get("Nav_Activity"),
        RailEntryKind.Settings => MailText.Get("Nav_Settings"),
        RailEntryKind.Inbox => MailText.Get("Nav_Inbox"),
        _ => entry.Label ?? "",
    };

    // The initial alone, drawn at the size of the other icons, with no
    // circle (spec, Navigation; Open 9). A FontIcon in the display face, so
    // it scales and recolours with the rail like any glyph.
    private static FontIcon MailboxMark(string label) => new()
    {
        Glyph = MailNames.Initial(label),
        FontFamily = new FontFamily("Segoe UI Variable Display"),
        FontSize = 16,
    };

    private void ApplyBadge(NavigationViewItem item, RailEntry entry)
    {
        string label = LabelOf(entry);
        if (entry.CannotConnect)
        {
            item.InfoBadge = entry.Count is { } failingCount
                ? new InfoBadge { Value = failingCount, Style = ThemeStyle("CautionValueInfoBadgeStyle") }
                : new InfoBadge { Style = ThemeStyle("CautionDotInfoBadgeStyle") };
            AutomationProperties.SetName(item, MailText.Format("Nav_CannotConnect_Format", label));
            ToolTipService.SetToolTip(item, MailText.Format("Nav_CannotConnect_Format", label));
            return;
        }

        item.InfoBadge = entry.Count is { } count ? new InfoBadge { Value = count } : null;
        AutomationProperties.SetName(item, entry.Count is { } n ? MailText.Format("Nav_Waiting_Format", label, n) : label);
        // The full label on hover: a label longer than the pane is cut with an
        // ellipsis before its number.
        ToolTipService.SetToolTip(item, entry.MailboxId is null ? null : label);
    }

    private static Style? ThemeStyle(string key)
        => Application.Current.Resources.TryGetValue(key, out object? style) ? style as Style : null;

    private void OnNavExpanding(NavigationView sender, NavigationViewItemExpandingEventArgs args)
    {
        if (args.ExpandingItemContainer?.Tag is RailEntry { MailboxId: { } id })
        {
            _folded.Remove(id);
            RefreshRailCounts();
        }
    }

    private void OnNavCollapsed(NavigationView sender, NavigationViewItemCollapsedEventArgs args)
    {
        if (args.CollapsedItemContainer?.Tag is RailEntry { MailboxId: { } id })
        {
            _folded.Add(id);
            RefreshRailCounts();
        }
    }

    // Selection follows the page, never the other way round: navigation goes
    // through ItemInvoked, so selecting here opens nothing. An opened subject
    // keeps the item of the list it came from.
    private void SyncRailSelection(MailDestination destination)
    {
        MailDestination? target = destination.Page switch
        {
            MailPage.Subject => null,
            MailPage.MailboxSettings or MailPage.AddMailbox => MailDestination.Settings,
            MailPage.Activity => MailDestination.Activity(),
            _ => destination,
        };
        if (target is null) return;

        foreach ((NavigationViewItem item, RailEntry entry) in _railItems)
        {
            if (entry.Destination is { } d && SameRailPlace(d, target))
            {
                _syncingSelection = true;
                try { Nav.SelectedItem = item; }
                finally { _syncingSelection = false; }
                return;
            }
        }
    }

    private static bool SameRailPlace(MailDestination a, MailDestination b)
        => a.Page == b.Page && a.MailboxId == b.MailboxId && a.AliasId == b.AliasId;

    // ── Rail width ───────────────────────────────────────────────────────────
    //
    // The open pane is as wide as its longest label needs, as SettingsWindow
    // measures it (the chrome constants are read there from the WinUI
    // left-pane item template), plus the badge of an item that wears one,
    // clamped between the two widths above.
    private const double ItemMargins = 8;
    private const double ContentPresenterMargins = 4 + 8;
    private const double ContentGridRightMargin = 14;
    private const double ChevronColumn = 40 - 14;
    private const double CollapsedIconColumn = 8;
    private const double ChildIndentation = 31;
    private const double BadgeColumn = 24;

    private void FitPaneToLabels()
    {
        double labelChrome = ContentPresenterMargins + ContentGridRightMargin;
        double widest = MinPaneWidth;

        foreach ((NavigationViewItem item, RailEntry entry) in _railItems)
        {
            bool isChild = entry.Kind is RailEntryKind.Inbox or RailEntryKind.Alias;
            double row = isChild
                ? ItemMargins + ChildIndentation + CollapsedIconColumn + labelChrome + MeasureLabel(item)
                : Nav.CompactPaneLength + labelChrome + MeasureLabel(item);
            if (entry.Kind == RailEntryKind.Group) row += ChevronColumn;
            if (entry.MailboxId is not null) row += BadgeColumn;
            widest = Math.Max(widest, row);
        }

        Nav.OpenPaneLength = Math.Ceiling(Math.Min(widest, MaxPaneWidth));
    }

    private static double MeasureLabel(NavigationViewItem item)
    {
        if (item.Content is not string text || text.Length == 0) return 0;
        var probe = new TextBlock
        {
            Text = text,
            Style = Application.Current.Resources["BodyTextBlockStyle"] as Style,
        };
        probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(probe.DesiredSize.Width);
    }
}
