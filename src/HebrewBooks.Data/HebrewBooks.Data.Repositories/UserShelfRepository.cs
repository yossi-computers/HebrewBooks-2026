using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using Microsoft.Data.Sqlite;

namespace HebrewBooks.Data.Repositories;

public sealed class UserShelfRepository : IMadafRepository
{
	private readonly IPathResolver _paths;

	private readonly ISqliteConnectionFactory _catalog;

	private readonly object _schemaLock = new object();

	private bool _schemaReady;

	public UserShelfRepository(IPathResolver paths, ISqliteConnectionFactory catalog)
	{
		_paths = paths;
		_catalog = catalog;
	}

	private SqliteConnection OpenUserDb()
	{
		string text = Path.Combine(_paths.UserDataRoot, "user-shelves.db");
		Directory.CreateDirectory(Path.GetDirectoryName(text));
		SqliteConnection sqliteConnection = new SqliteConnection("Data Source=" + text);
		sqliteConnection.Open();
		if (!_schemaReady)
		{
			lock (_schemaLock)
			{
				if (!_schemaReady)
				{
					UserShelfSchema.Ensure(sqliteConnection);
					_schemaReady = true;
				}
			}
		}
		return sqliteConnection;
	}

	public async Task<IReadOnlyList<MadafNode>> GetTreeAsync(CancellationToken ct = default(CancellationToken))
	{
		IReadOnlyList<MadafNode> result;
		await using (SqliteConnection db = OpenUserDb())
		{
			CancellationToken cancellationToken = ct;
			List<(long NodeId, string Title, long Pinned)> shelves = (await db.QueryAsync<(long, string, long)>(new CommandDefinition("SELECT NodeId, Title, Pinned FROM ShelfNode WHERE ParentId IS NULL AND Kind = 0", null, null, null, null, CommandFlags.Buffered, cancellationToken))).AsList();
			if (shelves.Count == 0)
			{
				result = Array.Empty<MadafNode>();
			}
			else
			{
				cancellationToken = ct;
				List<(long ParentId, string FileId)> links = (await db.QueryAsync<(long, string)>(new CommandDefinition("SELECT ParentId, FileId FROM ShelfNode WHERE Kind = 1 AND ParentId IS NOT NULL AND FileId IS NOT NULL", null, null, null, null, CommandFlags.Buffered, cancellationToken))).AsList();
				Dictionary<string, int> fileToId = await MapFileIdsToBookIdsAsync(links.Select(((long ParentId, string FileId) l) => l.FileId).Distinct().ToList(), ct);
				int value;
				Dictionary<long, IReadOnlyList<int>> booksByShelf = (from l in links
					group l by l.ParentId).ToDictionary<IGrouping<long, (long, string)>, long, IReadOnlyList<int>>((Func<IGrouping<long, (long, string)>, long>)((IGrouping<long, (long ParentId, string FileId)> g) => g.Key), (Func<IGrouping<long, (long, string)>, IReadOnlyList<int>>)((IGrouping<long, (long ParentId, string FileId)> g) => (from l in g
					select (!fileToId.TryGetValue(l.FileId, out value)) ? (-1) : value into id
					where id > 0
					select id).ToList()));
				Comparer<string> comparer = Comparer<string>.Create(HebrewCollation.Compare);
				result = (from s in shelves.OrderByDescending(((long NodeId, string Title, long Pinned) s) => s.Pinned != 0).ThenBy(((long NodeId, string Title, long Pinned) s) => s.Title, comparer)
					select new MadafNode((int)s.NodeId, s.Title, View: true, booksByShelf.GetValueOrDefault(s.NodeId, Array.Empty<int>()), s.Pinned != 0)).ToList();
			}
		}
		return result;
	}

	public async Task<IReadOnlyList<int>> GetBookIdsAsync(int madafId, CancellationToken ct = default(CancellationToken))
	{
		IReadOnlyList<int> result;
		await using (SqliteConnection db = OpenUserDb())
		{
			List<string> fileIds = (await db.QueryAsync<string>(new CommandDefinition("SELECT FileId FROM ShelfNode WHERE ParentId = @madafId AND Kind = 1 AND FileId IS NOT NULL", new { madafId }, null, null, null, CommandFlags.Buffered, ct))).AsList();
			Dictionary<string, int> map = await MapFileIdsToBookIdsAsync(fileIds, ct);
			result = (from f in fileIds
				select (!map.TryGetValue(f, out var value)) ? (-1) : value into id
				where id > 0
				select id).ToList();
		}
		return result;
	}

