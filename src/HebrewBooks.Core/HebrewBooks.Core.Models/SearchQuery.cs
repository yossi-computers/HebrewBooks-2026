using System.Collections.Generic;

namespace HebrewBooks.Core.Models;

public sealed record SearchQuery(string Text, int MaxProximity = 30, bool Hybur = false, bool IncludeNumbers = true, int MaxFilesToRetrieve = 10000, int Fuzziness = 0, IReadOnlyList<string>? RestrictToFileIds = null, IReadOnlyList<string>? RestrictToIndexPaths = null, int HitCountDivisor = 1);
