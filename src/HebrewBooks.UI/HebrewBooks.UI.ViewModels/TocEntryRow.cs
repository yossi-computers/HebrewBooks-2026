using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;

namespace HebrewBooks.UI.ViewModels;

public partial class TocEntryRow : ObservableObject
{
	[ObservableProperty]
	private string _title;

	[ObservableProperty]
	private int _page;

	[ObservableProperty]
	private int _level;

	public double IndentWidth => Level * 26;

	public string DepthGuide
	{
		get
		{
			if (Level <= 0)
			{
				return string.Empty;
			}
			return new string('•', Level);
		}
	}




	public TocEntryRow(string title, int page, int level = 0)
	{
		_title = title;
		_page = page;
		_level = level;
	}

}
