using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HebrewBooks.Core.Abstractions;
using HebrewBooks.Core.Models;
using Microsoft.Data.Sqlite;

namespace HebrewBooks.Data.Repositories;

public sealed class MadafRepository(ISqliteConnectionFactory connections) : IMadafRepository
{
	public async Task<IReadOnlyList<MadafNode>> GetTreeAsync(CancellationToken ct = default(CancellationToken))
	{
		IReadOnlyList<MadafNode> result;
		await using (SqliteConnection conn = connections.Open())
		{
			CancellationToken cancellationToken = ct;
			List<(int MadafID, string Name, long IsVisible)> madafs = (await conn.QueryAsync<(int, string, long)>(new CommandDefinition("SELECT MadafID, MadafName AS Name, IsVisible FROM Madaf ORDER BY MadafName COLLATE HEB", null, null, null, null, CommandFlags.Buffered, cancellationToken))).AsList();
			cancellationToken = ct;
			List<(int MadafID, int BookID)> source = (await conn.QueryAsync<(int MadafID, int BookID)>(new CommandDefinition("SELECT MadafID, BookID FROM BookMadaf", null, null, null, null, CommandFlags.Buffered, cancellationToken))).AsList();
			Dictionary<int, IReadOnlyList<int>> booksByMadaf = (from l in source
				group l by l.MadafID).ToDictionary((Func<IGrouping<int, (int, int)>, int>)((IGrouping<int, (int MadafID, int BookID)> g) => g.Key), (Func<IGrouping<int, (int, int)>, IReadOnlyList<int>>)((IGrouping<int, (int MadafID, int BookID)> g) => g.Select(((int MadafID, int BookID) l) => l.BookID).ToList()));
			result = madafs.Select(((int MadafID, string Name, long IsVisible) m) => new MadafNode(m.MadafID, m.Name, m.IsVisible != 0, booksByMadaf.GetValueOrDefault(m.MadafID, Array.Empty<int>()))).ToList();
		}
		return result;
	}

	public async Task<IReadOnlyList<int>> GetBookIdsAsync(int madafId, CancellationToken ct = default(CancellationToken))
	{
		IReadOnlyList<int> result;
		await using (SqliteConnection conn = connections.Open())
		{
			result = (await conn.QueryAsync<int>(new CommandDefinition("SELECT BookID FROM BookMadaf WHERE MadafID = @madafId", new { madafId }, null, null, null, CommandFlags.Buffered, ct))).AsList();
		}
		return result;
	}

	public async Task<int> AddAsync(string name, CancellationToken ct = default(CancellationToken))
	{
		int result;
		await using (SqliteConnection conn = connections.Open())
		{
			result = (int)(await conn.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO Madaf (MadafName, IsVisible) VALUES (@name, 1); SELECT last_insert_rowid();", new { name }, null, null, null, CommandFlags.Buffered, ct)));
		}
		return result;
	}

	public async Task RenameAsync(int madafId, string newName, CancellationToken ct = default(CancellationToken))
	{
		await using SqliteConnection conn = connections.Open();
		await conn.ExecuteAsync(new CommandDefinition("UPDATE Madaf SET MadafName = @newName WHERE MadafID = @madafId", new { madafId, newName }, null, null, null, CommandFlags.Buffered, ct));
	}

	public async Task DeleteAsync(int madafId, CancellationToken ct = default(CancellationToken))
	{
		await using SqliteConnection conn = connections.Open();
		using SqliteTransaction tx = conn.BeginTransaction();
		var parameters = new { madafId };
		CancellationToken cancellationToken = ct;
		await conn.ExecuteAsync(new CommandDefinition("DELETE FROM BookMadaf WHERE MadafID = @madafId", parameters, tx, null, null, CommandFlags.Buffered, cancellationToken));
		var parameters2 = new { madafId };
		cancellationToken = ct;
		await conn.ExecuteAsync(new CommandDefinition("DELETE FROM Madaf WHERE MadafID = @madafId", parameters2, tx, null, null, CommandFlags.Buffered, cancellationToken));
		tx.Commit();
	}

	public async Task AddBookAsync(int madafId, int bookId, CancellationToken ct = default(CancellationToken))
	{
		await using SqliteConnection conn = connections.Open();
		await conn.ExecuteAsync(new CommandDefinition("INSERT INTO BookMadaf (MadafID, BookID)\nSELECT @madafId, @bookId\nWHERE NOT EXISTS (\n    SELECT 1 FROM BookMadaf WHERE MadafID = @madafId AND BookID = @bookId\n)", new { madafId, bookId }, null, null, null, CommandFlags.Buffered, ct));
	}

	public async Task RemoveBookAsync(int madafId, int bookId, CancellationToken ct = default(CancellationToken))
	{
		await using SqliteConnection conn = connections.Open();
		await conn.ExecuteAsync(new CommandDefinition("DELETE FROM BookMadaf WHERE MadafID = @madafId AND BookID = @bookId", new { madafId, bookId }, null, null, null, CommandFlags.Buffered, ct));
	}
}
