using HebrewBooks.Core.Models;

namespace HebrewBooks.Services.Search;

public sealed record SearchResultRow(Book Book, int HitCount, string Location, int? PageNumber);
