using System;
using System.Collections.Generic;
using System.Linq;
using HebrewBooks.Core.Catalog;
using HebrewBooks.Core.Models;
using HebrewBooks.Data;
using HebrewBooks.Services.Search;

namespace HebrewBooks.Services.Catalog;

public static class CatalogSorting
{
	private static readonly IComparer<string?> Hebrew = Comparer<string>.Create(HebrewCollation.Compare);

	private static int YearKey(string? printYear, bool descending)
	{
		int? num = HebrewYear.Parse(printYear);
		if (!num.HasValue)
		{
			if (!descending)
			{
				return int.MaxValue;
			}
			return int.MinValue;
		}
		return num.GetValueOrDefault();
	}

	public static IEnumerable<Book> Apply(IEnumerable<Book> books, IReadOnlyList<SortLayer> layers)
	{
		List<SortLayer> list = layers.Where((SortLayer l) => l.Key != SortMode.HitCount).ToList();
		if (list.Count == 0)
		{
			return books;
		}
		IOrderedEnumerable<Book> orderedEnumerable = null;
		foreach (SortLayer item in list)
		{
			orderedEnumerable = AddLayer(orderedEnumerable, books, item);
		}
		return orderedEnumerable.ThenBy<Book, string>((Book b) => b.BookName, Hebrew);
	}

	private static IOrderedEnumerable<Book> AddLayer(IOrderedEnumerable<Book>? ordered, IEnumerable<Book> books, SortLayer layer)
	{
		if (layer.Key == SortMode.PrintYear)
		{
			Func<Book, int> keySelector = (Book b) => YearKey(b.PrintYear, layer.Descending);
			if (!layer.Descending)
			{
				if (ordered != null)
				{
					return ordered.ThenBy(keySelector);
				}
				return books.OrderBy(keySelector);
			}
			if (ordered != null)
			{
				return ordered.ThenByDescending(keySelector);
			}
			return books.OrderByDescending(keySelector);
		}
		Func<Book, string> keySelector2 = TextKey(layer.Key);
		if (!layer.Descending)
		{
			if (ordered != null)
			{
				return ordered.ThenBy<Book, string>(keySelector2, Hebrew);
			}
			return books.OrderBy<Book, string>(keySelector2, Hebrew);
		}
		if (ordered != null)
		{
			return ordered.ThenByDescending<Book, string>(keySelector2, Hebrew);
		}
		return books.OrderByDescending<Book, string>(keySelector2, Hebrew);
	}

	private static Func<Book, string?> TextKey(SortMode key)
	{
		return key switch
		{
			SortMode.AuthorName => (Book b) => b.AuthorName, 
			SortMode.PrintPlace => (Book b) => b.PrintPlace, 
			_ => (Book b) => b.BookName, 
		};
	}

	public static IComparer<SearchResultRow> RowComparer(IReadOnlyList<SortLayer> layers)
	{
		return Comparer<SearchResultRow>.Create(delegate(SearchResultRow a, SearchResultRow b)
		{
			foreach (SortLayer layer in layers)
			{
				int num = CompareBy(layer, a, b);
				if (num != 0)
				{
					return num;
				}
			}
			return Hebrew.Compare(a.Book.BookName, b.Book.BookName);
		});
	}

	private static int CompareBy(SortLayer layer, SearchResultRow a, SearchResultRow b)
	{
		int num = layer.Key switch
		{
			SortMode.PrintYear => YearKey(a.Book.PrintYear, layer.Descending).CompareTo(YearKey(b.Book.PrintYear, layer.Descending)), 
			SortMode.HitCount => a.HitCount.CompareTo(b.HitCount), 
			SortMode.AuthorName => Hebrew.Compare(a.Book.AuthorName, b.Book.AuthorName), 
			SortMode.PrintPlace => Hebrew.Compare(a.Book.PrintPlace, b.Book.PrintPlace), 
			_ => Hebrew.Compare(a.Book.BookName, b.Book.BookName), 
		};
		if (!layer.Descending)
		{
			return num;
		}
		return -num;
	}

	public static IEnumerable<Book> Apply(IEnumerable<Book> books, SortMode mode)
	{
		if (mode != SortMode.Id)
		{
			return Apply(books, new SortLayer[] { SortLayer.From(mode) });
		}
		return books;
	}

	public static IComparer<SearchResultRow> RowComparer(SortMode mode)
	{
		return RowComparer(new SortLayer[] { SortLayer.From(mode) });
	}
}
