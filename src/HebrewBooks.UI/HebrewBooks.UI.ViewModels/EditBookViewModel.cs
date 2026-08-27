using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using HebrewBooks.Core.Models;
using HebrewBooks.Services.Catalog;

namespace HebrewBooks.UI.ViewModels;

public partial class EditBookViewModel : ObservableObject
{
	private readonly CatalogService _catalog;

	[ObservableProperty]
	private Book _book = new Book();

	[ObservableProperty]
	private string? _errorMessage;





	public event Action? Saved;

	public EditBookViewModel(CatalogService catalog)
	{
		_catalog = catalog;
	}

	public void Load(Book book)
	{
		Book = book with { };
	}

	[RelayCommand]
	private async Task SaveAsync()
	{
		try
		{
			ErrorMessage = null;
			await _catalog.UpdateAsync(Book);
			this.Saved?.Invoke();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}
}
