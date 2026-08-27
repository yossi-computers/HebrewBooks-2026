using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Services.Otzraya;

public sealed class OtzrayaCatalogIndexer
{
	public sealed record ScanResult(int FilesSeen, int Inserted, int Updated, int Skipped, int Removed);

	private readonly ICatalogRepository _catalog;

	private readonly IPathResolver _paths;

	private static readonly Regex H1Regex = new Regex("<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

	private static readonly Regex H2Regex = new Regex("<h2[^>]*>(.*?)</h2>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

	private static readonly Regex TagStripper = new Regex("<[^>]+>", RegexOptions.Compiled);

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public OtzrayaCatalogIndexer(ICatalogRepository catalog, IPathResolver paths)
	{
		_catalog = catalog;
		_paths = paths;
	}

	public async Task<ScanResult> ScanAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		if (!Directory.Exists(_paths.OtzrayaRoot))
		{
			throw new DirectoryNotFoundException("Otzraya root not found: " + _paths.OtzrayaRoot);
		}
		Dictionary<string, Book> existing = await LoadExistingTextBooksAsync(ct);
		string[] files = Directory.EnumerateFiles(_paths.OtzrayaRoot, "*.txt", SearchOption.AllDirectories).ToArray();
		IReadOnlySet<string> official = OtzrayaSyncService.LoadManifestPaths(_paths.OtzrayaRoot);
		bool gate = official.Count > 0;
		int inserted = 0;
		int updated = 0;
		int skipped = 0;
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		bool cancelled = false;
		for (int i = 0; i < files.Length; i++)
		{
			if (ct.IsCancellationRequested)
			{
				cancelled = true;
				break;
			}
			string path = files[i];
			string rel = Path.GetRelativePath(_paths.OtzrayaRoot, path);
			if (gate && !official.Contains(rel.Replace('\\', '/')))
			{
				skipped++;
				progress?.Report((i + 1, files.Length));
				continue;
			}
			try
			{
				seen.Add(rel);
				Book book = await BuildBookFromFileAsync(path, rel, ct);
				if (existing.TryGetValue(rel, out Book value))
				{
					book = book with
					{
						ID = value.ID
					};
					await _catalog.UpdateAsync(book, ct);
					updated++;
				}
				else
				{
					await _catalog.AddAsync(book, ct);
					inserted++;
				}
			}
			catch (OperationCanceledException)
			{
				cancelled = true;
				break;
			}
			catch (Exception)
			{
				skipped++;
			}
			progress?.Report((i + 1, files.Length));
		}
		int removed = 0;
		if (!cancelled)
		{
			foreach (var (item, book3) in existing)
			{
				if (!seen.Contains(item))
				{
					try
					{
						await _catalog.DeleteAsync(book3.ID, CancellationToken.None);
						removed++;
					}
					catch
					{
					}
				}
			}
		}
		if (cancelled)
		{
			ct.ThrowIfCancellationRequested();
		}
		return new ScanResult(files.Length, inserted, updated, skipped, removed);
	}

	private async Task<Dictionary<string, Book>> LoadExistingTextBooksAsync(CancellationToken ct)
	{
		Dictionary<string, Book> dict = new Dictionary<string, Book>(StringComparer.Ordinal);
		int skip = 0;
		while (true)
		{
			IReadOnlyList<Book> readOnlyList = await _catalog.ListAsync(skip, 5000, null, ct);
			if (readOnlyList.Count == 0)
			{
				break;
			}
			foreach (Book item in readOnlyList)
			{
				if (string.Equals(item.SourceType, "Text", StringComparison.Ordinal) && !string.IsNullOrEmpty(item.RelativePath))
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

	private async Task<Book> BuildBookFromFileAsync(string path, string relativePath, CancellationToken ct)
	{
		string text = await File.ReadAllTextAsync(path, ct);
		string bookName = ExtractH1(text) ?? Path.GetFileNameWithoutExtension(path);
		IReadOnlyList<string> readOnlyList = ExtractH2List(text);
		string text2 = Path.GetDirectoryName(relativePath) ?? string.Empty;
		string[] value = text2.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
		string categories = string.Join('|', value);
		string description = JsonSerializer.Serialize(new TextBookMetadata(readOnlyList), JsonOpts);
		return new Book
		{
			FileID = relativePath,
			BookName = bookName,
			Folder = text2,
			Categories = categories,
			Searchable = true,
			SourceType = "Text",
			RelativePath = relativePath,
			Description = description,
			CountPage = readOnlyList.Count
		};
	}

	private static string? ExtractH1(string text)
	{
		Match match = H1Regex.Match(text);
		if (!match.Success)
		{
			return null;
		}
		return Clean(match.Groups[1].Value);
	}

	private static IReadOnlyList<string> ExtractH2List(string text)
	{
		List<string> list = new List<string>();
		foreach (Match item in H2Regex.Matches(text))
		{
			string text2 = Clean(item.Groups[1].Value);
			if (!string.IsNullOrEmpty(text2))
			{
				list.Add(text2);
			}
		}
		return list;
	}

	private static string Clean(string raw)
	{
		return TagStripper.Replace(raw, string.Empty).Trim();
	}
}
