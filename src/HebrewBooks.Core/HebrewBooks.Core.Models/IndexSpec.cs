using System.Collections.Generic;

namespace HebrewBooks.Core.Models;

public sealed record IndexSpec(string IndexPath, IReadOnlyList<string> SourceFolders, bool UseNativeEnumeration = false, string? RelativeKeyRoot = null);
