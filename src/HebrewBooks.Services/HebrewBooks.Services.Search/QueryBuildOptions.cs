using System;
using System.Collections.Generic;

namespace HebrewBooks.Services.Search;

public sealed record QueryBuildOptions(int DefaultProximity = 30, bool AddPrefixLetters = false, bool FirstWordOnly = false, bool LastWordOnly = false, RasheyTevotMap? RasheyTevot = null, bool ExpandRoots = false, bool ExpandNumberGender = false, bool ExpandGematria = false, bool ExpandSpelling = false, HebAramMap? Aramaic = null, Func<IReadOnlyCollection<string>, ISet<string>>? IndexWordFilter = null, int MaxTotalExpansions = 200, bool ExpandRashiOcr = false, bool RequireWordOrder = false, bool ExpandWeakLetters = false);
