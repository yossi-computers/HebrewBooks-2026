using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;

namespace HebrewBooks.Data.Repositories;

internal static class UserShelfSchema
{
	public static void Ensure(SqliteConnection conn)
	{
		conn.Execute("CREATE TABLE IF NOT EXISTS ShelfNode (\n    NodeId    INTEGER PRIMARY KEY AUTOINCREMENT,\n    ParentId  INTEGER NULL,\n    Kind      INTEGER NOT NULL,   -- 0=Shelf 1=Book 2=Page\n    Title     TEXT    NULL,\n    FileId    TEXT    NULL,\n    Page      INTEGER NULL,\n    Pinned    INTEGER NOT NULL DEFAULT 0,\n    SortOrder INTEGER NOT NULL DEFAULT 0\n);\nCREATE INDEX IF NOT EXISTS idx_shelfnode_parent ON ShelfNode (ParentId);");
		ReconcileFlatShelves(conn);
	}

	private static void ReconcileFlatShelves(SqliteConnection conn)
	{
		if (conn.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Shelf'") <= 0)
		{
			return;
		}
		List<(long, string, long)> source = conn.Query<(long, string, long)>("SELECT ShelfId, Name, Pinned FROM Shelf").AsList();
		HashSet<string> existing = new HashSet<string>(conn.Query<string>("SELECT Title FROM ShelfNode WHERE ParentId IS NULL AND Kind = 0 AND Title IS NOT NULL"), StringComparer.Ordinal);
		List<(long, string, long)> list = source.Where<(long, string, long)>(((long ShelfId, string Name, long Pinned) s) => !existing.Contains(s.Name)).ToList();
		using SqliteTransaction sqliteTransaction = conn.BeginTransaction();
		foreach (var item in list)
		{
			long p = conn.ExecuteScalar<long>("INSERT INTO ShelfNode (ParentId, Kind, Title, Pinned) VALUES (NULL, 0, @name, @pin); SELECT last_insert_rowid();", new
			{
				name = item.Item2,
				pin = item.Item3
			}, sqliteTransaction);
			foreach (string item2 in conn.Query<string>("SELECT FileId FROM ShelfBook WHERE ShelfId = @sid", new
			{
				sid = item.Item1
			}, sqliteTransaction).AsList())
			{
				conn.Execute("INSERT INTO ShelfNode (ParentId, Kind, FileId) VALUES (@p, 1, @f)", new
				{
					p = p,
					f = item2
				}, sqliteTransaction);
			}
		}
		conn.Execute("DROP TABLE IF EXISTS ShelfBook; DROP TABLE IF EXISTS Shelf;", null, sqliteTransaction);
		sqliteTransaction.Commit();
	}
}