	public async Task<int> AddAsync(string name, CancellationToken ct = default(CancellationToken))
	{
		int result;
		await using (SqliteConnection db = OpenUserDb())
		{
			result = (int)(await db.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO ShelfNode (ParentId, Kind, Title) VALUES (NULL, 0, @name); SELECT last_insert_rowid();", new { name }, null, null, null, CommandFlags.Buffered, ct)));
		}
		return result;
	}

	public async Task RenameAsync(int madafId, string newName, CancellationToken ct = default(CancellationToken))
	{
		await using SqliteConnection db = OpenUserDb();
		await db.ExecuteAsync(new CommandDefinition("UPDATE ShelfNode SET Title = @newName WHERE NodeId = @madafId", new { madafId, newName }, null, null, null, CommandFlags.Buffered, ct));
	}

	public async Task DeleteAsync(int madafId, CancellationToken ct = default(CancellationToken))
	{
		await using SqliteConnection db = OpenUserDb();
		await db.ExecuteAsync(new CommandDefinition("WITH RECURSIVE sub(id) AS (\n    SELECT @madafId\n    UNION ALL\n    SELECT n.NodeId FROM ShelfNode n JOIN sub ON n.ParentId = sub.id\n)\nDELETE FROM ShelfNode WHERE NodeId IN (SELECT id FROM sub);", new { madafId }, null, null, null, CommandFlags.Buffered, ct));
	}

	public async Task AddBookAsync(int madafId, int bookId, CancellationToken ct = default(CancellationToken))
	{
		string text = await FileIdForBookAsync(bookId, ct);
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		await using SqliteConnection db = OpenUserDb();
		await db.ExecuteAsync(new CommandDefinition("INSERT INTO ShelfNode (ParentId, Kind, FileId)\nSELECT @madafId, 1, @fileId\nWHERE NOT EXISTS (\n    SELECT 1 FROM ShelfNode WHERE ParentId = @madafId AND Kind = 1 AND FileId = @fileId);", new
		{
			madafId = madafId,
			fileId = text
		}, null, null, null, CommandFlags.Buffered, ct));
	}

	public async Task RemoveBookAsync(int madafId, int bookId, CancellationToken ct = default(CancellationToken))
	{
		string text = await FileIdForBookAsync(bookId, ct);
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		await using SqliteConnection db = OpenUserDb();
		await db.ExecuteAsync(new CommandDefinition("DELETE FROM ShelfNode WHERE ParentId = @madafId AND Kind = 1 AND FileId = @fileId", new
		{
			madafId = madafId,
			fileId = text
		}, null, null, null, CommandFlags.Buffered, ct));
	}

	private async Task<Dictionary<string, int>> MapFileIdsToBookIdsAsync(IReadOnlyList<string> fileIds, CancellationToken ct)
	{
		Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.Ordinal);
		if (fileIds.Count == 0)
		{
			return map;
		}
		Dictionary<string, int> result;
		await using (SqliteConnection cat = _catalog.Open())
		{
			foreach (var item in await cat.QueryAsync<(string, int)>(new CommandDefinition("SELECT FileID, ID FROM Katalog WHERE FileID IN @fileIds", new { fileIds }, null, null, null, CommandFlags.Buffered, ct)))
			{
				map[item.Item1] = item.Item2;
			}
			result = map;
		}
		return result;
	}

	private async Task<string?> FileIdForBookAsync(int bookId, CancellationToken ct)
	{
		string result;
		await using (SqliteConnection cat = _catalog.Open())
		{
			result = await cat.ExecuteScalarAsync<string>(new CommandDefinition("SELECT FileID FROM Katalog WHERE ID = @bookId", new { bookId }, null, null, null, CommandFlags.Buffered, ct));
		}
		return result;
	}
}
