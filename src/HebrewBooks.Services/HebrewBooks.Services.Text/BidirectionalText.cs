namespace HebrewBooks.Services.Text;

public static class BidirectionalText
{
	public static bool IsRtl(string? s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return false;
		}
		foreach (char ch in s)
		{
			if (IsHebrew(ch) || IsArabic(ch))
			{
				return true;
			}
			if (IsLatinLetter(ch))
			{
				return false;
			}
		}
		return false;
	}

	public static bool IsHebrew(char ch)
	{
		if (ch >= '\u0590')
		{
			return ch <= '\u05ff';
		}
		return false;
	}

	public static bool IsArabic(char ch)
	{
		if (ch >= '\u0600')
		{
			return ch <= 'ۿ';
		}
		return false;
	}

	public static bool IsLatinLetter(char ch)
	{
		switch (ch)
		{
		case 'A':
		case 'B':
		case 'C':
		case 'D':
		case 'E':
		case 'F':
		case 'G':
		case 'H':
		case 'I':
		case 'J':
		case 'K':
		case 'L':
		case 'M':
		case 'N':
		case 'O':
		case 'P':
		case 'Q':
		case 'R':
		case 'S':
		case 'T':
		case 'U':
		case 'V':
		case 'W':
		case 'X':
		case 'Y':
		case 'Z':
		case 'a':
		case 'b':
		case 'c':
		case 'd':
		case 'e':
		case 'f':
		case 'g':
		case 'h':
		case 'i':
		case 'j':
		case 'k':
		case 'l':
		case 'm':
		case 'n':
		case 'o':
		case 'p':
		case 'q':
		case 'r':
		case 's':
		case 't':
		case 'u':
		case 'v':
		case 'w':
		case 'x':
		case 'y':
		case 'z':
			return true;
		default:
			return false;
		}
	}

	public static string EnsureRtl(string? s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return s ?? "";
		}
		if (!IsRtl(s))
		{
			return "\u200f" + s;
		}
		return s;
	}
}
