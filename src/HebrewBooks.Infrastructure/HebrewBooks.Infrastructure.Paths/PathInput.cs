using System;
using System.Linq;

namespace HebrewBooks.Infrastructure.Paths;

public static class PathInput
{
	private static bool IsInvisibleMark(char c)
	{
		switch (c)
		{
		case '\u061c':
		case '\u200e':
		case '\u200f':
		case '\u202a':
		case '\u202b':
		case '\u202c':
		case '\u202d':
		case '\u202e':
		case '\u2066':
		case '\u2067':
		case '\u2068':
		case '\u2069':
		case '\ufeff':
			return true;
		default:
			return false;
		}
	}

	public static string? Normalize(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		string text = new string(raw.Where((char c) => !IsInvisibleMark(c)).ToArray()).Trim();
		text = text.Trim('"').Trim();
		while (text.Length > 0)
		{
			string text2 = text;
			if (text2[text2.Length - 1] != '\\')
			{
				string text3 = text;
				if (text3[text3.Length - 1] != '/')
				{
					break;
				}
			}
			if (text.EndsWith(":\\", StringComparison.Ordinal) || text.EndsWith(":/", StringComparison.Ordinal))
			{
				break;
			}
			string text4 = text;
			text = text4.Substring(0, text4.Length - 1).TrimEnd();
		}
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return null;
	}

	public static bool NeedsCleanup(string? raw)
	{
		if (!string.IsNullOrWhiteSpace(raw))
		{
			return Normalize(raw) != raw;
		}
		return false;
	}
}
