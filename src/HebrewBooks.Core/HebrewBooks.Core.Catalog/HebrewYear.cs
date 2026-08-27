using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HebrewBooks.Core.Catalog;

public static class HebrewYear
{
	public const int MinYear = 5200;

	public const int MaxYear = 5850;

	private const int ThousandsBase = 5000;

	private const int HebrewToCivilOffset = 3760;

	private static readonly ConcurrentDictionary<string, int> Cache = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

	private const char Gershayim = '״';

	private const char Geresh = '׳';

	public static int? Parse(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		int orAdd = Cache.GetOrAdd(raw.Trim(), ParseUncached);
		if (orAdd != 0)
		{
			return orAdd;
		}
		return null;
	}

	public static bool TryParse(string? raw, out int year)
	{
		int? num = Parse(raw);
		year = num.GetValueOrDefault();
		return num.HasValue;
	}

	private static int ParseUncached(string s)
	{
		for (int i = 0; i < s.Length; i++)
		{
			if (!char.IsAsciiDigit(s[i]))
			{
				continue;
			}
			int num = i;
			for (; i < s.Length && char.IsAsciiDigit(s[i]); i++)
			{
			}
			if (i - num <= 5)
			{
				int num2 = int.Parse(s.AsSpan(num, i - num));
				if (num2 >= 5200 && num2 <= 5850)
				{
					return num2;
				}
				if (num2 >= 1000 && num2 <= 2200)
				{
					return num2 + 3760;
				}
			}
		}
		int[] array = new int[3];
		int[] array2 = new int[3];
		bool flag = IsSingleToken(s);
		foreach (var item3 in Tokens(s))
		{
			int item = item3.From;
			int item2 = item3.To;
			int num3 = NumeralValue(s, item, item2);
			if (num3 == 0)
			{
				continue;
			}
			int num4 = ((num3 < 1000) ? (num3 + 5000) : num3);
			if ((num4 < 5200 || num4 > 5850) ? true : false)
			{
				continue;
			}
			int num5 = ((!HasAbbreviationMark(s, item, item2)) ? (IsDescending(s, item, item2) ? 1 : 2) : 0);
			if (num5 != 2 || flag)
			{
				int num6 = item2 - item;
				if (array2[num5] < num6)
				{
					array[num5] = num4;
					array2[num5] = num6;
				}
			}
		}
		if (array[0] == 0)
		{
			if (array[1] == 0)
			{
				return array[2];
			}
			return array[1];
		}
		return array[0];
	}

	private static IEnumerable<(int From, int To)> Tokens(string s)
	{
		int i = 0;
		while (i < s.Length)
		{
			for (; i < s.Length && IsSeparator(s[i]); i++)
			{
			}
			int num = i;
			for (; i < s.Length && !IsSeparator(s[i]); i++)
			{
			}
			if (i > num)
			{
				yield return (From: num, To: i);
			}
		}
	}

	private static bool IsSingleToken(string s)
	{
		int num = 0;
		foreach (var item in Tokens(s))
		{
			_ = item;
			if (++num > 1)
			{
				return false;
			}
		}
		return num == 1;
	}

