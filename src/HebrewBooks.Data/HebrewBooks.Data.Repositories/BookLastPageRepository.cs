using System;
using Dapper;
using HebrewBooks.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace HebrewBooks.Data.Repositories;

public sealed class BookLastPageRepository(ISqliteConnectionFactory connections, IProtectMode? protectMode = null) : IBookLastPageRepository
{
	public int? GetLastPage(string fileId)
	{
		if (protectMode != null && protectMode.IsActive)
		{
			return null;
		}
		if (string.IsNullOrEmpty(fileId))
		{
			return null;
		}
		using SqliteConnection cnn = connections.Open();
		return cnn.QueryFirstOrDefault<int?>("SELECT LastPage FROM BookLastPage WHERE FileID = @fileId", new { fileId });
	}

	public void Save(string fileId, int lastPage)
	{
		if ((protectMode != null && protectMode.IsActive) || connections.IsReadOnly || string.IsNullOrEmpty(fileId) || lastPage < 1)
		{
			return;
		}
		using SqliteConnection cnn = connections.Open();
		cnn.Execute("INSERT INTO BookLastPage(FileID, LastPage, ClosedAt)\nVALUES (@fileId, @lastPage, @now)\nON CONFLICT(FileID) DO UPDATE SET\n    LastPage = excluded.LastPage,\n    ClosedAt = excluded.ClosedAt", new
		{
			fileId = fileId,
			lastPage = lastPage,
			now = DateTime.UtcNow.ToString("o")
		});
	}

	public void Clear()
	{
		using SqliteConnection cnn = connections.Open();
		cnn.Execute("DELETE FROM BookLastPage");
	}
}
