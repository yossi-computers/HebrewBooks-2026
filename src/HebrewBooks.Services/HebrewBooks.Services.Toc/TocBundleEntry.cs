using System.Collections.Generic;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Services.Toc;

public sealed record TocBundleEntry(string FileId, string? BookName, IReadOnlyList<TocEntry> Toc);
