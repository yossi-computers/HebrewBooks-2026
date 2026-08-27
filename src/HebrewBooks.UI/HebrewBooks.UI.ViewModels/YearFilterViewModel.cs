using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.Core.Catalog;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.ViewModels;

public sealed partial class YearFilterViewModel : ObservableObject
{
	[ObservableProperty]
	private string _fromText = string.Empty;

	[ObservableProperty]
	private string _toText = string.Empty;

	[ObservableProperty]
	private bool _includeUnknown;

	private PrintYearRange _range = PrintYearRange.All;

	private bool _suppressRecompute;



	public PrintYearRange Range => _range;

	public bool IsActive => _range.IsActive;

	public bool HasFromError
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(FromText))
			{
				return !HebrewYear.Parse(FromText).HasValue;
			}
			return false;
		}
	}

	public bool HasToError
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(ToText))
			{
				return !HebrewYear.Parse(ToText).HasValue;
			}
			return false;
		}
	}

	public string Summary
	{
		get
		{
			int? num = _range.From;
			int? to = _range.To;
			if (!num.HasValue)
			{
				if (to.HasValue)
				{
					int valueOrDefault = to.GetValueOrDefault();
					return string.Format(SharedStrings.YearFilterRangeTo, HebrewYear.ToGematria(valueOrDefault));
				}
				return SharedStrings.YearFilterButton;
			}
			int valueOrDefault2 = num.GetValueOrDefault();
			if (to.HasValue)
			{
				int valueOrDefault = to.GetValueOrDefault();
				int year = valueOrDefault;
				return HebrewYear.ToGematria(valueOrDefault2) + "–" + HebrewYear.ToGematria(year);
			}
			return string.Format(SharedStrings.YearFilterRangeFrom, HebrewYear.ToGematria(valueOrDefault2));
		}
	}

	public string RangeEcho
	{
		get
		{
			int? num = _range.From;
			int? to = _range.To;
			if (!num.HasValue)
			{
				if (to.HasValue)
				{
					int valueOrDefault = to.GetValueOrDefault();
					return string.Format(SharedStrings.YearFilterRangeTo, Describe(valueOrDefault));
				}
				return string.Empty;
			}
			int valueOrDefault2 = num.GetValueOrDefault();
			if (to.HasValue)
			{
				int valueOrDefault = to.GetValueOrDefault();
				int year = valueOrDefault;
				return Describe(valueOrDefault2) + " – " + Describe(year);
			}
			return string.Format(SharedStrings.YearFilterRangeFrom, Describe(valueOrDefault2));
		}
	}






	public event Action? Changed;

	private static string Describe(int year)
	{
		return $"{HebrewYear.ToGematria(year)} ({HebrewYear.ToCivilYear(year)})";
	}

	private void Recompute()
	{
		if (!_suppressRecompute)
		{
			int? num = HebrewYear.Parse(FromText);
			int? num2 = HebrewYear.Parse(ToText);
			if (num.HasValue && num2.HasValue && num > num2)
			{
				int? num3 = num2;
				num2 = num;
				num = num3;
			}
			PrintYearRange printYearRange = new PrintYearRange(num, num2, IncludeUnknown);
			bool num4 = printYearRange != _range;
			_range = printYearRange;
			OnPropertyChanged("Range");
			OnPropertyChanged("IsActive");
			OnPropertyChanged("Summary");
			OnPropertyChanged("RangeEcho");
			OnPropertyChanged("HasFromError");
			OnPropertyChanged("HasToError");
			if (num4)
			{
				this.Changed?.Invoke();
			}
		}
	}

	[RelayCommand]
	private void Preset(string? spec)
	{
		if (!string.IsNullOrWhiteSpace(spec))
		{
			string[] array = spec.Split('|');
			if (array.Length == 2)
			{
				string text = array[0].Trim();
				string text2 = array[1].Trim();
				SetBoth((text.Length > 0 && int.TryParse(text, out var result)) ? HebrewYear.ToGematria(result) : string.Empty, (text2.Length > 0 && int.TryParse(text2, out var result2)) ? HebrewYear.ToGematria(result2) : string.Empty);
			}
		}
	}

	[RelayCommand]
	private void Clear()
	{
		if (IsActive || FromText.Length != 0 || ToText.Length != 0)
		{
			SetBoth(string.Empty, string.Empty);
		}
	}

	private void SetBoth(string from, string to)
	{
		_suppressRecompute = true;
		try
		{
			FromText = from;
			ToText = to;
		}
		finally
		{
			_suppressRecompute = false;
		}
		Recompute();
	}



}
