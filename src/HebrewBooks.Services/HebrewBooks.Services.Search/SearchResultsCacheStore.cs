using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Services.Search;

public sealed class SearchResultsCacheStore
{
	private sealed record CacheFile(long Stamp, IReadOnlyList<SearchHit> Hits);

	private sealed record RowCacheFile(long SavedUtcTicks, IReadOnlyList<SearchResultRow> Rows);

	private const long MaxTotalBytes = 67108864L;

	private const int CacheFormatVersion = 2;

	private const int RemoteCacheFormatVersion = 1;

	private static readonly long RemoteMaxAgeTicks = TimeSpan.FromHours(12.0).Ticks;

	private const int MaxMemoryEntries = 16;

	private const int MaxMemoryHits = 100000;

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		WriteIndented = false
	};

	private readonly string _dir;

	private readonly object _lock = new object();

	private readonly IProtectMode? _protect;

	private readonly Dictionary<string, (long Stamp, IReadOnlyList<SearchHit> Hits)> _memHits = new Dictionary<string, (long, IReadOnlyList<SearchHit>)>(StringComparer.Ordinal);

	private readonly Dictionary<string, (long SavedUtcTicks, IReadOnlyList<SearchResultRow> Rows)> _memRows = new Dictionary<string, (long, IReadOnlyList<SearchResultRow>)>(StringComparer.Ordinal);

	private readonly Queue<string> _memOrder = new Queue<string>();

	private int _memHitCount;

	private bool Disabled => _protect?.IsActive ?? false;

	public SearchResultsCacheStore(IProtectMode? protectMode = null)
	{
		_protect = protectMode;
		_dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HebrewBooks", "search-cache");
	}

	public static string Fingerprint(SearchQuery q)
	{
		return HashKey(new object[9]
		{
			2,
			q.Text,
			q.MaxProximity,
			q.Hybur,
			q.IncludeNumbers,
			q.Fuzziness,
			q.MaxFilesToRetrieve,
			q.RestrictToIndexPaths?.OrderBy<string, string>((string x) => x, StringComparer.Ordinal).ToArray(),
			q.RestrictToFileIds?.OrderBy<string, string>((string x) => x, StringComparer.Ordinal).ToArray()
		});
	}

	public static string RemoteFingerprint(string baseUrl, string query, int proximity, bool hybur, bool roots, bool gematria, bool spelling, bool numberGender, bool aramaic, bool rasheyTevot, bool requireWordOrder, bool rashiOcr, int fuzziness, int maxFiles, IReadOnlyCollection<string>? corpora, IReadOnlyCollection<string>? restrictFileIds)
	{
		return HashKey(new object[18]
		{
			"remote",
			1,
			baseUrl,
			query,
			proximity,
			hybur,
			roots,
			gematria,
			spelling,
			numberGender,
			aramaic,
			rasheyTevot,
			requireWordOrder,
			rashiOcr,
			fuzziness,
			maxFiles,
			corpora?.OrderBy<string, string>((string x) => x, StringComparer.Ordinal).ToArray(),
			restrictFileIds?.OrderBy<string, string>((string x) => x, StringComparer.Ordinal).ToArray()
		});
	}

	private static string HashKey(object?[] key)
	{
		string s = JsonSerializer.Serialize(key, JsonOpts);
		return ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
	}

	public IReadOnlyList<SearchHit>? TryLoad(string fingerprint, long indexStamp)
	{
		if (Disabled)
		{
			lock (_lock)
			{
				(long, IReadOnlyList<SearchHit>) value;
				return (_memHits.TryGetValue(fingerprint, out value) && value.Item1 == indexStamp) ? value.Item2 : null;
			}
		}
		try
		{
			string path = PathFor(fingerprint);
			if (!File.Exists(path))
			{
				return null;
			}
			CacheFile cacheFile;
			lock (_lock)
			{
				cacheFile = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path), JsonOpts);
			}
			if ((object)cacheFile == null || cacheFile.Stamp != indexStamp || cacheFile.Hits == null)
			{
				return null;
			}
			try
			{
				File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
			}
			catch
			{
			}
			return cacheFile.Hits;
		}
		catch
		{
			return null;
		}
	}

	public void Save(string fingerprint, long indexStamp, IReadOnlyList<SearchHit> hits)
	{
		if (Disabled)
		{
			lock (_lock)
			{
				if (!_memHits.ContainsKey(fingerprint))
				{
					_memOrder.Enqueue(fingerprint);
				}
				else
				{
					_memHitCount -= _memHits[fingerprint].Hits.Count;
				}
				_memHits[fingerprint] = (indexStamp, hits);
				_memHitCount += hits.Count;
				TrimMemory();
				return;
			}
		}
		try
		{
			Directory.CreateDirectory(_dir);
			string contents = JsonSerializer.Serialize(new CacheFile(indexStamp, hits), JsonOpts);
			string text = PathFor(fingerprint);
			lock (_lock)
			{
				string text2 = text + ".tmp";
				File.WriteAllText(text2, contents);
				File.Move(text2, text, overwrite: true);
				Prune();
			}
		}
		catch
		{
		}
	}

	public IReadOnlyList<SearchResultRow>? TryLoadRows(string fingerprint)
	{
		if (Disabled)
		{
			lock (_lock)
			{
				if (!_memRows.TryGetValue(fingerprint, out (long, IReadOnlyList<SearchResultRow>) value))
				{
					return null;
				}
				return (DateTime.UtcNow.Ticks - value.Item1 > RemoteMaxAgeTicks) ? null : value.Item2;
			}
		}
		try
		{
			string path = RowPathFor(fingerprint);
			if (!File.Exists(path))
			{
				return null;
			}
			RowCacheFile rowCacheFile;
			lock (_lock)
			{
				rowCacheFile = JsonSerializer.Deserialize<RowCacheFile>(File.ReadAllText(path), JsonOpts);
			}
			if (rowCacheFile?.Rows == null)
			{
				return null;
			}
			if (DateTime.UtcNow.Ticks - rowCacheFile.SavedUtcTicks > RemoteMaxAgeTicks)
			{
				return null;
			}
			try
			{
				File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
			}
			catch
			{
			}
			return rowCacheFile.Rows;
		}
		catch
		{
			return null;
		}
	}

	public void SaveRows(string fingerprint, IReadOnlyList<SearchResultRow> rows)
	{
		if (Disabled)
		{
			lock (_lock)
			{
				if (!_memRows.ContainsKey(fingerprint))
				{
					_memOrder.Enqueue(fingerprint);
				}
				else
				{
					_memHitCount -= _memRows[fingerprint].Rows.Count;
				}
				_memRows[fingerprint] = (DateTime.UtcNow.Ticks, rows);
				_memHitCount += rows.Count;
				TrimMemory();
				return;
			}
		}
		try
		{
			Directory.CreateDirectory(_dir);
			string contents = JsonSerializer.Serialize(new RowCacheFile(DateTime.UtcNow.Ticks, rows), JsonOpts);
			string text = RowPathFor(fingerprint);
			lock (_lock)
			{
				string text2 = text + ".tmp";
				File.WriteAllText(text2, contents);
				File.Move(text2, text, overwrite: true);
				Prune();
			}
		}
		catch
		{
		}
	}

	public void Clear()
	{
		lock (_lock)
		{
			_memHits.Clear();
			_memRows.Clear();
			_memOrder.Clear();
			_memHitCount = 0;
		}
		if (Disabled)
		{
			return;
		}
		try
		{
			if (Directory.Exists(_dir))
			{
				Directory.Delete(_dir, recursive: true);
			}
		}
		catch
		{
		}
	}

	private void TrimMemory()
	{
		while (_memOrder.Count > 1 && (_memOrder.Count > 16 || _memHitCount > 100000))
		{
			string key = _memOrder.Dequeue();
			(long, IReadOnlyList<SearchResultRow>) value2;
			if (_memHits.Remove(key, out (long, IReadOnlyList<SearchHit>) value))
			{
				_memHitCount -= value.Item2.Count;
			}
			else if (_memRows.Remove(key, out value2))
			{
				_memHitCount -= value2.Item2.Count;
			}
		}
	}

	private void Prune()
	{
		FileInfo[] files = new DirectoryInfo(_dir).GetFiles("*.json");
		long num = files.Sum((FileInfo f) => f.Length);
		if (num <= 67108864)
		{
			return;
		}
		foreach (FileInfo item in files.OrderBy((FileInfo f) => f.LastWriteTimeUtc))
		{
			if (num <= 67108864)
			{
				break;
			}
			try
			{
				num -= item.Length;
				item.Delete();
			}
			catch
			{
			}
		}
	}

	private string PathFor(string fingerprint)
	{
		return Path.Combine(_dir, fingerprint + ".json");
	}

	private string RowPathFor(string fingerprint)
	{
		return Path.Combine(_dir, fingerprint + ".rows.json");
	}

	private static string ToHex(byte[] bytes)
	{
		StringBuilder stringBuilder = new StringBuilder(bytes.Length * 2);
		foreach (byte b in bytes)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString();
	}
}
