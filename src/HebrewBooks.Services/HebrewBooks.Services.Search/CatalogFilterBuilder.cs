using System.Collections.Generic;
using System.Globalization;
using HebrewBooks.Data;

namespace HebrewBooks.Services.Search;

public static class CatalogFilterBuilder
{
	public sealed record Built(string Sql, IReadOnlyDictionary<string, object?> Parameters);

	private const string SelectColumns = "Katalog.FileID,\nKatalog.BookName,\nKatalog.AuthorName,\nKatalog.PrintPlace,\nKatalog.PrintYear,\nKatalog.CountPage,\nKatalog.ID,\nKatalog.Description,\nKatalog.Folder,\nKatalog.Categories";

	public static Built BuildJoinedWithHits(SortMode sort, bool restrictToVisibleMadaf)
	{
		List<string> list = new List<string> { "SELECT Katalog.FileID,\nKatalog.BookName,\nKatalog.AuthorName,\nKatalog.PrintPlace,\nKatalog.PrintYear,\nKatalog.CountPage,\nKatalog.ID,\nKatalog.Description,\nKatalog.Folder,\nKatalog.Categories, TempList.HitCount, TempList.Location", "FROM Katalog INNER JOIN TempList ON Katalog.ID = TempList.KatalogID" };
		if (restrictToVisibleMadaf)
		{
			list.Add("INNER JOIN BookMadaf ON BookMadaf.BookID = Katalog.ID INNER JOIN Madaf ON Madaf.MadafID = BookMadaf.MadafID");
			list.Add("WHERE Madaf.IsVisible = 1");
		}
		list.Add("ORDER BY " + OrderByClause(sort));
		return new Built(string.Join(' ', list), new Dictionary<string, object>(0));
	}

	public static Built BuildKatalogOnly(SortMode sort, bool restrictToVisibleMadaf)
	{
		List<string> list = new List<string> { "SELECT Katalog.FileID,\nKatalog.BookName,\nKatalog.AuthorName,\nKatalog.PrintPlace,\nKatalog.PrintYear,\nKatalog.CountPage,\nKatalog.ID,\nKatalog.Description,\nKatalog.Folder,\nKatalog.Categories", "FROM Katalog" };
		if (restrictToVisibleMadaf)
		{
			list.Add("INNER JOIN BookMadaf ON BookMadaf.BookID = Katalog.ID INNER JOIN Madaf ON Madaf.MadafID = BookMadaf.MadafID");
			list.Add("WHERE Madaf.IsVisible = 1");
		}
		list.Add("ORDER BY " + OrderByClause(sort));
		return new Built(string.Join(' ', list), new Dictionary<string, object>(0));
	}

	public static Built BuildByFileIds(IReadOnlyCollection<int> ids, SortMode sort)
	{
		if (ids.Count == 0)
		{
			return new Built("SELECT Katalog.FileID,\nKatalog.BookName,\nKatalog.AuthorName,\nKatalog.PrintPlace,\nKatalog.PrintYear,\nKatalog.CountPage,\nKatalog.ID,\nKatalog.Description,\nKatalog.Folder,\nKatalog.Categories FROM Katalog WHERE 1 = 0", new Dictionary<string, object>());
		}
		List<string> list = new List<string>(ids.Count);
		Dictionary<string, object> dictionary = new Dictionary<string, object>(ids.Count);
		int num = 0;
		foreach (int id in ids)
		{
			string text = "@id" + num.ToString(CultureInfo.InvariantCulture);
			list.Add(text);
			dictionary[text] = id;
			num++;
		}
		return new Built($"SELECT {"Katalog.FileID,\nKatalog.BookName,\nKatalog.AuthorName,\nKatalog.PrintPlace,\nKatalog.PrintYear,\nKatalog.CountPage,\nKatalog.ID,\nKatalog.Description,\nKatalog.Folder,\nKatalog.Categories"} FROM Katalog WHERE Katalog.ID IN ({string.Join(", ", list)}) ORDER BY {OrderByClause(sort)}", dictionary);
	}

	private static string OrderByClause(SortMode sort)
	{
		return sort switch
		{
			SortMode.BookName => "Katalog.BookName COLLATE HEB", 
			SortMode.AuthorName => "Katalog.AuthorName COLLATE HEB", 
			SortMode.PrintPlace => "Katalog.PrintPlace COLLATE HEB", 
			SortMode.PrintYear => SqliteConnectionFactory.HebrewYearOrderBy("Katalog.PrintYear", descending: false), 
			SortMode.PrintYearDesc => SqliteConnectionFactory.HebrewYearOrderBy("Katalog.PrintYear", descending: true), 
			SortMode.HitCount => "TempList.HitCount DESC", 
			_ => "Katalog.ID", 
		};
	}
}
