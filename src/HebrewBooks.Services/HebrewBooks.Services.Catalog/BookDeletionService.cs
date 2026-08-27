using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Core.Resources;

namespace HebrewBooks.Services.Catalog;

public sealed class BookDeletionService
{
	private readonly ICatalogRepository _catalog;

	private readonly IPathResolver _paths;

	private readonly ISearchEngine _searchEngine;

	private readonly bool _deletionAllowed;

	public BookDeletionService(ICatalogRepository catalog, IPathResolver paths, ISearchEngine searchEngine, bool deletionAllowed = true)
	{
		_catalog = catalog;
		_paths = paths;
		_searchEngine = searchEngine;
		_deletionAllowed = deletionAllowed;
	}

	private string? ResolveOnDiskPath(Book book)
	{
		if (string.IsNullOrEmpty(book.SourceType) || string.Equals(book.SourceType, "PDF", StringComparison.Ordinal))
		{
			if (!int.TryParse(book.FileID, out var result))
			{
				return null;
			}
			return _paths.PdfPath(result, book.Folder);
		}
		if (string.Equals(book.SourceType, "Personal", StringComparison.Ordinal))
		{
			string text = ((!string.IsNullOrEmpty(book.RelativePath)) ? book.RelativePath : book.FileID);
			if (!string.IsNullOrEmpty(text))
			{
				return _paths.PersonalFilePath(text);
			}
			return null;
		}
		return null;
	}

	public async Task<IReadOnlyList<DeletionResult>> DeleteAsync(IReadOnlyList<Book> books, CancellationToken ct = default(CancellationToken))
	{
		if (!_deletionAllowed)
		{
			return books.Select((Book b) => DeletionResult.Skipped(b, CoreStrings.C4)).ToList();
		}
		List<DeletionResult> results = new List<DeletionResult>(books.Count);
		List<string> indexPaths = new List<string>(books.Count);
		foreach (Book book in books)
		{
			ct.ThrowIfCancellationRequested();
			try
			{
				string path = ResolveOnDiskPath(book);
				if (path == null)
				{
					results.Add(DeletionResult.Skipped(book, CoreStrings.C5));
					continue;
				}
				if (File.Exists(path))
				{
					try
					{
						File.Delete(path);
					}
					catch (IOException ex)
					{
						results.Add(DeletionResult.Failed(book, CoreStrings.C2 + ex.Message));
						goto end_IL_00d6;
					}
					catch (UnauthorizedAccessException ex2)
					{
						results.Add(DeletionResult.Failed(book, CoreStrings.C3 + ex2.Message));
						goto end_IL_00d6;
					}
				}
				if (book.ID > 0)
				{
					await _catalog.DeleteAsync(book.ID, ct).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (string.Equals(book.SourceType ?? "PDF", "PDF", StringComparison.Ordinal))
				{
					indexPaths.Add(path);
				}
				results.Add(DeletionResult.Ok(book));
				end_IL_00d6:;
			}
			catch (Exception ex3)
			{
				results.Add(DeletionResult.Failed(book, ex3.Message));
			}
		}
		if (indexPaths.Count > 0)
		{
			string[] pathsCopy = indexPaths.ToArray();
			string indexPath = _paths.IndexesRoot;
			Task.Run(delegate
			{
				try
				{
					_searchEngine.RemoveDocumentsFromIndex(indexPath, pathsCopy);
				}
				catch
				{
				}
			}, CancellationToken.None);
		}
		return results;
	}
}
