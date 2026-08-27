using System;
using System.Collections.Generic;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Services.Catalog;

public static class CategoryFilter
{
	private static readonly char[] Separator = new char[1] { '|' };

	public static IEnumerable<string> Parse(string? raw)
	{
		if (string.IsNullOrEmpty(raw))
		{
			yield break;
		}
		string[] array = raw.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			if (text.Length > 0)
			{
				yield return text;
			}
		}
	}

	public static bool MatchesAny(Book book, IReadOnlySet<string> selected)
	{
		if (selected.Count == 0)
		{
			return true;
		}
		foreach (string item in Parse(book.Categories))
		{
			if (selected.Contains(item))
			{
				return true;
			}
		}
		return false;
	}
}
