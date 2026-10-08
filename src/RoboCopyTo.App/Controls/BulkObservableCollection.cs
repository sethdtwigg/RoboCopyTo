using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace RoboCopyTo.App.Controls;

/// <summary>
/// An ObservableCollection that can append a batch and trim to a cap with a single Reset notification,
/// instead of one notification (and one list shift) per line.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void AppendCapped(IReadOnlyList<T> batch, int cap)
    {
        if (batch.Count == 0)
            return;
        var overflow = Count + batch.Count - cap;
        if (overflow <= 0 && batch.Count == 1)
        {
            Add(batch[0]);
            return;
        }

        CheckReentrancy();
        var items = (List<T>)Items;
        if (overflow > 0)
        {
            var fromExisting = Math.Min(overflow, items.Count);
            items.RemoveRange(0, fromExisting);
            var fromBatch = overflow - fromExisting;
            items.AddRange(fromBatch > 0 ? batch.Skip(fromBatch) : batch);
        }
        else
        {
            items.AddRange(batch);
        }
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
