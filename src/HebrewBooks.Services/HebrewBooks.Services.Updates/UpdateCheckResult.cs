using System;

namespace HebrewBooks.Services.Updates;

public sealed record UpdateCheckResult(bool IsAvailable, Version? Latest, string? DownloadUrl, string? Error);
