using System.Collections.Generic;

namespace HebrewBooks.Services.Search;

public sealed record SynonymGroup(string Source, IReadOnlyList<string> Chips);
