using Microsoft.Data.Sqlite;

namespace HebrewBooks.Data;

public interface ISqliteConnectionFactory
{
	string DbPath { get; }

	bool IsReadOnly { get; }

	SqliteConnection Open();

	void EnsureSchema();
}
