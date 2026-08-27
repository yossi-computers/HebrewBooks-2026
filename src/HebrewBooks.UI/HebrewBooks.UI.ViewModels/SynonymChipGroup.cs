using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HebrewBooks.UI.ViewModels;

public sealed class SynonymChipGroup
{
	public string Source { get; }

	public ObservableCollection<SynonymChipVm> Chips { get; }

	public SynonymChipGroup(string source, IEnumerable<SynonymChipVm> chips)
	{
		Source = source;
		Chips = new ObservableCollection<SynonymChipVm>(chips);
	}
}
