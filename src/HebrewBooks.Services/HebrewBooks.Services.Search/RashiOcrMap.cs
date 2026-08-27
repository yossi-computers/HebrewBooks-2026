using System;
using System.Collections.Generic;
using System.Text;

namespace HebrewBooks.Services.Search;

public sealed class RashiOcrMap
{
	private static readonly Dictionary<char, char> Pairs = new Dictionary<char, char>
	{
		['א'] = 'ח',
		['ח'] = 'א',
		['ת'] = 'מ',
		['מ'] = 'ת',
		['ב'] = 'נ',
		['נ'] = 'ב',
		['ל'] = 'צ',
		['צ'] = 'ל',
		['ס'] = 'ם',
		['ם'] = 'ס'
	};

	public const int MaxVariantsPerWord = 64;

	public IReadOnlyList<string> ExpandForQuery(string word)
	{
		if (string.IsNullOrEmpty(word))
		{
			return new string[1] { word };
		}
		List<int> list = new List<int>(word.Length);
		for (int i = 0; i < word.Length; i++)
		{
			if (Pairs.ContainsKey(word[i]))
			{
				list.Add(i);
			}
		}
		if (list.Count == 0)
		{
			return new string[1] { word };
		}
		int num = 1 << list.Count;
		if (num > 64)
		{
			num = 64;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		List<string> list2 = new List<string>(num);
		char[] array = word.ToCharArray();
		for (int j = 0; j < num; j++)
		{
			for (int k = 0; k < list.Count; k++)
			{
				int num2 = list[k];
				array[num2] = ((((j >> k) & 1) == 1) ? Pairs[word[num2]] : word[num2]);
			}
			string item = new string(array);
			if (hashSet.Add(item))
			{
				list2.Add(item);
			}
		}
		return list2;
	}

	public bool HasAnySwap(string word)
	{
		foreach (char key in word)
		{
			if (Pairs.ContainsKey(key))
			{
				return true;
			}
		}
		return false;
	}

	public string Encode(string word)
	{
		if (string.IsNullOrEmpty(word))
		{
			return word;
		}
		if (!HasAnySwap(word))
		{
			return word;
		}
		StringBuilder stringBuilder = new StringBuilder(word.Length * 2);
		foreach (char c in word)
		{
			if (Pairs.TryGetValue(c, out var value))
			{
				stringBuilder.Append('[').Append(c).Append(value)
					.Append(']');
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}
}
