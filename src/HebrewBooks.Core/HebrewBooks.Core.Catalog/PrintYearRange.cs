namespace HebrewBooks.Core.Catalog;

public readonly record struct PrintYearRange(int? From, int? To, bool IncludeUnknown)
{
	public bool IsActive
	{
		get
		{
			if (!From.HasValue)
			{
				return To.HasValue;
			}
			return true;
		}
	}

	public static readonly PrintYearRange All = new PrintYearRange(null, null, IncludeUnknown: true);

	public bool Matches(string? printYear)
	{
		if (!IsActive)
		{
			return true;
		}
		int? num = HebrewYear.Parse(printYear);
		if (!num.HasValue)
		{
			return IncludeUnknown;
		}
		if (!From.HasValue || num >= From)
		{
			if (To.HasValue)
			{
				return num <= To;
			}
			return true;
		}
		return false;
	}
}
