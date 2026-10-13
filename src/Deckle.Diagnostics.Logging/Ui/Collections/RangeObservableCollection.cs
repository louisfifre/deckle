using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Deckle.Diagnostics.Logging.Ui.Collections;

// ObservableCollection has no bulk replacement API: rebuilding the log
// projection item by item would send WinUI one notification per row. This
// collection mutates its backing list under one Reset notification.
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CheckReentrancy();
        Items.Clear();
        foreach (T item in items) Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }
}
