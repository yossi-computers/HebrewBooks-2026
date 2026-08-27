using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using HebrewBooks.Data;
using HebrewBooks.Services.Catalog;
using Microsoft.Extensions.Logging;

namespace HebrewBooks.Services.Search;

public sealed class SearchOrchestrator(ISearchEngine engine, ICatalogRepository catalog, IPathResolver paths, PopularitySnapshot popularity, ILogger<SearchOrchestrator> logger)
{
	private sealed class HitCountReporter : IProgress<SearchHit>
	{
		private readonly IProgress<int> _sink;

		private int _count;

		private long _lastTick;

		public HitCountReporter(IProgress<int> sink)
		{
			_sink = sink;
		}

		public void Report(SearchHit value)
		{
			_count++;
			long tickCount = Environment.TickCount64;
			if (tickCount - _lastTick >= 100)
			{
				_lastTick = tickCount;
				_sink.Report(_count);
			}
		}
	}

	private bool _primaryOpened;

	private readonly HashSet<string> _secondaryOpened = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> _secondaryDiagLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly SemaphoreSlim _openLock = new SemaphoreSlim(1, 1);

	private readonly Dictionary<string, InBookHitInfo> _inBookCache = new Dictionary<string, InBookHitInfo>(StringComparer.Ordinal);

	private readonly object _inBookLock = new object();

	private readonly Dictionary<string, string?> _markedPdfCache = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly object _markedPdfLock = new object();

	private const int StreamFlushSize = 25;

	private const int StreamFlushIntervalMs = 200;

	public ISearchEngine Engine => engine;

