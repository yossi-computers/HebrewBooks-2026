using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using HebrewBooks.Services.Catalog;
using HebrewBooks.Services.Search;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.ViewModels;

public sealed partial class SortCriterionOption : ObservableObject
{
	private readonly string _ascendingLabel;

	private readonly string _descendingLabel;

	[ObservableProperty]
	private int _rank;

	[ObservableProperty]
	private bool _descending;

	[ObservableProperty]
	private bool _isAvailable = true;

	public SortMode Mode { get; }

	public string Label { get; }

	public string ShortLabel { get; }

	public string? UnavailableNote
	{
		get
		{
			if (!IsAvailable)
			{
				return SharedStrings.SortRelevanceUnavailable;
			}
			return null;
		}
	}

	public bool IsSelected => Rank > 0;

	public string DirectionLabel
	{
		get
		{
			if (!Descending)
			{
				return _ascendingLabel;
			}
			return _descendingLabel;
		}
	}

	public string DirectionArrow
	{
		get
		{
			if (!Descending)
			{
				return "↑";
			}
			return "↓";
		}
	}




	public SortCriterionOption(SortMode mode, string label, string shortLabel, string ascending, string descending, bool defaultDescending = false)
	{
		Mode = mode;
		Label = label;
		ShortLabel = shortLabel;
		_ascendingLabel = ascending;
		_descendingLabel = descending;
		_descending = defaultDescending;
	}

	public SortLayer ToLayer()
	{
		return new SortLayer(Mode, Descending);
	}



}
