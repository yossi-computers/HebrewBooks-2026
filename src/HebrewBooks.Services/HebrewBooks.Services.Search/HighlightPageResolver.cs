using System;
using System.Collections.Generic;
using System.Globalization;

namespace HebrewBooks.Services.Search;

public static class HighlightPageResolver
{
	private readonly record struct Token(bool IsPageBreak, int Value)
	{
		public static readonly Token PageBreak = new Token(IsPageBreak: true, 0);
	}

	public static int? ResolvePage(string? highlightInfo, int wordOffset)
	{
		if (string.IsNullOrEmpty(highlightInfo))
		{
			return null;
		}
		if (wordOffset < 0)
		{
			return null;
		}
		int num = 1;
		foreach (Token item in EnumerateTokens(highlightInfo))
		{
			if (item.IsPageBreak)
			{
				num++;
			}
			else if (item.Value >= wordOffset)
			{
				return num;
			}
		}
		return num;
	}

	private static IEnumerable<Token> EnumerateTokens(string s)
	{
		int i = 0;
		while (i < s.Length)
		{
			for (; i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',' || s[i] == ';'); i++)
			{
			}
			if (i >= s.Length)
			{
				break;
			}
			if (StartsWith(s, i, "PageBreak"))
			{
				yield return Token.PageBreak;
				i += "PageBreak".Length;
				continue;
			}
			int num = i;
			for (; i < s.Length && (char.IsDigit(s[i]) || s[i] == '-'); i++)
			{
			}
			int result;
			if (i == num)
			{
				i++;
			}
			else if (int.TryParse(s.AsSpan(num, i - num), NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
			{
				yield return new Token(IsPageBreak: false, result);
			}
		}
	}

	private static bool StartsWith(string s, int i, string prefix)
	{
		if (i + prefix.Length > s.Length)
		{
			return false;
		}
		for (int j = 0; j < prefix.Length; j++)
		{
			if (s[i + j] != prefix[j])
			{
				return false;
			}
		}
		return true;
	}
}
