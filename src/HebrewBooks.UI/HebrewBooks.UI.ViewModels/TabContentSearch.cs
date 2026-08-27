using System.Collections.Generic;
using HebrewBooks.Services.Search;

namespace HebrewBooks.UI.ViewModels;

public sealed class TabContentSearch
{
	public required IReadOnlyList<SearchResultRow> Results { get; init; }

	public string? SelectedResultFileId { get; init; }

	public string QueryText { get; init; } = "";

	public string ResultFilterText { get; init; } = "";

	public string ActiveChipsSummary { get; init; } = "";

	public string? ContentOpenFileId { get; init; }

	public string StatusText { get; init; } = "";
}
