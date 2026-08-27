using System.Collections.Generic;
using HebrewBooks.Services.Search;

namespace HebrewBooks.UI.Navigation;

public sealed record NavigationEntry(string? BookFileId, string? BookSourceType, string? BookRelativePath, int Page, string FilterText, bool IsContentMode, IReadOnlyList<SearchResultRow>? ContentResults, string? SelectedResultFileId, string? StatusText);
