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

public partial class AddBookViewModel : ObservableObject
{
	private readonly CatalogService _catalog;

	[ObservableProperty]
	private Book _book = new Book();

	[ObservableProperty]
	private string? _errorMessage;


	public int? AddedId { get; private set; }




	public event Action? Saved;

	public AddBookViewModel(CatalogService catalog)
	{
		_catalog = catalog;
	}

	[RelayCommand]
	private async Task SaveAsync()
	{
		try
		{
			ErrorMessage = null;
			AddedId = await _catalog.AddAsync(Book);
			this.Saved?.Invoke();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}
}
