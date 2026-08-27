using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Services.Toc;

public sealed class TocBundleService
{
	private readonly ICatalogRepository _catalog;

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		WriteIndented = true
	};

	public TocBundleService(ICatalogRepository catalog)
	{
		_catalog = catalog;
	}

	public async Task<int> ExportAsync(string destPath, CancellationToken ct = default(CancellationToken))
	{
		List<Book> allBooks = new List<Book>();
		int skip = 0;
		while (true)
		{
			IReadOnlyList<Book> readOnlyList = await _catalog.ListAsync(skip, 5000, null, ct);
			if (readOnlyList.Count == 0)
			{
				break;
			}
			allBooks.AddRange(readOnlyList);
			if (readOnlyList.Count < 5000)
			{
				break;
			}
			skip += 5000;
		}
		List<int> bookIds = allBooks.Select((Book b) => b.ID).ToList();
		IReadOnlyDictionary<int, IReadOnlyList<TocEntry>> readOnlyDictionary = await _catalog.GetTocsAsync(bookIds, ct);
		List<TocBundleEntry> entries = new List<TocBundleEntry>(readOnlyDictionary.Count);
		foreach (Book item in allBooks)
		{
			if (readOnlyDictionary.TryGetValue(item.ID, out var value) && !string.IsNullOrEmpty(item.FileID))
			{
				entries.Add(new TocBundleEntry(item.FileID, item.BookName, value));
			}
		}
		TocBundle value2 = new TocBundle("HebrewBooks-TOC-v1", entries);
		string directoryName = Path.GetDirectoryName(destPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string tmp = destPath + ".tmp";
		await using (FileStream stream = File.Create(tmp))
		{
			await JsonSerializer.SerializeAsync((Stream)stream, value2, JsonOpts, ct);
		}
		if (File.Exists(destPath))
		{
			File.Replace(tmp, destPath, null);
		}
		else
		{
			File.Move(tmp, destPath);
		}
		return entries.Count;
	}

	public async Task<(int Matched, int Missing)> ImportAsync(string srcPath, ImportMode mode, CancellationToken ct = default(CancellationToken))
	{
		(int Matched, int Missing) result;
		await using (FileStream stream = File.OpenRead(srcPath))
		{
			TocBundle tocBundle = (await JsonSerializer.DeserializeAsync<TocBundle>((Stream)stream, JsonOpts, ct)) ?? throw new InvalidDataException("Bundle file is empty or malformed.");
			if (!string.Equals(tocBundle.Format, "HebrewBooks-TOC-v1", StringComparison.Ordinal))
			{
				throw new InvalidDataException("Unsupported bundle format: " + tocBundle.Format);
			}
			int matched = 0;
			int missing = 0;
			foreach (TocBundleEntry entry in tocBundle.Books)
			{
				ct.ThrowIfCancellationRequested();
				if (!string.IsNullOrEmpty(entry.FileId))
				{
					Book book = await _catalog.GetByFileIdAsync(entry.FileId, ct);
					if ((object)book == null)
					{
						missing++;
						continue;
					}
					if (mode == ImportMode.SkipExisting && (await _catalog.GetTocAsync(book.ID, ct)).Count > 0)
					{
						matched++;
						continue;
					}
					await _catalog.SetTocAsync(book.ID, entry.Toc, ct);
					matched++;
				}
			}
			result = (Matched: matched, Missing: missing);
		}
		return result;
	}
}
