using System.Collections.Generic;
using System.ComponentModel;

namespace HebrewBooks.Core.Catalog;

public sealed class GroupHeaderRow : CatalogRow, INotifyPropertyChanged
{
	private bool _isExpanded;

	private static readonly PropertyChangedEventArgs _isExpandedArgs = new PropertyChangedEventArgs("IsExpanded");

	public string Title { get; }

	public string Author { get; }

	public IReadOnlyList<BookRow> Children { get; }

	public int Count => Children.Count;

	public string? BookName => Title;

	public string? AuthorName => Author;

	public bool IsExpanded
	{
		get
		{
			return _isExpanded;
		}
		set
		{
			if (_isExpanded != value)
			{
				_isExpanded = value;
				this.PropertyChanged?.Invoke(this, _isExpandedArgs);
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public GroupHeaderRow(string title, string author, IReadOnlyList<BookRow> children)
	{
		Title = title;
		Author = author;
		Children = children;
	}
}
