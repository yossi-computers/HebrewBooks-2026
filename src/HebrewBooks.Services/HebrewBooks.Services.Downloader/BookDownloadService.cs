using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Core.Resources;

namespace HebrewBooks.Services.Downloader;

public sealed class BookDownloadService
{
	private readonly ICatalogRepository _catalog;

	private readonly IPathResolver _paths;

	private readonly R2MirrorClient _r2;

	private readonly IProtectMode? _protect;

	private readonly IPdfLinearizer? _linearizer;

	public const int MirrorParallelism = 16;

	private static readonly Regex PreviewPattern = new Regex("preview\\('(\\d+)'\\)", RegexOptions.Compiled);

	private static readonly Regex SpanPattern = new Regex("<span\\s+id=\"(?<id>cpMstr_lbl[^\"]+|sefername|authorname|sefernameeng|authornameeng)\"[^>]*>(?<val>[^<]*)</span>", RegexOptions.Compiled);

	private static readonly Regex TocSelectPattern = new Regex("<select\\s+name=\"ctl00\\$cpMstr\\$ctl06\"[^>]*>(?<body>[\\s\\S]*?)</select>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex TocOptionPattern = new Regex("<option\\s+value=\"(?<page>\\d+)\">(?<title>[^<]*)</option>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex CharsetPattern = new Regex("charset\\s*=\\s*[\"']?(?<cs>[A-Za-z0-9_\\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private bool BookFetchBlocked
	{
		get
		{
			IProtectMode? protect = _protect;
			if (protect != null && protect.IsActive)
			{
				return !_protect.AllowsBookFetch;
			}
			return false;
		}
	}

	public bool DownloadsBlockedByProtectMode => BookFetchBlocked;

	public async Task<int> SyncDiskFilesToCatalogAsync(IProgress<int>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		if (_protect?.IsActive ?? false)
		{
			return 0;
		}
		if (!Directory.Exists(_paths.PdfsRoot))
		{
			return 0;
		}
		List<int> diskIds = new List<int>();
		foreach (string item in Directory.EnumerateFiles(_paths.PdfsRoot, "*.pdf"))
		{
			if (int.TryParse(Path.GetFileNameWithoutExtension(item), out var result))
			{
				diskIds.Add(result);
			}
		}
		diskIds.Sort();
		int added = 0;
		for (int i = 0; i < diskIds.Count; i++)
		{
			ct.ThrowIfCancellationRequested();
			int fileId = diskIds[i];
			if ((object)(await _catalog.GetByFileIdAsync(fileId.ToString(), ct).ConfigureAwait(continueOnCapturedContext: false)) != null)
			{
				continue;
			}
			try
			{
				if ((await DownloadBookAsync(fileId, ct).ConfigureAwait(continueOnCapturedContext: false)).Success)
				{
					added++;
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch
			{
			}
			progress?.Report(i + 1);
		}
		return added;
	}

	public BookDownloadService(ICatalogRepository catalog, IPathResolver paths, R2MirrorClient r2, IProtectMode? protectMode = null, IPdfLinearizer? linearizer = null)
	{
		_catalog = catalog;
		_paths = paths;
		_r2 = r2;
		_protect = protectMode;
		_linearizer = linearizer;
	}

	public async Task<int> GetMaxLocalAsync(CancellationToken ct = default(CancellationToken))
	{
		int result;
		return int.TryParse(await _catalog.MaxFileIdAsync(ct).ConfigureAwait(continueOnCapturedContext: false), out result) ? result : 0;
	}

	public async Task<int> GetMaxOnSiteAsync(CancellationToken ct = default(CancellationToken))
	{
		if (_protect?.IsActive ?? false)
		{
			return 0;
		}
		string input = await CurlAsync("https://hebrewbooks.org/latest", ct).ConfigureAwait(continueOnCapturedContext: false);
		int num = 0;
		foreach (Match item in PreviewPattern.Matches(input))
		{
			if (int.TryParse(item.Groups[1].Value, out var result) && result > num)
			{
				num = result;
			}
		}
		return num;
	}

	public async Task<DownloadOutcome> DownloadBookAsync(int fileId, CancellationToken ct = default(CancellationToken))
	{
		if (_protect?.IsActive ?? false)
		{
			return new DownloadOutcome(fileId, Success: false, 0, null, "protect-mode (downloads disabled)");
		}
		string pdfsRoot = _paths.PdfsRoot;
		Directory.CreateDirectory(pdfsRoot);
		string pdfPath = Path.Combine(pdfsRoot, fileId + ".pdf");
		BookMetadata meta = null;
		try
		{
			meta = await FetchMetadataAsync(fileId, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
		}
		bool alreadyHave = File.Exists(pdfPath);
		if (!alreadyHave)
		{
			try
			{
				await CurlDownloadAsync($"https://download.hebrewbooks.org/downloadhandler.ashx?req={fileId}", pdfPath, ct);
			}
			catch (Exception ex)
			{
				try
				{
					if (File.Exists(pdfPath))
					{
						File.Delete(pdfPath);
					}
				}
				catch
				{
				}
				return new DownloadOutcome(fileId, Success: false, 0, null, CoreStrings.C6 + ex.Message);
			}
			if (!File.Exists(pdfPath))
			{
				return new DownloadOutcome(fileId, Success: false, 0, null, CoreStrings.C10);
			}
			long length = new FileInfo(pdfPath).Length;
			if (!IsPdfFile(pdfPath))
			{
				try
				{
					File.Delete(pdfPath);
				}
				catch
				{
				}
				return new DownloadOutcome(fileId, Success: false, 0, null, $"{CoreStrings.C7}{length}{CoreStrings.C8}");
			}
			IPdfLinearizer linearizer = _linearizer;
			if (linearizer != null && linearizer.IsAvailable)
			{
				await _linearizer.LinearizeInPlaceAsync(pdfPath, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		Book existing = await _catalog.GetByFileIdAsync(fileId.ToString(), ct).ConfigureAwait(continueOnCapturedContext: false);
		Book book = new Book
		{
			ID = (existing?.ID ?? 0),
			FileID = fileId.ToString(),
			BookName = (meta?.BookName ?? existing?.BookName ?? $"{CoreStrings.C9}{fileId}"),
			AuthorName = (meta?.AuthorName ?? existing?.AuthorName),
			PrintPlace = (meta?.PrintPlace ?? existing?.PrintPlace),
			PrintYear = (meta?.PrintYear ?? existing?.PrintYear),
			CountPage = (meta?.CountPage ?? (existing?.CountPage).GetValueOrDefault()),
			Description = (meta?.Description ?? existing?.Description),
			Folder = existing?.Folder,
			Categories = existing?.Categories,
			Searchable = true
		};
		int catalogId;
		if ((object)existing == null)
		{
			catalogId = await _catalog.AddAsync(book, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		else
		{
			await _catalog.UpdateAsync(book, ct).ConfigureAwait(continueOnCapturedContext: false);
			catalogId = existing.ID;
		}
		IReadOnlyList<TocEntry> toc = meta?.Toc;
		if (toc != null && toc.Count > 0)
		{
			try
			{
				if ((await _catalog.GetTocAsync(catalogId, ct).ConfigureAwait(continueOnCapturedContext: false)).Count == 0)
				{
					await _catalog.SetTocAsync(catalogId, toc, ct).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			catch
			{
			}
		}
		return new DownloadOutcome(fileId, Success: true, catalogId, book.BookName, null, alreadyHave);
	}

	public async Task<DownloadCandidateInfo> PeekBookInfoAsync(int fileId, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			BookMetadata bookMetadata = await FetchMetadataAsync(fileId, ct).ConfigureAwait(continueOnCapturedContext: false);
			return new DownloadCandidateInfo(fileId, string.IsNullOrWhiteSpace(bookMetadata?.BookName) ? $"{CoreStrings.C9}{fileId}" : bookMetadata.BookName, bookMetadata?.AuthorName, (object)bookMetadata != null);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return new DownloadCandidateInfo(fileId, $"{CoreStrings.C9}{fileId}", null, Found: false);
		}
	}

	private async Task<BookMetadata?> FetchMetadataAsync(int fileId, CancellationToken ct)
	{
		string text = await CurlAsync($"https://hebrewbooks.org/{fileId}", ct).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (Match item in SpanPattern.Matches(text))
		{
			string value = item.Groups["id"].Value;
			string text2 = StripWrappingQuotes(NormalizeGershayim(WebHtmlDecode(item.Groups["val"].Value)).Trim());
			if (text2.Length > 0)
			{
				fields[value] = text2;
			}
		}
		int num = 0;
		if (fields.TryGetValue("cpMstr_lblPages", out string value2) && int.TryParse(value2, out var result))
		{
			num = result;
		}
		return new BookMetadata(fileId, Pick(new string[2] { "cpMstr_lblHebSefername", "cpMstr_lblSefername" }), Pick(new string[2] { "cpMstr_lblHebAuth", "cpMstr_lblAuth" }), Pick(new string[2] { "cpMstr_lblHebPlace", "cpMstr_lblPlace" }), Pick(new string[2] { "cpMstr_lblHebDate", "cpMstr_lblDate" }), (num > 0) ? new int?(num) : ((int?)null), Pick(new string[1] { "cpMstr_lblDesc" }), ParseTocFromHtml(text));
		string? Pick(string[] keys)
		{
			foreach (string key in keys)
			{
				if (fields.TryGetValue(key, out string value3) && value3.Length > 0)
				{
					return value3;
				}
			}
			return null;
		}
	}

	internal static IReadOnlyList<TocEntry> ParseTocFromHtml(string html)
	{
		if (string.IsNullOrEmpty(html))
		{
			return Array.Empty<TocEntry>();
		}
		Match match = TocSelectPattern.Match(html);
		if (!match.Success)
		{
			return Array.Empty<TocEntry>();
		}
		List<TocEntry> list = new List<TocEntry>();
		foreach (Match item in TocOptionPattern.Matches(match.Groups["body"].Value))
		{
			if (int.TryParse(item.Groups["page"].Value, out var result) && result > 0)
			{
				string text = StripWrappingQuotes(NormalizeGershayim(WebHtmlDecode(item.Groups["title"].Value)).Trim());
				if (text.Length != 0)
				{
					list.Add(new TocEntry(text, result));
				}
			}
		}
		return list;
	}

	private static string WebHtmlDecode(string s)
	{
		if (string.IsNullOrEmpty(s) || s.IndexOf('&') < 0)
		{
			return s;
		}
		return WebUtility.HtmlDecode(s);
	}

	internal static string NormalizeGershayim(string s)
	{
		if (string.IsNullOrEmpty(s) || s.IndexOf('\'') < 0)
		{
			return s;
		}
		return s.Replace("''", "\"");
	}

	internal static string StripWrappingQuotes(string s)
	{
		if (s.Length < 4)
		{
			return s;
		}
		if (s[0] == '"')
		{
			if (s[s.Length - 1] == '"')
			{
				ReadOnlySpan<char> span = s.AsSpan(1, s.Length - 2);
				if (span.IndexOf('"') < 0)
				{
					return s;
				}
				return span.ToString();
			}
		}
		return s;
	}

	public IReadOnlySet<int> EnumerateDiskFileIds()
	{
		HashSet<int> hashSet = new HashSet<int>();
		if (!Directory.Exists(_paths.PdfsRoot))
		{
			return hashSet;
		}
		foreach (string item in Directory.EnumerateFiles(_paths.PdfsRoot, "*.pdf"))
		{
			if (int.TryParse(Path.GetFileNameWithoutExtension(item), out var result))
			{
				hashSet.Add(result);
			}
		}
		return hashSet;
	}

	public async Task<IReadOnlySet<int>> ListMirrorFileIdsAsync(IProgress<int>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		HashSet<int> ids = new HashSet<int>();
		if (_protect?.IsActive ?? false)
		{
			return ids;
		}
		string prefix = "HebrewBooks/books/";
		foreach (var item in await _r2.ListPrefixAsync(prefix, progress, ct).ConfigureAwait(continueOnCapturedContext: false))
		{
			if (int.TryParse(Path.GetFileNameWithoutExtension(item.Item1), out var result))
			{
				ids.Add(result);
			}
		}
		return ids;
	}

	public async Task<CompletionScan> ScanForMissingAsync(int maxOnSite, IProgress<int>? listProgress = null, CancellationToken ct = default(CancellationToken))
	{
		IReadOnlySet<int> readOnlySet = await ListMirrorFileIdsAsync(listProgress, ct).ConfigureAwait(continueOnCapturedContext: false);
		IReadOnlySet<int> readOnlySet2 = EnumerateDiskFileIds();
		int num = ((readOnlySet.Count > 0) ? readOnlySet.Max() : 0);
		List<int> list = new List<int>();
		foreach (int item in readOnlySet)
		{
			if (!readOnlySet2.Contains(item))
			{
				list.Add(item);
			}
		}
		for (int i = num + 1; i <= maxOnSite; i++)
		{
			if (!readOnlySet2.Contains(i))
			{
				list.Add(i);
			}
		}
		list.Sort();
		return new CompletionScan(readOnlySet.Count, readOnlySet2.Count, num, list);
	}

	public async Task<IReadOnlySet<int>> MirrorPrefetchAsync(IReadOnlyList<int> ids, IProgress<int>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		if (BookFetchBlocked || ids.Count == 0)
		{
			return new HashSet<int>();
		}
		Directory.CreateDirectory(_paths.PdfsRoot);
		ConcurrentBag<int> hits = new ConcurrentBag<int>();
		SemaphoreSlim gate = new SemaphoreSlim(16);
		try
		{
			int done = 0;
			await Task.WhenAll(((IEnumerable<int>)ids).Select((Func<int, Task>)async delegate(int id)
			{
				string pdfPath = Path.Combine(_paths.PdfsRoot, id + ".pdf");
				if (!File.Exists(pdfPath))
				{
					await gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
					try
					{
						if (await TryDownloadFromMirrorAsync(id, pdfPath, ct).ConfigureAwait(continueOnCapturedContext: false))
						{
							hits.Add(id);
						}
					}
					finally
					{
						gate.Release();
					}
				}
				progress?.Report(Interlocked.Increment(ref done));
			})).ConfigureAwait(continueOnCapturedContext: false);
			return new HashSet<int>(hits);
		}
		finally
		{
			if (gate != null)
			{
				((IDisposable)gate).Dispose();
			}
		}
	}

	private async Task<bool> TryDownloadFromMirrorAsync(int fileId, string destPath, CancellationToken ct)
	{
		try
		{
			await _r2.DownloadKeyAsync($"{"HebrewBooks/books"}/{fileId}.pdf", destPath, null, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return false;
		}
		if (!File.Exists(destPath) || !IsPdfFile(destPath))
		{
			try
			{
				if (File.Exists(destPath))
				{
					File.Delete(destPath);
				}
			}
			catch
			{
			}
			return false;
		}
		return true;
	}

	private static async Task<string> CurlAsync(string url, CancellationToken ct)
	{
		ProcessStartInfo startInfo = NewCurlPsi(url);
		using Process proc = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start curl.");
		using MemoryStream ms = new MemoryStream();
		Task copyTask = proc.StandardOutput.BaseStream.CopyToAsync(ms, ct);
		string stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		await copyTask.ConfigureAwait(continueOnCapturedContext: false);
		await proc.WaitForExitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (proc.ExitCode != 0)
		{
			throw new InvalidOperationException($"curl exit {proc.ExitCode}: {stderr.Trim()}");
		}
		return DecodeHtmlBody(ms.ToArray());
	}

	private static string DecodeHtmlBody(byte[] bytes)
	{
		if (bytes.Length == 0)
		{
			return string.Empty;
		}
		if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191)
		{
			return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
		}
		if (bytes.Length >= 2 && bytes[0] == byte.MaxValue && bytes[1] == 254)
		{
			return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
		}
		int count = Math.Min(bytes.Length, 4096);
		string input = Encoding.ASCII.GetString(bytes, 0, count);
		Match match = CharsetPattern.Match(input);
		if (match.Success)
		{
			string value = match.Groups["cs"].Value;
			try
			{
				Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
				return Encoding.GetEncoding(value).GetString(bytes);
			}
			catch
			{
			}
		}
		return Encoding.UTF8.GetString(bytes);
	}

	private static async Task CurlDownloadAsync(string url, string destPath, CancellationToken ct)
	{
		ProcessStartInfo processStartInfo = NewCurlPsi(url);
		processStartInfo.ArgumentList.Insert(0, "--fail");
		processStartInfo.ArgumentList.Add("-o");
		processStartInfo.ArgumentList.Add(destPath);
		using Process proc = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Failed to start curl.");
		string stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		await proc.WaitForExitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (proc.ExitCode != 0)
		{
			string text = stderr.Trim();
			if (text.Length > 200)
			{
				text = text.Substring(0, 200) + "...";
			}
			throw new InvalidOperationException($"curl exit {proc.ExitCode}: {text}");
		}
	}

	private static bool IsPdfFile(string path)
	{
		try
		{
			using FileStream fileStream = File.OpenRead(path);
			Span<byte> buffer = stackalloc byte[5];
			return fileStream.Read(buffer) == 5 && buffer[0] == 37 && buffer[1] == 80 && buffer[2] == 68 && buffer[3] == 70 && buffer[4] == 45;
		}
		catch
		{
			return false;
		}
	}

	private static ProcessStartInfo NewCurlPsi(string url)
	{
		return new ProcessStartInfo
		{
			FileName = CurlLocator.Path,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
			ArgumentList = { "-sL", "--compressed", "--max-time", "180", "-A", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36", "-H", "Accept-Language: en,he;q=0.9", url }
		};
	}
}
