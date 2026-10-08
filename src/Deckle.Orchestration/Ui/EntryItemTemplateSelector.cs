using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Orchestration;

// Picks the card template for each item of an entry's expander. The expander
// takes either static Items or an ItemsSource, never both; the monitor cards
// vary at run time, so every card goes through ItemsSource and this selector.
public sealed partial class EntryItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? KindTemplate { get; set; }

    public DataTemplate? ExecutableTemplate { get; set; }

    public DataTemplate? MonitorHdrTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        EntryKindItem => KindTemplate,
        EntryExecutableItem => ExecutableTemplate,
        MonitorHdrRow => MonitorHdrTemplate,
        _ => null,
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
