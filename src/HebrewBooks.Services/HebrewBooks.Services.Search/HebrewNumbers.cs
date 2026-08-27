using System;
using System.Collections.Generic;

namespace HebrewBooks.Services.Search;

public static class HebrewNumbers
{
	private sealed record Entry(string[] Words, string[] Gematria);

	private static readonly (string[] Words, string[] Gematria)[] Table = new(string[], string[])[20]
	{
		(new string[2] { "אחד", "אחת" }, new string[1] { "א" }),
		(new string[4] { "שניים", "שתיים", "שני", "שתי" }, new string[1] { "ב" }),
		(new string[2] { "שלושה", "שלוש" }, new string[1] { "ג" }),
		(new string[2] { "ארבעה", "ארבע" }, new string[1] { "ד" }),
		(new string[2] { "חמישה", "חמש" }, new string[1] { "ה" }),
		(new string[3] { "שישה", "ששה", "שש" }, new string[1] { "ו" }),
		(new string[2] { "שבעה", "שבע" }, new string[1] { "ז" }),
		(new string[1] { "שמונה" }, new string[1] { "ח" }),
		(new string[2] { "תשעה", "תשע" }, new string[1] { "ט" }),
		(new string[2] { "עשרה", "עשר" }, new string[1] { "י" }),
		(new string[1] { "עשרים" }, new string[1] { "כ" }),
		(new string[1] { "שלושים" }, new string[1] { "ל" }),
		(new string[1] { "ארבעים" }, new string[1] { "מ" }),
		(new string[1] { "חמישים" }, new string[1] { "נ" }),
		(new string[2] { "שישים", "ששים" }, new string[1] { "ס" }),
		(new string[1] { "שבעים" }, new string[1] { "ע" }),
		(new string[1] { "שמונים" }, new string[1] { "פ" }),
		(new string[1] { "תשעים" }, new string[1] { "צ" }),
		(new string[1] { "מאה" }, new string[1] { "ק" }),
		(new string[1] { "מאתיים" }, new string[1] { "ר" })
	};

	private static readonly Dictionary<string, Entry> ByForm = BuildMap();

	private static Dictionary<string, Entry> BuildMap()
	{
		Dictionary<string, Entry> dictionary = new Dictionary<string, Entry>(StringComparer.Ordinal);
		(string[], string[])[] table = Table;
		for (int i = 0; i < table.Length; i++)
		{
			(string[], string[]) tuple = table[i];
			string[] item = tuple.Item1;
			string[] item2 = tuple.Item2;
			Entry value = new Entry(item, item2);
			string[] array = item;
			foreach (string key in array)
			{
				dictionary[key] = value;
			}
			array = item2;
			foreach (string key2 in array)
			{
				dictionary[key2] = value;
			}
		}
		return dictionary;
	}

	public static IReadOnlyList<string>? ExpandGender(string word)
	{
		if (string.IsNullOrEmpty(word))
		{
			return null;
		}
		if (!ByForm.TryGetValue(word, out Entry value))
		{
			return null;
		}
		if (Array.IndexOf(value.Words, word) < 0)
		{
			return null;
		}
		if (value.Words.Length < 2)
		{
			return null;
		}
		return Ordered(word, value.Words);
	}

	public static IReadOnlyList<string>? ExpandGematria(string word)
	{
		if (string.IsNullOrEmpty(word))
		{
			return null;
		}
		if (!ByForm.TryGetValue(word, out Entry value))
		{
			return null;
		}
		string[] array = ((Array.IndexOf(value.Words, word) >= 0) ? value.Gematria : value.Words);
		if (array.Length == 0)
		{
			return null;
		}
		return Ordered(word, array);
	}

	private static IReadOnlyList<string> Ordered(string first, string[] rest)
	{
		List<string> list = new List<string>(rest.Length + 1) { first };
		foreach (string text in rest)
		{
			if (text != first)
			{
				list.Add(text);
			}
		}
		return list;
	}
}
