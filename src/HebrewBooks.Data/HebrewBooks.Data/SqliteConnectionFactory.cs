using System;
using HebrewBooks.Core.Catalog;
using Microsoft.Data.Sqlite;

namespace HebrewBooks.Data;

public sealed class SqliteConnectionFactory(string dbPath, bool sharedReadOnlyMaster = false) : ISqliteConnectionFactory
{
	private readonly bool _sharedReadOnlyMaster = sharedReadOnlyMaster;

	private volatile bool _readOnly;

	public const string HebrewYearFunction = "HEBYEAR";

	public string DbPath { get; } = dbPath;

	public bool IsReadOnly => _readOnly;

	public SqliteConnection Open()
	{
		if (_readOnly)
		{
			return OpenWith(SqliteOpenMode.ReadOnly);
		}
		try
		{
			return OpenWith(SqliteOpenMode.ReadWriteCreate);
		}
		catch (SqliteException ex) when (IsReadOnlyLocation(ex))
		{
			_readOnly = true;
			return OpenWith(SqliteOpenMode.ReadOnly);
		}
	}

	private SqliteConnection OpenWith(SqliteOpenMode mode)
	{
		SqliteConnection sqliteConnection = new SqliteConnection(((mode == SqliteOpenMode.ReadOnly) ? new SqliteConnectionStringBuilder
		{
			DataSource = ToImmutableReadOnlyUri(DbPath),
			Cache = SqliteCacheMode.Private,
			ForeignKeys = true
		} : new SqliteConnectionStringBuilder
		{
			DataSource = DbPath,
			Mode = mode,
			Cache = SqliteCacheMode.Shared,
			ForeignKeys = true
		}).ToString());
		sqliteConnection.Open();
		sqliteConnection.CreateCollation("HEB", HebrewCollation.Compare);
		sqliteConnection.CreateFunction("HEBYEAR", (string? printYear) => HebrewYear.Parse(printYear), isDeterministic: true);
		return sqliteConnection;
	}

	public static string HebrewYearOrderBy(string column, bool descending)
	{
		return $"{"HEBYEAR"}({column}) IS NULL, {"HEBYEAR"}({column})" + (descending ? " DESC" : "");
	}

	private static string ToImmutableReadOnlyUri(string path)
	{
		string text = path.Replace('\\', '/').Replace("%", "%25").Replace("#", "%23")
			.Replace("?", "%3F")
			.Replace(" ", "%20");
		return (text.StartsWith('/') ? ("file://" + text) : ("file:///" + text)) + "?mode=ro&immutable=1";
	}

	private bool IsReadOnlyLocation(SqliteException ex)
	{
		int sqliteErrorCode = ex.SqliteErrorCode;
		bool flag = ((sqliteErrorCode == 8 || sqliteErrorCode == 14) ? true : false);
		bool flag2 = flag;
		if (!flag2)
		{
			bool flag3 = sharedReadOnlyMaster;
			if (flag3)
			{
				int sqliteErrorCode2 = ex.SqliteErrorCode;
				bool flag4 = ((sqliteErrorCode2 == 5 || sqliteErrorCode2 == 10) ? true : false);
				flag3 = flag4;
			}
			flag2 = flag3;
		}
		return flag2;
	}

