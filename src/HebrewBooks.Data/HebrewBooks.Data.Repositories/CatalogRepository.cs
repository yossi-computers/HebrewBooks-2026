using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using Microsoft.Data.Sqlite;

namespace HebrewBooks.Data.Repositories;

public sealed class CatalogRepository(ISqliteConnectionFactory connections) : ICatalogRepository
{
	private const string SelectColumns = "ID, FileID, BookName, AuthorName, PrintPlace, PrintYear, CountPage, Description, Folder, Categories, Searchable, SourceType, RelativePath";

	private const string SelectColumnsNoDesc = "ID, FileID, BookName, AuthorName, PrintPlace, PrintYear, CountPage, Folder, Categories, Searchable, SourceType, RelativePath";

	public async Task<Book?> GetByIdAsync(int id, CancellationToken ct = default(CancellationToken))
	{
		Book result;
		await using (SqliteConnection conn = connections.Open())
		{
			result = await conn.QuerySingleOrDefaultAsync<Book>(new CommandDefinition("SELECT ID, FileID, BookName, AuthorName, PrintPlace, PrintYear, CountPage, Description, Folder, Categories, Searchable, SourceType, RelativePath FROM Katalog WHERE ID = @id", new { id }, null, null, null, CommandFlags.Buffered, ct));
		}
		return result;
	}

	public async Task<Book?> GetByFileIdAsync(string fileId, CancellationToken ct = default(CancellationToken))
	{
		Book result;
		await using (SqliteConnection conn = connections.Open())
		{
			result = await conn.QuerySingleOrDefaultAsync<Book>(new CommandDefinition("SELECT ID, FileID, BookName, AuthorName, PrintPlace, PrintYear, CountPage, Description, Folder, Categories, Searchable, SourceType, RelativePath FROM Katalog WHERE FileID = @fileId", new { fileId }, null, null, null, CommandFlags.Buffered, ct));
		}
		return result;
	}

	public async Task<IReadOnlyList<Book>> ListAsync(int skip, int take, string? sortBy = null, CancellationToken ct = default(CancellationToken), bool includeDescription = true)
	{
		string value = sortBy switch
		{
			"BookName" => "BookName COLLATE HEB", 
			"AuthorName" => "AuthorName COLLATE HEB", 
			"PrintYear" => SqliteConnectionFactory.HebrewYearOrderBy("PrintYear", descending: false), 
			"PrintYearDesc" => SqliteConnectionFactory.HebrewYearOrderBy("PrintYear", descending: true), 
			"PrintPlace" => "PrintPlace COLLATE HEB", 
			_ => "ID", 
		};
		string value2 = (includeDescription ? "ID, FileID, BookName, AuthorName, PrintPlace, PrintYear, CountPage, Description, Folder, Categories, Searchable, SourceType, RelativePath" : "ID, FileID, BookName, AuthorName, PrintPlace, PrintYear, CountPage, Folder, Categories, Searchable, SourceType, RelativePath");
		IReadOnlyList<Book> result;
		await using (SqliteConnection conn = connections.Open())
		{
			result = (await conn.QueryAsync<Book>(new CommandDefinition($"SELECT {value2} FROM Katalog ORDER BY {value} LIMIT @take OFFSET @skip", new { take, skip }, null, null, null, CommandFlags.Buffered, ct))).AsList();
		}
		return result;
	}

	public async Task<IReadOnlyList<Book>> FindByIdsAsync(IReadOnlyList<int> ids, CancellationToken ct = default(CancellationToken))
	{
		if (ids.Count == 0)
		{
			return Array.Empty<Book>();
		}
		IReadOnlyList<Book> result;
		await using (SqliteConnection conn = connections.Open())
		{
			result = (await conn.QueryAsync<Book>(new CommandDefinition("SELECT ID, FileID, BookName, AuthorName, PrintPlace, PrintYear, CountPage, Description, Folder, Categories, Searchable, SourceType, RelativePath FROM Katalog WHERE ID IN @ids", new { ids }, null, null, null, CommandFlags.Buffered, ct))).AsList();
		}
		return result;
	}

