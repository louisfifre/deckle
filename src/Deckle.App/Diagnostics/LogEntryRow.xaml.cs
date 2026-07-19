using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.App;

public sealed partial class LogEntryRow : UserControl
{
    public static readonly DependencyProperty MessageWrappingProperty =
        DependencyProperty.Register(
            nameof(MessageWrapping),
            typeof(TextWrapping),
            typeof(LogEntryRow),
            new PropertyMetadata(TextWrapping.NoWrap));

    public LogEntryRow()
    {
        InitializeComponent();
    }

    public TextWrapping MessageWrapping
    {
        get => (TextWrapping)GetValue(MessageWrappingProperty);
        set => SetValue(MessageWrappingProperty, value);
    }
}
