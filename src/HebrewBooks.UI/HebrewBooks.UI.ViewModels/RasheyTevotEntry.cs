using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;

namespace HebrewBooks.UI.ViewModels;

public partial class RasheyTevotEntry : ObservableObject
{
	[ObservableProperty]
	private string _acronym;

	[ObservableProperty]
	private string _expansions;



	public RasheyTevotEntry(string acronym, string expansions)
	{
		_acronym = acronym;
		_expansions = expansions;
	}
}
