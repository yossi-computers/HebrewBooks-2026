using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.ViewModels;

public sealed partial class TopicFilterViewModel : ObservableObject
{
	private readonly List<string> _all = new List<string>();

	private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.Ordinal);

	[ObservableProperty]
	private bool _hasSelection;

	[ObservableProperty]
	private bool _hasSuggestions;

	[ObservableProperty]
	private string _searchText = string.Empty;

	[ObservableProperty]
	private bool _isPopupOpen;

	private const int MaxSuggestions = 50;




	public ObservableCollection<string> Selected { get; } = new ObservableCollection<string>();

	public ObservableCollection<string> Suggestions { get; } = new ObservableCollection<string>();

	public string Summary
	{
		get
		{
			if (_selected.Count != 0)
			{
				return $"{SharedStrings.S2343}{_selected.Count})";
			}
			return SharedStrings.S983;
		}
	}

	public IReadOnlySet<string> SelectedSet => _selected;








	public event Action? Changed;

	public void Initialize(IEnumerable<string> all)
	{
		_all.Clear();
		_all.AddRange(all);
		RefreshSuggestions();
	}

	private void RefreshSuggestions()
	{
		Suggestions.Clear();
		string q = (SearchText ?? "").Trim();
		IEnumerable<string> source = _all.Where((string t) => !_selected.Contains(t));
		source = (string.IsNullOrEmpty(q) ? source.Take(50) : source.Where((string t) => t.Contains(q, StringComparison.OrdinalIgnoreCase)));
		int num = 0;
		foreach (string item in source)
		{
			Suggestions.Add(item);
			if (++num >= 50)
			{
				break;
			}
		}
		HasSuggestions = Suggestions.Count > 0;
	}

	[RelayCommand]
	public void Add(string? name)
	{
		if (!string.IsNullOrWhiteSpace(name) && _selected.Add(name))
		{
			Selected.Add(name);
			SearchText = string.Empty;
			HasSelection = _selected.Count > 0;
			OnPropertyChanged("Summary");
			IsPopupOpen = true;
			this.Changed?.Invoke();
		}
	}

	[RelayCommand]
	public void Remove(string? name)
	{
		if (!string.IsNullOrEmpty(name) && _selected.Remove(name))
		{
			Selected.Remove(name);
			RefreshSuggestions();
			HasSelection = _selected.Count > 0;
			OnPropertyChanged("Summary");
			this.Changed?.Invoke();
		}
	}

	[RelayCommand]
	public void Clear()
	{
		if (_selected.Count != 0)
		{
			_selected.Clear();
			Selected.Clear();
			SearchText = string.Empty;
			HasSelection = false;
			OnPropertyChanged("Summary");
			this.Changed?.Invoke();
		}
	}

}
