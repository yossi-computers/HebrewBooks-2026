namespace HebrewBooks.Core.Models;

public sealed record SearchHit(string FileID, int HitCount, string Location, int? PageNumber);