	private static bool IsSeparator(char c)
	{
		bool flag = char.IsWhiteSpace(c);
		if (!flag)
		{
			bool flag2;
			switch (c)
			{
			case '(':
			case ')':
			case ',':
			case '-':
			case '.':
			case '/':
			case ':':
			case ';':
			case '[':
			case ']':
			case '|':
			case '–':
			case '—':
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		return flag;
	}

	private static int NumeralValue(string s, int from, int to)
	{
		if (to - from > 2 && s[from] == 'ה' && IsAbbreviationMark(s[from + 1]))
		{
			from += 2;
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		bool flag = false;
		for (int i = from; i < to; i++)
		{
			char c = s[i];
			if (!IsAbbreviationMark(c))
			{
				int num6 = LetterValue(c);
				if (num6 == 0)
				{
					return 0;
				}
				num += num6;
				if (num6 < 10)
				{
					num2++;
					num5 += num6;
					flag = flag || c == 'ט';
				}
				else if (num6 < 100)
				{
					num3++;
				}
				else
				{
					num4 += num6;
				}
			}
		}
		if ((num4 > 900 || num4 == 0) ? true : false)
		{
			return 0;
		}
		if (num3 > 1)
		{
			return 0;
		}
		bool flag2 = num2 > 1;
		if (flag2)
		{
			bool flag3 = num2 == 2 && num3 == 0 && flag;
			if (flag3)
			{
				bool flag4 = (uint)(num5 - 15) <= 1u;
				flag3 = flag4;
			}
			flag2 = !flag3;
		}
		if (flag2)
		{
			return 0;
		}
		return num;
	}

	private static bool IsDescending(string s, int from, int to)
	{
		int num = int.MaxValue;
		for (int i = from; i < to; i++)
		{
			int num2 = LetterValue(s[i]);
			if (num2 != 0)
			{
				if (num2 > num)
				{
					return false;
				}
				num = num2;
			}
		}
		return true;
	}

	private static bool HasAbbreviationMark(string s, int from, int to)
	{
		for (int i = from; i < to; i++)
		{
			if (IsAbbreviationMark(s[i]))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsAbbreviationMark(char c)
	{
		switch (c)
		{
		case '"':
		case '\'':
		case '׳':
		case '״':
		case '‘':
		case '’':
		case '“':
		case '”':
			return true;
		default:
			return false;
		}
	}

	private static int LetterValue(char c)
	{
		switch (c)
		{
		case 'א':
			return 1;
		case 'ב':
			return 2;
		case 'ג':
			return 3;
		case 'ד':
			return 4;
		case 'ה':
			return 5;
		case 'ו':
			return 6;
		case 'ז':
			return 7;
		case 'ח':
			return 8;
		case 'ט':
			return 9;
		case 'י':
			return 10;
		case 'ך':
		case 'כ':
			return 20;
		case 'ל':
			return 30;
		case 'ם':
		case 'מ':
			return 40;
		case 'ן':
		case 'נ':
			return 50;
		case 'ס':
			return 60;
		case 'ע':
			return 70;
		case 'ף':
		case 'פ':
			return 80;
		case 'ץ':
		case 'צ':
			return 90;
		case 'ק':
			return 100;
		case 'ר':
			return 200;
		case 'ש':
			return 300;
		case 'ת':
			return 400;
		default:
			return 0;
		}
	}

	public static string ToGematria(int year)
	{
		if ((year < 4500 || year > 6500) ? true : false)
		{
			return year.ToString(CultureInfo.InvariantCulture);
		}
		int num = year % 1000;
		if (num == 0)
		{
			return year.ToString(CultureInfo.InvariantCulture);
		}
		StringBuilder stringBuilder = new StringBuilder(6);
		int num2;
		for (num2 = num / 100; num2 >= 4; num2 -= 4)
		{
			stringBuilder.Append('ת');
		}
		if (num2 > 0)
		{
			stringBuilder.Append("קרש"[num2 - 1]);
		}
		num %= 100;
		switch (num)
		{
		case 15:
			stringBuilder.Append("טו");
			break;
		case 16:
			stringBuilder.Append("טז");
			break;
		default:
		{
			int num3 = num / 10;
			if (num3 > 0)
			{
				stringBuilder.Append("יכלמנסעפצ"[num3 - 1]);
			}
			int num4 = num % 10;
			if (num4 > 0)
			{
				stringBuilder.Append("אבגדהוזחט"[num4 - 1]);
			}
			break;
		}
		}
		if (stringBuilder.Length == 1)
		{
			stringBuilder.Append('׳');
			return stringBuilder.ToString();
		}
		string text = stringBuilder.ToString();
		string text2 = text;
		return text2.Substring(0, text2.Length - 1) + '״' + text[text.Length - 1];
	}

	public static int ToCivilYear(int hebrewYear)
	{
		return hebrewYear - 3760;
	}
}
