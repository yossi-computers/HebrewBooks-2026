using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.Services.Catalog;
using HebrewBooks.Services.Search;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.ViewModels;

public sealed partial class SortFilterViewModel : ObservableObject
{



	public ObservableCollection<SortCriterionOption> Criteria { get; }

	public IReadOnlyList<SortLayer> Layers => (from c in Criteria
		where c.IsSelected
		orderby c.Rank
		select c.ToLayer()).ToList();

	public bool IsActive => Criteria.Any((SortCriterionOption c) => c.IsSelected);

	public string Summary
	{
		get
		{
			List<SortCriterionOption> list = (from c in Criteria
				where c.IsSelected
				orderby c.Rank
				select c).ToList();
			if (list.Count == 0)
			{
				return SharedStrings.SortButton;
			}
			return SharedStrings.SortButtonActive + string.Join(" · ", list.Select((SortCriterionOption c) => c.ShortLabel + " " + c.DirectionArrow));
		}
	}

	public bool RelevanceAvailable
	{
		set
		{
			SortCriterionOption sortCriterionOption = Criteria.First((SortCriterionOption c) => c.Mode == SortMode.HitCount);
			if (sortCriterionOption.IsAvailable != value)
			{
				sortCriterionOption.IsAvailable = value;
				if (!value && sortCriterionOption.IsSelected)
				{
					Remove(sortCriterionOption);
				}
			}
		}
	}




	public event Action? Changed;

	public SortFilterViewModel()
	{
		Criteria = new ObservableCollection<SortCriterionOption>
		{
			new SortCriterionOption(SortMode.BookName, SharedStrings.SortCritBookName, SharedStrings.SortCritBookNameShort, SharedStrings.SortDirAZ, SharedStrings.SortDirZA),
			new SortCriterionOption(SortMode.AuthorName, SharedStrings.SortCritAuthor, SharedStrings.SortCritAuthorShort, SharedStrings.SortDirAZ, SharedStrings.SortDirZA),
			new SortCriterionOption(SortMode.PrintYear, SharedStrings.SortCritYear, SharedStrings.SortCritYearShort, SharedStrings.SortDirOldNew, SharedStrings.SortDirNewOld),
			new SortCriterionOption(SortMode.PrintPlace, SharedStrings.SortCritPlace, SharedStrings.SortCritPlaceShort, SharedStrings.SortDirAZ, SharedStrings.SortDirZA),
			new SortCriterionOption(SortMode.HitCount, SharedStrings.SortCritRelevance, SharedStrings.SortCritRelevanceShort, SharedStrings.SortDirLessMore, SharedStrings.SortDirMoreLess, defaultDescending: true)
		};
	}

	[RelayCommand]
	public void Toggle(SortCriterionOption? criterion)
	{
		if (criterion == null || !criterion.IsAvailable)
		{
			return;
		}
		if (criterion.IsSelected)
		{
			criterion.Descending = !criterion.Descending;
		}
		else
		{
			criterion.Rank = Criteria.Count((SortCriterionOption c) => c.IsSelected) + 1;
		}
		Publish();
	}

	[RelayCommand]
	public void Remove(SortCriterionOption? criterion)
	{
		if (criterion == null || !criterion.IsSelected)
		{
			return;
		}
		int removed = criterion.Rank;
		criterion.Rank = 0;
		foreach (SortCriterionOption item in Criteria.Where((SortCriterionOption c) => c.Rank > removed))
		{
			item.Rank--;
		}
		Publish();
	}

	[RelayCommand]
	public void Clear()
	{
		if (!IsActive)
		{
			return;
		}
		foreach (SortCriterionOption criterion in Criteria)
		{
			criterion.Rank = 0;
		}
		Publish();
	}

	private void Publish()
	{
		OnPropertyChanged("Layers");
		OnPropertyChanged("IsActive");
		OnPropertyChanged("Summary");
		this.Changed?.Invoke();
	}
}
