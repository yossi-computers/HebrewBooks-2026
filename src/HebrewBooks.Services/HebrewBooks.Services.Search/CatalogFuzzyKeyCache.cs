using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HebrewBooks.Core.Models;
using Microsoft.Extensions.Logging;

namespace HebrewBooks.Services.Search;

public sealed class CatalogFuzzyKeyCache
{
	public readonly record struct Fields(string NameNorm, string NameSkel, string AuthNorm, string AuthSkel);

	private const int Magic = 1212302936;

	private const int Version = 1;

	private readonly string _filePath;

	private readonly ILogger? _log;

	private readonly ConcurrentDictionary<int, Fields> _cache = new ConcurrentDictionary<int, Fields>();

	public CatalogFuzzyKeyCache(string userDataDir, ILogger? log = null)
	{
		_filePath = Path.Combine(userDataDir, "fuzzy-keys.bin");
		_log = log;
	}

	public static Fields Compute(Book b)
	{
		string text = HebrewFuzzyMatch.Normalize(b.BookName);
		string text2 = HebrewFuzzyMatch.Normalize(b.AuthorName);
		return new Fields(text, HebrewFuzzyMatch.Skeleton(text), text2, HebrewFuzzyMatch.Skeleton(text2));
	}

	public Fields For(Book b)
	{
		return _cache.GetOrAdd(b.ID, (int _, Book book) => Compute(book), b);
	}

	public Task WarmAsync(IReadOnlyList<Book> books, CancellationToken ct = default(CancellationToken))
	{
		int count = books.Count;
		int num = 0;
		for (int i = 0; i < books.Count; i++)
		{
			if (books[i].ID > num)
			{
				num = books[i].ID;
			}
		}
		(int count, int maxId) fp = (count: count, maxId: num);
		return Task.Run(delegate
		{
			try
			{
				if (TryLoad(fp, ct))
				{
					_log?.LogInformation("FuzzyKeyCache: loaded {N} entries from disk", _cache.Count);
				}
				else
				{
					_cache.Clear();
					foreach (Book book in books)
					{
						ct.ThrowIfCancellationRequested();
						_cache.TryAdd(book.ID, Compute(book));
					}
					Save(fp, ct);
					_log?.LogInformation("FuzzyKeyCache: built + persisted {N} entries", _cache.Count);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception exception)
			{
				_log?.LogWarning(exception, "FuzzyKeyCache warm failed (search still works lazily)");
			}
		}, ct);
	}

	public void Invalidate()
	{
		_cache.Clear();
		try
		{
			if (File.Exists(_filePath))
			{
				File.Delete(_filePath);
			}
		}
		catch (Exception exception)
		{
			_log?.LogDebug(exception, "FuzzyKeyCache: delete on invalidate failed");
		}
	}

	private bool TryLoad((int Count, int MaxId) fp, CancellationToken ct)
	{
		if (!File.Exists(_filePath))
		{
			return false;
		}
		try
		{
			using FileStream input = File.OpenRead(_filePath);
			using BinaryReader binaryReader = new BinaryReader(input, Encoding.UTF8);
			if (binaryReader.ReadInt32() != 1212302936 || binaryReader.ReadInt32() != 1)
			{
				return false;
			}
			if (binaryReader.ReadInt32() != fp.Count || binaryReader.ReadInt32() != fp.MaxId)
			{
				return false;
			}
			int num = binaryReader.ReadInt32();
			_cache.Clear();
			for (int i = 0; i < num; i++)
			{
				ct.ThrowIfCancellationRequested();
				int key = binaryReader.ReadInt32();
				string nameNorm = binaryReader.ReadString();
				string nameSkel = binaryReader.ReadString();
				string authNorm = binaryReader.ReadString();
				string authSkel = binaryReader.ReadString();
				_cache[key] = new Fields(nameNorm, nameSkel, authNorm, authSkel);
			}
			return _cache.Count > 0;
		}
		catch (Exception exception)
		{
			_log?.LogDebug(exception, "FuzzyKeyCache: load failed — will rebuild");
			_cache.Clear();
			return false;
		}
	}

	private void Save((int Count, int MaxId) fp, CancellationToken ct)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
			string text = _filePath + ".tmp";
			using (FileStream output = File.Create(text))
			{
				using BinaryWriter binaryWriter = new BinaryWriter(output, Encoding.UTF8);
				binaryWriter.Write(1212302936);
				binaryWriter.Write(1);
				binaryWriter.Write(fp.Count);
				binaryWriter.Write(fp.MaxId);
				binaryWriter.Write(_cache.Count);
				foreach (KeyValuePair<int, Fields> item in _cache)
				{
					ct.ThrowIfCancellationRequested();
					binaryWriter.Write(item.Key);
					binaryWriter.Write(item.Value.NameNorm);
					binaryWriter.Write(item.Value.NameSkel);
					binaryWriter.Write(item.Value.AuthNorm);
					binaryWriter.Write(item.Value.AuthSkel);
				}
			}
			File.Move(text, _filePath, overwrite: true);
		}
		catch (Exception exception)
		{
			_log?.LogWarning(exception, "FuzzyKeyCache: save failed");
		}
	}
}
