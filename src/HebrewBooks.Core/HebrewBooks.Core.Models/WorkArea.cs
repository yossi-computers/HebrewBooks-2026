using System;
using System.Collections.Generic;

namespace HebrewBooks.Core.Models;

public sealed record WorkArea(string Name, DateTime CreatedUtc, IReadOnlyList<int> BookIds);
