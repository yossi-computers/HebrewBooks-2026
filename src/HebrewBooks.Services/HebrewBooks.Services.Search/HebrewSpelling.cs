using System;
using System.Collections.Generic;
using System.Text;

namespace HebrewBooks.Services.Search;

public static class HebrewSpelling
{
	private const int MinWordLen = 3;

	private const int MinResultLen = 2;

	private const int MaxVariants = 12;

	private const char Vav = 'ו';

	private const char Yod = 'י';

	public static IReadOnlyList<string> Expand(string word)
	{
		if (!IsCandidate(word))
		{
			return new string[1] { word };
		}
		List<string> result = new List<string> { word };
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal) { word };
		List<int> list = new List<int>();
		for (int i = 1; i < word.Length - 1; i++)
		{
			if (word[i] == 'ו' || word[i] == 'י')
			{
				list.Add(i);
			}
		}
		int num = Math.Min(list.Count, 4);
		for (int j = 1; j < 1 << num; j++)
		{
			if (result.Count > 12)
			{
				break;
			}
			StringBuilder stringBuilder = new StringBuilder(word.Length);
			for (int k = 0; k < word.Length; k++)
			{
				int num2 = list.IndexOf(k);
				if (num2 < 0 || num2 >= num || (j & (1 << num2)) == 0)
				{
					stringBuilder.Append(word[k]);
				}
			}
			Add(stringBuilder.ToString());
		}
		for (int l = 1; l < word.Length; l++)
		{
			if (result.Count > 12)
			{
				break;
			}
			if (word[l - 1] != 'ו' && word[l] != 'ו')
			{
				string text = word.Substring(0, l);
				string text2 = word;
				int num3 = l;
				Add(text + "ו" + text2.Substring(num3, text2.Length - num3));
			}
			if (word[l - 1] != 'י' && word[l] != 'י')
			{
				string text3 = word.Substring(0, l);
				string text2 = word;
				int num3 = l;
				Add(text3 + "י" + text2.Substring(num3, text2.Length - num3));
			}
		}
		return result;
		void Add(string w)
		{
			if (w.Length >= 2 && result.Count <= 12 && seen.Add(w))
			{
				result.Add(w);
			}
		}
	}

	private static bool IsCandidate(string s)
	{
		if (string.IsNullOrEmpty(s) || s.Length < 3)
		{
			return false;
		}
		foreach (char c in s)
		{
			if (c < 'א' || c > 'ת')
			{
				return false;
			}
		}
		return true;
	}
}
