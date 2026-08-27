using System.Collections.Generic;

namespace HebrewBooks.Core.Abstractions;

public sealed record InBookHitInfo(int HitCount, IReadOnlyList<int> Pages, IReadOnlyList<string> MatchedTerms, string HighlightXml = "");
