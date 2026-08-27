using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.UI.Resources;
using HebrewBooks.UI.Services;

namespace HebrewBooks.UI.ViewModels;

public partial class TocEditorViewModel : ObservableObject
{
	private readonly ICatalogRepository _catalog;

	private readonly TocContributor? _contributor;

	private int _bookId;

	private string? _fileId;

	private string _sourceType = "PDF";

	[ObservableProperty]
	private string _bookTitle = string.Empty;

	[ObservableProperty]
	private TocEntryRow? _selectedEntry;

	[ObservableProperty]
	private string _statusText = string.Empty;

	private const int MaxTocLevel = 5;








	public ObservableCollection<TocEntryRow> Entries { get; } = new ObservableCollection<TocEntryRow>();











	public event Action? Saved;

	public TocEditorViewModel(ICatalogRepository catalog, TocContributor? contributor = null)
	{
		_catalog = catalog;
		_contributor = contributor;
	}

	public async Task LoadAsync(int bookId, string bookTitle, string? fileId = null, string? sourceType = null)
	{
		_bookId = bookId;
		BookTitle = bookTitle;
		_fileId = fileId;
		_sourceType = sourceType ?? "PDF";
		Entries.Clear();
		foreach (TocEntry item in await _catalog.GetTocAsync(bookId))
		{
			Entries.Add(new TocEntryRow(item.Title, item.Page, item.Level));
		}
		StatusText = ((Entries.Count == 0) ? SharedStrings.S979 : $"{Entries.Count}{SharedStrings.S2339}");
	}

	[RelayCommand]
	private void AddRow()
	{
		int num;
		if (Entries.Count <= 0)
		{
			num = 1;
		}
		else
		{
			ObservableCollection<TocEntryRow> entries = Entries;
			num = entries[entries.Count - 1].Page + 1;
		}
		int page = num;
		int num2;
		if (Entries.Count <= 0)
		{
			num2 = 0;
		}
		else
		{
			ObservableCollection<TocEntryRow> entries2 = Entries;
			num2 = entries2[entries2.Count - 1].Level;
		}
		int level = num2;
		TocEntryRow tocEntryRow = new TocEntryRow(string.Empty, page, level);
		Entries.Add(tocEntryRow);
		SelectedEntry = tocEntryRow;
	}

	[RelayCommand]
	private void Indent()
	{
		TocEntryRow selectedEntry = SelectedEntry;
		if (selectedEntry != null && selectedEntry.Level < 5)
		{
			selectedEntry.Level++;
		}
	}

	[RelayCommand]
	private void Outdent()
	{
		TocEntryRow selectedEntry = SelectedEntry;
		if (selectedEntry != null && selectedEntry.Level > 0)
		{
			selectedEntry.Level--;
		}
	}

	[RelayCommand]
	private void RemoveRow()
	{
		if (SelectedEntry != null)
		{
			Entries.Remove(SelectedEntry);
			SelectedEntry = null;
		}
	}

	[RelayCommand]
	private void MoveUp()
	{
		if (SelectedEntry != null)
		{
			int num = Entries.IndexOf(SelectedEntry);
			if (num > 0)
			{
				Entries.Move(num, num - 1);
			}
		}
	}

	[RelayCommand]
	private void MoveDown()
	{
		if (SelectedEntry != null)
		{
			int num = Entries.IndexOf(SelectedEntry);
			if (num >= 0 && num < Entries.Count - 1)
			{
				Entries.Move(num, num + 1);
			}
		}
	}

	[RelayCommand]
	private async Task SaveAsync()
	{
		try
		{
			List<TocEntry> entries = (from r in Entries
				where !string.IsNullOrWhiteSpace(r.Title) && r.Page > 0
				select new TocEntry(r.Title.Trim(), r.Page, r.Level)).ToList();
			await _catalog.SetTocAsync(_bookId, entries);
			StatusText = ((entries.Count == 0) ? SharedStrings.S981 : $"{SharedStrings.S2340}{entries.Count}{SharedStrings.S2341}");
			TryContribute(entries);
			this.Saved?.Invoke();
		}
		catch (Exception ex)
		{
			StatusText = SharedStrings.S2342 + ex.Message;
		}
	}

	private void TryContribute(IReadOnlyList<TocEntry> entries)
	{
		if (_contributor != null && _contributor.IsAvailable && entries.Count != 0 && string.Equals(_sourceType, "PDF", StringComparison.Ordinal) && int.TryParse(_fileId, out var result) && result > 0)
		{
			string text = TocSerializer.Serialize(entries);
			if (!string.IsNullOrWhiteSpace(text))
			{
				_contributor.ContributeAsync(result, text);
			}
		}
	}
}
