using System.Collections.Generic;

namespace HebrewBooks.Core.Models;

public sealed record MadafNode(int MadafID, string? Name, bool View, IReadOnlyList<int> BookIds, bool Pinned = false);
