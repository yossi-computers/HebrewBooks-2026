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

public sealed class ShelfTreeRepository : IShelfTreeRepository
{
	private sealed record NodeRow(long NodeId, long? ParentId, long Kind, string? Title, string? FileId, long? Page, long Pinned, long SortOrder);

	private readonly IPathResolver _paths;

	private readonly object _schemaLock = new object();

	private bool _schemaReady;

	private string PublisherDbPath => Path.Combine(_paths.UserDataRoot, "shelves-publisher.db");

	public ShelfTreeRepository(IPathResolver paths)
	{
		_paths = paths;
	}

	private SqliteConnection Open()
	{
		string text = Path.Combine(_paths.UserDataRoot, "user-shelves.db");
		Directory.CreateDirectory(Path.GetDirectoryName(text));
		SqliteConnection sqliteConnection = new SqliteConnection("Data Source=" + text);
		sqliteConnection.Open();
		EnsureSchema(sqliteConnection);
		return sqliteConnection;
	}

	private void EnsureSchema(SqliteConnection conn)
	{
		if (_schemaReady)
		{
			return;
		}
		lock (_schemaLock)
		{
			if (!_schemaReady)
			{
				UserShelfSchema.Ensure(conn);
				_schemaReady = true;
			}
		}
	}

	public async Task<IReadOnlyList<ShelfTreeNode>> GetTreeAsync(CancellationToken ct = default(CancellationToken))
	{
		IReadOnlyList<ShelfTreeNode> publisher = ReadPublisherForest();
		IReadOnlyList<ShelfTreeNode> readOnlyList = await ReadUserForestAsync(ct);
		if (publisher.Count == 0)
		{
			return readOnlyList;
		}
		if (readOnlyList.Count == 0)
		{
			return publisher;
		}
		return publisher.Concat(readOnlyList).ToList();
	}

	private async Task<IReadOnlyList<ShelfTreeNode>> ReadUserForestAsync(CancellationToken ct)
	{
		IReadOnlyList<ShelfTreeNode> result;
		await using (SqliteConnection db = Open())
		{
			List<NodeRow> rows = (await db.QueryAsync<NodeRow>(new CommandDefinition("SELECT NodeId, ParentId, Kind, Title, FileId, Page, Pinned, SortOrder FROM ShelfNode", null, null, null, null, CommandFlags.Buffered, ct))).AsList();
			result = BuildForest(rows, isPublisher: false);
		}
		return result;
	}

