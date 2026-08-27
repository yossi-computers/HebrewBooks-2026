using System.Collections.Generic;
using HebrewBooks.Core.Models;

namespace HebrewBooks.Services.Downloader;

public sealed record BookMetadata(int FileID, string? BookName, string? AuthorName, string? PrintPlace, string? PrintYear, int? CountPage, string? Description, IReadOnlyList<TocEntry> Toc);