	public void EnsureSchema()
	{
		using SqliteConnection sqliteConnection = Open();
		if (_readOnly)
		{
			return;
		}
		try
		{
			using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
			{
				sqliteCommand.CommandText = "PRAGMA foreign_keys = ON;\nPRAGMA journal_mode = WAL;\n\n-- Catalog of books. Column names are clean PascalCase English; legacy mdb names like\n-- Book/Name/Place/Yoar/File/description are mapped to BookName/AuthorName/PrintPlace/\n-- PrintYear/FileID/Description during the one-time migration in HebrewBooks.Migration.\n-- SourceType discriminates: 'PDF' = legacy hebrewbooks PDF, 'Text' = Otzraya HTML/.txt.\n-- RelativePath holds the path-from-OtzrayaRoot for text books (e.g.\n-- \"תלמוד בבלי\\סדר מועד\\שבת.txt\"); NULL for PDFs (which use FileID + Folder).\nCREATE TABLE IF NOT EXISTS Katalog (\n    ID           INTEGER PRIMARY KEY,\n    FileID       TEXT,\n    BookName     TEXT,\n    AuthorName   TEXT,\n    PrintPlace   TEXT,\n    PrintYear    TEXT,\n    CountPage    INTEGER,\n    Description  TEXT,\n    Folder       TEXT,\n    Categories   TEXT,\n    Searchable   INTEGER NOT NULL DEFAULT 1,\n    SourceType   TEXT    NOT NULL DEFAULT 'PDF',\n    RelativePath TEXT\n);\nCREATE INDEX IF NOT EXISTS IX_Katalog_FileID     ON Katalog(FileID);\nCREATE INDEX IF NOT EXISTS IX_Katalog_BookName   ON Katalog(BookName COLLATE HEB);\nCREATE INDEX IF NOT EXISTS IX_Katalog_AuthorName ON Katalog(AuthorName COLLATE HEB);\nCREATE INDEX IF NOT EXISTS IX_Katalog_Folder     ON Katalog(Folder);\nCREATE INDEX IF NOT EXISTS IX_Katalog_SourceType ON Katalog(SourceType);\n\n-- \"Madaf\" = shelf / category. View renamed to IsVisible because View is reserved-ish.\nCREATE TABLE IF NOT EXISTS Madaf (\n    MadafID   INTEGER PRIMARY KEY,\n    MadafName TEXT,\n    IsVisible INTEGER NOT NULL DEFAULT 0\n);\n\nCREATE TABLE IF NOT EXISTS BookMadaf (\n    ID      INTEGER PRIMARY KEY AUTOINCREMENT,\n    MadafID INTEGER,\n    BookID  INTEGER\n);\nCREATE INDEX IF NOT EXISTS IX_BookMadaf_BookID  ON BookMadaf(BookID);\nCREATE INDEX IF NOT EXISTS IX_BookMadaf_MadafID ON BookMadaf(MadafID);\n\n-- Working tables for search-result materialization. Reset on every search.\n-- (FIle column from VB6 is renamed File for clarity; KatalogID is unambiguous.)\nCREATE TABLE IF NOT EXISTS TempList (\n    KatalogID INTEGER,\n    HitCount  INTEGER,\n    Location  TEXT,\n    ResID     INTEGER\n);\nCREATE INDEX IF NOT EXISTS IX_TempList_KatalogID ON TempList(KatalogID);\n\nCREATE TABLE IF NOT EXISTS TempToList (\n    ID        INTEGER PRIMARY KEY AUTOINCREMENT,\n    Name      TEXT,\n    HitCount  INTEGER,\n    Location  TEXT,\n    ResID     INTEGER,\n    KatalogID INTEGER\n);\n\n-- Full-text search over Hebrew metadata. Populated via triggers below.\nCREATE VIRTUAL TABLE IF NOT EXISTS Katalog_fts USING fts5(\n    BookName, AuthorName, Description,\n    content='Katalog', content_rowid='ID',\n    tokenize='unicode61 remove_diacritics 0'\n);\n\nCREATE TRIGGER IF NOT EXISTS Katalog_ai AFTER INSERT ON Katalog BEGIN\n    INSERT INTO Katalog_fts(rowid, BookName, AuthorName, Description)\n    VALUES (new.ID, new.BookName, new.AuthorName, new.Description);\nEND;\nCREATE TRIGGER IF NOT EXISTS Katalog_ad AFTER DELETE ON Katalog BEGIN\n    INSERT INTO Katalog_fts(Katalog_fts, rowid, BookName, AuthorName, Description)\n    VALUES ('delete', old.ID, old.BookName, old.AuthorName, old.Description);\nEND;\nCREATE TRIGGER IF NOT EXISTS Katalog_au AFTER UPDATE ON Katalog BEGIN\n    INSERT INTO Katalog_fts(Katalog_fts, rowid, BookName, AuthorName, Description)\n    VALUES ('delete', old.ID, old.BookName, old.AuthorName, old.Description);\n    INSERT INTO Katalog_fts(rowid, BookName, AuthorName, Description)\n    VALUES (new.ID, new.BookName, new.AuthorName, new.Description);\nEND;\n\n-- BookLastPage: remembers which page the user was on when they switched away from each\n-- book. Restored on the next open IF the open path isn't a content-search hit (search\n-- opens jump to the first hit page instead). Keyed by FileID; one row per book.\nCREATE TABLE IF NOT EXISTS BookLastPage (\n    FileID   TEXT    NOT NULL PRIMARY KEY,\n    LastPage INTEGER NOT NULL,\n    ClosedAt TEXT    NOT NULL\n);\n\n-- Favorites: books the user starred. Optionally grouped into folders (FolderName='' =\n-- root). A book can sit in multiple folders (one row per (FileID, FolderName)). The\n-- right-side favorites sidebar (Phase 2B/C) reads from here.\nCREATE TABLE IF NOT EXISTS Favorites (\n    ID         INTEGER PRIMARY KEY AUTOINCREMENT,\n    FileID     TEXT    NOT NULL,\n    FolderName TEXT    NOT NULL DEFAULT '',\n    AddedAt    TEXT    NOT NULL,\n    SortOrder  INTEGER NOT NULL DEFAULT 0,\n    UNIQUE (FileID, FolderName)\n);\nCREATE INDEX IF NOT EXISTS IX_Favorites_FileID ON Favorites(FileID);\nCREATE INDEX IF NOT EXISTS IX_Favorites_Folder ON Favorites(FolderName, SortOrder);\n\n-- Folder metadata for favorites (phase 2C). Lets the user create EMPTY folders ahead\n-- of time and drag books into them. Without this table the folder list would have to\n-- be derived from Favorites.FolderName, which drops a folder the moment its last book\n-- moves out.\nCREATE TABLE IF NOT EXISTS FavoriteFolders (\n    Name      TEXT NOT NULL PRIMARY KEY,\n    SortOrder INTEGER NOT NULL DEFAULT 0,\n    CreatedAt TEXT NOT NULL\n);";
				sqliteCommand.ExecuteNonQuery();
			}
			EnsureColumn(sqliteConnection, "Katalog", "SourceType", "TEXT NOT NULL DEFAULT 'PDF'");
			EnsureColumn(sqliteConnection, "Katalog", "RelativePath", "TEXT");
			EnsureColumn(sqliteConnection, "Katalog", "TocJson", "TEXT");
		}
		catch (SqliteException ex) when (IsReadOnlyLocation(ex))
		{
			_readOnly = true;
		}
	}

	private static void EnsureColumn(SqliteConnection conn, string table, string column, string typeAndConstraint)
	{
		using SqliteCommand sqliteCommand = conn.CreateCommand();
		sqliteCommand.CommandText = "PRAGMA table_info(" + table + ")";
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			if (string.Equals(sqliteDataReader.GetString(1), column, StringComparison.Ordinal))
			{
				return;
			}
		}
		sqliteDataReader.Close();
		using SqliteCommand sqliteCommand2 = conn.CreateCommand();
		sqliteCommand2.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {typeAndConstraint}";
		sqliteCommand2.ExecuteNonQuery();
	}
}
