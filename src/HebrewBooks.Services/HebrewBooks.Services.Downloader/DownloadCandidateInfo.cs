namespace HebrewBooks.Services.Downloader;

public sealed record DownloadCandidateInfo(int FileId, string BookName, string? AuthorName, bool Found);
