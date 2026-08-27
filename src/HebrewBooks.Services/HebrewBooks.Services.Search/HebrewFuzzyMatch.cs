using System;
using System.Text;

namespace HebrewBooks.Services.Search;

public static class HebrewFuzzyMatch
{
	public static string Normalize(string? s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(s.Length);
		bool flag = false;
		for (int i = 0; i < s.Length; i++)
		{
			char c = s[i];
			if (c >= '\u0591' && c <= '\u05c7')
			{
				continue;
			}
			bool flag2;
			switch (c)
			{
			case '"':
			case '\'':
			case '`':
			case '׳':
			case '״':
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			if (flag2)
			{
				continue;
			}
			c = c switch
			{
				'ך' => 'כ', 
				'ם' => 'מ', 
				'ן' => 'נ', 
				'ף' => 'פ', 
				'ץ' => 'צ', 
				_ => c, 
			};
			flag2 = char.IsWhiteSpace(c);
			if (!flag2)
			{
				bool flag3;
				switch (c)
				{
				case '(':
				case ')':
				case ',':
				case '-':
				case '.':
				case '/':
				case '[':
				case '\\':
				case ']':
				case '_':
				case '־':
					flag3 = true;
					break;
				default:
					flag3 = false;
					break;
				}
				flag2 = flag3;
			}
			if (flag2)
			{
				if (stringBuilder.Length > 0)
				{
					flag = true;
				}
				continue;
			}
			if (flag)
			{
				stringBuilder.Append(' ');
				flag = false;
			}
			stringBuilder.Append(char.ToLowerInvariant(c));
		}
		return stringBuilder.ToString();
	}

	public static string Skeleton(string norm)
	{
		if (string.IsNullOrEmpty(norm))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(norm.Length);
		foreach (char c in norm)
		{
			if ((c != 'ו' && c != 'י') || 1 == 0)
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	public static int ThresholdFor(int len)
	{
		if (len > 3)
		{
			if (len > 6)
			{
				return 2;
			}
			return 1;
		}
		return 0;
	}

	public static bool AnyWordWithinDistance(string[] queryWords, string candidate)
	{
		if (queryWords.Length == 0 || candidate.Length == 0)
		{
			return false;
		}
		string[] array = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in queryWords)
		{
			int num = ThresholdFor(text.Length);
			if (num == 0)
			{
				continue;
			}
			string[] array2 = array;
			foreach (string b in array2)
			{
				if (BoundedLevenshtein(text, b, num) <= num)
				{
					return true;
				}
			}
		}
		return false;
	}

	public static int BoundedLevenshtein(string a, string b, int max)
	{
		int length = a.Length;
		int length2 = b.Length;
		if (Math.Abs(length - length2) > max)
		{
			return max + 1;
		}
		if (length == 0)
		{
			if (length2 > max)
			{
				return max + 1;
			}
			return length2;
		}
		if (length2 == 0)
		{
			if (length > max)
			{
				return max + 1;
			}
			return length;
		}
		int[] array = new int[length2 + 1];
		int[] array2 = new int[length2 + 1];
		for (int i = 0; i <= length2; i++)
		{
			array[i] = i;
		}
		for (int j = 1; j <= length; j++)
		{
			array2[0] = j;
			int num = j;
			char c = a[j - 1];
			for (int k = 1; k <= length2; k++)
			{
				int num2 = ((c != b[k - 1]) ? 1 : 0);
				array2[k] = Math.Min(Math.Min(array[k] + 1, array2[k - 1] + 1), array[k - 1] + num2);
				if (array2[k] < num)
				{
					num = array2[k];
				}
			}
			if (num > max)
			{
				return max + 1;
			}
			int[] array3 = array2;
			array2 = array;
			array = array3;
		}
		if (array[length2] > max)
		{
			return max + 1;
		}
		return array[length2];
	}
}
