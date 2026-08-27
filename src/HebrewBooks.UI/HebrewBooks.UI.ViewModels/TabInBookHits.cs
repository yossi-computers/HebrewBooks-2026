using System.Collections.Generic;
using HebrewBooks.Core.Abstractions;

namespace HebrewBooks.UI.ViewModels;

public sealed class TabInBookHits
{
	public required InBookHitInfo Hits { get; init; }

	public required IReadOnlyList<int> Pages { get; init; }

	public int HitIndex { get; init; } = -1;

	public int? SelectedHitPage { get; init; }
}
