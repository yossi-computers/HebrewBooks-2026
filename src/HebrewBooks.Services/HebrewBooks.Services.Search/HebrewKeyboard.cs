using System.Collections.Generic;
using System.Text;

namespace HebrewBooks.Services.Search;

public static class HebrewKeyboard
{
	private static readonly Dictionary<char, char> Layout = new Dictionary<char, char>
	{
		['q'] = '/',
		['w'] = '\'',
		['e'] = 'ק',
		['r'] = 'ר',
		['t'] = 'א',
		['y'] = 'ט',
		['u'] = 'ו',
		['i'] = 'ן',
		['o'] = 'ם',
		['p'] = 'פ',
		['a'] = 'ש',
		['s'] = 'ד',
		['d'] = 'ג',
		['f'] = 'כ',
		['g'] = 'ע',
		['h'] = 'י',
		['j'] = 'ח',
		['k'] = 'ל',
		['l'] = 'ך',
		[';'] = 'ף',
		['\''] = ',',
		['z'] = 'ז',
		['x'] = 'ס',
		['c'] = 'ב',
		['v'] = 'ה',
		['b'] = 'נ',
		['n'] = 'מ',
		['m'] = 'צ',
		[','] = 'ת',
		['.'] = 'ץ',
		['/'] = '.'
	};

	private static bool IsHebrewLetter(char c)
	{
		if (c >= 'א')
		{
			return c <= 'ת';
		}
		return false;
	}

	public static bool LooksMistyped(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		foreach (char c in text)
		{
			if (IsHebrewLetter(c))
			{
				num3++;
				continue;
			}
			char c2 = char.ToLowerInvariant(c);
			if (c2 >= 'a' && c2 <= 'z')
			{
				num++;
				if (Layout.ContainsKey(c2))
				{
					num2++;
				}
			}
		}
		if (num3 == 0 && num >= 2)
		{
			return num2 == num;
		}
		return false;
	}

	public static string ToHebrew(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text;
		}
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		foreach (char c in text)
		{
			stringBuilder.Append(Layout.TryGetValue(char.ToLowerInvariant(c), out var value) ? value : c);
		}
		return stringBuilder.ToString();
	}

	public static string? TryRecover(string? text)
	{
		if (!LooksMistyped(text))
		{
			return null;
		}
		return ToHebrew(text);
	}
}
