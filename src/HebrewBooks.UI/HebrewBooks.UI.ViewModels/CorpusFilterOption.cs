using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;

namespace HebrewBooks.UI.ViewModels;

public sealed partial class CorpusFilterOption : ObservableObject
{
	[ObservableProperty]
	private bool _isSelected;

	public string Value { get; }

	public string Label { get; }


	public CorpusFilterOption(string value, string label, bool isSelected)
	{
		Value = value;
		Label = label;
		_isSelected = isSelected;
	}
}