	public async Task<InBookHitInfo> GetInBookHitsCachedAsync(string fileName, string queryText, string? displayQuery = null, bool addPrefixes = false, bool expandRoots = false, bool expandNumberGender = false, bool expandGematria = false, bool expandSpelling = false, HebAramMap? aramaic = null, int fuzziness = 0, bool expandWeakLetters = false, CancellationToken ct = default(CancellationToken))
	{
		bool flag = aramaic != null && aramaic.Count > 0;
		string key = fileName + "|" + queryText + "|" + displayQuery + "|" + (addPrefixes ? "1" : "0") + "|" + (expandRoots ? "1" : "0") + "|" + (expandNumberGender ? "1" : "0") + "|" + (expandGematria ? "1" : "0") + "|" + (expandSpelling ? "1" : "0") + "|" + (flag ? "1" : "0") + "|" + (expandWeakLetters ? "1" : "0") + "|f" + Math.Clamp(fuzziness, 0, 10);
		lock (_inBookLock)
		{
			if (_inBookCache.TryGetValue(key, out InBookHitInfo value))
			{
				return value;
			}
		}
		bool hasDisplayQuery = !string.IsNullOrWhiteSpace(displayQuery);
		InBookHitInfo inBookHitInfo = await engine.GetInBookHitsAsync(fileName, queryText, !hasDisplayQuery, fuzziness, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (hasDisplayQuery)
		{
			IReadOnlyList<string> matchedTerms = QueryBuilder.ExtractHighlightTerms(displayQuery, addPrefixes, expandRoots, expandNumberGender, expandGematria, expandSpelling, aramaic, expandRashiOcr: false, dropPhraseConstituents: false, expandWeakLetters);
			inBookHitInfo = inBookHitInfo with
			{
				MatchedTerms = matchedTerms
			};
		}
		lock (_inBookLock)
		{
			_inBookCache[key] = inBookHitInfo;
		}
		return inBookHitInfo;
	}

	public async Task<string?> GetMarkedPdfPathCachedAsync(string fileName, string queryText, CancellationToken ct = default(CancellationToken))
	{
		string key = fileName + "|" + queryText;
		lock (_markedPdfLock)
		{
			if (_markedPdfCache.TryGetValue(key, out string value))
			{
				return value;
			}
		}
		string text = await engine.GenerateHighlightedPdfAsync(fileName, queryText, ct).ConfigureAwait(continueOnCapturedContext: false);
		lock (_markedPdfLock)
		{
			_markedPdfCache[key] = text;
		}
		return text;
	}

	public async Task EnsureIndexOpenAsync(CancellationToken ct = default(CancellationToken))
	{
		await _openLock.WaitAsync(ct);
		try
		{
			if (!_primaryOpened)
			{
				logger.LogInformation("Opening primary search index: {IndexesRoot}", paths.IndexesRoot);
				await engine.OpenIndexAsync(paths.IndexesRoot, ct);
				engine.FileNameToCatalogId = MakeFileIdResolver(paths);
				_primaryOpened = true;
			}
			await TryOpenSecondaryAsync(paths.OtzrayaIndexPath, ct);
			await TryOpenSecondaryAsync(paths.PersonalIndexPath, ct);
		}
		finally
		{
			_openLock.Release();
		}
	}

	private async Task TryOpenSecondaryAsync(string? indexPath, CancellationToken ct)
	{
		if (string.IsNullOrEmpty(indexPath) || _secondaryOpened.Contains(indexPath))
		{
			return;
		}
		if (!Directory.Exists(indexPath))
		{
			if (_secondaryDiagLogged.Add(indexPath))
			{
				logger.LogWarning("Secondary content-search index folder NOT FOUND: {IndexPath}. Content search for this corpus (Otzraya/Personal) will return NO results until that index folder is built in-app or copied onto the drive.", indexPath);
			}
			return;
		}
		int ixCount = Directory.EnumerateFiles(indexPath, "*.ix").Count();
		if (ixCount == 0)
		{
			if (_secondaryDiagLogged.Add(indexPath))
			{
				logger.LogWarning("Secondary content-search index folder has NO .ix segments (not built): {IndexPath}. Content search for this corpus will return NO results until the index is (re)built.", indexPath);
			}
		}
		else
		{
			await engine.OpenIndexAsync(indexPath, ct);
			_secondaryOpened.Add(indexPath);
			_secondaryDiagLogged.Remove(indexPath);
			logger.LogInformation("Secondary content-search index opened: {IndexPath} ({IxCount} .ix segments)", indexPath, ixCount);
		}
	}

	public static bool IsIndexBuilt(string? indexPath)
	{
		if (string.IsNullOrEmpty(indexPath))
		{
			return false;
		}
		try
		{
			return Directory.Exists(indexPath) && Directory.EnumerateFiles(indexPath, "*.ix").Any();
		}
		catch
		{
			return false;
		}
	}

	internal Func<string, string?> MakeFileIdResolver(IPathResolver paths)
	{
		string otzrayaRoot = paths.OtzrayaRoot;
		string personalRoot = paths.PersonalRoot;
		string currentDriveRoot = null;
		try
		{
			currentDriveRoot = Path.GetPathRoot(paths.OtzrayaRoot);
		}
		catch
		{
		}
		ConcurrentDictionary<string, string> longPathCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		int diagLogged = 0;
		return delegate(string fullPath)
		{
			if (string.IsNullOrEmpty(fullPath))
			{
				return (string?)null;
			}
			string rehomed = fullPath;
			if (!string.IsNullOrEmpty(currentDriveRoot))
			{
				try
				{
					string pathRoot = Path.GetPathRoot(fullPath);
					if (!string.IsNullOrEmpty(pathRoot) && !string.Equals(pathRoot, currentDriveRoot, StringComparison.OrdinalIgnoreCase))
					{
						rehomed = currentDriveRoot + fullPath.Substring(pathRoot.Length);
					}
				}
				catch
				{
				}
			}
			string text = ((rehomed.IndexOf('~') < 0) ? rehomed : longPathCache.GetOrAdd(fullPath, (string _) => TryGetLongPathName(rehomed) ?? rehomed));
			if (!string.IsNullOrEmpty(otzrayaRoot) && text.StartsWith(otzrayaRoot, StringComparison.OrdinalIgnoreCase))
			{
				return Path.GetRelativePath(otzrayaRoot, text);
			}
			if (!string.IsNullOrEmpty(personalRoot) && text.StartsWith(personalRoot, StringComparison.OrdinalIgnoreCase))
			{
				return Path.GetRelativePath(personalRoot, text);
			}
			string text2 = RelativeUnderCorpusFolder(text, otzrayaRoot) ?? RelativeUnderCorpusFolder(text, personalRoot);
			if (text2 != null)
			{
				return text2;
			}
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(text);
			if (Interlocked.CompareExchange(ref diagLogged, 0, 0) < 8 && (text.Count((char ch) => ch == '\\') >= 2 || text.IndexOf('~') >= 0) && fileNameWithoutExtension.Any((char ch) => ch > '\u007f') && Interlocked.Increment(ref diagLogged) <= 8)
			{
				logger.LogWarning("FileId resolve FELL THROUGH to PDF-stem for a corpus-looking path — Otzraya/Personal hit will be dropped on the catalog join. hit='{Hit}' resolved='{Resolved}' otzrayaRoot='{OtzRoot}' personalRoot='{PerRoot}'", fullPath, text, otzrayaRoot, personalRoot);
			}
			return (!string.IsNullOrEmpty(fileNameWithoutExtension)) ? fileNameWithoutExtension : null;
		};
	}

	private static string? RelativeUnderCorpusFolder(string path, string? corpusRoot)
	{
		if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(corpusRoot))
		{
			return null;
		}
		string fileName = Path.GetFileName(corpusRoot.TrimEnd('\\', '/'));
		if (string.IsNullOrEmpty(fileName))
		{
			return null;
		}
		char[] separator = new char[2] { '\\', '/' };
		string[] array = path.Split(separator, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length - 1; i++)
		{
			if (string.Equals(array[i], fileName, StringComparison.OrdinalIgnoreCase))
			{
				return string.Join(Path.DirectorySeparatorChar, array.Skip(i + 1));
			}
		}
		return null;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetLongPathNameW(string lpszShortPath, StringBuilder lpszLongPath, uint cchBuffer);

	private static string? TryGetLongPathName(string shortPath)
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder(260);
			uint longPathNameW = GetLongPathNameW(shortPath, stringBuilder, (uint)stringBuilder.Capacity);
			if (longPathNameW == 0)
			{
				return null;
			}
			if (longPathNameW > stringBuilder.Capacity)
			{
				stringBuilder = new StringBuilder((int)(longPathNameW + 1));
				if (GetLongPathNameW(shortPath, stringBuilder, (uint)stringBuilder.Capacity) == 0)
				{
					return null;
				}
			}
			return stringBuilder.ToString();
		}
		catch
		{
			return null;
		}
	}

	public async Task<IReadOnlyList<SearchResultRow>> RunAsync(SearchQuery query, SortMode sort = SortMode.HitCount, IProgress<SearchResultRow>? progress = null, CancellationToken ct = default(CancellationToken), IProgress<int>? liveHitCount = null)
	{
		await EnsureIndexOpenAsync(ct);
		IReadOnlyList<string> restrictToFileIds = query.RestrictToFileIds;
		HashSet<string> restrict = ((restrictToFileIds != null && restrictToFileIds.Count > 0) ? new HashSet<string>(restrictToFileIds, StringComparer.Ordinal) : null);
		lock (_inBookLock)
		{
			_inBookCache.Clear();
		}
		lock (_markedPdfLock)
		{
			_markedPdfCache.Clear();
		}
		Queue<SearchHit> pending = new Queue<SearchHit>();
		List<SearchHit> allHits = new List<SearchHit>();
		HashSet<string> emittedFileIds = new HashSet<string>(StringComparer.Ordinal);
		object pendingLock = new object();
		long lastFlushTicks = Environment.TickCount64;
		IProgress<SearchHit> progress2 = ((progress != null) ? ((IProgress<SearchHit>)new Progress<SearchHit>(delegate(SearchHit hit)
		{
			lock (pendingLock)
			{
				pending.Enqueue(hit);
			}
			long tickCount = Environment.TickCount64;
			int count;
			lock (pendingLock)
			{
				count = pending.Count;
			}
			if (count >= 25 || tickCount - lastFlushTicks >= 200)
			{
				lastFlushTicks = tickCount;
				FlushAsync();
			}
		})) : ((IProgress<SearchHit>)((liveHitCount == null) ? null : new HitCountReporter(liveHitCount))));
		allHits.AddRange(await engine.SearchAsync(query, progress2, ct));
		await FlushAsync();
		if (allHits.Count == 0)
		{
			return Array.Empty<SearchResultRow>();
		}
		List<string> fileIds = allHits.Select((SearchHit h) => h.FileID).Distinct<string>(StringComparer.Ordinal).ToList();
		Dictionary<string, Book> byFileId = (await catalog.FindByFileIdsAsync(fileIds, ct)).Where((Book b) => !string.IsNullOrEmpty(b.FileID)).GroupBy<Book, string>((Book b) => b.FileID, StringComparer.Ordinal).ToDictionary<IGrouping<string, Book>, string, Book>((IGrouping<string, Book> g) => g.Key, (IGrouping<string, Book> g) => g.First(), StringComparer.Ordinal);
		Book value;
		List<SearchResultRow> list = (from h in allHits
			where restrict == null || restrict.Contains(h.FileID)
			select (!byFileId.TryGetValue(h.FileID, out value)) ? null : new SearchResultRow(value, h.HitCount, h.Location, h.PageNumber) into r
			where (object)r != null
			select r).Cast<SearchResultRow>().ToList();
		int num = 0;
		int num2 = 0;
		string text = null;
		foreach (SearchHit item in allHits)
		{
			string fileID = item.FileID;
			if (!string.IsNullOrEmpty(fileID) && (fileID.IndexOf('\\') >= 0 || !long.TryParse(fileID, out var _)))
			{
				num++;
				if (byFileId.ContainsKey(fileID))
				{
					num2++;
				}
				else if (text == null)
				{
					text = fileID;
				}
			}
		}
		logger.LogInformation("Search composition: totalHits={Total} joinedRows={Rows} secondaryHits(Otzraya/Personal)={SecHits} secondaryMatchedInCatalog={SecMatched} sampleUnmatchedFileId='{Sample}'", allHits.Count, list.Count, num, num2, text ?? "(none)");
		return ApplySort(list, sort);
		async Task FlushAsync()
		{
			List<SearchHit> batch;
			lock (pendingLock)
			{
				if (pending.Count == 0)
				{
					return;
				}
				batch = new List<SearchHit>(pending);
				pending.Clear();
			}
			List<string> fileIds2 = batch.Select((SearchHit h) => h.FileID).Distinct<string>(StringComparer.Ordinal).ToList();
			IReadOnlyList<Book> source;
			try
			{
				source = await catalog.FindByFileIdsAsync(fileIds2, ct);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			Dictionary<string, Book> dictionary = source.Where((Book b) => !string.IsNullOrEmpty(b.FileID)).GroupBy<Book, string>((Book b) => b.FileID, StringComparer.Ordinal).ToDictionary<IGrouping<string, Book>, string, Book>((IGrouping<string, Book> g) => g.Key, (IGrouping<string, Book> g) => g.First(), StringComparer.Ordinal);
			foreach (SearchHit item2 in batch)
			{
				if ((restrict == null || restrict.Contains(item2.FileID)) && emittedFileIds.Add(item2.FileID) && dictionary.TryGetValue(item2.FileID, out value))
				{
					progress.Report(new SearchResultRow(value, item2.HitCount, item2.Location, item2.PageNumber));
				}
			}
		}
	}

	public async Task<IReadOnlyList<SearchResultRow>> RehydrateAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct = default(CancellationToken))
	{
		if (hits.Count == 0)
		{
			return Array.Empty<SearchResultRow>();
		}
		List<string> fileIds = hits.Select((SearchHit h) => h.FileID).Distinct<string>(StringComparer.Ordinal).ToList();
		Dictionary<string, Book> byFileId = (await catalog.FindByFileIdsAsync(fileIds, ct)).Where((Book b) => !string.IsNullOrEmpty(b.FileID)).GroupBy<Book, string>((Book b) => b.FileID, StringComparer.Ordinal).ToDictionary<IGrouping<string, Book>, string, Book>((IGrouping<string, Book> g) => g.Key, (IGrouping<string, Book> g) => g.First(), StringComparer.Ordinal);
		Book value;
		return (from h in hits
			select (!byFileId.TryGetValue(h.FileID, out value)) ? null : new SearchResultRow(value, h.HitCount, h.Location, h.PageNumber) into r
			where (object)r != null
			select r).Cast<SearchResultRow>().ToList();
	}

	private IReadOnlyList<SearchResultRow> ApplySort(List<SearchResultRow> rows, SortMode sort)
	{
		IComparer<string> comparer = Comparer<string>.Create(HebrewCollation.Compare);
		switch (sort)
		{
		case SortMode.BookName:
			return rows.OrderBy<SearchResultRow, string>((SearchResultRow r) => r.Book.BookName, comparer).ToList();
		case SortMode.AuthorName:
			return rows.OrderBy<SearchResultRow, string>((SearchResultRow r) => r.Book.AuthorName, comparer).ToList();
		case SortMode.PrintPlace:
			return rows.OrderBy<SearchResultRow, string>((SearchResultRow r) => r.Book.PrintPlace, comparer).ToList();
		case SortMode.PrintYear:
		case SortMode.PrintYearDesc:
			return rows.Order(CatalogSorting.RowComparer(sort)).ToList();
		case SortMode.HitCount:
			return rows.OrderByDescending((SearchResultRow r) => (double)r.HitCount * popularity.BoostFactor(r.Book.FileID)).ToList();
		default:
			return rows.OrderBy((SearchResultRow r) => r.Book.ID).ToList();
		}
	}
}
