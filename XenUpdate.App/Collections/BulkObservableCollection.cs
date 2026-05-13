using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace XenUpdate.App.Collections;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> with a <see cref="ReplaceAll(IEnumerable{T})"/>
/// helper that swaps all items in a single bulk operation. Instead of firing one
/// <see cref="NotifyCollectionChangedAction.Add"/> per item, it raises a single
/// <see cref="NotifyCollectionChangedAction.Reset"/>, which virtualizing UI controls
/// (DataGrid, ListView) can handle far more efficiently for large result sets.
/// Regular <see cref="ObservableCollection{T}"/> semantics are preserved for all other APIs.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>Creates an empty collection.</summary>
    public BulkObservableCollection() { }

    /// <summary>Creates the collection pre-populated with the given items.</summary>
    public BulkObservableCollection(IEnumerable<T> collection) : base(collection) { }

    /// <summary>
    /// Clears the collection, adds every item from <paramref name="items"/>, and
    /// raises a single Reset notification afterwards. Safe to call on the UI thread.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();

        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
