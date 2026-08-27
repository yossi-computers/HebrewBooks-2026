using System;
using System.Collections.Generic;

namespace HebrewBooks.Services.Search;

public static class HebrewMorph
{
	private const int MinStemLen = 3;

	private const int MaxExpansions = 24;

	private static readonly string[] SinglePrefixes = new string[8] { "ו", "ה", "ש", "כ", "ל", "ב", "מ", "ד" };

	private static readonly string[] DoublePrefixes = new string[9] { "וה", "וב", "וכ", "ול", "ומ", "וש", "כש", "שה", "של" };

	private static readonly string[] Suffixes = new string[28]
	{
		"ותיהם", "ותיכם", "ותינו", "יהם", "יכם", "יהן", "ינו", "נו", "תם", "תן",
		"כם", "כן", "ות", "ים", "יו", "יה", "תי", "תה", "ני", "הם",
		"הן", "ה", "ת", "י", "ו", "ם", "ן", "ך"
	};

	private static readonly string[] SurfaceEndings = new string[20]
	{
		"", "ים", "ות", "י", "ה", "ו", "ת", "תי", "תו", "תה",
		"נו", "ם", "ן", "יו", "יה", "יך", "כם", "הם", "תם", "ית"
	};

	public static IReadOnlyList<string> ExpandForQuery(string word)
	{
		return Core(word);
	}

	public static IReadOnlyList<string> ExpandForHighlight(string word)
	{
		return Core(word);
	}

	private static IReadOnlyList<string> Core(string word)
	{
		if (!IsCandidate(word))
		{
			return new string[1] { word };
		}
		List<string> result = new List<string> { word };
		List<string> list = new List<string> { word };
		string[] doublePrefixes = DoublePrefixes;
		foreach (string text in doublePrefixes)
		{
			if (word.Length - text.Length >= 3 && word.StartsWith(text, StringComparison.Ordinal))
			{
				string text2 = word;
				int length = text.Length;
				list.Add(text2.Substring(length, text2.Length - length));
			}
		}
		doublePrefixes = SinglePrefixes;
		foreach (string value in doublePrefixes)
		{
			if (word.Length - 1 >= 3 && word.StartsWith(value, StringComparison.Ordinal))
			{
				string text2 = word;
				list.Add(text2.Substring(1, text2.Length - 1));
			}
		}
		foreach (string item in list)
		{
			if (item != word)
			{
				Add(item);
			}
		}
		foreach (string item2 in list)
		{
			string text3 = item2;
			doublePrefixes = Suffixes;
			foreach (string text4 in doublePrefixes)
			{
				if (item2.Length - text4.Length >= 3 && item2.EndsWith(text4, StringComparison.Ordinal))
				{
					string text2 = item2;
					int length = text4.Length;
					text3 = Refinalize(text2.Substring(0, text2.Length - length));
					break;
				}
			}
			if (text3 != item2)
			{
				Add(text3);
			}
			string text5 = StripFinal(text3);
			if (text5.Length >= 3)
			{
				doublePrefixes = SurfaceEndings;
				foreach (string text6 in doublePrefixes)
				{
					Add(Refinalize(text5 + text6));
				}
			}
		}
		return result;
		void Add(string s)
		{
			if (s.Length >= 2 && result.Count < 24 && !result.Contains(s))
			{
				result.Add(s);
			}
		}
	}

	private static bool IsCandidate(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return false;
		}
		int num = 0;
		foreach (char c in s)
		{
			bool flag;
			switch (c)
			{
			case '"':
			case '\'':
			case '(':
			case ')':
			case '*':
			case '?':
			case '~':
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				return false;
			}
			if (c >= 'א' && c <= 'ת')
			{
				num++;
			}
			else if (char.IsDigit(c))
			{
				return false;
			}
		}
		return num >= 3;
	}

	private static string Refinalize(string s)
	{
		if (s.Length == 0)
		{
			return s;
		}
		char c;
		switch (s[s.Length - 1])
		{
		case 'כ':
			c = 'ך';
			break;
		case 'מ':
			c = 'ם';
			break;
		case 'נ':
			c = 'ן';
			break;
		case 'פ':
			c = 'ף';
			break;
		case 'צ':
			c = 'ץ';
			break;
		default:
			c = s[s.Length - 1];
			break;
		}
		char c2 = c;
		if (c2 != s[s.Length - 1])
		{
			return s.Substring(0, s.Length - 1) + c2;
		}
		return s;
	}

	private static string StripFinal(string s)
	{
		if (s.Length == 0)
		{
			return s;
		}
		char c;
		switch (s[s.Length - 1])
		{
		case 'ך':
			c = 'כ';
			break;
		case 'ם':
			c = 'מ';
			break;
		case 'ן':
			c = 'נ';
			break;
		case 'ף':
			c = 'פ';
			break;
		case 'ץ':
			c = 'צ';
			break;
		default:
			c = s[s.Length - 1];
			break;
		}
		char c2 = c;
		if (c2 != s[s.Length - 1])
		{
			return s.Substring(0, s.Length - 1) + c2;
		}
		return s;
	}
}
