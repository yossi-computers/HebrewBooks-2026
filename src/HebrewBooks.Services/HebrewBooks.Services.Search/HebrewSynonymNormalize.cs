using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HebrewBooks.Services.Search;

public static class HebrewSynonymNormalize
{
	private const string PrefixLetters = "והבכלמשד";

	public static string BaseKey(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(raw.Length);
		string text = raw.Normalize(NormalizationForm.FormD);
		foreach (char c in text)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
			{
				bool flag;
				switch (c)
				{
				case '"':
				case '\'':
				case '`':
				case '־':
				case '׳':
				case '״':
					flag = true;
					break;
				default:
					flag = false;
					break;
				}
				if (!flag)
				{
					stringBuilder.Append(c);
				}
			}
		}
		string text2 = stringBuilder.ToString().Trim();
		return string.Join(' ', text2.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
	}

	public static IEnumerable<string> Candidates(string query)
	{
		string baseKey = BaseKey(query);
		if (baseKey.Length == 0)
		{
			yield break;
		}
		yield return baseKey;
		string[] array = baseKey.Split(' ');
		string text = StripOnePrefix(array[0]);
		if (text != array[0])
		{
			array[0] = text;
			yield return string.Join(' ', array);
			array = baseKey.Split(' ');
		}
		if (array.Length <= 1)
		{
			yield break;
		}
		bool flag = false;
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = StripOnePrefix(array[i]);
			if (text2 != array[i])
			{
				array[i] = text2;
				flag = true;
			}
		}
		if (flag)
		{
			yield return string.Join(' ', array);
		}
	}

	private static string StripOnePrefix(string word)
	{
		if (word.Length >= 3 && "והבכלמשד".IndexOf(word[0]) >= 0 && IsHebrew(word[1]))
		{
			return word.Substring(1, word.Length - 1);
		}
		return word;
	}

	public static (string stripped, char prefix)? TryStripLeadingPrefix(string baseKey)
	{
		if (string.IsNullOrEmpty(baseKey))
		{
			return null;
		}
		int num = baseKey.IndexOf(' ');
		string text = ((num < 0) ? baseKey : baseKey.Substring(0, num));
		if (text.Length >= 3 && "והבכלמשד".IndexOf(text[0]) >= 0 && IsHebrew(text[1]))
		{
			string item;
			if (num >= 0)
			{
				string text2 = text;
				string text3 = text2.Substring(1, text2.Length - 1);
				text2 = baseKey;
				int num2 = num;
				item = text3 + text2.Substring(num2, text2.Length - num2);
			}
			else
			{
				string text2 = text;
				item = text2.Substring(1, text2.Length - 1);
			}
			return (item, text[0]);
		}
		return null;
	}

	private static bool IsHebrew(char c)
	{
		if (c >= 'א')
		{
			return c <= 'ת';
		}
		return false;
	}
}