	private IReadOnlyList<ShelfTreeNode> ReadPublisherForest()
	{
		if (!File.Exists(PublisherDbPath))
		{
			return Array.Empty<ShelfTreeNode>();
		}
		try
		{
			using SqliteConnection sqliteConnection = new SqliteConnection("Data Source=" + PublisherDbPath + ";Mode=ReadOnly");
			sqliteConnection.Open();
			if (sqliteConnection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ShelfNode'") == 0L)
			{
				return Array.Empty<ShelfTreeNode>();
			}
			return BuildForest(sqliteConnection.Query<NodeRow>("SELECT NodeId, ParentId, Kind, Title, FileId, Page, Pinned, SortOrder FROM ShelfNode").AsList(), isPublisher: true);
		}
		catch
		{
			return Array.Empty<ShelfTreeNode>();
		}
	}

	private static IReadOnlyList<ShelfTreeNode> BuildForest(List<NodeRow> rows, bool isPublisher)
	{
		ILookup<long?, NodeRow> byParent = rows.ToLookup((NodeRow r) => r.ParentId);
		Comparer<string?> heb = Comparer<string>.Create(HebrewCollation.Compare);
		return Build(null);
		List<ShelfTreeNode> Build(long? parentId)
		{
			return (from r in byParent[parentId]
				orderby r.Pinned != 0 descending, r.SortOrder
				select r).ThenBy<NodeRow, string>((NodeRow r) => r.Title, heb).Select(delegate(NodeRow r)
			{
				int nodeId = (int)r.NodeId;
				long? parentId2 = r.ParentId;
				int? parentId3;
				if (parentId2.HasValue)
				{
					long valueOrDefault = parentId2.GetValueOrDefault();
					parentId3 = (int)valueOrDefault;
				}
				else
				{
					parentId3 = null;
				}
				int kind = (int)r.Kind;
				string? title = r.Title;
				string? fileId = r.FileId;
				parentId2 = r.Page;
				int? page;
				if (parentId2.HasValue)
				{
					long valueOrDefault2 = parentId2.GetValueOrDefault();
					page = (int)valueOrDefault2;
				}
				else
				{
					page = null;
				}
				return new ShelfTreeNode(nodeId, parentId3, (ShelfNodeKind)kind, title, fileId, page, r.Pinned != 0, (int)r.SortOrder, Build(r.NodeId), isPublisher);
			}).ToList();
		}
	}

	public async Task<int> AddShelfAsync(int? parentId, string name, CancellationToken ct = default(CancellationToken))
	{
		return await InsertAsync(parentId, ShelfNodeKind.Shelf, name, null, null, ct);
	}

	public async Task<int> AddBookAsync(int? parentId, string fileId, CancellationToken ct = default(CancellationToken))
	{
		return await InsertAsync(parentId, ShelfNodeKind.Book, null, fileId, null, ct);
	}

	public async Task<int> AddPageAsync(int parentId, string fileId, int page, string? label, CancellationToken ct = default(CancellationToken))
	{
		return await InsertAsync(parentId, ShelfNodeKind.Page, label, fileId, page, ct);
	}

	private async Task<int> InsertAsync(int? parentId, ShelfNodeKind kind, string? title, string? fileId, int? page, CancellationToken ct)
	{
		int result;
		await using (SqliteConnection db = Open())
		{
			result = (int)(await db.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO ShelfNode (ParentId, Kind, Title, FileId, Page) VALUES (@parentId, @kind, @title, @fileId, @page); SELECT last_insert_rowid();", new
			{
				parentId = parentId,
				kind = (int)kind,
				title = title,
				fileId = fileId,
				page = page
			}, null, null, null, CommandFlags.Buffered, ct)));
		}
		return result;
	}

	public async Task RenameAsync(int nodeId, string newTitle, CancellationToken ct = default(CancellationToken))
	{
		await using SqliteConnection db = Open();
		await db.ExecuteAsync(new CommandDefinition("UPDATE ShelfNode SET Title = @newTitle WHERE NodeId = @nodeId", new { nodeId, newTitle }, null, null, null, CommandFlags.Buffered, ct));
	}

	public async Task DeleteAsync(int nodeId, CancellationToken ct = default(CancellationToken))
	{
		await using SqliteConnection db = Open();
		await db.ExecuteAsync(new CommandDefinition("WITH RECURSIVE sub(id) AS (\n    SELECT @nodeId\n    UNION ALL\n    SELECT n.NodeId FROM ShelfNode n JOIN sub ON n.ParentId = sub.id\n)\nDELETE FROM ShelfNode WHERE NodeId IN (SELECT id FROM sub);", new { nodeId }, null, null, null, CommandFlags.Buffered, ct));
	}

	public async Task SetPinnedAsync(int nodeId, bool pinned, CancellationToken ct = default(CancellationToken))
	{
		await using SqliteConnection db = Open();
		await db.ExecuteAsync(new CommandDefinition("UPDATE ShelfNode SET Pinned = @p WHERE NodeId = @nodeId", new
		{
			nodeId = nodeId,
			p = (pinned ? 1 : 0)
		}, null, null, null, CommandFlags.Buffered, ct));
	}

	public async Task MoveAsync(int nodeId, int? newParentId, CancellationToken ct = default(CancellationToken))
	{
		await using SqliteConnection db = Open();
		await db.ExecuteAsync(new CommandDefinition("UPDATE ShelfNode SET ParentId = @newParentId WHERE NodeId = @nodeId", new { nodeId, newParentId }, null, null, null, CommandFlags.Buffered, ct));
	}
}
