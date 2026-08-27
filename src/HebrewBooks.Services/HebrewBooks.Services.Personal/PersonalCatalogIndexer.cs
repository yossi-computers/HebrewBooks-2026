using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Core.Resources;

namespace HebrewBooks.Services.Personal;

public sealed class PersonalCatalogIndexer
{
	public sealed record ScanResult(int FilesSeen, int Inserted, int Updated, int Skipped, int Removed, bool PruneSkipped = false);

	private readonly ICatalogRepository _catalog;

	private readonly IPathResolver _paths;

	public PersonalCatalogIndexer(ICatalogRepository catalog, IPathResolver paths)
	{
		_catalog = catalog;
		_paths = paths;
	}

	public async Task<ScanResult> ScanAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		string root = _paths.PersonalRoot;
		Dictionary<string, Book> existing = await LoadExistingPersonalBooksAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!Directory.Exists(root))
		{
			return new ScanResult(0, 0, 0, 0, 0, existing.Count > 0);
		}
		string[] files = Directory.EnumerateFiles(root, "*.pdf", SearchOption.AllDirectories).ToArray();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		int inserted = 0;
		int updated = 0;
		int skipped = 0;
		bool cancelled = false;
		for (int i = 0; i < files.Length; i++)
		{
			if (ct.IsCancellationRequested)
			{
				cancelled = true;
				break;
			}
			string text = files[i];
			try
			{
				string relativePath = Path.GetRelativePath(root, text);
				seen.Add(relativePath);
				Book book = BuildBookFromFile(text, relativePath);
				if (existing.TryGetValue(relativePath, out Book value))
				{
					book = book with
					{
						ID = value.ID
					};
					await _catalog.UpdateAsync(book, ct).ConfigureAwait(continueOnCapturedContext: false);
					updated++;
				}
				else
				{
					await _catalog.AddAsync(book, ct).ConfigureAwait(continueOnCapturedContext: false);
					inserted++;
				}
			}
			catch (OperationCanceledException)
			{
				cancelled = true;
				break;
			}
			catch
			{
				skipped++;
			}
			progress?.Report((i + 1, files.Length));
		}
		int removed = 0;
		bool pruneSkipped = false;
		if (!cancelled)
		{
			if (seen.Count == 0 && existing.Count > 0)
			{
				pruneSkipped = true;
			}
			else
			{
				foreach (var (item, book3) in existing)
				{
					if (!seen.Contains(item))
					{
						try
						{
							await _catalog.DeleteAsync(book3.ID, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
							removed++;
						}
						catch
						{
						}
					}
				}
			}
		}
		if (cancelled)
		{
			ct.ThrowIfCancellationRequested();
		}
		return new ScanResult(files.Length, inserted, updated, skipped, removed, pruneSkipped);
	}

	private async Task<Dictionary<string, Book>> LoadExistingPersonalBooksAsync(CancellationToken ct)
	{
		Dictionary<string, Book> dict = new Dictionary<string, Book>(StringComparer.Ordinal);
		int skip = 0;
		while (true)
		{
			IReadOnlyList<Book> readOnlyList = await _catalog.ListAsync(skip, 5000, null, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (readOnlyList.Count == 0)
			{
				break;
			}
			foreach (Book item in readOnlyList)
			{
				if (string.Equals(item.SourceType, "Personal", StringComparison.Ordinal) && !string.IsNullOrEmpty(item.RelativePath))
				{
					dict[item.RelativePath] = item;
				}
			}
			if (readOnlyList.Count < 5000)
			{
				break;
			}
			skip += 5000;
		}
		return dict;
	}

	private static Book BuildBookFromFile(string absolutePath, string relativePath)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(absolutePath);
		string folder = Path.GetDirectoryName(relativePath) ?? string.Empty;
		return new Book
		{
			FileID = relativePath,
			BookName = (string.IsNullOrWhiteSpace(fileNameWithoutExtension) ? CoreStrings.C14 : fileNameWithoutExtension),
			Folder = folder,
			Categories = CoreStrings.C15,
			Searchable = true,
			SourceType = "Personal",
			RelativePath = relativePath
		};
	}
}
