using System;
using System.Collections.Generic;
using Dapper;
using HebrewBooks.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace HebrewBooks.Data.Repositories;

public sealed class FavoritesRepository(ISqliteConnectionFactory connections) : IFavoritesRepository
{
	private bool ReadOnly => connections.IsReadOnly;

	public IReadOnlyList<FavoriteEntry> GetAll()
	{
		using SqliteConnection cnn = connections.Open();
		return cnn.Query<FavoriteEntry>("SELECT FileID, FolderName, SortOrder\nFROM Favorites\nORDER BY FolderName, SortOrder, AddedAt").AsList();
	}

	public bool IsFavorited(string fileId)
	{
		if (string.IsNullOrEmpty(fileId))
		{
			return false;
		}
		using SqliteConnection cnn = connections.Open();
		return cnn.ExecuteScalar<long>("SELECT COUNT(*) FROM Favorites WHERE FileID = @fileId", new { fileId }) > 0;
	}

	public void Add(string fileId, string folderName = "")
	{
		if (ReadOnly || string.IsNullOrEmpty(fileId))
		{
			return;
		}
		using SqliteConnection cnn = connections.Open();
		cnn.Execute("INSERT OR IGNORE INTO Favorites(FileID, FolderName, AddedAt, SortOrder)\nVALUES (@fileId, @folderName, @now, 0)", new
		{
			fileId = fileId,
			folderName = folderName,
			now = DateTime.UtcNow.ToString("o")
		});
	}

	public void Remove(string fileId, string folderName = "")
	{
		if (ReadOnly || string.IsNullOrEmpty(fileId))
		{
			return;
		}
		using SqliteConnection cnn = connections.Open();
		cnn.Execute("DELETE FROM Favorites WHERE FileID = @fileId AND FolderName = @folderName", new { fileId, folderName });
	}

	public void RemoveAll(string fileId)
	{
		if (ReadOnly || string.IsNullOrEmpty(fileId))
		{
			return;
		}
		using SqliteConnection cnn = connections.Open();
		cnn.Execute("DELETE FROM Favorites WHERE FileID = @fileId", new { fileId });
	}

	public IReadOnlyList<string> GetFolders()
	{
		using SqliteConnection cnn = connections.Open();
		return cnn.Query<string>("SELECT Name FROM FavoriteFolders ORDER BY SortOrder, Name").AsList();
	}

	public void CreateFolder(string name)
	{
		if (ReadOnly || string.IsNullOrWhiteSpace(name))
		{
			return;
		}
		using SqliteConnection cnn = connections.Open();
		cnn.Execute("INSERT OR IGNORE INTO FavoriteFolders(Name, SortOrder, CreatedAt) VALUES(@name, 0, @now)", new
		{
			name = name.Trim(),
			now = DateTime.UtcNow.ToString("o")
		});
	}

	public void DeleteFolder(string name)
	{
		if (ReadOnly || string.IsNullOrEmpty(name))
		{
			return;
		}
		using SqliteConnection sqliteConnection = connections.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		sqliteConnection.Execute("UPDATE Favorites SET FolderName = '' WHERE FolderName = @name", new { name }, sqliteTransaction);
		sqliteConnection.Execute("DELETE FROM FavoriteFolders WHERE Name = @name", new { name }, sqliteTransaction);
		sqliteTransaction.Commit();
	}

	public void MoveBookToFolder(string fileId, string newFolderName)
	{
		if (ReadOnly || string.IsNullOrEmpty(fileId))
		{
			return;
		}
		using SqliteConnection sqliteConnection = connections.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		sqliteConnection.Execute("DELETE FROM Favorites WHERE FileID = @fileId", new { fileId }, sqliteTransaction);
		sqliteConnection.Execute("INSERT INTO Favorites(FileID, FolderName, AddedAt, SortOrder)\nVALUES (@fileId, @folder, @now, 0)", new
		{
			fileId = fileId,
			folder = (newFolderName ?? ""),
			now = DateTime.UtcNow.ToString("o")
		}, sqliteTransaction);
		sqliteTransaction.Commit();
	}

	public void Clear()
	{
		using SqliteConnection sqliteConnection = connections.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		sqliteConnection.Execute("DELETE FROM Favorites", null, sqliteTransaction);
		sqliteConnection.Execute("DELETE FROM FavoriteFolders", null, sqliteTransaction);
		sqliteTransaction.Commit();
	}
}
