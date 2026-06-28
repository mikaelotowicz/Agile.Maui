using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Agile.Maui;

/// <summary>
/// Observable collection optimized for virtualized lists.
/// </summary>
/// <remarks>
/// <see cref="AddRange"/> emits a single add notification for the whole batch, which
/// lets native virtualized controls update one page at a time instead of processing
/// one change notification per item.
/// </remarks>
public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    private static readonly PropertyChangedEventArgs CountChanged = new(nameof(Count));
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");

    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();

        var list = items.ToList();

        if (list.Count == 0)
            return;

        var startIndex = Count;

        foreach (var item in list)
            Items.Add(item);

        OnPropertyChanged(CountChanged);
        OnPropertyChanged(IndexerChanged);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, (IList)list, startIndex));
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();

        var list = items.ToList();
        var previousCount = Count;

        if (previousCount == 0 && list.Count == 0)
            return;

        Items.Clear();

        foreach (var item in list)
            Items.Add(item);

        OnPropertyChanged(CountChanged);
        OnPropertyChanged(IndexerChanged);

        if (previousCount == 0 && list.Count > 0)
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add, (IList)list, 0));
            return;
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }
}
