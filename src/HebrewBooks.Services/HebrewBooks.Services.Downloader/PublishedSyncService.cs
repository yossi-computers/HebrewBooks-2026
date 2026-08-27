using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Services.Catalog;

namespace HebrewBooks.Services.Downloader;

public sealed class PublishedSyncService
{
	public sealed record SyncProgress(int Done, int Total, string CurrentFile);

	public sealed record SyncResult(int Added, int Skipped, int Errors)
	{
		public IReadOnlyList<string> ChangedPaths { get; init; } = Array.Empty<string>();
	}

	public sealed record PublishedItem(string FileId, string? BookName, string? AuthorName, string? PrintPlace, string? PrintYear, int? CountPage, string? PublishedAtUtc);

	private sealed record Manifest([property: JsonPropertyName("lastSync")] string LastSync);

	private sealed record PublishedDto([property: JsonPropertyName("fileId")] string FileId, [property: JsonPropertyName("bookName")] string? BookName, [property: JsonPropertyName("authorName")] string? AuthorName, [property: JsonPropertyName("printPlace")] string? PrintPlace, [property: JsonPropertyName("printYear")] string? PrintYear, [property: JsonPropertyName("countPage")] int? CountPage, [property: JsonPropertyName("categories")] string? Categories, [property: JsonPropertyName("description")] string? Description, [property: JsonPropertyName("sha256")] string? Sha256, [property: JsonPropertyName("publishedAtUtc")] string? PublishedAtUtc);

	private const string ManifestFileName = "published.sync.json";

	private readonly IPathResolver _paths;

	private readonly HttpClient _http;

	private readonly CatalogService _catalog;

