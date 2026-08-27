namespace HebrewBooks.Data;

public static class HebrewCollation
{
	public const string Name = "HEB";

	public static int Compare(string? a, string? b)
	{
		if ((object)a == b)
		{
			return 0;
		}
		if (a == null)
		{
			return -1;
		}
		if (b == null)
		{
			return 1;
		}
		int num = HebrewGroup(a);
		int num2 = HebrewGroup(b);
		if (num != num2)
		{
			return num.CompareTo(num2);
		}
		return string.CompareOrdinal(a, b);
	}

	private static int HebrewGroup(string s)
	{
		if (s.Length == 0)
		{
			return 2;
		}
		char c = s[0];
		if (c < 'א' || c > 'ת')
		{
			return 2;
		}
		return 1;
	}
}
