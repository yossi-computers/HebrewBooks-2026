using System;
using System.Collections.Generic;
using System.Linq;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Core.Catalog;

public static class BookGrouping
{
	public readonly record struct GroupKey(string NormalizedTitle, string Author);

	private static readonly char[] Separators = new char[5] { '-', '–', '—', '־', ',' };

	private static readonly HashSet<string> GenericPrefixes = new HashSet<string>(StringComparer.Ordinal) { "ספר" };

	public static string? NormalizeTitle(string? bookName, bool hasAuthor)
	{
		string text = bookName?.Trim();
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		int num = text.IndexOfAny(Separators);
		if (num > 0)
		{
			string text2 = text.Substring(0, num).Trim();
			if (text2.Length > 0 && (!IsSingleWord(text2) || !GenericPrefixes.Contains(text2)))
			{
				return text2;
			}
		}
		string[] array = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		if (hasAuthor)
		{
			if (array.Length < 2)
			{
				return text;
			}
			return array[0] + " " + array[1];
		}
		if (array.Length < 3)
		{
			return text;
		}
		return $"{array[0]} {array[1]} {array[2]}";
	}

	private static bool IsSingleWord(string s)
	{
		for (int i = 0; i < s.Length; i++)
		{
			if (char.IsWhiteSpace(s[i]))
			{
				return false;
			}
		}
		return true;
	}

	public static IReadOnlyList<CatalogRow> BuildTopLevel(IReadOnlyList<Book> ranked, Comparison<string?> childOrder)
	{
		int count = ranked.Count;
		GroupKey?[] array = new GroupKey?[count];
		Dictionary<GroupKey, int> dictionary = new Dictionary<GroupKey, int>();
		for (int i = 0; i < count; i++)
		{
			Book book = ranked[i];
			string text = book.AuthorName?.Trim() ?? "";
			string text2 = NormalizeTitle(book.BookName, text.Length > 0);
			if (text2 == null)
			{
				array[i] = null;
				continue;
			}
			GroupKey groupKey = new GroupKey(text2, text);
			array[i] = groupKey;
			dictionary[groupKey] = ((!dictionary.TryGetValue(groupKey, out var value)) ? 1 : (value + 1));
		}
		Dictionary<GroupKey, List<Book>> dictionary2 = new Dictionary<GroupKey, List<Book>>();
		for (int j = 0; j < count; j++)
		{
			GroupKey? groupKey2 = array[j];
			if (!groupKey2.HasValue)
			{
				continue;
			}
			GroupKey valueOrDefault = groupKey2.GetValueOrDefault();
			if (dictionary[valueOrDefault] >= 2)
			{
				if (!dictionary2.TryGetValue(valueOrDefault, out var value2))
				{
					value2 = (dictionary2[valueOrDefault] = new List<Book>());
				}
				value2.Add(ranked[j]);
			}
		}
		List<CatalogRow> list2 = new List<CatalogRow>(count);
		HashSet<GroupKey> hashSet = new HashSet<GroupKey>();
		for (int k = 0; k < count; k++)
		{
			GroupKey? groupKey2 = array[k];
			if (groupKey2.HasValue)
			{
				GroupKey valueOrDefault2 = groupKey2.GetValueOrDefault();
				if (dictionary[valueOrDefault2] >= 2)
				{
					if (hashSet.Add(valueOrDefault2))
					{
						List<Book> list3 = dictionary2[valueOrDefault2];
						list3.Sort((Book x, Book y) => childOrder(x.BookName, y.BookName));
						List<BookRow> children = list3.Select((Book mb) => new BookRow(mb)
						{
							IsChildInGroup = true
						}).ToList();
						list2.Add(new GroupHeaderRow(valueOrDefault2.NormalizedTitle, valueOrDefault2.Author, children));
					}
					continue;
				}
			}
			list2.Add(new BookRow(ranked[k]));
		}
		return list2;
	}

	public static IEnumerable<CatalogRow> Flatten(IEnumerable<CatalogRow> topLevel)
	{
		foreach (CatalogRow row in topLevel)
		{
			yield return row;
			if (!(row is GroupHeaderRow { IsExpanded: not false } groupHeaderRow))
			{
				continue;
			}
			foreach (BookRow child in groupHeaderRow.Children)
			{
				yield return child;
			}
		}
	}
}
