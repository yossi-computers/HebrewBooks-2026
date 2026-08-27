using System;
using System.Collections.Generic;

namespace HebrewBooks.Services.Search;

public static class HebrewWeakLetters
{
	private static readonly char[] WeakByPriority = new char[3] { 'ע', 'א', 'ה' };

	private static readonly char[] FinalTriad = new char[3] { 'ה', 'א', 'ע' };

	private const int MinWordLen = 3;

	private const int MinSkeletonLetters = 3;

	private const int MaxVariants = 32;

	private static bool IsWeak(char c)
	{
		if (c == 'א' || c == 'ה' || c == 'ע')
		{
			return true;
		}
		return false;
	}

	public static IReadOnlyList<string> Expand(string word)
	{
		if (!IsCandidate(word))
		{
			return new string[1] { word };
		}
		List<string> result = new List<string> { word };
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal) { word };
		AddFinalSwaps(word);
		int num = 0;
		string text = word;
		foreach (char c in text)
		{
			if ((c != 'א' && c != 'ה' && c != 'ע') || 1 == 0)
			{
				num++;
			}
		}
		if (num < 3)
		{
			return result;
		}
		for (int j = 1; j < word.Length - 1; j++)
		{
			if (result.Count > 32)
			{
				break;
			}
			if (IsWeak(word[j]))
			{
				string text2 = word.Substring(0, j);
				text = word;
				int i = j + 1;
				AddWithFinals(text2 + text.Substring(i, text.Length - i));
			}
		}
		char reference;
		for (int k = 1; k <= word.Length; k++)
		{
			if (result.Count > 32)
			{
				break;
			}
			char c2 = word[k - 1];
			char c3 = ((k < word.Length) ? word[k] : '\0');
			char[] weakByPriority = WeakByPriority;
			foreach (char c4 in weakByPriority)
			{
				if (c4 != c2 && c4 != c3)
				{
					ReadOnlySpan<char> readOnlySpan = word.Substring(0, k);
					reference = c4;
					ReadOnlySpan<char> readOnlySpan2 = new ReadOnlySpan<char>(in reference);
					text = word;
					int num2 = k;
					AddWithFinals(string.Concat(readOnlySpan, readOnlySpan2, text.Substring(num2, text.Length - num2)));
				}
			}
		}
		for (int l = 0; l < word.Length; l++)
		{
			if (result.Count > 32)
			{
				break;
			}
			if (!IsWeak(word[l]))
			{
				continue;
			}
			char[] weakByPriority = WeakByPriority;
			foreach (char c5 in weakByPriority)
			{
				if (c5 != word[l])
				{
					ReadOnlySpan<char> readOnlySpan3 = word.Substring(0, l);
					reference = c5;
					ReadOnlySpan<char> readOnlySpan4 = new ReadOnlySpan<char>(in reference);
					text = word;
					int num2 = l + 1;
					AddWithFinals(string.Concat(readOnlySpan3, readOnlySpan4, text.Substring(num2, text.Length - num2)));
				}
			}
		}
		return result;
		void Add(string w)
		{
			if (w.Length >= 2 && result.Count <= 32 && seen.Add(w))
			{
				result.Add(w);
			}
		}
		void AddFinalSwaps(string w)
		{
			char c6 = w[w.Length - 1];
			if ((c6 == 'א' || c6 == 'ה' || c6 == 'ע') ? true : false)
			{
				char[] finalTriad = FinalTriad;
				foreach (char c7 in finalTriad)
				{
					if (c7 != c6)
					{
						Add(w.Substring(0, w.Length - 1) + c7);
					}
				}
			}
		}
		void AddWithFinals(string v)
		{
			Add(v);
			AddFinalSwaps(v);
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
