using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;

namespace HebrewBooks.UI.ViewModels;

public sealed partial class FavoriteFolderViewModel : ObservableObject
{
	[ObservableProperty]
	private string _name = string.Empty;

	public ObservableCollection<FavoriteBookEntry> Books { get; } = new ObservableCollection<FavoriteBookEntry>();

}
