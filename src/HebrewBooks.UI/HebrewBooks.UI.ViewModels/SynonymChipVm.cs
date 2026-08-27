using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;

namespace HebrewBooks.UI.ViewModels;

public sealed partial class SynonymChipVm : ObservableObject
{
	[ObservableProperty]
	private bool _isSelected;

	public string Term { get; }


	public SynonymChipVm(string term)
	{
		Term = term;
	}
}