	private readonly IProtectMode? _protect;

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true
	};

	public string FeedBaseUrl { get; set; } = "https://hebrewbooks.pages.dev";

	public PublishedSyncService(IPathResolver paths, HttpClient http, CatalogService catalog, IProtectMode? protectMode = null)
	{
		_paths = paths;
		_http = http;
		_catalog = catalog;
		_protect = protectMode;
	}

	public async Task<IReadOnlyList<PublishedItem>> PeekAsync(CancellationToken ct = default(CancellationToken))
	{
		if (_protect?.IsActive ?? false)
		{
			return Array.Empty<PublishedItem>();
		}
		List<PublishedDto> list = await FetchFeedAsync(LoadLastSync(), ct).ConfigureAwait(continueOnCapturedContext: false);
		List<PublishedItem> result = new List<PublishedItem>();
		foreach (PublishedDto dto in list)
		{
			ct.ThrowIfCancellationRequested();
			if ((object)(await _catalog.GetByFileIdAsync(dto.FileId, ct).ConfigureAwait(continueOnCapturedContext: false)) == null)
			{
				result.Add(new PublishedItem(dto.FileId, dto.BookName, dto.AuthorName, dto.PrintPlace, dto.PrintYear, dto.CountPage, dto.PublishedAtUtc));
			}
		}
		return result;
	}

	private async Task<List<PublishedDto>> FetchFeedAsync(string? since, CancellationToken ct)
	{
		string requestUri = FeedBaseUrl.TrimEnd('/') + "/api/catalog/published" + (string.IsNullOrEmpty(since) ? "" : ("?since=" + Uri.EscapeDataString(since)));
		using HttpResponseMessage resp = await _http.GetAsync(requestUri, ct).ConfigureAwait(continueOnCapturedContext: false);
		resp.EnsureSuccessStatusCode();
		return (await resp.Content.ReadFromJsonAsync<List<PublishedDto>>(JsonOpts, ct).ConfigureAwait(continueOnCapturedContext: false)) ?? new List<PublishedDto>();
	}

	public async Task<SyncResult> SyncAsync(IProgress<SyncProgress>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		if (_protect?.IsActive ?? false)
		{
			return new SyncResult(0, 0, 0);
		}
		string since = LoadLastSync();
		List<PublishedDto> items = await FetchFeedAsync(since, ct).ConfigureAwait(continueOnCapturedContext: false);
		Directory.CreateDirectory(_paths.PdfsRoot);
		int added = 0;
		int skipped = 0;
		int errors = 0;
		int done = 0;
		List<string> changed = new List<string>();
		string maxSince = since ?? "";
		foreach (PublishedDto dto in items)
		{
			ct.ThrowIfCancellationRequested();
			done++;
			progress?.Report(new SyncProgress(done, items.Count, dto.BookName ?? dto.FileId));
			try
			{
				if (!int.TryParse(dto.FileId, out var numericId))
				{
					errors++;
					continue;
				}
				if ((object)(await _catalog.GetByFileIdAsync(dto.FileId, ct).ConfigureAwait(continueOnCapturedContext: false)) != null)
				{
					skipped++;
					maxSince = Later(maxSince, dto.PublishedAtUtc);
					continue;
				}
				byte[] array = await _http.GetByteArrayAsync($"{FeedBaseUrl.TrimEnd('/')}/api/catalog/published/{numericId}/file", ct).ConfigureAwait(continueOnCapturedContext: false);
				if (!LooksLikePdf(array))
				{
					errors++;
					continue;
				}
				if (!string.IsNullOrEmpty(dto.Sha256) && !string.Equals(Sha256Hex(array), dto.Sha256, StringComparison.OrdinalIgnoreCase))
				{
					errors++;
					continue;
				}
				string pdfPath = _paths.PdfPath(numericId, null);
				string directoryName = Path.GetDirectoryName(pdfPath);
				if (!string.IsNullOrEmpty(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				await WriteAtomicAsync(pdfPath, array, ct).ConfigureAwait(continueOnCapturedContext: false);
				await _catalog.AddAsync(new Book
				{
					FileID = dto.FileId,
					BookName = dto.BookName,
					AuthorName = dto.AuthorName,
					PrintPlace = dto.PrintPlace,
					PrintYear = dto.PrintYear,
					CountPage = dto.CountPage,
					Categories = dto.Categories,
					Description = dto.Description,
					SourceType = "PDF",
					Searchable = true
				}, ct).ConfigureAwait(continueOnCapturedContext: false);
				changed.Add(pdfPath);
				added++;
				maxSince = Later(maxSince, dto.PublishedAtUtc);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception)
			{
				errors++;
			}
		}
		if (!string.IsNullOrEmpty(maxSince))
		{
			SaveLastSync(maxSince);
		}
		return new SyncResult(added, skipped, errors)
		{
			ChangedPaths = changed
		};
	}

	private static bool LooksLikePdf(byte[] b)
	{
		if (b.Length >= 5 && b[0] == 37 && b[1] == 80 && b[2] == 68 && b[3] == 70)
		{
			return b[4] == 45;
		}
		return false;
	}

	private static string Sha256Hex(byte[] b)
	{
		return Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
	}

	private static string Later(string a, string? b)
	{
		if (!string.IsNullOrEmpty(b))
		{
			if (string.CompareOrdinal(b, a) <= 0)
			{
				return a;
			}
			return b;
		}
		return a;
	}

	private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken ct)
	{
		string tmp = path + ".sync.tmp";
		await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (File.Exists(path))
		{
			File.Replace(tmp, path, null);
		}
		else
		{
			File.Move(tmp, path);
		}
	}

	private string ManifestPath()
	{
		return Path.Combine(Path.GetDirectoryName(_paths.CatalogDbPath), "published.sync.json");
	}

	private string? LoadLastSync()
	{
		try
		{
			string path = ManifestPath();
			if (!File.Exists(path))
			{
				return null;
			}
			using FileStream utf8Json = File.OpenRead(path);
			return JsonSerializer.Deserialize<Manifest>(utf8Json, JsonOpts)?.LastSync;
		}
		catch
		{
			return null;
		}
	}

	private void SaveLastSync(string lastSync)
	{
		try
		{
			string text = ManifestPath();
			Directory.CreateDirectory(Path.GetDirectoryName(text));
			string text2 = text + ".tmp";
			using (FileStream utf8Json = File.Create(text2))
			{
				JsonSerializer.Serialize(utf8Json, new Manifest(lastSync), JsonOpts);
			}
			if (File.Exists(text))
			{
				File.Replace(text2, text, null);
			}
			else
			{
				File.Move(text2, text);
			}
		}
		catch
		{
		}
	}
}
