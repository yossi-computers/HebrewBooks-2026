using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Services.Catalog;

public sealed class CatalogService(ICatalogRepository repo)
{
	public Task<Book?> GetAsync(int id, CancellationToken ct = default(CancellationToken))
	{
		return repo.GetByIdAsync(id, ct);
	}

	public Task<Book?> GetByFileIdAsync(string fileId, CancellationToken ct = default(CancellationToken))
	{
		return repo.GetByFileIdAsync(fileId.Trim(), ct);
	}

	public Task<IReadOnlyList<Book>> ListAsync(int skip, int take, string? sortBy, CancellationToken ct = default(CancellationToken), bool includeDescription = true)
	{
		return repo.ListAsync(skip, take, sortBy, ct, includeDescription);
	}

	public Task<int> CountAsync(CancellationToken ct = default(CancellationToken))
	{
		return repo.CountAsync(ct);
	}

	public async Task<int> AddAsync(Book book, CancellationToken ct = default(CancellationToken))
	{
		Book clean = Sanitize(book);
		Validate(clean);
		if (!string.IsNullOrEmpty(clean.FileID))
		{
			Book book2 = await repo.GetByFileIdAsync(clean.FileID, ct);
			if ((object)book2 != null)
			{
				throw new InvalidOperationException($"A book with FileID '{clean.FileID}' already exists (ID={book2.ID}).");
			}
		}
		return await repo.AddAsync(clean, ct);
	}

	public async Task UpdateAsync(Book book, CancellationToken ct = default(CancellationToken))
	{
		Book book2 = Sanitize(book);
		if (book2.ID <= 0)
		{
			throw new InvalidOperationException("Cannot update a book without ID.");
		}
		Validate(book2);
		await repo.UpdateAsync(book2, ct);
	}

	public Task DeleteAsync(int id, CancellationToken ct = default(CancellationToken))
	{
		return repo.DeleteAsync(id, ct);
	}

	private static Book Sanitize(Book book)
	{
		return book with
		{
			FileID = NullIfWhite(book.FileID?.Trim()),
			BookName = NullIfWhite(book.BookName?.Trim()),
			AuthorName = NullIfWhite(book.AuthorName?.Trim()),
			PrintPlace = NullIfWhite(book.PrintPlace?.Trim()),
			PrintYear = NullIfWhite(book.PrintYear?.Trim()),
			Description = book.Description?.Trim(),
			Folder = NullIfWhite(book.Folder?.Trim()),
			Categories = NullIfWhite(book.Categories?.Trim())
		};
	}

	private static string? NullIfWhite(string? s)
	{
		if (!string.IsNullOrWhiteSpace(s))
		{
			return s;
		}
		return null;
	}

	private static void Validate(Book book)
	{
		if (string.IsNullOrWhiteSpace(book.BookName))
		{
			throw new ArgumentException("BookName is required.");
		}
		int? countPage = book.CountPage;
		if (countPage.HasValue && countPage.GetValueOrDefault() < 0)
		{
			throw new ArgumentException("CountPage cannot be negative.");
		}
	}
}
