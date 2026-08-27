namespace HebrewBooks.Core.Models;

public sealed record IndexProgressReport(int Step, int PercentDone, long FilesToIndex, long FilesRead, long KbToIndex, long KbRead, long DocsInIndex, long WordsInIndex, int ElapsedSeconds, int EstRemainingSeconds, long DiskFreeBytes, string IndexName, string IndexLocation, string? CurrentFileName, string? CurrentFileLocation, string? CurrentFileType, long CurrentFileSizeBytes, long CurrentFileWords, int CurrentFilePercent);
