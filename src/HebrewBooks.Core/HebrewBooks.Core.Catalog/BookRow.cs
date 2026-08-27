using HebrewBooks.Core.Models;

namespace HebrewBooks.Core.Catalog;

public sealed class BookRow : CatalogRow
{
	public Book Book { get; }

	public bool IsChildInGroup { get; init; }

	public string? BookName => Book.BookName;

	public string? AuthorName => Book.AuthorName;

	public string? FileID => Book.FileID;

	public string SourceType => Book.SourceType;

	public string? PrintPlace => Book.PrintPlace;

	public string? PrintYear => Book.PrintYear;

	public int? CountPage => Book.CountPage;

	public string? Description => Book.Description;

	public BookRow(Book book)
	{
		Book = book;
	}
}
