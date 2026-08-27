using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using HebrewBooks.Core.Models;

namespace HebrewBooks.UI.ViewModels;

public sealed partial class FavoriteBookEntry : ObservableObject
{
	[ObservableProperty]
	private string _folderName;

	public Book Book { get; }

	public string FileID => Book.FileID ?? string.Empty;

	public string BookName => Book.BookName ?? string.Empty;

	public string AuthorName => Book.AuthorName ?? string.Empty;


	public FavoriteBookEntry(Book book, string folderName)
	{
		Book = book;
		_folderName = folderName ?? string.Empty;
	}
}
