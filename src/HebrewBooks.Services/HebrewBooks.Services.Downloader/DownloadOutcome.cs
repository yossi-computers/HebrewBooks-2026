namespace HebrewBooks.Services.Downloader;

public sealed record DownloadOutcome(int FileID, bool Success, int CatalogId = 0, string? Title = null, string? Error = null, bool WasAlreadyOnDisk = false);
