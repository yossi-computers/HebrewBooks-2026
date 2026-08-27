using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using HebrewBooks.Core.Models;
using HebrewBooks.UI.Resources;

namespace HebrewBooks.UI.ViewModels;

public partial class OpenBookTab : ObservableObject
{
	[ObservableProperty]
	private Book _book;

	[ObservableProperty]
	private string _title = "";

	[ObservableProperty]
	private bool _isActive;

	[ObservableProperty]
	private bool _isPreview;

	public string? FileId => Book?.FileID;

	public int LastPage { get; set; }

	public string InBookQuery { get; set; } = "";

	public TabContentSearch? ContentSearch { get; set; }

	public TabInBookHits? InBookHits { get; set; }





	public OpenBookTab(Book book)
	{
		SetBook(book);
	}

	public void SetBook(Book book)
	{
		Book = book;
		Title = (string.IsNullOrWhiteSpace(book.BookName) ? (book.FileID ?? SharedStrings.S153) : book.BookName);
	}
}