	public async Task<IReadOnlyList<Book>> FindByFileIdsAsync(IReadOnlyList<string> fileIds, CancellationToken ct = default(CancellationToken))
	{
		if (fileIds.Count == 0)
		{
			return Array.Empty<Book>();
		}
		IReadOnlyList<Book> result;
		await using (SqliteConnection conn = connections.Open())
		{
			List<Book> all = new List<Book>(fileIds.Count);
			for (int i = 0; i < fileIds.Count; i += 500)
			{
				List<string> ids = fileIds.Skip(i).Take(500).ToList();
				all.AddRange(await conn.QueryAsync<Book>(new CommandDefinition("SELECT ID, FileID, BookName, AuthorName, PrintPlace, PrintYear, CountPage, Description, Folder, Categories, Searchable, SourceType, RelativePath FROM Katalog WHERE FileID IN @ids", new { ids }, null, null, null, CommandFlags.Buffered, ct)));
			}
			result = all;
		}
		return result;
	}

	public async Task<int> AddAsync(Book book, CancellationToken ct = default(CancellationToken))
	{
		int result;
		await using (SqliteConnection conn = connections.Open())
		{
			result = (int)(await conn.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO Katalog (FileID, BookName, AuthorName, PrintPlace, PrintYear, CountPage, Description, Folder, Categories, Searchable, SourceType, RelativePath)\nVALUES (@FileID, @BookName, @AuthorName, @PrintPlace, @PrintYear, @CountPage, @Description, @Folder, @Categories, @Searchable, @SourceType, @RelativePath);\nSELECT last_insert_rowid();", book, null, null, null, CommandFlags.Buffered, ct)));
		}
		return result;
	}

	public async Task UpdateAsync(Book book, CancellationToken ct = default(CancellationToken))
	{
		await using SqliteConnection conn = connections.Open();
		await conn.ExecuteAsync(new CommandDefinition("UPDATE Katalog SET\n    FileID       = @FileID,\n    BookName     = @BookName,\n    AuthorName   = @AuthorName,\n    PrintPlace   = @PrintPlace,\n    PrintYear    = @PrintYear,\n    CountPage    = @CountPage,\n    Description  = @Description,\n    Folder       = @Folder,\n    Categories   = @Categories,\n    Searchable   = @Searchable,\n    SourceType   = @SourceType,\n    RelativePath = @RelativePath\nWHERE ID = @ID", book, null, null, null, CommandFlags.Buffered, ct));
	}

	public async Task DeleteAsync(int id, CancellationToken ct = default(CancellationToken))
	{
		await using SqliteConnection conn = connections.Open();
		using SqliteTransaction tx = conn.BeginTransaction();
		var parameters = new { id };
		CancellationToken cancellationToken = ct;
		string fileId = await conn.ExecuteScalarAsync<string>(new CommandDefinition("SELECT FileID FROM Katalog WHERE ID = @id", parameters, tx, null, null, CommandFlags.Buffered, cancellationToken));
		var parameters2 = new { id };
		cancellationToken = ct;
		await conn.ExecuteAsync(new CommandDefinition("DELETE FROM Katalog WHERE ID = @id", parameters2, tx, null, null, CommandFlags.Buffered, cancellationToken));
		var parameters3 = new { id };
		cancellationToken = ct;
		await conn.ExecuteAsync(new CommandDefinition("DELETE FROM BookMadaf WHERE BookID = @id", parameters3, tx, null, null, CommandFlags.Buffered, cancellationToken));
		if (!string.IsNullOrEmpty(fileId))
		{
			var parameters4 = new { fileId };
			cancellationToken = ct;
			await conn.ExecuteAsync(new CommandDefinition("DELETE FROM Favorites WHERE FileID = @fileId", parameters4, tx, null, null, CommandFlags.Buffered, cancellationToken));
			var parameters5 = new { fileId };
			cancellationToken = ct;
			await conn.ExecuteAsync(new CommandDefinition("DELETE FROM BookLastPage WHERE FileID = @fileId", parameters5, tx, null, null, CommandFlags.Buffered, cancellationToken));
		}
		tx.Commit();
	}

	public async Task<int> CountAsync(CancellationToken ct = default(CancellationToken))
	{
		int result;
		await using (SqliteConnection conn = connections.Open())
		{
			result = await conn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM Katalog", null, null, null, null, CommandFlags.Buffered, ct));
		}
		return result;
	}

	public async Task<string?> MaxFileIdAsync(CancellationToken ct = default(CancellationToken))
	{
		string result;
		await using (SqliteConnection conn = connections.Open())
		{
			result = (await conn.ExecuteScalarAsync<long?>(new CommandDefinition("SELECT MAX(CAST(FileID AS INTEGER)) FROM Katalog WHERE FileID IS NOT NULL AND FileID != '' AND SourceType = 'PDF' " + $"AND CAST(FileID AS INTEGER) < {9000000}", null, null, null, null, CommandFlags.Buffered, ct)))?.ToString(CultureInfo.InvariantCulture);
		}
		return result;
	}

	public async Task<IReadOnlyList<string>> GetDistinctCategoriesAsync(CancellationToken ct = default(CancellationToken))
	{
		IReadOnlyList<string> result;
		await using (SqliteConnection conn = connections.Open())
		{
			IEnumerable<string> obj = await conn.QueryAsync<string>(new CommandDefinition("SELECT DISTINCT Categories FROM Katalog WHERE Categories IS NOT NULL AND Categories != ''", null, null, null, null, CommandFlags.Buffered, ct));
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			foreach (string item in obj)
			{
				if (string.IsNullOrEmpty(item))
				{
					continue;
				}
				string[] array = item.Split('|');
				for (int i = 0; i < array.Length; i++)
				{
					string text = array[i].Trim();
					if (text.Length > 0)
					{
						hashSet.Add(text);
					}
				}
			}
			List<string> list = hashSet.ToList();
			list.Sort(HebrewCollation.Compare);
			result = list;
		}
		return result;
	}

	public async Task<IReadOnlyList<TocEntry>> GetTocAsync(int bookId, CancellationToken ct = default(CancellationToken))
	{
		IReadOnlyList<TocEntry> result;
		await using (SqliteConnection conn = connections.Open())
		{
			result = TocSerializer.Parse(await conn.ExecuteScalarAsync<string>(new CommandDefinition("SELECT TocJson FROM Katalog WHERE ID = @bookId", new { bookId }, null, null, null, CommandFlags.Buffered, ct)));
		}
		return result;
	}

	public async Task SetTocAsync(int bookId, IReadOnlyList<TocEntry> entries, CancellationToken ct = default(CancellationToken))
	{
		string json = TocSerializer.Serialize(entries);
		await using SqliteConnection conn = connections.Open();
		await conn.ExecuteAsync(new CommandDefinition("UPDATE Katalog SET TocJson = @json WHERE ID = @bookId", new { bookId, json }, null, null, null, CommandFlags.Buffered, ct));
	}

	public async Task<IReadOnlyDictionary<int, IReadOnlyList<TocEntry>>> GetTocsAsync(IReadOnlyList<int> bookIds, CancellationToken ct = default(CancellationToken))
	{
		Dictionary<int, IReadOnlyList<TocEntry>> result = new Dictionary<int, IReadOnlyList<TocEntry>>();
		if (bookIds.Count == 0)
		{
			return result;
		}
		IReadOnlyDictionary<int, IReadOnlyList<TocEntry>> result2;
		await using (SqliteConnection conn = connections.Open())
		{
			for (int i = 0; i < bookIds.Count; i += 500)
			{
				List<int> ids = bookIds.Skip(i).Take(500).ToList();
				foreach (var item2 in await conn.QueryAsync<(int, string)>(new CommandDefinition("SELECT ID AS Id, TocJson AS Json FROM Katalog WHERE ID IN @ids AND TocJson IS NOT NULL", new { ids }, null, null, null, CommandFlags.Buffered, ct)))
				{
					int item = item2.Item1;
					IReadOnlyList<TocEntry> readOnlyList = TocSerializer.Parse(item2.Item2);
					if (readOnlyList.Count > 0)
					{
						result[item] = readOnlyList;
					}
				}
			}
			result2 = result;
		}
		return result2;
	}

	public async Task<IReadOnlyList<RawTocRow>> GetRawTocsAsync(CancellationToken ct = default(CancellationToken))
	{
		IReadOnlyList<RawTocRow> result;
		await using (SqliteConnection conn = connections.Open())
		{
			result = (from r in await conn.QueryAsync<(string FileId, string SourceType, string TocJson)>(new CommandDefinition("SELECT FileID AS FileId, SourceType AS SourceType, TocJson AS TocJson FROM Katalog WHERE TocJson IS NOT NULL", null, null, null, null, CommandFlags.Buffered, ct))
				where !string.IsNullOrEmpty(r.FileId) && !string.IsNullOrWhiteSpace(r.TocJson)
				select new RawTocRow(r.FileId, r.SourceType ?? "PDF", r.TocJson)).ToList();
		}
		return result;
	}
}
