using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace HebrewBooks.UI.Collections;

public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
	public void ReplaceAll(IEnumerable<T> items)
	{
		base.Items.Clear();
		foreach (T item in items)
		{
			base.Items.Add(item);
		}
		OnPropertyChanged(new PropertyChangedEventArgs("Count"));
		OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
	}

	public void InsertRange(int index, IEnumerable<T> items)
	{
		foreach (T item in items)
		{
			Insert(index++, item);
		}
	}

	public void RemoveRange(int index, int count)
	{
		for (int i = 0; i < count; i++)
		{
			RemoveAt(index);
		}
	}
}
