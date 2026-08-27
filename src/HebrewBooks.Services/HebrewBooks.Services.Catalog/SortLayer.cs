using HebrewBooks.Services.Search;

namespace HebrewBooks.Services.Catalog;

public readonly record struct SortLayer(SortMode Key, bool Descending)
{
	public static SortLayer From(SortMode mode)
	{
		return mode switch
		{
			SortMode.PrintYearDesc => new SortLayer(SortMode.PrintYear, Descending: true), 
			SortMode.HitCount => new SortLayer(SortMode.HitCount, Descending: true), 
			_ => new SortLayer(mode, Descending: false), 
		};
	}
}
