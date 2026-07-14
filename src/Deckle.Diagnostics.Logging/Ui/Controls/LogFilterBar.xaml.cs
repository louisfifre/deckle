using System.Collections.ObjectModel;
using System.Diagnostics.Tracing;
using System.Text;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Deckle.Diagnostics.Logging.Ui.Controls;

public sealed partial class LogFilterBar : UserControl
{
    private const string ResourceLibrary = "Deckle.Diagnostics.Logging";
    private readonly ObservableCollection<ActiveLogFilterChip> _activeFilters = [];
    private readonly SortedDictionary<string, string> _modules = new(StringComparer.Ordinal);
    private LogFilterSelection? _selection;
    private bool _isSubscribed;
    private bool _isSynchronizingOptions;

    public event EventHandler? FilterChanged;

    public LogFilterSelection Selection
    {
        get => _selection ?? throw new InvalidOperationException("A filter selection has not been assigned.");
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_selection, value)) return;
            Unsubscribe();
            _selection = value;
            Subscribe();
            Refresh();
        }
    }

    public LogFilterBar()
    {
        InitializeComponent();
        ActiveFilters.ItemsSource = _activeFilters;
        BuildFixedOptions();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    public void Observe(EventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_modules.ContainsKey(entry.Provider)) return;
        _modules.Add(entry.Provider, FormatModule(entry.Provider));
        RebuildOptions(ModuleOptions, LogFilterDimension.Module,
            OrderedModules());
    }

    public void Detach() => Unsubscribe();

    private void BuildFixedOptions()
    {
        RebuildOptions(SeverityOptions, LogFilterDimension.Severity,
            LogFilterSelection.SeverityOrder.Select(level =>
                new LogFilterOption(level.ToString(), Localize("LogFilter_Severity_" + level))));

        RebuildOptions(CategoryOptions, LogFilterDimension.Category,
            LogFilterSelection.CategoryOrder.Select(category =>
                new LogFilterOption(category.ToString(), Localize("LogFilter_Category_" + category))));
    }

    private void RebuildOptions(
        ListView host,
        LogFilterDimension dimension,
        IEnumerable<LogFilterOption> options)
    {
        bool wasSynchronizing = _isSynchronizingOptions;
        _isSynchronizingOptions = true;
        try
        {
            host.Items.Clear();
            foreach (LogFilterOption option in options)
            {
                var token = new LogFilterToken(dimension, option.Value);
                var item = new ListViewItem
                {
                    Content = option.Label,
                    Style = (Style)Resources["LogFilterListViewItemStyle"],
                    Tag = token,
                };
                host.Items.Add(item);
                item.IsSelected = _selection?.Contains(token) == true;
            }
        }
        finally
        {
            _isSynchronizingOptions = wasSynchronizing;
        }
    }

    private void OnOptionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingOptions) return;

        _isSynchronizingOptions = true;
        try
        {
            foreach (ListViewItem item in e.RemovedItems.OfType<ListViewItem>())
                if (item.Tag is LogFilterToken token)
                    Selection.Remove(token);

            foreach (ListViewItem item in e.AddedItems.OfType<ListViewItem>())
                if (item.Tag is LogFilterToken token)
                    Selection.Add(token);
        }
        finally
        {
            _isSynchronizingOptions = false;
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e) => Selection.Clear();

    private void OnRemoveFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ActiveLogFilterChip chip })
            Selection.Remove(chip.Token);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        Refresh();
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Subscribe()
    {
        if (_selection is null || _isSubscribed) return;
        _selection.Changed += OnSelectionChanged;
        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (_selection is null || !_isSubscribed) return;
        _selection.Changed -= OnSelectionChanged;
        _isSubscribed = false;
    }

    private void Refresh()
    {
        if (_selection is null) return;

        _activeFilters.Clear();
        foreach (LogFilterToken token in _selection.GetTokens())
        {
            string dimension = Localize("LogFilter_Dimension_" + token.Dimension);
            string value = GetTokenLabel(token);
            string label = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                Localize("LogFilter_Chip_Format"), dimension, value);
            _activeFilters.Add(new ActiveLogFilterChip(
                token,
                label,
                string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    Localize("LogFilter_Remove_Format"), label)));
        }

        ActiveFiltersScroller.Visibility = _activeFilters.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ResetButton.IsEnabled = _activeFilters.Count > 0;
        ResetButton.Visibility = _activeFilters.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        SynchronizeOptions(SeverityOptions);
        SynchronizeOptions(ModuleOptions);
        SynchronizeOptions(CategoryOptions);
    }

    private void SynchronizeOptions(ListView host)
    {
        bool wasSynchronizing = _isSynchronizingOptions;
        _isSynchronizingOptions = true;
        try
        {
            foreach (ListViewItem item in host.Items.OfType<ListViewItem>())
                if (item.Tag is LogFilterToken token)
                    item.IsSelected = Selection.Contains(token);
        }
        finally
        {
            _isSynchronizingOptions = wasSynchronizing;
        }
    }

    private string GetTokenLabel(LogFilterToken token) => token.Dimension switch
    {
        LogFilterDimension.Severity => Localize("LogFilter_Severity_" + token.Value),
        LogFilterDimension.Category => Localize("LogFilter_Category_" + token.Value),
        LogFilterDimension.Module => _modules.TryGetValue(token.Value, out string? label)
            ? label
            : FormatModule(token.Value),
        _ => token.Value,
    };

    private static string FormatModule(string provider)
    {
        string name = provider.StartsWith("Deckle-", StringComparison.Ordinal)
            ? provider[7..]
            : provider;
        string? normalized = name switch
        {
            "Whisp" => Localize("LogFilter_Module_Dictation"),
            "Llm" => "LLM",
            "Vad" => "VAD",
            "AnytypeMcp" => "Anytype MCP",
            "SettingsUx" => "Settings UX",
            _ => null,
        };
        if (normalized is not null) return normalized;

        var builder = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
                builder.Append(' ');
            builder.Append(name[i]);
        }
        return builder.ToString();
    }

    private static string Localize(string key) => Loc.GetFrom(ResourceLibrary, key);

    private IEnumerable<LogFilterOption> OrderedModules()
        => _modules
            .Select(pair => new LogFilterOption(pair.Key, pair.Value))
            .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase);

    private static void SetCloseGlyphOpacity(object sender, double opacity)
    {
        if (sender is Button { Content: StackPanel panel } &&
            panel.Children.Count > 1 && panel.Children[1] is FontIcon icon)
            icon.Opacity = opacity;
    }

    private void OnChipPointerEntered(object sender, PointerRoutedEventArgs e)
        => SetCloseGlyphOpacity(sender, 1);

    private void OnChipPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button && button.FocusState == FocusState.Unfocused)
            SetCloseGlyphOpacity(sender, 0);
    }

    private void OnChipGotFocus(object sender, RoutedEventArgs e)
        => SetCloseGlyphOpacity(sender, 1);

    private void OnChipLostFocus(object sender, RoutedEventArgs e)
        => SetCloseGlyphOpacity(sender, 0);

    private readonly record struct LogFilterOption(string Value, string Label);
}

public sealed record ActiveLogFilterChip(
    LogFilterToken Token,
    string Label,
    string AccessibleName);
